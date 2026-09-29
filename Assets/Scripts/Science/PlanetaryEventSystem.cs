using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Config;
using SRG.Economy;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Galaxy.Politics;
using SRG.UI.Screens;

namespace SRG.Science
{
    /// <summary>
    /// Ежедневный тик планетарных событий: триггеры (PriceAbove, RandomChance, ...),
    /// затухание активных, цепочки на истечение, ImmediateActions при старте.
    /// Заменяет старый TickEvents из PlanetaryTechSystem.
    /// </summary>
    public static class PlanetaryEventSystem
    {
        /// <summary>События длятся месяцами — тикать каждый ход бессмысленно. Выравниваем с TradeSystem.</summary>
        public const int TickStrideTurns = TradeSystem.TickStrideTurns;

        // Per-turn instrumentation. Спайк T184 (evt=227мс) должен подсветить, сколько именно
        // планет тикнуло и сколько событий стартовало — норма это ~1/Stride планет × редкий
        // trigger, а не всплеск.
        internal static int _diagPlanetsTicked;
        internal static int _diagEventsStarted;
        internal static string _diagLastStartedEventId;

        public static void TickAll(GalaxyData galaxy, GalaxyGenerationContext ctx)
        {
            if (galaxy == null || ctx?.Config?.Events == null) return;
            _diagPlanetsTicked = 0;
            _diagEventsStarted = 0;
            _diagLastStartedEventId = null;
            // Амортизация: раньше все планеты тикали синхронно на CurrentTurn % Stride == 0 →
            // Turn 3 давал 999 мс всплеск (сотни StartEvent + News-постов одномоментно). Теперь
            // каждая планета имеет свой фазовый сдвиг по хешу Uid → 1/Stride планет тикает
            // каждый ход, частота на планету та же (1 раз в Stride ходов).
            int stride = Mathf.Max(1, TickStrideTurns);
            int turn = galaxy.CurrentTurn;
            foreach (var star in galaxy.StarsMap.Values)
                foreach (var planet in star.Planets)
                {
                    if (!IsInhabited(planet)) continue;
                    int offset = planet.Uid != null ? (planet.Uid.GetHashCode() & 0x7fffffff) % stride : 0;
                    if ((turn - offset) % stride != 0) continue;
                    _diagPlanetsTicked++;
                    TickPlanet(planet, galaxy, ctx.Config);
                }
        }

        private static void TickPlanet(PlanetData planet, GalaxyData galaxy, GalaxyConfig cfg)
        {
            // 1) Затухание активных событий + ChainOnExpire.
            TickActiveEvents(planet, galaxy, cfg);

            // 2) Триггеры — для событий не активных и не на кулдауне.
            foreach (var kv in cfg.Events)
            {
                string eventId = kv.Key;
                var evtCfg = kv.Value;
                if (evtCfg.Triggers == null || evtCfg.Triggers.Count == 0) continue;
                if (IsActive(planet, eventId)) continue;
                if (IsOnCooldown(planet, eventId, evtCfg.Cooldown, galaxy.CurrentTurn)) continue;
                if (!EvaluateTriggers(planet, galaxy, cfg, eventId, evtCfg.Triggers, evtCfg.TriggerLogic)) continue;

                StartEvent(planet, eventId, evtCfg, galaxy, cfg, reason: "trigger");
            }
        }

        private static void TickActiveEvents(PlanetData planet, GalaxyData galaxy, GalaxyConfig cfg)
        {
            if (planet.Settlement.ActiveEvents == null || planet.Settlement.ActiveEvents.Count == 0) return;
            for (int i = planet.Settlement.ActiveEvents.Count - 1; i >= 0; i--)
            {
                var evt = planet.Settlement.ActiveEvents[i];
                if (evt.RemainingMonths < 0) continue;
                // Тик идёт раз в TickStrideTurns ходов, поэтому вычитаем stride за раз —
                // чтобы средняя длительность событий не растянулась.
                evt.RemainingMonths -= TickStrideTurns;
                if (evt.RemainingMonths > 0) continue;

                // Событие истекло.
                planet.Settlement.ActiveEvents.RemoveAt(i);
                planet.Settlement.EventCooldowns ??= new Dictionary<string, int>();
                planet.Settlement.EventCooldowns[evt.EventId] = galaxy.CurrentTurn;

                EconomicLog.Custom(galaxy.CurrentTurn, "EVENT", EconomicLog.Safe(planet.Name), "EXPIRED",
                    $"event={evt.EventId}");

                // Цепочка на истечение.
                if (cfg.Events.TryGetValue(evt.EventId, out var evtCfg)
                    && evtCfg.ChainOnExpire != null
                    && !string.IsNullOrEmpty(evtCfg.ChainOnExpire.EventId)
                    && evtCfg.ChainOnExpire.Chance > 0f
                    && Random.value <= evtCfg.ChainOnExpire.Chance)
                {
                    string nextId = evtCfg.ChainOnExpire.EventId;
                    if (cfg.Events.TryGetValue(nextId, out var nextCfg)
                        && !IsActive(planet, nextId)
                        && !IsOnCooldown(planet, nextId, nextCfg.Cooldown, galaxy.CurrentTurn))
                    {
                        StartEvent(planet, nextId, nextCfg, galaxy, cfg, reason: "chain");
                    }
                }
            }
        }

