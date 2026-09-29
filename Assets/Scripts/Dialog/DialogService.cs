using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using SRG.Core;
using SRG.Galaxy;
using SRG.Scripting;
using SRG.Ships.Player;

namespace SRG.Dialog
{
    /// <summary>Глобальный контекст диалога — влияет на выбор dialogId
    /// (<see cref="DialogService.ResolveShipDialogId(ShipData, DialogScope)"/>) и на условия
    /// реплик (<c>scope == "Gov"</c>). Всего два значения:
    /// <br/><br/>
    /// • <see cref="Space"/> — радио-контакт из космоса (клик по чужому кораблю/станции).
    /// • <see cref="Gov"/> — административный диалог: правительство планеты, командование
    ///   станции, капитанский мостик линкора (общее по смыслу — «власть на месте»).
    /// </summary>
    public enum DialogScope { Space, Gov }

    /// <summary>Чья реплика рендерится: текст узла говорит NPC, текст ответа — игрок.
    /// От этого зависит, в кого целятся плейсхолдеры-обращения из пулов.</summary>
    public enum DialogVoice { Npc, Player }

    // Состояние одного активного диалога. Передаётся в обработчики действий —
    // им доступен игрок, цель (планета или корабль) и словарь Data для произвольных переменных.
    public class DialogContext
    {
        public DialogScope Scope;
        public string      DialogId;
        public DialogTree  Tree;
        public string      CurrentNodeId;
        public ShipData    PlayerShip;
        public PlanetData  TargetPlanet;
        public ShipData    TargetShip;
        public Dictionary<string, string> Data = new();
        public bool ShouldClose;
    }

    // Центральный runtime диалогов: реестр действий, навигация по узлам, события.
    // Запуск:  DialogService.Start("dialog_id", DialogScope.Gov, player, planet: p);
    // Реестр:  DialogService.RegisterAction("Method", (ctx, args) => { ... });
    public static class DialogService
    {
        public delegate void DialogActionHandler(DialogContext ctx, IList<string> args);
        public delegate bool DialogConditionEvaluator(DialogContext ctx, string expr);
        /// <summary>Резолвер тега подстановки: возвращает строковое значение для шаблона
        /// вида <c>{tag_name}</c> в текстах диалога. Может вернуть null/пусто.</summary>
        public delegate string DialogTagResolver(DialogContext ctx);
        /// <summary>Резолвер параметризованного тега <c>{prefix:arg}</c> — arg передаётся as-is
        /// (может содержать вложенные <c>&lt;…&gt;</c>-плейсхолдеры, которые резолвер сам должен раскрыть).</summary>
        public delegate string DialogParamTagResolver(DialogContext ctx, string arg);

        private static readonly Dictionary<string, DialogActionHandler> _actions =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DialogTagResolver> _tags =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DialogParamTagResolver> _paramTags =
            new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Имена тегов и префиксы param-тегов, которые нельзя мемоизировать: их значение
        /// меняется от вызова к вызову намеренно (<c>{pool:…}</c> выбирает случайный вариант, и два
        /// упоминания в одном узле обязаны дать разные фразы).</summary>
        private static readonly HashSet<string> _volatileTags =
            new(StringComparer.OrdinalIgnoreCase);
        private static DialogConditionEvaluator _conditionEvaluator;
        private static DialogContext _active;

        // ── Memo-кэш резолва тегов на один проход рендера ──────────────────────────
        //
        // Рендер узла резолвит теги многократно: GetVisibleReplies гоняет Condition по всем
        // репликам, ResolveNodeText — по TextVariants, Substitute — по тексту узла и по каждой
        // подписи кнопки. В Ship_default/intro 15 реплик, и условия там опираются на теги,
        // за которыми стоят сервисные Preview (can_trade, can_extort, needs_truce, can_protect,
        // can_rob_cargo, can_preserve…). Часть тегов встречается в двух условиях сразу
        // (is_hireable, player_is_pirate, is_partner_of_player) — без кэша это буквально
        // повторный вызов того же Preview в одном и том же проходе.
        //
        // Кэш живёт от одной мутации состояния до другой: сбрасывается на смене узла/контекста
        // и вокруг каждого действия (действие пишет ctx.Data, от которого теги и зависят).
        // Внутри самого прохода рендера действий не выполняется, поэтому значения стабильны.
        private static readonly Dictionary<string, string> _tagMemo =
            new(StringComparer.OrdinalIgnoreCase);
        private static DialogContext _memoCtx;
        private static bool _memoValid;

        /// <summary>Сбросить memo-кэш тегов. Вызывается автоматически при смене узла/контекста и
        /// вокруг выполнения действий; вручную нужен только коду, который меняет мир в обход
        /// действий диалога и хочет, чтобы открытый узел это сразу увидел.</summary>
        public static void InvalidateTagCache()
        {
            if (_tagMemo.Count > 0) _tagMemo.Clear();
            _memoValid = false;
            _memoCtx = null;
        }

        // Стек «вызывающих» диалогов. Push сохраняет туда текущий контекст, End автоматически
        // возвращается к нему после закрытия дочернего. Позволяет ветвиться в поддиалог
        // (магазин, квест) и вернуться в исходное меню без ручного пробрасывания состояния.
        private static readonly Stack<DialogContext> _stack = new();
        public static int StackDepth => _stack.Count;

        // {name} или {name|fallback}. Имя допускает . : < > (для param-тегов вроде {pool:Attack.<ShipType>Ok}).
        private static readonly Regex _tagPattern = new(@"\{([A-Za-z0-9_.:<>]+)(?:\|([^{}]*))?\}", RegexOptions.Compiled);

        public static DialogContext Active => _active;
        public static bool IsActive => _active != null;

        public static event Action<DialogContext> OnDialogStarted;
        public static event Action<DialogContext> OnDialogNodeChanged;
        public static event Action<DialogContext> OnDialogEnded;

