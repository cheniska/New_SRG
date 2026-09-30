using SRG.Config;
using SRG.Galaxy;
using SRG.Galaxy.Politics;

namespace SRG.NpcAI
{
    /// <summary>
    /// Единый набор фильтров по планетам. До июля 2026 условия «Race не None/Mixed,
    /// Population>0, не hostile, оккупация ok» были продублированы в 5+ местах
    /// (NpcTargeting, ActionLandResupply, ActionGoodsTrader, TraderAI, spawn-политики).
    /// Здесь — одна точка. Правки условий (напр., добавили новый служебный race-код)
    /// делаются только здесь.
    /// </summary>
    public static class PlanetPredicates
    {
        /// <summary>Планета имеет валидную расу (не None/Mixed).</summary>
        public static bool HasValidRace(PlanetData planet)
            => planet != null
               && !string.IsNullOrEmpty(planet.Race)
               && planet.Race != GalaxyConstants.RACE_NONE_KEY
               && planet.Race != GalaxyConstants.RACE_MIXED_KEY;

        /// <summary>Планета обитаема: валидная раса + население.</summary>
        public static bool IsInhabited(PlanetData planet)
            => HasValidRace(planet)
               && planet.Settlement != null
               && planet.Settlement.Population > 0;

        /// <summary>Планета не враждебна кораблю (по эффективному контроллёру).</summary>
        public static bool IsFriendlyFor(PlanetData planet, ShipData ship)
        {
            if (planet == null || ship == null) return false;
            var rel = OwnerRaceRelationsManager.Instance;
            return rel == null || !rel.AreHostile(ship, planet);
        }

        /// <summary>Пригодна для посадки данным кораблём: обитаема, не враждебна,
        /// LandingBlocked отсекается для чужаков.</summary>
        public static bool IsLandableFor(PlanetData planet, ShipData ship)
        {
            if (!IsInhabited(planet) || ship == null) return false;
            if (!IsFriendlyFor(planet, ship)) return false;
            if (planet.Settlement.LandingBlocked
                && OccupationService.GetControllingOwner(planet) != ship.Owner)
                return false;
            return true;
        }

        /// <summary>Пригодна для торговли: обитаема + есть Shop.Goods.</summary>
        public static bool IsTradeViable(PlanetData planet)
            => IsInhabited(planet) && planet.Settlement.Shop?.Goods != null;

        /// <summary>Полноценная initial-spawn планета: обитаема, валидный Owner (не None/Mixed/Unresolved),
        /// не NonPlanetary-раса, не оккупирована. Прежде жила в NpcSystemSpawner.IsInhabitableForSpawn.</summary>
        public static bool IsInitialSpawnViable(PlanetData planet, GalaxyConfig config)
        {
            if (!IsInhabited(planet)) return false;
            if (string.IsNullOrEmpty(planet.Owner)) return false;
            if (planet.Owner == GalaxyConstants.OWNER_NONE_KEY
                || planet.Owner == GalaxyConstants.OWNER_UNRESOLVED_KEY
                || planet.Owner == GalaxyConstants.OWNER_MIXED_KEY) return false;

            // NonPlanetary-раса (синтеты) — гражданский initial-флот не выдаётся.
            if (config?.Races != null
                && config.Races.TryGetValue(planet.Race, out var raceCfg)
                && raceCfg != null
                && raceCfg.NonPlanetary != 0) return false;

            // Оккупированная планета — эффективный контроллёр не совпадает с родным Owner.
            if (!string.IsNullOrEmpty(planet.Settlement.OccupiedByOwner)
                && planet.Settlement.OccupiedByOwner != planet.Owner)
                return false;

            return true;
        }
    }
}