        // ──────────────────────────────────────────
        // Запуск события
        // ──────────────────────────────────────────

        private static void StartEvent(PlanetData planet, string eventId, EventConfig evtCfg,
            GalaxyData galaxy, GalaxyConfig cfg, string reason)
        {
            _diagEventsStarted++;
            _diagLastStartedEventId = eventId;
            // Используем существующий ApplyEvent для добавления в ActiveEvents + штраф ПТУ.
            PlanetaryTechSystem.ApplyEvent(planet, eventId, cfg);

            EconomicLog.Custom(galaxy.CurrentTurn, "EVENT", EconomicLog.Safe(planet.Name), "STARTED",
                $"event={eventId} reason={reason} duration_range={(evtCfg.DurationMonths != null && evtCfg.DurationMonths.Length >= 2 ? $"{evtCfg.DurationMonths[0]}..{evtCfg.DurationMonths[1]}" : "indef")}");

            if (!string.IsNullOrEmpty(evtCfg.NewsMessage))
            {
                string planetName = string.IsNullOrEmpty(planet.Name) ? "неизвестная планета" : planet.Name;
                string starName = planet.ParentStar?.Name ?? "неизвестная система";
                // Подставляем плейсхолдеры {planet}/{star}. В конфиге NewsMessage уже пишется в стиле
                // «На планете {planet} …» — не префиксим его повторно, только резолвим теги.
                string resolved = evtCfg.NewsMessage
                    .Replace("{planet}", planetName)
                    .Replace("{star}",   starName);
                GameConsoleController.AddEntry($"[Событие] {planetName}: {resolved}");
                string ctrl = OccupationService.GetControllingOwner(planet);
                GalaxyNewsService.PostForSide(GalaxyNewsService.CAT_PLANET, resolved, ctrl);
            }

            // ImmediateActions
            if (evtCfg.ImmediateActions != null)
                foreach (var act in evtCfg.ImmediateActions)
                    ApplyImmediateAction(planet, galaxy, cfg, act);
        }

        private static void ApplyImmediateAction(PlanetData planet, GalaxyData galaxy,
            GalaxyConfig cfg, EventActionConfig action)
        {
            if (action == null || string.IsNullOrEmpty(action.Type)) return;
            int turn = galaxy.CurrentTurn;

            switch (action.Type)
            {
                case "ChangeGovernment":
                    GovernmentChangeService.ChangeGovernment(planet, cfg, action, turn);
                    break;

                case "InflationShock":
                {
                    InflationSystem.ShockType st = action.ShockType?.ToLowerInvariant() switch
                    {
                        "war" => InflationSystem.ShockType.War,
                        "revolution" => InflationSystem.ShockType.Revolution,
                        _ => InflationSystem.ShockType.Revolution
                    };
                    string sourceSide = OccupationService.GetControllingOwner(planet);
                    InflationSystem.ApplyShock(galaxy, cfg, st, sourceSide);
                    break;
                }

                case "AddNotification":
                    GameConsoleController.AddEntry($"[Событие] {planet.Name}: {action.Message ?? "-"}");
                    break;
            }
        }

        // ──────────────────────────────────────────
        // Триггеры
        // ──────────────────────────────────────────

        private static bool EvaluateTriggers(PlanetData planet, GalaxyData galaxy, GalaxyConfig cfg,
            string eventId, List<EventTriggerConfig> triggers, string logic)
        {
            if (triggers == null || triggers.Count == 0) return false;
            bool orMode = string.Equals(logic, "Or", System.StringComparison.OrdinalIgnoreCase);
            float chanceMul = galaxy.ResearchState?.GetEventChanceMultiplier(eventId) ?? 1f;

            foreach (var t in triggers)
            {
                bool r = EvalOne(planet, galaxy, cfg, t, chanceMul);
                if (orMode && r) return true;
                if (!orMode && !r) return false;
            }
            return !orMode; // And: все прошли. Or: пустой цикл — никаких true.
        }