        public static void RegisterAction(string name, DialogActionHandler handler)
        {
            if (string.IsNullOrEmpty(name) || handler == null) return;
            _actions[name] = handler;
        }

        /// <summary>Хук переустановки штатных модулей (<c>DialogModules</c>). Ставится при первой
        /// установке; <see cref="Reset"/> дёргает его, чтобы очищенные реестры немедленно
        /// наполнились заново.
        /// <br/>
        /// Без этого Reset был ловушкой: он чистил _actions/_tags, но статические флаги
        /// «уже зарегистрировано» в модулях оставались взведёнными, повторный вызов регистрации
        /// выходил по первой строке — и все действия с тегами исчезали навсегда, а каждый диалог
        /// становился пустым.</summary>
        internal static Action ModuleReinstaller;

        /// <summary>Сбрасывает реестры actions/tags/conditions и закрывает активный диалог, после
        /// чего заново ставит штатные модули (см. <see cref="ModuleReinstaller"/>). Пользовательские
        /// регистрации, сделанные в обход <c>DialogModules</c>, при этом теряются — их владелец
        /// должен переустановить их сам.
        /// Использовать при выгрузке сцены / hot-reload скриптов, чтобы не тащить старые лямбды.</summary>
        public static void Reset()
        {
            _stack.Clear();
            if (_active != null) End();
            _actions.Clear();
            _tags.Clear();
            _paramTags.Clear();
            _volatileTags.Clear();
            _conditionEvaluator = null;
            InvalidateTagCache();
            ModuleReinstaller?.Invoke();
        }

        public static void UnregisterAction(string name)
        {
            if (!string.IsNullOrEmpty(name)) _actions.Remove(name);
        }

        public static void UnregisterTag(string name)
        {
            if (!string.IsNullOrEmpty(name)) _tags.Remove(name);
        }

        public static void UnregisterParamTag(string prefix)
        {
            if (!string.IsNullOrEmpty(prefix)) _paramTags.Remove(prefix);
        }

        /// <summary>Зарегистрировать тег подстановки. Вызывать из user-скриптов (Start/Awake) после
        /// инициализации сцены. Регистронезависимо. Перезапись по имени допустима.
        /// <br/>
        /// <paramref name="isVolatile"/> — тег намеренно недетерминирован (случайный выбор фразы) и
        /// не должен мемоизироваться в пределах прохода рендера.</summary>
        public static void RegisterTag(string name, DialogTagResolver resolver, bool isVolatile = false)
        {
            if (string.IsNullOrEmpty(name) || resolver == null) return;
            _tags[name] = resolver;
            MarkVolatile(name, isVolatile);
        }

        /// <summary>Зарегистрировать префикс-тег вида <c>{prefix:arg}</c>. Резолвер получает arg
        /// как строку — например, <c>Attack.&lt;ShipType&gt;Ok</c>.
        /// <paramref name="isVolatile"/> — см. <see cref="RegisterTag"/>.</summary>
        public static void RegisterParamTag(string prefix, DialogParamTagResolver resolver, bool isVolatile = false)
        {
            if (string.IsNullOrEmpty(prefix) || resolver == null) return;
            _paramTags[prefix] = resolver;
            MarkVolatile(prefix, isVolatile);
        }

        /// <summary>Тег-обёртка над <c>ctx.Data[key]</c> — самая частая форма тега: значение кладёт
        /// действие, а тег его показывает. Заменяет полтора десятка одинаковых лямбд
        /// <c>ctx?.Data != null &amp;&amp; ctx.Data.TryGetValue(KEY, out var v) ? v : "0"</c>.</summary>
        /// <param name="fallback">Что вернуть, пока действие ничего не положило. Пустая строка
        /// пропускает ход к дефолту из TextsConfig; "0"/"" — исторические значения этих тегов.</param>
        public static void RegisterDataTag(string tag, string dataKey, string fallback = "")
        {
            if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(dataKey)) return;
            RegisterTag(tag, ctx =>
                ctx?.Data != null && ctx.Data.TryGetValue(dataKey, out var v) ? v : fallback);
        }

        private static void MarkVolatile(string key, bool isVolatile)
        {
            if (isVolatile) _volatileTags.Add(key);
            else            _volatileTags.Remove(key);
        }

        /// <summary>Возвращает текущее строковое значение тега в контексте активного диалога,
        /// либо null, если тег не зарегистрирован. Результат мемоизируется до ближайшей мутации
        /// состояния (см. <see cref="InvalidateTagCache"/>), кроме тегов, помеченных volatile.</summary>
        public static string ResolveTag(string name, DialogContext ctx = null)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var bound = ctx ?? _active;

            // Кэш привязан к конкретному контексту: при ветвлении в поддиалог (Push) у нового
            // ctx свой Data, и значения прежнего к нему не относятся.
            if (_memoValid && !ReferenceEquals(bound, _memoCtx)) InvalidateTagCache();

            bool cacheable = !IsVolatile(name);
            if (cacheable && _memoValid && _tagMemo.TryGetValue(name, out var memo)) return memo;

            string value = ResolveTagUncached(name, bound);

