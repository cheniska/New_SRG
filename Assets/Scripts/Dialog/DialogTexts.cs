using System.Collections.Generic;
using SRG.Config;
using SRG.Dialog.PlanetGreetings;
using UnityEngine;
using SRG.Simulation;

namespace SRG.Dialog
{
    /// <summary>
    /// Мост между структурой диалогов (DialogsConfig.json) и фразами (TextsConfig.json → секция
    /// <c>"Dialog"</c>). Выполняет две задачи:
    /// <list type="bullet">
    ///   <item><b>Линковка на загрузке</b> (<see cref="Link"/>): пулы строк и тексты правил
    ///   приветствий переезжают из текст-конфига в те же поля, откуда их читал рантайм раньше
    ///   (<see cref="DialogsConfig.StringPools"/>, <c>GreetingRule.Texts</c>). Благодаря этому
    ///   селекторы/рендереры приветствий и <see cref="DialogService.PickFromPool"/> не изменились.</item>
    ///   <item><b>Дефолты тегов</b> (<see cref="ResolveDefault"/>): то, что раньше писалось
    ///   инлайном как <c>{tag|текст}</c> в каждом узле диалога.</item>
    /// </list>
    /// Инлайновые фоллбэки в DialogsConfig.json больше не используются; парсер их всё ещё понимает
    /// (обратная совместимость с чужими конфигами), но <see cref="DialogConfigValidator"/> варнит
    /// на каждый — иначе дубли фраз расползаются по структурному конфигу заново.
    /// </summary>
    public static class DialogTexts
    {
        private static DialogTextsConfig _cfg = new();

        // Маски из TagDefaults (ключи с '*'), разложенные на префикс/суффикс — чтобы не гонять
        // регексы на каждый неразрешённый тег. Обычно 1–2 записи (nav_star_*_dist).
        private static readonly List<(string prefix, string suffix, string value)> _masks = new();

        /// <summary>Активная секция текстов. Никогда не null — до <see cref="Link"/> пустая.</summary>
        public static DialogTextsConfig Config => _cfg;

        /// <summary>Связать структурный конфиг диалогов с текстовым. Вызывается после того, как
        /// оба файла распарсены (см. GalaxyManager.EnsureContextInitialized), до валидации.</summary>
        public static void Link(DialogsConfig dialogs, TextConfig texts)
        {
            _cfg = texts?.Dialog ?? new DialogTextsConfig();
            RebuildMasks();

            if (dialogs == null) return;
            LinkPools(dialogs);
            LinkGreetings("PlanetGreetings", dialogs.PlanetGreetings?.Rules);
            LinkGreetings("ShipGreetings",   dialogs.ShipGreetings?.Rules);
        }

        private static void RebuildMasks()
        {
            _masks.Clear();
            if (_cfg.TagDefaults == null) return;
            foreach (var kv in _cfg.TagDefaults)
            {
                int star = kv.Key?.IndexOf('*') ?? -1;
                if (star < 0) continue;
                _masks.Add((kv.Key.Substring(0, star), kv.Key.Substring(star + 1), kv.Value));
            }
        }