        private static bool EvalOne(PlanetData planet, GalaxyData galaxy, GalaxyConfig cfg, EventTriggerConfig t, float chanceMultiplier)
        {
            if (t == null || string.IsNullOrEmpty(t.Type)) return false;

            switch (t.Type)
            {
                case "PriceAbove":
                case "PriceBelow":
                {
                    if (string.IsNullOrEmpty(t.GoodId)) return false;
                    if (cfg.Goods == null || !cfg.Goods.TryGetValue(t.GoodId, out var gCfg)) return false;
                    float threshold = gCfg.BasePrice * t.MultiplierVsBase;
                    // ForDays задано в ходах, история хранится в семплах (1 семпл / TickStrideTurns ходов).
                    int samplesNeeded = Mathf.Max(1, Mathf.CeilToInt(t.ForDays / (float)TradeSystem.TickStrideTurns));
                    if (planet.Settlement.PriceHistory == null || !planet.Settlement.PriceHistory.TryGetValue(t.GoodId, out var hist)) return false;
                    if (hist.Count < samplesNeeded) return false;
                    int start = hist.Count - samplesNeeded;
                    for (int i = start; i < hist.Count; i++)
                    {
                        bool cond = t.Type == "PriceAbove" ? (hist[i] >= threshold) : (hist[i] <= threshold);
                        if (!cond) return false;
                    }
                    return true;
                }

                case "StockAbove":
                case "StockBelow":
                {
                    if (string.IsNullOrEmpty(t.GoodId)) return false;
                    if (planet.Settlement.Shop?.Goods == null || !planet.Settlement.Shop.Goods.TryGetValue(t.GoodId, out var entry)) return false;
                    return t.Type == "StockAbove" ? entry.Stock >= t.Threshold : entry.Stock <= t.Threshold;
                }

                case "DaysSinceLast":
                {
                    if (string.IsNullOrEmpty(t.EventId)) return false;
                    if (planet.Settlement.EventCooldowns == null) return true; // никогда не было
                    if (!planet.Settlement.EventCooldowns.TryGetValue(t.EventId, out var lastTurn)) return true;
                    return (galaxy.CurrentTurn - lastTurn) >= t.Days;
                }

                case "EventActive":
                    if (string.IsNullOrEmpty(t.EventId)) return false;
                    return IsActive(planet, t.EventId);

                case "EventJustExpired":
                {
                    if (string.IsNullOrEmpty(t.EventId)) return false;
                    if (planet.Settlement.EventCooldowns == null) return false;
                    if (!planet.Settlement.EventCooldowns.TryGetValue(t.EventId, out var lastTurn)) return false;
                    return (galaxy.CurrentTurn - lastTurn) <= Mathf.Max(1, t.Days);
                }

                case "GovernmentIs":
                    return string.Equals(planet.Settlement.Government, t.Government, System.StringComparison.OrdinalIgnoreCase);

                case "GTUAbove":
                    return galaxy.GtuLevel >= t.Level;

                case "RandomChance":
                {
                    float p = t.ChancePerDay * chanceMultiplier;
                    return p > 0f && Random.value <= p;
                }
            }
            return false;
        }

        // ──────────────────────────────────────────
        // Общее
        // ──────────────────────────────────────────

        private static bool IsActive(PlanetData planet, string eventId)
        {
            if (planet.Settlement.ActiveEvents == null) return false;
            for (int i = 0; i < planet.Settlement.ActiveEvents.Count; i++)
                if (planet.Settlement.ActiveEvents[i].EventId == eventId) return true;
            return false;
        }

        private static bool IsOnCooldown(PlanetData planet, string eventId, int cooldown, int currentTurn)
        {
            if (cooldown <= 0) return false;
            if (planet.Settlement.EventCooldowns == null) return false;
            if (!planet.Settlement.EventCooldowns.TryGetValue(eventId, out var last)) return false;
            return (currentTurn - last) < cooldown;
        }

        private static bool IsInhabited(PlanetData planet)
        {
            if (planet == null) return false;
            if (string.IsNullOrEmpty(planet.Race)) return false;
            if (string.Equals(planet.Race, GalaxyConstants.RACE_NONE_KEY, System.StringComparison.OrdinalIgnoreCase)) return false;
            return planet.Settlement.Population > 0;
        }
    }
}
