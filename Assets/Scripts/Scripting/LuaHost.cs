using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;
using SRG.Utils;

namespace SRG.Scripting
{
    /// <summary>
    /// Централизованный владелец Lua-скриптов проекта.
    /// <br/>
    /// <b>Core</b> — движковый инстанс: дебаг-консоль, диалоговые Lua-действия, наши квесты.
    /// Ему доверяем полностью, без лимитов на количество инструкций.
    /// <br/>
    /// <b>GetOrCreateMod(id)</b> — по одному <see cref="Script"/> на сторонний мод. У каждого мода
    /// свой рантайм: Globals, Options, координатор корутин. Ошибка в одном моде не портит движок
    /// и не течёт в другой мод.
    /// <br/><br/>
    /// Все Script'ы получают одинаковый набор биндингов через <see cref="LuaBindings"/> и
    /// одинаковый сэндбокс (<see cref="CoreModules.Preset_HardSandbox"/>): без io/os/debug/require.
    /// </summary>
    public static class LuaHost
    {
        private static Script _core;
        private static readonly Dictionary<string, Script> _mods = new(StringComparer.Ordinal);
        private static bool _bootstrapped;

        public static Script Core
        {
            get { EnsureBootstrapped(); return _core; }
        }

        public static IReadOnlyDictionary<string, Script> Mods
        {
            get { EnsureBootstrapped(); return _mods; }
        }

        public static Script GetOrCreateMod(string modId)
        {
            EnsureBootstrapped();
            if (string.IsNullOrEmpty(modId))
                throw new ArgumentException("modId is empty", nameof(modId));
            if (!_mods.TryGetValue(modId, out var s))
                _mods[modId] = s = NewScript(trusted: false);
            return s;
        }

        public static void UnloadMod(string modId)
        {
            if (!string.IsNullOrEmpty(modId)) _mods.Remove(modId);
        }

        /// <summary>Полный сброс. Использовать при возврате в главное меню / hot-reload.</summary>
        public static void Reset()
        {
            _core = null;
            _mods.Clear();
            // Кэш чанков держит замыкания и Table'ы старого Script — после сброса они невалидны.
            _chunkCache.Clear();
            _bootstrapped = false;
        }

        /// <summary>
        /// Сделать статический API доступным из Lua под именем типа (например <c>GalaxyManager</c>).
        /// Для слоёв выше симуляции; уже созданные скрипты (Core и моды) получают его сразу.
        /// </summary>
        public static void RegisterStaticApi(Type t)
        {
            if (!LuaBindings.AddStaticApi(t) || !_bootstrapped) return;
            LuaBindings.BindStaticApi(_core, t);
            foreach (var mod in _mods.Values) LuaBindings.BindStaticApi(mod, t);
        }

        private static void EnsureBootstrapped()
        {
            if (_bootstrapped) return;
            _bootstrapped = true;
            LuaBindings.RegisterTypes();
            _core = NewScript(trusted: true);
        }

        private static Script NewScript(bool trusted)
        {
            var s = new Script(CoreModules.Preset_HardSandbox);
            s.Options.DebugPrint = msg => GameLog.Add(msg);
            // В MoonSharp 2.0.0.0 нет native instruction-limit; для модов throttling делается
            // через coroutine-yield-counter при исполнении их хуков — см. LuaHost.CallMod().
            LuaBindings.BindGlobals(s);
            return s;
        }

        // ── Evaluation helpers ─────────────────────────────────────────────

        /// <summary>Выполнить строку в CoreScript. Кидает исключение при синтаксисе/рантайме.</summary>
        public static DynValue Do(string source) => Core.DoString(source);

