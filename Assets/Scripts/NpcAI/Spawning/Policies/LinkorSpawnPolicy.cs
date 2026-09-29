using SRG.Config;
using SRG.Galaxy;
using SRG.Galaxy.Politics;

namespace SRG.NpcAI.Spawning.Policies
{
    /// <summary>
    /// Top-up per-sector политика для линкора.
    /// Target = BaseTarget (=1) на сектор. Спавн у крупнейшей коалиц. планеты сектора (sector capital).
    /// При gloss потерь ~0.5% шанса в день → ожидание ~200 дней (геометрическое).
    /// </summary>
    public class LinkorSpawnPolicy : ISpawnPolicy
    {
        public string ShipTypeId => "Linkor";

        public void DailyTick(SpawnTickContext ctx)
        {
            var policy = ctx.Config.Spawn?.GetPolicy(ShipTypeId);
            if (policy == null || policy.BaseChance <= 0f) return;

            int target = policy.BaseTarget > 0 ? policy.BaseTarget : 1;

            foreach (var sector in ctx.Galaxy.Sectors)
            {
                int count = ctx.Counters.GetShipsAtSector(sector.Uid, ShipTypeId);
                if (count >= target) continue;
                if (!SpawnFormulas.ShouldSpawn(count, target, policy.BaseChance)) continue;

                var (planet, star) = FindSectorCapital(sector, policy);
                if (planet == null || star == null) continue;
                if (!SpawnFormulas.StarCanSpawnMore(star, ctx.Settings)) continue;

                SpawnSystem.SpawnShipAtPlanet(ShipTypeId, planet.Owner, planet.Race, planet, star, ctx.Gen,
                    reason: $"sector={sector.Name} target={target} count={count}");
            }
        }

        /// <summary>Самая населённая обитаемая коалиц. планета сектора.</summary>
        private static (PlanetData, StarData) FindSectorCapital(SectorData sector, ShipSpawnPolicyConfig policy)
        {
            PlanetData best = null;
            StarData bestStar = null;
            int bestPop = -1;

            foreach (var star in sector.Stars)
                foreach (var planet in star.Planets)
                {
                    if (planet == null) continue;
                    // Гейт по эффективному контроллёру: оккупированная столица сектора не может
                    // выпускать линкор для родной стороны.
                    string effectiveOwner = OccupationService.GetControllingOwner(planet);
                    if (policy.AllowedOwners != null && policy.AllowedOwners.Count > 0
                        && !policy.AllowedOwners.Contains(effectiveOwner)) continue;
                    if (string.IsNullOrEmpty(planet.Race)
                        || planet.Race == GalaxyConstants.RACE_NONE_KEY
                        || planet.Race == GalaxyConstants.RACE_MIXED_KEY) continue;
                    if (planet.Settlement.Population <= bestPop) continue;
                    best = planet; bestStar = star; bestPop = planet.Settlement.Population;
                }
            return (best, bestStar);
        }
    }
}
