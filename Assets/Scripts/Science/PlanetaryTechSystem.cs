using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Economy;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Simulation;

namespace SRG.Science
{
    /// <summary>
    /// Симуляция ПТУ (планетарный технический уровень) и ГТУ (галактический).
    /// Вызывается раз в ход из GalaxyData.GalaxyNextDay.
    /// </summary>
    public static class PlanetaryTechSystem
    {
        /// <summary>
        /// Основная точка входа — тикаем ПТУ всех планет, затем пересчитываем ГТУ.
        /// </summary>
        public static void TickAll(GalaxyData galaxy, GalaxyGenerationContext ctx)
        {
            if (ctx?.Config == null) return;

            foreach (var star in galaxy.StarsMap.Values)
                foreach (var planet in star.Planets)
                    if (IsInhabited(planet))
                        TickPlanetPtu(planet, ctx);

            galaxy.GtuLevel = CalculateGtu(galaxy, ctx);
        }

        // ──────────────────────────────────────────
        // ПТУ
        // ──────────────────────────────────────────

        private static void TickPlanetPtu(PlanetData planet, GalaxyGenerationContext ctx)
        {
            var cfg = ctx.Config;
            var sci = cfg?.Science;
            int perLevel = Mathf.Max(1, sci?.CreditsPerPtuLevel ?? 2);

            int newPtu = Mathf.Clamp(1 + planet.Settlement.InventionCredits / perLevel, 1, 10);
            if (newPtu == planet.Settlement.TechLevel) return;

            planet.Settlement.TechLevel = newPtu;
            EconomicLog.Custom(GameWorld.GeneratedGalaxy?.CurrentTurn ?? 0,
                "SCIENCE", EconomicLog.Safe(planet.Name), "PTU_CHANGED",
                $"ptu={newPtu} credits={planet.Settlement.InventionCredits} per_level={perLevel}");
        }

        // ──────────────────────────────────────────
        // ГТУ
        // ──────────────────────────────────────────

        // ГТУ = максимальный уровень L, для которого число планет коалиции с TechLevel ≥ L
        // достигает порога RequiredPlanets из конфига. Растёт монотонно по мере прогресса
        // планет; падает только при реальной потере/деградации (захват, разрушение).
        private static int CalculateGtu(GalaxyData galaxy, GalaxyGenerationContext ctx)
        {
            var cfg = ctx.ActiveGalaxyConfig?.GTU;
            int minValue = cfg?.MinValue ?? 1;
            int maxValue = Mathf.Max(minValue, cfg?.MaxValue ?? 8);

            if (cfg?.Levels == null || cfg.Levels.Count == 0)
                return minValue;

            int[] histogram = new int[maxValue + 2];
            foreach (var star in galaxy.StarsMap.Values)
            {
                foreach (var planet in star.Planets)
                {
                    if (!IsInhabited(planet)) continue;
                    if (!IsCoalitionPlanet(planet, ctx)) continue;
                    int lvl = Mathf.Clamp(planet.Settlement.TechLevel, 0, maxValue);
                    histogram[lvl]++;
                }
            }

            int countAtOrAbove = 0;
            for (int lvl = maxValue; lvl > minValue; lvl--)
            {
                countAtOrAbove += histogram[lvl];
                int required = GetRequiredPlanets(cfg, lvl);
                if (required > 0 && countAtOrAbove >= required)
                    return lvl;
            }
            return minValue;
        }

        private static int GetRequiredPlanets(GtuConfig cfg, int level)
        {
            foreach (var entry in cfg.Levels)
                if (entry.Level == level) return entry.RequiredPlanets;
            return 0;
        }

        private static bool IsInhabited(PlanetData planet) =>
            !string.IsNullOrEmpty(planet.Race) &&
            !string.Equals(planet.Race, GalaxyConstants.RACE_NONE_KEY, System.StringComparison.OrdinalIgnoreCase);

        private static bool IsCoalitionPlanet(PlanetData planet, GalaxyGenerationContext ctx)
        {
            if (string.IsNullOrEmpty(planet.Owner) ||
                planet.Owner == GalaxyConstants.OWNER_NONE_KEY ||
                planet.Owner == GalaxyConstants.OWNER_UNRESOLVED_KEY)
                return false;

            var owners = ctx.Config?.Ships?.Owners;
            if (owners == null) return true;
            return owners.ContainsKey(planet.Owner) && planet.Owner == "Coalition";
        }

        // ──────────────────────────────────────────
        // Публичные хелперы
        // ──────────────────────────────────────────

        /// <summary>
        /// Добавляет событие на планету. Если DurationMonths в конфиге null — бессрочное (RemainingMonths = -1).
        /// </summary>
        public static void ApplyEvent(PlanetData planet, string eventId, GalaxyConfig cfg)
        {
            if (planet == null || string.IsNullOrEmpty(eventId)) return;
            if (cfg?.Events == null || !cfg.Events.TryGetValue(eventId, out var evtCfg)) return;

            // Не дублируем одинаковые события
            if (planet.Settlement.ActiveEvents == null)
                planet.Settlement.ActiveEvents = new System.Collections.Generic.List<ActivePlanetEvent>();

            foreach (var existing in planet.Settlement.ActiveEvents)
                if (existing.EventId == eventId) return;

            int duration = -1;
            if (evtCfg.DurationMonths != null && evtCfg.DurationMonths.Length >= 2)
                duration = GameRng.Range(evtCfg.DurationMonths[0], evtCfg.DurationMonths[1] + 1);

            planet.Settlement.ActiveEvents.Add(new ActivePlanetEvent { EventId = eventId, RemainingMonths = duration });

            // Мгновенный штраф ПТУ при захвате/революции — режем кредиты, чтобы следующий
            // тик не «откатил» уровень обратно из накопленных изобретений.
            if (evtCfg.PTU != null && evtCfg.PTU.PTUPenalty > 0)
            {
                int perLevel = Mathf.Max(1, cfg.Science?.CreditsPerPtuLevel ?? 2);
                planet.Settlement.InventionCredits = Mathf.Max(0, planet.Settlement.InventionCredits - evtCfg.PTU.PTUPenalty * perLevel);
                planet.Settlement.TechLevel = Mathf.Max(1, planet.Settlement.TechLevel - evtCfg.PTU.PTUPenalty);
            }
        }

        /// <summary>
        /// Удаляет событие с планеты (при освобождении планеты и т.п.).
        /// </summary>
        public static void RemoveEvent(PlanetData planet, string eventId)
        {
            planet?.Settlement.ActiveEvents?.RemoveAll(e => e.EventId == eventId);
        }
    }
}