        /// <summary>Выполнить строку в CoreScript с локальным окружением: переменные из
        /// <paramref name="locals"/> становятся видимы как обычные глобалы (<c>ctx</c>, <c>target</c>,
        /// <c>args</c>), а базовые биндинги (Player/Mathf/WormholeService/…) остаются доступны через
        /// metatable <c>__index</c> у Script.Globals. Локалы <b>перекрывают</b> одноимённые глобалы.</summary>
        public static DynValue DoWithEnv(string source, IDictionary<string, object> locals)
        {
            var s = Core;
            var env = new Table(s);
            var meta = new Table(s);
            meta["__index"] = s.Globals;
            env.MetaTable = meta;

            if (locals != null)
            {
                foreach (var kv in locals)
                {
                    env[kv.Key] = kv.Value == null
                        ? DynValue.Nil
                        : DynValue.FromObject(s, kv.Value);
                }
            }
            return s.DoString(source, env);
        }

        // Скомпилированный чанк + его окружение и набор ключей, засеянных прошлым вызовом.
        private readonly struct CachedChunk
        {
            public readonly DynValue Function;
            public readonly Table Env;
            public readonly List<string> SeededKeys;
            public CachedChunk(DynValue fn, Table env) { Function = fn; Env = env; SeededKeys = new List<string>(); }
        }

        private static readonly Dictionary<string, CachedChunk> _chunkCache = new(StringComparer.Ordinal);

        /// <summary>Скомпилировать исходник в кэш заранее, не исполняя его. Кидает
        /// <see cref="SyntaxErrorException"/> на битом коде — этим пользуется валидация конфигов,
        /// чтобы поймать опечатку на загрузке, а не в момент показа узла диалога.</summary>
        public static void Precompile(string source)
        {
            if (string.IsNullOrEmpty(source) || _chunkCache.ContainsKey(source)) return;
            GetOrCompile(source);
        }

        private static CachedChunk GetOrCompile(string source)
        {
            if (_chunkCache.TryGetValue(source, out var cached)) return cached;
            var s = Core;
            var env = new Table(s);
            var meta = new Table(s);
            meta["__index"] = s.Globals;
            env.MetaTable = meta;
            var chunk = new CachedChunk(s.LoadString(source, env), env);
            _chunkCache[source] = chunk;
            return chunk;
        }

        /// <summary>Как <see cref="DoWithEnv"/>, но чанк компилируется один раз и переиспользуется:
        /// окружение живёт вместе с ним, а локалы перезасеваются перед каждым вызовом.
        /// <br/><br/>
        /// Для того, что исполняется часто — условия реплик и <c>{lua:…}</c> в текстах диалогов
        /// (проверяются на каждый рендер узла) — это принципиально: <see cref="DoWithEnv"/>
        /// перекомпилирует исходник при каждом вызове.
        /// <br/><br/>
        /// Кэш ключуется исходником, поэтому годится только для скриптов из конфига (их число
        /// ограничено). Не вызывать на строках, склеенных из рантайм-значений, — иначе кэш растёт
        /// без предела; для такого есть <see cref="DoWithEnv"/>.</summary>
        public static DynValue EvalCached(string source, IDictionary<string, object> locals)
        {
            if (string.IsNullOrEmpty(source)) return DynValue.Nil;
            var s = Core;
            var chunk = GetOrCompile(source);

            // Ключи прошлого вызова, которых нет в этом, гасим — иначе Lua увидит устаревший
            // target/planet от предыдущего диалога вместо nil.
            for (int i = 0; i < chunk.SeededKeys.Count; i++)
            {
                var key = chunk.SeededKeys[i];
                if (locals == null || !locals.ContainsKey(key)) chunk.Env[key] = DynValue.Nil;
            }
            chunk.SeededKeys.Clear();

            if (locals != null)
            {
                foreach (var kv in locals)
                {
                    chunk.Env[kv.Key] = kv.Value == null ? DynValue.Nil : DynValue.FromObject(s, kv.Value);
                    chunk.SeededKeys.Add(kv.Key);
                }
            }

            return s.Call(chunk.Function);
        }

        /// <summary>Форматирование результата для вывода в консоль.</summary>
        public static string Format(DynValue v)
        {
            if (v == null || v.IsVoid() || v.IsNil()) return "nil";
            return v.Type switch
            {
                DataType.String  => v.String,
                DataType.Number  => v.Number.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
                DataType.Boolean => v.Boolean ? "true" : "false",
                _                => v.ToPrintString()
            };
        }
    }
}