        private static void LinkPools(DialogsConfig dialogs)
        {
            if (_cfg.Pools == null || _cfg.Pools.Count == 0) return;
            dialogs.StringPools ??= new Dictionary<string, List<string>>();
            int overridden = 0;
            foreach (var kv in _cfg.Pools)
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Value == null) continue;
                if (dialogs.StringPools.ContainsKey(kv.Key)) overridden++;
                dialogs.StringPools[kv.Key] = kv.Value;
            }
            if (overridden > 0)
                Debug.LogWarning($"[DialogTexts] {overridden} пул(ов) заданы и в DialogsConfig.StringPools, " +
                                 "и в TextsConfig.Dialog.Pools — взяты значения из TextsConfig. " +
                                 "Удали дубли из DialogsConfig.json.");
        }

        private static void LinkGreetings(string scope, List<GreetingRule> rules)
        {
            if (rules == null || _cfg.Greetings == null) return;
            int bound = 0, inlineKept = 0;
            foreach (var rule in rules)
            {
                if (rule == null || string.IsNullOrEmpty(rule.Id)) continue;
                if (!_cfg.Greetings.TryGetValue(rule.Id, out var list) || list == null || list.Count == 0)
                {
                    // Правило без текстов в текст-конфиге: инлайновые Texts (legacy/моды) оставляем как есть.
                    if (rule.Texts != null && rule.Texts.Count > 0) inlineKept++;
                    continue;
                }
                rule.Texts = list;
                bound++;
            }
            if (inlineKept > 0)
                Debug.LogWarning($"[DialogTexts] {scope}: {inlineKept} правил(о) держат тексты инлайном — " +
                                 $"перенеси их в TextsConfig.Dialog.Greetings по Id.");
            if (bound == 0 && rules.Count > 0)
                Debug.LogWarning($"[DialogTexts] {scope}: ни одно из {rules.Count} правил не получило текстов " +
                                 "из TextsConfig.Dialog.Greetings — проверь, что ключи совпадают с Id правил.");
        }

        /// <summary>Значение тега по умолчанию, когда резолвер вернул пусто.
        /// Порядок: имя говорящего (<c>speaker_name</c> → Speakers[dialogId]) → точный ключ
        /// в TagDefaults → семейство пула в PoolDefaults → маска в TagDefaults.
        /// <br/>
        /// Возвращает <c>null</c>, если дефолта нет — тогда <see cref="DialogService.Substitute"/>
        /// оставляет в тексте видимый <c>{tag}</c>. Пустая строка — валидный дефолт
        /// и означает «подставить ничего», поэтому проверять результат надо на null, а не на пустоту.</summary>
        public static string ResolveDefault(string tag, DialogContext ctx)
        {
            if (string.IsNullOrEmpty(tag)) return null;

            if (tag == "speaker_name")
            {
                var speaker = ResolveSpeaker(ctx);
                if (speaker != null) return speaker;
            }

            if (_cfg.TagDefaults != null && _cfg.TagDefaults.TryGetValue(tag, out var exact))
                return exact;

            // {pool:Attack.<ShipType>Fear} → сначала сам шаблон, затем семейство "Attack".
            // Шаблон точнее: внутри одного семейства у Fear/HaveBusiness/Suspect разные фразы.
            if (tag.StartsWith("pool:", System.StringComparison.Ordinal) && _cfg.PoolDefaults != null)
            {
                string key = tag.Substring(5);
                if (_cfg.PoolDefaults.TryGetValue(key, out var exactPool)) return exactPool;
                int dot = key.IndexOf('.');
                if (dot > 0 && _cfg.PoolDefaults.TryGetValue(key.Substring(0, dot), out var famDef))
                    return famDef;
            }

            for (int i = 0; i < _masks.Count; i++)
            {
                var (prefix, suffix, value) = _masks[i];
                if (tag.Length >= prefix.Length + suffix.Length &&
                    tag.StartsWith(prefix, System.StringComparison.Ordinal) &&
                    tag.EndsWith(suffix, System.StringComparison.Ordinal))
                    return value;
            }

            return null;
        }

        /// <summary>Фраза для кода — подпись кнопки, пометка в списке, заголовок-заглушка.
        /// Единственный способ получить в C# текст, который увидит игрок: строковых литералов
        /// с фразами в коде диалогов быть не должно.
        /// <br/>
        /// Пустой результат при отсутствующем ключе намеренный — литерал-«подстраховка» на
        /// русском пережил бы любую локализацию и молча вернулся бы игроку. Пропажу видно в логе.</summary>
        public static string Phrase(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (_cfg.Phrases != null && _cfg.Phrases.TryGetValue(key, out var v)) return v ?? "";
            WarnOnce(_missingPhrases, key,
                $"[DialogTexts] Нет фразы '{key}' в TextsConfig.Dialog.Phrases — показана пустая строка.");
            return "";
        }

        /// <summary>Нейтральное значение плейсхолдера приветствия (<c>Player</c> → «капитан»).
        /// См. <see cref="DialogTextsConfig.GreetingDefaults"/>.</summary>
        public static string GreetingDefault(string placeholder)
        {
            if (string.IsNullOrEmpty(placeholder)) return "";
            if (_cfg.GreetingDefaults != null && _cfg.GreetingDefaults.TryGetValue(placeholder, out var v))
                return v ?? "";
            WarnOnce(_missingGreetingDefaults, placeholder,
                $"[DialogTexts] Нет нейтрального значения '<{placeholder}>' в " +
                "TextsConfig.Dialog.GreetingDefaults — в приветствии будет пустое место.");
            return "";
        }

        /// <summary>Все известные нейтральные значения — для засева карты подстановок приветствия.</summary>
        public static Dictionary<string, string> GreetingDefaultsMap
            => _cfg.GreetingDefaults ?? new Dictionary<string, string>();

        // Ругаемся на каждый отсутствующий ключ по одному разу: фразы запрашиваются на каждый
        // рендер узла, и без этого лог утонул бы в повторах одной и той же строки.
        private static readonly HashSet<string> _missingPhrases = new();
        private static readonly HashSet<string> _missingGreetingDefaults = new();

        private static void WarnOnce(HashSet<string> seen, string key, string message)
        {
            if (!seen.Add(key)) return;
            Debug.LogWarning(message);
        }

        /// <summary>Имя говорящего по умолчанию для активного дерева диалога.
        /// Учитывает <c>AliasOf</c>: алиас наследует говорящего оригинала, если своего не задано.</summary>
        private static string ResolveSpeaker(DialogContext ctx)
        {
            string id = ctx?.DialogId;
            if (string.IsNullOrEmpty(id) || _cfg.Speakers == null) return null;
            if (_cfg.Speakers.TryGetValue(id, out var direct)) return direct;

            var dialogs = GameWorld.Context?.Config?.Dialogs?.Dialogs;
            if (dialogs != null && dialogs.TryGetValue(id, out var tree) && !string.IsNullOrEmpty(tree?.AliasOf)
                && _cfg.Speakers.TryGetValue(tree.AliasOf, out var viaAlias))
                return viaAlias;

            return null;
        }
    }
}
