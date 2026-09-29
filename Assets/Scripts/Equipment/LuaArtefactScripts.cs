using System.Collections.Generic;
using UnityEngine;
using MoonSharp.Interpreter;
using SRG.Config;
using SRG.Galaxy;
using SRG.Scripting;

namespace SRG.Equipment
{
    /// <summary>
    /// Lua-адаптеры для трёх типов артефактных скриптов (Turn/Use/Subturn). Скрипт из конфига
    /// («тело функции») один раз оборачивается в <c>return function(ship, item, cfg[, subTurn]) …end</c>,
    /// компилируется в <see cref="Closure"/> в общем движке <see cref="LuaHost.Core"/> и хранится
    /// как обычный <see cref="IArtefactTurnScript"/>/<see cref="IArtefactActionScript"/>/
    /// Каждый вызов — прямой invoke замыкания без парсинга/поиска
    /// глобалов, добавочные аллокации минимальны.
    ///
    /// Ошибка исполнения одного вызова логируется один раз per script id (чтобы не спамить),
    /// после чего скрипт продолжает работать в следующие ходы (падение не «глушит» артефакт).
    /// </summary>
    public static class LuaArtefactScripts
    {
        private static readonly HashSet<string> _loggedErrors = new();

        /// <summary>Общий стайлер сообщения об ошибке: логируем один раз per (kind + id).</summary>
        private static void LogOnce(string kind, string id, System.Exception e)
        {
            string key = kind + "|" + id;
            if (_loggedErrors.Contains(key)) return;
            _loggedErrors.Add(key);
            Debug.LogError($"[LuaArtefactScripts] {kind} '{id}': {e.Message}\n{e.StackTrace}");
        }

        /// <summary>Скомпилировать «тело скрипта» в замыкание с указанными именами параметров.
        /// Возвращает null при ошибке (компилятор пишет её в консоль один раз per id).</summary>
        private static Closure Compile(string kind, string id, string body, params string[] paramNames)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            string paramList = string.Join(", ", paramNames);
            string wrapped = $"return function({paramList})\n{body}\nend";
            try
            {
                var dv = LuaHost.Core.DoString(wrapped);
                if (dv.Type != DataType.Function) return null;
                return dv.Function;
            }
            catch (System.Exception e)
            {
                LogOnce(kind + "-compile", id, e);
                return null;
            }
        }

        // ── Turn ──────────────────────────────────────────────────────────

        private sealed class LuaTurnScript : IArtefactTurnScript
        {
            private readonly string _id;
            private readonly Closure _closure;

            public LuaTurnScript(string id, Closure closure) { _id = id; _closure = closure; }

            public void OnTurn(ShipData ship, ItemInstance item, ItemsConfig equipConfig, int subTurn)
            {
                if (_closure == null) return;
                try { _closure.Call(ship, item, equipConfig, subTurn); }
                catch (System.Exception e) { LogOnce("TurnScript", _id, e); }
            }
        }

        public static IArtefactTurnScript CompileTurn(string id, string body)
        {
            var c = Compile("TurnScript", id, body, "ship", "item", "cfg", "subTurn");
            return c != null ? new LuaTurnScript(id, c) : null;
        }

        // ── Use ───────────────────────────────────────────────────────────

        private sealed class LuaActionScript : IArtefactActionScript
        {
            private readonly string _id;
            private readonly Closure _closure;

            public LuaActionScript(string id, Closure closure) { _id = id; _closure = closure; }

            public ActionResult TryUse(ShipData ship, ItemInstance item, ItemsConfig equipConfig)
            {
                if (_closure == null) return ActionResult.Fail($"UseScript '{_id}' не скомпилирован.");
                try
                {
                    var res = _closure.Call(ship, item, equipConfig);
                    return ParseResult(res);
                }
                catch (System.Exception e)
                {
                    LogOnce("UseScript", _id, e);
                    return ActionResult.Fail($"UseScript '{_id}': ошибка исполнения.");
                }
            }

            /// <summary>Ожидаемый возврат Lua: таблица { ok=bool, message=string?, consume=bool?, wear=number? }
            /// либо nil (= Ok без побочек). Одна строка = { ok=true, message=… }.</summary>
            private static ActionResult ParseResult(DynValue v)
            {
                if (v == null || v.IsNil() || v.IsVoid()) return ActionResult.Ok();
                if (v.Type == DataType.String) return ActionResult.Ok(v.String);
                if (v.Type == DataType.Boolean) return v.Boolean ? ActionResult.Ok() : ActionResult.Fail();
                if (v.Type != DataType.Table) return ActionResult.Ok();

                var t = v.Table;
                bool ok = true;
                var okv = t.Get("ok");
                if (okv.Type == DataType.Boolean) ok = okv.Boolean;

                string msg = null;
                var mv = t.Get("message");
                if (mv.Type == DataType.String) msg = mv.String;

                bool consume = false;
                var cv = t.Get("consume");
                if (cv.Type == DataType.Boolean) consume = cv.Boolean;

                int wear = 0;
                var wv = t.Get("wear");
                if (wv.Type == DataType.Number) wear = (int)wv.Number;

                return ok ? ActionResult.Ok(msg, consume, wear) : ActionResult.Fail(msg);
            }
        }

        public static IArtefactActionScript CompileUse(string id, string body)
        {
            var c = Compile("UseScript", id, body, "ship", "item", "cfg");
            return c != null ? new LuaActionScript(id, c) : null;
        }

        // ── Bulk-компиляция всего ItemsConfig ─────────────────────────

        /// <summary>
        /// Пройти по всем предметам конфига и зарегистрировать Lua-скрипты (Turn/Use),
        /// у которых заполнены соответствующие тела. Регистрационный id — ItemId (т.е.
        /// TurnCode/UseCode неявно становятся равными ItemId, если Lua-скрипт задан).
        /// Явные *Code перебивают: если задан и он, и скрипт — используется Lua-версия
        /// (регистрация идёт под явным *Code).
        /// Идемпотентно: повторный вызов заменяет ранее скомпилированные скрипты.
        /// </summary>
        public static void CompileAndRegisterAll(ItemsConfig equipConfig)
        {
            if (equipConfig == null) return;
            foreach (var (category, itemId, cfg) in equipConfig.EnumerateAllItems())
            {
                if (cfg == null) continue;

                if (!string.IsNullOrWhiteSpace(cfg.TurnScript))
                {
                    string id = string.IsNullOrEmpty(cfg.TurnCode) ? itemId : cfg.TurnCode;
                    var script = CompileTurn(id, cfg.TurnScript);
                    if (script != null)
                    {
                        ArtefactTurnRegistry.Register(id, script);
                        if (string.IsNullOrEmpty(cfg.TurnCode)) cfg.TurnCode = id;
                    }
                }

                if (!string.IsNullOrWhiteSpace(cfg.UseScript))
                {
                    string id = string.IsNullOrEmpty(cfg.UseCode) ? itemId : cfg.UseCode;
                    var script = CompileUse(id, cfg.UseScript);
                    if (script != null)
                    {
                        ArtefactActionRegistry.Register(id, script);
                        if (string.IsNullOrEmpty(cfg.UseCode)) cfg.UseCode = id;
                    }
                }
            }
        }
    }
}