            if (cacheable)
            {
                if (!_memoValid) { _memoCtx = bound; _memoValid = true; }
                _tagMemo[name] = value;
            }
            return value;
        }

        private static bool IsVolatile(string name)
        {
            if (_volatileTags.Count == 0) return false;
            if (_volatileTags.Contains(name)) return true;
            int colon = name.IndexOf(':');
            return colon > 0 && _volatileTags.Contains(name[..colon]);
        }

        private static string ResolveTagUncached(string name, DialogContext bound)
        {
            if (_tags.TryGetValue(name, out var resolver))
            {
                try { return resolver(bound); }
                catch (Exception e) { Debug.LogError($"[DialogService] Tag '{name}' resolver упал: {e.Message}"); return null; }
            }
            int colon = name.IndexOf(':');
            if (colon > 0 && _paramTags.TryGetValue(name[..colon], out var pResolver))
            {
                try { return pResolver(bound, name[(colon + 1)..]); }
                catch (Exception e) { Debug.LogError($"[DialogService] ParamTag '{name}' resolver упал: {e.Message}"); return null; }
            }
            return null;
        }

        /// <summary>Подставляет все <c>{tag}</c> в тексте. Если тег не зарегистрирован или вернул
        /// пустую строку — берётся дефолт из TextsConfig.Dialog (<see cref="DialogTexts.ResolveDefault"/>),
        /// иначе в тексте остаётся видимый <c>{tag}</c>, чтобы дырка не прошла незамеченной.
        /// <br/>
        /// Инлайновый <c>{tag|fallback}</c> — legacy: синтаксис ещё понимается ради чужих конфигов,
        /// но в наших JSON-ах фраз быть не должно (валидатор варнит). Приоритет у TextsConfig.</summary>
        public static string Substitute(string text, DialogContext ctx = null)
            => Substitute(text, ctx, DialogVoice.Npc);

        /// <summary>Подстановка с указанием, чья это реплика. Плейсхолдеры в строках пулов
        /// относительны говорящему: <c>&lt;Ranger&gt;</c> в реплике NPC («Ты не похож на лидера,
        /// &lt;Ranger&gt;») — это игрок, а в реплике игрока («Иди под мое командование,
        /// &lt;Ranger&gt;») — собеседник. Без этого имя подставлялось не того лица.</summary>
        public static string Substitute(string text, DialogContext ctx, DialogVoice voice)
        {
            var prev = CurrentVoice;
            CurrentVoice = voice;
            try { return Substitute(text, ctx ?? _active, 0); }
            finally { CurrentVoice = prev; }
        }

        /// <summary>Чья реплика подставляется прямо сейчас. Читает резолвер тега <c>pool</c>
        /// (см. <see cref="DialogActions"/>), чтобы выбрать сторону для <c>&lt;Player&gt;</c>/
        /// <c>&lt;Ranger&gt;</c>. Подстановка синхронна и однопоточна, поэтому статики достаточно.</summary>
        public static DialogVoice CurrentVoice { get; private set; } = DialogVoice.Npc;

        // Дефолты тегов сами содержат теги («Получил свои {extort_fee} кр.»), поэтому результат
        // подстановки прогоняется ещё раз. Глубина ограничена: тег, который резолвится в себя,
        // иначе зациклил бы рендер узла.
        private const int MaxSubstituteDepth = 4;

        private static string Substitute(string text, DialogContext bound, int depth)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;
            return _tagPattern.Replace(text, m =>
            {
                string name = m.Groups[1].Value;
                string value = ResolveTag(name, bound);
                if (string.IsNullOrEmpty(value))
                {
                    // null → дефолта нет; "" → дефолт есть и означает «подставить ничего».
                    value = DialogTexts.ResolveDefault(name, bound)
                            ?? (m.Groups[2].Success ? m.Groups[2].Value : m.Value);
                    // Шаблон оставили как есть — вложенных тегов в нём нет, рекурсия только зациклит.
                    if (value == m.Value) return value;
                }
                return depth < MaxSubstituteDepth ? Substitute(value, bound, depth + 1) : value;
            });
        }

        public static void SetConditionEvaluator(DialogConditionEvaluator eval) => _conditionEvaluator = eval;

        public static bool Start(
            string dialogId, DialogScope scope, ShipData playerShip,
            PlanetData planet = null, ShipData targetShip = null)
        {
            var cfg = GalaxyManager.Instance?.Context?.Config?.Dialogs;
            if (cfg?.Dialogs == null || !cfg.Dialogs.TryGetValue(dialogId, out var tree))
            {
                Debug.LogWarning($"[DialogService] Диалог '{dialogId}' не найден в конфиге.");
                return false;
            }
            if (tree.Nodes == null || string.IsNullOrEmpty(tree.StartNode)
                || !tree.Nodes.ContainsKey(tree.StartNode))
            {
                Debug.LogWarning($"[DialogService] Диалог '{dialogId}' имеет некорректный StartNode.");
                return false;
            }

            if (_active != null)
            {
                Debug.LogWarning($"[DialogService] Start('{dialogId}') вызван при активном диалоге '{_active.DialogId}'; старый закрыт.");
                End();
            }

            _active = new DialogContext
            {
                Scope        = scope,
                DialogId     = dialogId,
                Tree         = tree,
                PlayerShip   = playerShip,
                TargetPlanet = planet,
                TargetShip   = targetShip,
            };
            InvalidateTagCache();
            OnDialogStarted?.Invoke(_active);
            EnterNode(tree.StartNode);
            return true;
        }

        public static void End()
        {
            if (_active == null) return;
            var ended = _active;
            _active = null;
            InvalidateTagCache();
            OnDialogEnded?.Invoke(ended);

            // Стек не пуст — возвращаемся к вызывающему диалогу и уведомляем UI.
            if (_stack.Count > 0)
            {
                _active = _stack.Pop();
                InvalidateTagCache();
                OnDialogStarted?.Invoke(_active);
                OnDialogNodeChanged?.Invoke(_active);
            }
        }

        /// <summary>
        /// Полностью закрыть весь стек диалогов. Использовать, когда контент явно хочет
        /// оборвать всю цепочку (например, «улететь с планеты» из глубоко вложенного меню).
        /// </summary>
        public static void EndAll()
        {
            _stack.Clear();
            if (_active != null)
            {
                var ended = _active;
                _active = null;
                InvalidateTagCache();
                OnDialogEnded?.Invoke(ended);
            }
        }

        /// <summary>
        /// Открыть <paramref name="dialogId"/> как поддиалог: текущий контекст сохраняется в стек,
        /// после End() автоматически восстанавливается. player/planet/target наследуются из
        /// текущего активного, если не переданы явно. Возвращает false, если dialogId не найден
        /// (в этом случае родитель не тронут).
        /// </summary>
        public static bool Push(
            string dialogId, DialogScope? scope = null,
            ShipData playerShip = null, PlanetData planet = null, ShipData targetShip = null)
        {
            var cfg = GalaxyManager.Instance?.Context?.Config?.Dialogs;
            if (cfg?.Dialogs == null || !cfg.Dialogs.TryGetValue(dialogId, out var tree))
            {
                Debug.LogWarning($"[DialogService] Push('{dialogId}') — диалог не найден.");
                return false;
            }
            if (tree.Nodes == null || string.IsNullOrEmpty(tree.StartNode)
                || !tree.Nodes.ContainsKey(tree.StartNode))
            {
                Debug.LogWarning($"[DialogService] Push('{dialogId}') — некорректный StartNode.");
                return false;
            }

            var parent = _active;
            if (parent != null) _stack.Push(parent);

            _active = new DialogContext
            {
                Scope        = scope        ?? parent?.Scope        ?? DialogScope.Space,
                DialogId     = dialogId,
                Tree         = tree,
                PlayerShip   = playerShip   ?? parent?.PlayerShip,
                TargetPlanet = planet       ?? parent?.TargetPlanet,
                TargetShip   = targetShip   ?? parent?.TargetShip,
            };
            InvalidateTagCache();
            OnDialogStarted?.Invoke(_active);
            EnterNode(tree.StartNode);
            return true;
        }

        /// <summary>Закрыть текущий диалог. Если он был открыт через <see cref="Push"/>, управление
        /// возвращается вызывающему; иначе эквивалентно <see cref="End"/>.</summary>
        public static void Pop()
        {
            if (_active == null) return;
            _active.ShouldClose = true;
            End();
        }

        /// <summary>Подбор dialogId для корабля/станции с учётом контекста разговора.
        /// Станции (<see cref="ShipData.IsStation"/>) уходят в отдельный резолвер
        /// (<see cref="ResolveStationDialogId"/>). Для обычных кораблей fallback-цепочка:
        /// UID → Ship_{ShipTypeId}_{suffix} → Ship_{ShipTypeId}_default → Ship_{Owner}_{suffix}
        /// → Ship_{Owner}_default → Ship_{Race}_{suffix} → Ship_{Race}_default → Ship_{suffix}
        /// → Ship_default. Суффикс: <see cref="DialogScope.Space"/> → "_space" (радио),
        /// <see cref="DialogScope.Gov"/> → "_gov" (капитанский мостик). При отсутствии
        /// scope-специфичного ключа используется общий "_default".</summary>
        public static string ResolveShipDialogId(ShipData ship, DialogScope scope)
        {
            var dialogs = GalaxyManager.Instance?.Context?.Config?.Dialogs?.Dialogs;
            if (dialogs == null || dialogs.Count == 0 || ship == null) return null;
            if (ship.IsStation) return ResolveStationDialogId(ship, scope);

            string suffix = ScopeSuffix(scope);
            string cluster = ResolveDialogCluster(ship?.Owner);
            // Каждый шаг даёт до двух кандидатов: сначала scope-специфичный (если есть),
            // потом общий _default. Так один и тот же корабль может иметь отдельные тексты
            // «в космосе» и «на мостике», не дублируя весь fallback.
            var candidates = new List<string>(14) { ship.Uid };
            AddScopeCandidates(candidates, "Ship_" + ship.ShipTypeId, suffix);
            AddScopeCandidates(candidates, "Ship_" + ship.Owner,      suffix);
            AddScopeCandidates(candidates, "Ship_" + ship.Race,       suffix);
            if (!string.Equals(cluster, ship.Owner, StringComparison.Ordinal))
                AddScopeCandidates(candidates, "Ship_" + cluster,     suffix);
            AddScopeCandidates(candidates, "Ship",                    suffix);
            for (int i = 0; i < candidates.Count; i++)
                if (!string.IsNullOrEmpty(candidates[i]) && dialogs.ContainsKey(candidates[i])) return ResolveAlias(candidates[i], dialogs);
            return null;
        }

        /// <summary>Раскрытие псевдонима <see cref="DialogTree.AliasOf"/>. Если запись
        /// <paramref name="id"/> — алиас, возвращает id целевого дерева (с защитой от циклов
        /// глубиной до 8). Иначе возвращает исходный id.</summary>
        public static string ResolveAlias(string id, IReadOnlyDictionary<string, DialogTree> dialogs)
        {
            if (string.IsNullOrEmpty(id) || dialogs == null) return id;
            for (int i = 0; i < 8; i++)
            {
                if (!dialogs.TryGetValue(id, out var tree) || tree == null) return id;
                if (string.IsNullOrEmpty(tree.AliasOf)) return id;
                if (string.Equals(tree.AliasOf, id, StringComparison.Ordinal)) return id;
                id = tree.AliasOf;
            }
            return id;
        }

        /// <summary>Кластер диалогов для Owner-стороны. Берёт <see cref="Config.OwnerConfig.DialogCluster"/>
        /// из GalaxyConfig; при отсутствии — сам Owner-id. Пустой owner → null.</summary>
        public static string ResolveDialogCluster(string owner)
        {
            if (string.IsNullOrEmpty(owner)) return null;
            var owners = GalaxyManager.Instance?.Context?.Config?.Ships?.Owners;
            if (owners != null && owners.TryGetValue(owner, out var oc)
                && !string.IsNullOrEmpty(oc?.DialogCluster))
                return oc.DialogCluster;
            return owner;
        }

        /// <summary>Legacy-обёртка (без scope) — эквивалент Space-контекста.</summary>
        public static string ResolveShipDialogId(ShipData ship) => ResolveShipDialogId(ship, DialogScope.Space);

        // Отдельный RNG для пулов — независимый от игровой симуляции; параллельные вызовы дают
        // разные варианты. Тот же поток, что в DialogActions._poolRng — здесь дублируется, чтобы
        // NpcConversationLog не тянул зависимости от DialogActions.
        private static readonly System.Random _stringPoolRng = new();

        /// <summary>Случайная строка из именованного пула <see cref="DialogsConfig.StringPools"/>.
        /// Плейсхолдеры <c>&lt;Player&gt;</c>/<c>&lt;Ship&gt;</c>/<c>&lt;Money&gt;</c> и т.п. НЕ подставляются —
        /// сервис возвращает строку как есть. Для подстановки использовать <see cref="Substitute"/>
        /// с DialogContext либо ручную замену.
        /// null → пул не найден или пуст (fallback за вызывающим).</summary>
        public static string PickFromPool(string poolKey)
        {
            if (string.IsNullOrEmpty(poolKey)) return null;
            var pools = GalaxyManager.Instance?.Context?.Config?.Dialogs?.StringPools;
            if (pools == null || !pools.TryGetValue(poolKey, out var list) || list == null || list.Count == 0)
                return null;
            return list[_stringPoolRng.Next(list.Count)];
        }

        /// <summary>Резолвер dialogId для входящего вызова агрессора к игроку.
        /// Возвращает id первого найденного диалога по цепочке с суффиксом <c>_incoming_{kind}</c>
        /// (Rob/Extort/Ceasefire/OfferMoney):
        /// UID → Ship_{ShipTypeId}_incoming_{kind} → Ship_{Owner}_incoming_{kind} →
        /// Ship_{Race}_incoming_{kind} → Ship_incoming_{kind}. Если не найдено ничего —
        /// null (AI-инициатор должен отказаться от диалога и перейти к обычной механике).</summary>
        public static string ResolveIncomingDialogId(ShipData initiator, string kind)
        {
            var dialogs = GalaxyManager.Instance?.Context?.Config?.Dialogs?.Dialogs;
            if (dialogs == null || dialogs.Count == 0 || initiator == null || string.IsNullOrEmpty(kind)) return null;
            string suffix = "_incoming_" + kind;
            string cluster = ResolveDialogCluster(initiator.Owner);
            var candidates = new List<string>(8) { initiator.Uid };
            if (!string.IsNullOrEmpty(initiator.ShipTypeId)) candidates.Add("Ship_" + initiator.ShipTypeId + suffix);
            if (!string.IsNullOrEmpty(initiator.Owner))      candidates.Add("Ship_" + initiator.Owner      + suffix);
            if (!string.IsNullOrEmpty(initiator.Race))       candidates.Add("Ship_" + initiator.Race       + suffix);
            if (!string.IsNullOrEmpty(cluster) && !string.Equals(cluster, initiator.Owner, StringComparison.Ordinal))
                candidates.Add("Ship_" + cluster + suffix);
            candidates.Add("Ship" + suffix);
            for (int i = 0; i < candidates.Count; i++)
                if (!string.IsNullOrEmpty(candidates[i]) && dialogs.ContainsKey(candidates[i])) return ResolveAlias(candidates[i], dialogs);
            return null;
        }

        /// <summary>Подбор dialogId для космической станции (<see cref="ShipData.IsStation"/>).
        /// Отдельная цепочка от обычных кораблей: конфиг станций живёт под ключами
        /// <c>Station_*</c> (по HullType корпуса, расе, дефолту). Fallback-цепочка:
        /// UID → Station_{HullType}_{suffix} → Station_{HullType}_default →
        /// Station_{Race}_{suffix} → Station_{Race}_default → Station_{suffix} → Station_default →
        /// (в самом конце) Ship_default, чтобы старые конфиги без Station_-ключей не падали.
        /// Суффикс: <see cref="DialogScope.Space"/> → "_space" (радио), <see cref="DialogScope.Gov"/>
        /// → "_gov" (командование станции).</summary>
        public static string ResolveStationDialogId(ShipData station, DialogScope scope)
        {
            var dialogs = GalaxyManager.Instance?.Context?.Config?.Dialogs?.Dialogs;
            if (dialogs == null || dialogs.Count == 0 || station == null) return null;

            string hullType = station.GetHullItem()?.GetParamString("HullType");
            string suffix = ScopeSuffix(scope);
            string cluster = ResolveDialogCluster(station.Owner);
            var candidates = new List<string>(14) { station.Uid };
            AddScopeCandidates(candidates, "Station_" + hullType,      suffix);
            AddScopeCandidates(candidates, "Station_" + station.Owner, suffix);
            AddScopeCandidates(candidates, "Station_" + station.Race,  suffix);
            if (!string.Equals(cluster, station.Owner, StringComparison.Ordinal))
                AddScopeCandidates(candidates, "Station_" + cluster,   suffix);
            AddScopeCandidates(candidates, "Station",                  suffix);
            // Последний рубеж — общий Ship_default, чтобы полностью пустой Station_-конфиг
            // не оставил станцию без единой реплики.
            candidates.Add("Ship_default");
            for (int i = 0; i < candidates.Count; i++)
                if (!string.IsNullOrEmpty(candidates[i]) && dialogs.ContainsKey(candidates[i])) return ResolveAlias(candidates[i], dialogs);
            return null;
        }

        /// <summary>Формат scope-суффикса: Space → "_space", Gov → "_gov". Резолверы всегда
        /// пробуют сначала scope-суффикс, затем «_default» — так один ключ может обслуживать
        /// оба контекста (fallback), либо иметь отдельные варианты для радио и правительства.</summary>
        private static string ScopeSuffix(DialogScope scope) => scope switch
        {
            DialogScope.Space => "_space",
            DialogScope.Gov   => "_gov",
            _                 => "",
        };

        /// <summary>Добавляет к списку кандидатов пару (scope-специфик + _default) для базового
        /// префикса. Если prefix пустой/некорректный — не добавляет ничего.</summary>
        private static void AddScopeCandidates(List<string> candidates, string prefix, string suffix)
        {
            if (string.IsNullOrEmpty(prefix) || prefix.EndsWith("_", StringComparison.Ordinal)) return;
            if (!string.IsNullOrEmpty(suffix)) candidates.Add(prefix + suffix);
            candidates.Add(prefix + "_default");
        }

        /// <summary>Подбор dialogId для планеты. Планетный диалог всегда идёт в контексте
        /// «правительства» — <see cref="DialogScope.Gov"/> (нет радио-планеты, только на
        /// поверхности), поэтому scope-суффикса здесь нет: дифференциация идёт по данным
        /// поселения. Fallback-цепочка: UID → Planet_{Government}_default →
        /// Planet_{EconomyType}_default → Planet_{Race}_default → Planet_default.</summary>
        public static string ResolvePlanetDialogId(PlanetData planet)
        {
            var dialogs = GalaxyManager.Instance?.Context?.Config?.Dialogs?.Dialogs;
            if (dialogs == null || dialogs.Count == 0 || planet == null) return null;
            string gov = planet.Settlement?.Government;
            string econ = planet.Settlement?.EconomyType;
            string[] candidates =
            {
                planet.Uid,
                !string.IsNullOrEmpty(gov)         ? $"Planet_{gov}_default"         : null,
                !string.IsNullOrEmpty(econ)        ? $"Planet_{econ}_default"        : null,
                !string.IsNullOrEmpty(planet.Race) ? $"Planet_{planet.Race}_default" : null,
                "Planet_default",
            };
            for (int i = 0; i < candidates.Length; i++)
                if (!string.IsNullOrEmpty(candidates[i]) && dialogs.ContainsKey(candidates[i])) return candidates[i];
            return null;
        }

        public static DialogNode CurrentNode()
        {
            if (_active?.Tree?.Nodes == null || string.IsNullOrEmpty(_active.CurrentNodeId)) return null;
            _active.Tree.Nodes.TryGetValue(_active.CurrentNodeId, out var n);
            return n;
        }

        /// <summary>Текст текущего узла — реплика NPC — с учётом <see cref="DialogNode.TextVariants"/>:
        /// берётся первый вариант, чьё условие выполнено, иначе <see cref="DialogNode.Text"/>.</summary>
        public static string ResolveNodeText(DialogNode node)
        {
            if (node?.TextVariants != null)
                for (int i = 0; i < node.TextVariants.Count; i++)
                {
                    var v = node.TextVariants[i];
                    if (v == null) continue;
                    if (string.IsNullOrEmpty(v.Condition) || EvaluateCondition(v.Condition)) return v.Text;
                }
            return node?.Text;
        }

        public static List<(int Index, DialogReply Reply)> GetVisibleReplies()
        {
            var list = new List<(int, DialogReply)>();
            var node = CurrentNode();
            if (node?.Replies == null) return list;
            for (int i = 0; i < node.Replies.Count; i++)
            {
                var r = node.Replies[i];
                if (r == null) continue;
                if (!EvaluateCondition(r.Condition)) continue;
                list.Add((i, r));
            }
            return list;
        }

        // Флаг для защиты от повторной обработки клика по одной и той же реплике,
        // пока действия текущей ещё выполняются (действия могут вызвать SkipTurn и т.п.).
        private static bool _choosingReply;

        public static void ChooseReply(int replyIndex)
        {
            if (_active == null || _choosingReply) return;
            var node = CurrentNode();
            if (node?.Replies == null || replyIndex < 0 || replyIndex >= node.Replies.Count) return;
            var reply = node.Replies[replyIndex];
            if (reply == null) return;

            var startingCtx = _active;
            _choosingReply = true;
            try
            {
            if (reply.OnEnter != null)
                foreach (var a in reply.OnEnter) InvokeAction(a);
            if (reply.Actions != null)
                foreach (var a in reply.Actions) InvokeAction(a);

            if (_active == null) return;                // действие могло вызвать End
            // Действие сменило активный контекст (Push/Pop/Start/CloseAll) — reply.NextNode
            // относится к исходному диалогу, применять его к новому нельзя, а fallback
            // «пустой NextNode → End» закроет только что открытый поддиалог.
            if (_active != startingCtx) return;
            if (_active.ShouldClose) { End(); return; }

            if (string.IsNullOrEmpty(reply.NextNode)) { End(); return; }

            if (!_active.Tree.Nodes.ContainsKey(reply.NextNode))
            {
                Debug.LogWarning($"[DialogService] NextNode '{reply.NextNode}' не найден; диалог закрыт.");
                End();
                return;
            }
            EnterNode(reply.NextNode);
            }
            finally { _choosingReply = false; }
        }

        private static void EnterNode(string nodeId)
        {
            _active.CurrentNodeId = nodeId;
            InvalidateTagCache();
            var node = CurrentNode();
            if (node?.OnEnter != null)
                foreach (var a in node.OnEnter) InvokeAction(a);
            if (_active == null) return;
            if (_active.ShouldClose) { End(); return; }
            // Узел мог быть засеян действиями OnEnter — рендер должен видеть свежие значения.
            InvalidateTagCache();
            OnDialogNodeChanged?.Invoke(_active);
        }

        private static void InvokeAction(DialogAction a)
        {
            if (a == null) return;
            // Действие и пишет ctx.Data, и само читает теги (Lua-скрипты зовут tag()/is()).
            // Сбрасываем с обеих сторон: до — чтобы действие не увидело значений, снятых
            // до предыдущей мутации, после — чтобы их не увидел рендер.
            InvalidateTagCache();
            try { InvokeActionCore(a); }
            finally { InvalidateTagCache(); }
        }

        private static void InvokeActionCore(DialogAction a)
        {

            // Lua-путь: если задан Script — исполняем его, Method игнорируем.
            if (!string.IsNullOrEmpty(a.Script))
            {
                try
                {
                    var env = BuildLuaEnv(_active);
                    env["args"] = a.Args;
                    LuaHost.EvalCached(a.Script, env);
                }
                catch (MoonSharp.Interpreter.InterpreterException iex)
                { Debug.LogError($"[DialogService] Script упал: {iex.DecoratedMessage ?? iex.Message}"); }
                catch (Exception e)
                { Debug.LogError($"[DialogService] Script упал: {e.Message}"); }
                return;
            }

            if (string.IsNullOrEmpty(a.Method)) return;
            if (!_actions.TryGetValue(a.Method, out var h))
            {
                Debug.LogWarning($"[DialogService] Нет обработчика для действия '{a.Method}'.");
                return;
            }
            try { h(_active, a.Args ?? new List<string>()); }
            catch (Exception e) { Debug.LogError($"[DialogService] Действие '{a.Method}' упало: {e.Message}"); }
        }

        private static bool EvaluateCondition(string expr)
        {
            if (string.IsNullOrEmpty(expr)) return true;

            // Префикс "lua:" — исполняем как Lua-выражение, ожидаем boolean/число/строку.
            // Fallback на false при ошибке (реплика скрывается, но диалог не падает).
            if (expr.Length > 4 && expr.StartsWith("lua:", StringComparison.OrdinalIgnoreCase))
            {
                var body = expr.Substring(4).TrimStart();
                try
                {
                    // EvalCached, не DoWithEnv: условия проверяются на каждый рендер узла,
                    // перекомпиляция чанка на каждую реплику здесь недопустима.
                    var result = LuaHost.EvalCached("return (" + body + ")", BuildLuaEnv(_active));
                    return LuaResultToBool(result);
                }
                catch (MoonSharp.Interpreter.InterpreterException iex)
                { Debug.LogError($"[DialogService] Lua-condition '{body}': {iex.DecoratedMessage ?? iex.Message}"); return false; }
                catch (Exception e)
                { Debug.LogError($"[DialogService] Lua-condition '{body}': {e.Message}"); return false; }
            }

            if (_conditionEvaluator != null)
            {
                try { return _conditionEvaluator(_active, expr); }
                catch (Exception e)
                {
                    Debug.LogError($"[DialogService] Condition '{expr}' упало: {e.Message}");
                    return false;
                }
            }
            try { return DefaultEvaluateCondition(_active, expr); }
            catch (Exception e)
            {
                Debug.LogError($"[DialogService] Default condition '{expr}' упало: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Базовый парсер условий с поддержкой AND (<c>&amp;&amp;</c>), OR (<c>||</c>), скобок и отрицания (<c>!</c>).
        /// Атом — сравнение <c>tag op value</c> или одиночный <c>tag</c> как boolean.
        /// Операторы: <c>== != &gt; &lt; &gt;= &lt;=</c>. Сравнение числовое, если обе стороны парсятся в float,
        /// иначе — регистронезависимое строковое. Используется, если user-скрипт не подменил
        /// <see cref="SetConditionEvaluator"/>.
        /// </summary>
        public static bool DefaultEvaluateCondition(DialogContext ctx, string expr)
        {
            if (string.IsNullOrEmpty(expr)) return true;
            return EvalOr(ctx, expr.Trim());
        }

        /// <summary>Окружение для Lua-скриптов и условий диалога. Помимо объектов сцены
        /// (<c>ctx/player/target/planet</c>) прокидывает словарь тегов, чтобы Lua-условие
        /// говорило на том же языке, что и тег-DSL:
        /// <code>
        /// "lua: is('can_trade') and not is('player_is_pirate')"
        /// "lua: tonumber(tag('reward_amount')) > 500"
        /// </code>
        /// <c>tag(name)</c> — строковое значение тега (никогда не nil, при отсутствии — пустая строка);
        /// <c>is(name)</c> — оно же как boolean по правилам DSL (пусто/0/false/no/нет → false).</summary>
        public static Dictionary<string, object> BuildLuaEnv(DialogContext ctx)
        {
            return new Dictionary<string, object>
            {
                ["ctx"]    = ctx,
                ["player"] = ctx?.PlayerShip,
                ["target"] = ctx?.TargetShip,
                ["planet"] = ctx?.TargetPlanet,
                ["tag"]    = (Func<string, string>)(n => ResolveTag(n, ctx) ?? ""),
                ["is"]     = (Func<string, bool>)(n =>
                {
                    var v = ResolveTag(n, ctx);
                    return !string.IsNullOrEmpty(v) && !IsFalsey(v);
                }),
            };
        }

        private static bool EvalOr(DialogContext ctx, string expr)
        {
            var parts = SplitTopLevel(expr, "||");
            if (parts.Count == 1) return EvalAnd(ctx, parts[0]);
            for (int i = 0; i < parts.Count; i++)
                if (EvalAnd(ctx, parts[i])) return true;
            return false;
        }

        private static bool EvalAnd(DialogContext ctx, string expr)
        {
            var parts = SplitTopLevel(expr, "&&");
            if (parts.Count == 1) return EvalNot(ctx, parts[0]);
            for (int i = 0; i < parts.Count; i++)
                if (!EvalNot(ctx, parts[i])) return false;
            return true;
        }

        private static bool EvalNot(DialogContext ctx, string expr)
        {
            expr = expr.Trim();
            // '!' — только префиксный оператор; '!=' обрабатывается в атоме.
            if (expr.Length > 0 && expr[0] == '!' && (expr.Length == 1 || expr[1] != '='))
                return !EvalNot(ctx, expr.Substring(1));
            return EvalAtom(ctx, expr);
        }

        private static bool EvalAtom(DialogContext ctx, string expr)
        {
            expr = expr.Trim();
            if (expr.Length >= 2 && expr[0] == '(' && expr[^1] == ')' && IsOuterParenBalanced(expr))
                return EvalOr(ctx, expr.Substring(1, expr.Length - 2));

            // Операторы — двусимвольные сначала, чтобы не путать с одиночными.
            // Игнорируем скобки/строки: используем FindTopLevelOp.
            string[] ops = { "==", "!=", ">=", "<=", ">", "<" };
            foreach (var op in ops)
            {
                int idx = FindTopLevelOp(expr, op);
                if (idx <= 0) continue;
                string lhsRaw = expr.Substring(0, idx).Trim();
                string rhsRaw = expr.Substring(idx + op.Length).Trim();
                string lhs = ResolveTag(lhsRaw, ctx) ?? lhsRaw;
                string rhs = StripQuotes(rhsRaw);
                string rhsResolved = ResolveTag(rhs, ctx);
                if (rhsResolved != null) rhs = rhsResolved;
                return CompareValues(lhs, rhs, op);
            }

            // Одиночный тег как boolean.
            string val = ResolveTag(expr, ctx);
            return !string.IsNullOrEmpty(val) && !IsFalsey(val);
        }

        // Разбивает expr на top-level куски по разделителю (не проваливаемся в скобки/кавычки).
        // Возвращает исходную строку, если разделитель не найден на верхнем уровне.
        private static List<string> SplitTopLevel(string expr, string sep)
        {
            var res = new List<string>();
            int depth = 0, start = 0;
            bool inStr = false; char strCh = '\0';
            for (int i = 0; i < expr.Length; i++)
            {
                char c = expr[i];
                if (inStr)
                {
                    if (c == strCh) inStr = false;
                    continue;
                }
                if (c == '"' || c == '\'') { inStr = true; strCh = c; continue; }
                if (c == '(') { depth++; continue; }
                if (c == ')') { depth = System.Math.Max(0, depth - 1); continue; }
                if (depth == 0 && i + sep.Length <= expr.Length &&
                    string.CompareOrdinal(expr, i, sep, 0, sep.Length) == 0)
                {
                    res.Add(expr.Substring(start, i - start));
                    start = i + sep.Length;
                    i += sep.Length - 1;
                }
            }
            res.Add(expr.Substring(start));
            return res;
        }

        // Найти оператор на нулевом уровне вложенности (не внутри скобок/строк).
        private static int FindTopLevelOp(string expr, string op)
        {
            int depth = 0; bool inStr = false; char strCh = '\0';
            for (int i = 0; i < expr.Length; i++)
            {
                char c = expr[i];
                if (inStr) { if (c == strCh) inStr = false; continue; }
                if (c == '"' || c == '\'') { inStr = true; strCh = c; continue; }
                if (c == '(') { depth++; continue; }
                if (c == ')') { depth = System.Math.Max(0, depth - 1); continue; }
                if (depth == 0 && i + op.Length <= expr.Length &&
                    string.CompareOrdinal(expr, i, op, 0, op.Length) == 0)
                    return i;
            }
            return -1;
        }

        // Верно ли, что внешние скобки образуют единую пару (а не "(a) && (b)").
        private static bool IsOuterParenBalanced(string expr)
        {
            if (expr.Length < 2 || expr[0] != '(' || expr[^1] != ')') return false;
            int depth = 0; bool inStr = false; char strCh = '\0';
            for (int i = 0; i < expr.Length; i++)
            {
                char c = expr[i];
                if (inStr) { if (c == strCh) inStr = false; continue; }
                if (c == '"' || c == '\'') { inStr = true; strCh = c; continue; }
                if (c == '(') depth++;
                else if (c == ')') { depth--; if (depth == 0 && i != expr.Length - 1) return false; }
            }
            return depth == 0;
        }

        private static bool CompareValues(string lhs, string rhs, string op)
        {
            // Сначала пробуем числовое сравнение.
            if (float.TryParse(lhs, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lf) &&
                float.TryParse(rhs, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var rf))
            {
                switch (op)
                {
                    case "==": return Mathf.Approximately(lf, rf);
                    case "!=": return !Mathf.Approximately(lf, rf);
                    case ">":  return lf > rf;
                    case "<":  return lf < rf;
                    case ">=": return lf >= rf;
                    case "<=": return lf <= rf;
                }
            }
            // Строковое сравнение (регистронезависимое).
            int cmp = string.Compare(lhs ?? "", rhs ?? "", StringComparison.OrdinalIgnoreCase);
            return op switch
            {
                "==" => cmp == 0,
                "!=" => cmp != 0,
                ">"  => cmp > 0,
                "<"  => cmp < 0,
                ">=" => cmp >= 0,
                "<=" => cmp <= 0,
                _    => false
            };
        }

        private static string StripQuotes(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[^1] == s[0])
                return s.Substring(1, s.Length - 2);
            return s;
        }

        private static bool LuaResultToBool(MoonSharp.Interpreter.DynValue v)
        {
            if (v == null || v.IsNil() || v.IsVoid()) return false;
            return v.Type switch
            {
                MoonSharp.Interpreter.DataType.Boolean => v.Boolean,
                MoonSharp.Interpreter.DataType.Number  => v.Number != 0.0,
                MoonSharp.Interpreter.DataType.String  => !string.IsNullOrEmpty(v.String) && !IsFalsey(v.String),
                _ => true, // table/userdata/function — «истина» по Lua-семантике
            };
        }

        private static bool IsFalsey(string s) =>
            string.Equals(s, "0", StringComparison.Ordinal) ||
            string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "no", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "нет", StringComparison.OrdinalIgnoreCase);
    }
}
