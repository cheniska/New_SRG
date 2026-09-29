using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SRG.Dialog
{
    /// <summary>
    /// Проверяет консистентность <see cref="DialogsConfig"/> при загрузке:
    ///   • StartNode существует;
    ///   • все NextNode ссылаются на существующие узлы (null/пусто — «конец» — допустимо);
    ///   • у каждого узла есть либо Text, либо хотя бы одна Reply;
    ///   • у Actions задан либо Method, либо Script (существование Method-обработчика не проверяем — регистрируется позже);
    ///   • Condition парсится <see cref="DialogService.DefaultEvaluateCondition"/> без исключений;
    ///   • в текстах нет инлайновых фоллбэков <c>{tag|фраза}</c> — фразы живут в
    ///     TextsConfig.Dialog (TagDefaults/Speakers/PoolDefaults), см. <see cref="DialogTexts"/>;
    ///   • каждый <c>{pool:Key}</c> указывает на существующий пул (или на семейство с дефолтом),
    ///     иначе в текст попадёт видимый <c>{pool:…}</c>.
    /// Все проблемы логируются как warnings; конфиг не блокируется.
    /// </summary>
    public static class DialogConfigValidator
    {
        private static readonly Regex TagPattern = new(@"\{([A-Za-z0-9_.:<>]+)(?:\|([^{}]*))?\}", RegexOptions.Compiled);
        /// <summary>Начало инлайнового фоллбэка. Без закрывающей скобки — чтобы поймать и вложенные
        /// случаи, до которых <see cref="TagPattern"/> не дотягивается.</summary>
        private static readonly Regex FallbackStart = new(@"\{([A-Za-z0-9_.:<>]+)\|", RegexOptions.Compiled);

        public static void Validate(DialogsConfig cfg)
        {
            if (cfg == null) return;
            int problems = 0;
            var pools = cfg.StringPools;

            if (cfg.Dialogs != null)
            {
                foreach (var kv in cfg.Dialogs)
                {
                    string treeId = kv.Key;
                    var tree = kv.Value;
                    if (tree == null) { problems += Warn($"[DialogConfig] Tree '{treeId}': null."); continue; }
                    if (tree.Nodes == null || tree.Nodes.Count == 0)
                    { problems += Warn($"[DialogConfig] Tree '{treeId}': нет узлов."); continue; }
                    if (string.IsNullOrEmpty(tree.StartNode) || !tree.Nodes.ContainsKey(tree.StartNode))
                        problems += Warn($"[DialogConfig] Tree '{treeId}': StartNode='{tree.StartNode}' не найден.");

                    // Speaker/Title — не узлы, но там жила треть всех инлайновых фоллбэков.
                    problems += ValidateTags(treeId, "Speaker", tree.Speaker, pools);
                    problems += ValidateTags(treeId, "Title",   tree.Title,   pools);

                    foreach (var nkv in tree.Nodes)
                    {
                        var node = nkv.Value;
                        if (node == null)
                        { problems += Warn($"[DialogConfig] {treeId}/{nkv.Key}: null."); continue; }

                        bool hasText = !string.IsNullOrEmpty(node.Text);
                        bool hasReplies = node.Replies != null && node.Replies.Count > 0;
                        if (!hasText && !hasReplies)
                            problems += Warn($"[DialogConfig] {treeId}/{nkv.Key}: пустой узел (нет Text и Replies).");

                        problems += ValidateActions(treeId, nkv.Key, "OnEnter", node.OnEnter);

                        if (node.Replies != null)
                            for (int i = 0; i < node.Replies.Count; i++)
                            {
                                var r = node.Replies[i];
                                if (r == null) continue;
                                if (!string.IsNullOrEmpty(r.NextNode) && !tree.Nodes.ContainsKey(r.NextNode))
                                    problems += Warn($"[DialogConfig] {treeId}/{nkv.Key}.replies[{i}]: NextNode='{r.NextNode}' не найден.");
                                problems += ValidateCondition(treeId, $"{nkv.Key}.replies[{i}]", r.Condition);
                                problems += ValidateActions(treeId, $"{nkv.Key}.replies[{i}]", "OnEnter", r.OnEnter);
                                problems += ValidateActions(treeId, $"{nkv.Key}.replies[{i}]", "Actions", r.Actions);
                                problems += ValidateTags(treeId, $"{nkv.Key}.replies[{i}].Text", r.Text, pools);
                            }

                        problems += ValidateTags(treeId, $"{nkv.Key}.Text",    node.Text,    pools);
                        problems += ValidateTags(treeId, $"{nkv.Key}.Speaker", node.Speaker, pools);
                    }
                }
            }

            problems += ValidateGreetings(cfg);

            if (problems > 0)
                Debug.LogWarning($"[DialogConfig] Валидация завершена: {problems} проблема(ы).");
        }

        private static int ValidateActions(string treeId, string nodePath, string listName, List<DialogAction> actions)
        {
            if (actions == null) return 0;
            int p = 0;
            for (int i = 0; i < actions.Count; i++)
            {
                var a = actions[i];
                if (a == null || (string.IsNullOrEmpty(a.Method) && string.IsNullOrEmpty(a.Script)))
                { p += Warn($"[DialogConfig] {treeId}/{nodePath}.{listName}[{i}]: пусто и Method, и Script."); continue; }
                if (!string.IsNullOrEmpty(a.Script))
                    p += CheckLuaSyntax($"{treeId}/{nodePath}.{listName}[{i}] Script", a.Script);
            }
            return p;
        }

        /// <summary>Проверяет условие реплики. Для <c>lua:</c>-условий компилирует выражение —
        /// синтаксическая ошибка иначе всплыла бы только в рантайме, где условие даёт false
        /// и реплика просто молча исчезает из списка.</summary>
        private static int ValidateCondition(string treeId, string nodePath, string expr)
        {
            if (string.IsNullOrEmpty(expr)) return 0;

            if (expr.Length > 4 && expr.StartsWith("lua:", System.StringComparison.OrdinalIgnoreCase))
                return CheckLuaSyntax($"{treeId}/{nodePath} Condition",
                    "return (" + expr.Substring(4).TrimStart() + ")");

            try { DialogService.DefaultEvaluateCondition(null, expr); }
            catch (System.Exception e)
            { return Warn($"[DialogConfig] {treeId}/{nodePath} Condition '{expr}': {e.Message}"); }
            return 0;
        }

        /// <summary>Компилирует Lua-исходник, чтобы поймать синтаксис на загрузке. Побочный эффект
        /// полезен: скомпилированный чанк остаётся в кэше <see cref="SRG.Scripting.LuaHost"/>,
        /// поэтому первый показ узла не платит за компиляцию.</summary>
        private static int CheckLuaSyntax(string where, string source)
        {
            try { SRG.Scripting.LuaHost.Precompile(source); }
            catch (MoonSharp.Interpreter.SyntaxErrorException e)
            { return Warn($"[DialogConfig] {where}: синтаксис Lua — {e.DecoratedMessage ?? e.Message}"); }
            catch (System.Exception e)
            { return Warn($"[DialogConfig] {where}: не удалось скомпилировать Lua — {e.Message}"); }
            return 0;
        }

        private static int ValidateTags(string treeId, string where, string text,
            Dictionary<string, List<string>> pools)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int p = 0;

            // Инлайновая фраза в структурном конфиге — ровно то, ради чего заводились TagDefaults:
            // иначе один и тот же текст расползается по десяткам узлов. Ищем по началу '{tag|',
            // а не по TagPattern: у того фоллбэк — [^{}]*, и вложенные теги
            // ({pool:X|Атакую «{target}»}) он молча пропускает — как и сам Substitute,
            // из-за чего такой узел показывает игроку литерал со скобками.
            foreach (Match f in FallbackStart.Matches(text))
                p += Warn($"[DialogConfig] {treeId}/{where}: инлайновый фоллбэк '{f.Value}…'. " +
                          $"Перенеси фразу в TextsConfig.Dialog (TagDefaults/PoolDefaults) " +
                          $"и оставь только '{{{f.Groups[1].Value}}}'.");

            foreach (Match m in TagPattern.Matches(text))
            {
                string tag = m.Groups[1].Value;
                if (tag.StartsWith("pool:", System.StringComparison.Ordinal))
                    p += ValidatePoolRef(treeId, where, tag, pools);
            }
            return p;
        }

        /// <summary>Проверяет, что <c>{pool:Key}</c> к чему-то ведёт. Ключи с <c>&lt;…&gt;</c>
        /// (<c>Attack.&lt;ShipType&gt;Ok</c>) раскрываются в рантайме, поэтому для них требуем лишь
        /// одно из двух: хотя бы один подходящий пул по шаблону либо дефолт семейства в PoolDefaults.</summary>
        private static int ValidatePoolRef(string treeId, string where, string tag,
            Dictionary<string, List<string>> pools)
        {
            if (pools == null || pools.Count == 0) return 0;   // конфиг ещё не слинкован — не шумим

            string key = tag.Substring(5);
            int dot = key.IndexOf('.');
            string family = dot > 0 ? key.Substring(0, dot) : key;
            var poolDefaults = DialogTexts.Config?.PoolDefaults;
            // Дефолт может быть задан на сам шаблон или на всё семейство — см. DialogTexts.ResolveDefault.
            bool hasDefault = poolDefaults != null
                && (poolDefaults.ContainsKey(key) || poolDefaults.ContainsKey(family));

            if (key.IndexOf('<') >= 0)
            {
                if (hasDefault) return 0;
                // «Attack.<ShipType>Ok» → префикс «Attack.» + суффикс «Ok».
                int lt = key.IndexOf('<'), gt = key.IndexOf('>');
                if (gt <= lt) return 0;
                string prefix = key.Substring(0, lt), suffix = key.Substring(gt + 1);
                foreach (var k in pools.Keys)
                    if (k.Length > prefix.Length + suffix.Length &&
                        k.StartsWith(prefix, System.StringComparison.Ordinal) &&
                        k.EndsWith(suffix, System.StringComparison.Ordinal))
                        return 0;
                return Warn($"[DialogConfig] {treeId}/{where}: шаблон '{{{tag}}}' не совпал ни с одним пулом " +
                            $"('{prefix}*{suffix}') и не имеет PoolDefaults ни по шаблону, ни по '{family}'.");
            }

            if (pools.ContainsKey(key) || hasDefault) return 0;
            return Warn($"[DialogConfig] {treeId}/{where}: пул '{key}' не найден в TextsConfig.Dialog.Pools.");
        }

        private static int ValidateGreetings(DialogsConfig cfg)
        {
            int p = 0;
            p += ValidateGreetingRules("PlanetGreetings", cfg.PlanetGreetings?.Rules);
            p += ValidateGreetingRules("ShipGreetings",   cfg.ShipGreetings?.Rules);
            return p;
        }

        private static int ValidateGreetingRules(string scope,
            List<SRG.Dialog.PlanetGreetings.GreetingRule> rules)
        {
            if (rules == null) return 0;
            int p = 0;
            for (int i = 0; i < rules.Count; i++)
            {
                var r = rules[i];
                if (r == null) continue;
                if (string.IsNullOrEmpty(r.Id))
                {
                    // Без Id правило нечем связать с текстами — оно никогда ничего не скажет.
                    p += Warn($"[DialogConfig] {scope}[{i}]: нет Id — тексты из TextsConfig не подцепятся.");
                    continue;
                }
                if (r.Texts == null || r.Texts.Count == 0)
                    p += Warn($"[DialogConfig] {scope}[{i}] Id='{r.Id}': нет текстов — " +
                              $"добавь ключ '{r.Id}' в TextsConfig.Dialog.Greetings.");
            }
            return p;
        }

        private static int Warn(string msg) { Debug.LogWarning(msg); return 1; }
    }
}
