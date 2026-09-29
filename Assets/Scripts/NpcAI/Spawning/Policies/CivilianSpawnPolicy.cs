using System.Collections.Generic;
using SRG.Config;
using SRG.Galaxy;
using SRG.Galaxy.Politics;

namespace SRG.NpcAI.Spawning.Policies
{
    /// <summary>
    /// Top-up per-planet политика для гражданских кораблей (Transport/Liner/Diplomat).
    /// Один экземпляр на тип — конструктор принимает shipTypeId.
    ///
    /// Каждый день для каждой обитаемой планеты:
    ///   target = TargetByRace[planet.Race] + (EconomyBonus[planet.EconomyType] || 0)
    ///   count  = живые корабли этого типа, у которых HomePlanetUid == planet
    ///   если shortage > 0 и звезда не в cooldown → ролл и спавн у этой планеты.
    /// </summary>
    public class CivilianSpawnPolicy : ISpawnPolicy
    {
        public string ShipTypeId { get; }
        public CivilianSpawnPolicy(string shipTypeId) { ShipTypeId = shipTypeId; }

        public void DailyTick(SpawnTickContext ctx)
        {
            var spawnCfg = ctx.Config.Spawn;
            var policy   = spawnCfg?.GetPolicy(ShipTypeId);
            if (policy == null || policy.BaseChance <= 0f) return;

            foreach (var sector in ctx.Galaxy.Sectors)
                foreach (var star in sector.Stars)
                {
                    if (!SpawnFormulas.StarCanSpawnMore(star, ctx.Settings)) continue;
                    // Квоту пере-проверяем перед КАЖДОЙ планетой: SpawnShipAtPlanet инкрементит
                    // star.SpawnsToday, и правило «≤ MaxShipsSpawnedPerStarPerDay спавнов в систему за день»
                    // должно применяться и внутри одного тика (иначе система с 5 планетами могла бы
                    // выдать до 5 гражданских за ход).
                    foreach (var planet in star.Planets)
                    {
                        if (!SpawnFormulas.StarCanSpawnMore(star, ctx.Settings)) break;
                        TryTickPlanet(planet, star, policy, ctx);
                    }
                }
        }

        private void TryTickPlanet(PlanetData planet, StarData star, ShipSpawnPolicyConfig policy, SpawnTickContext ctx)
        {
            if (planet == null || string.IsNullOrEmpty(planet.Race)) return;
            if (planet.Race == GalaxyConstants.RACE_NONE_KEY || planet.Race == GalaxyConstants.RACE_MIXED_KEY) return;

            // Гейт на owner — по эффективному контроллёру. Оккупированная планета переходит под
            // top-up оккупанта, родная сторона больше не спавнит гражданских.
            string effectiveOwner = OccupationService.GetControllingOwner(planet);
            if (policy.AllowedOwners != null && policy.AllowedOwners.Count > 0
                && !policy.AllowedOwners.Contains(effectiveOwner)) return;
            if (policy.AllowedRaces != null && policy.AllowedRaces.Count > 0
                && !policy.AllowedRaces.Contains(planet.Race)) return;

            int target = ResolveTarget(planet, policy);
            if (target <= 0) return;

            int count = ctx.Counters.GetShipsAtPlanet(planet.Uid, ShipTypeId);

            if (!SpawnFormulas.ShouldSpawn(count, target, policy.BaseChance)) return;

            SpawnSystem.SpawnShipAtPlanet(ShipTypeId, effectiveOwner, planet.Race, planet, star, ctx.Gen,
                reason: $"target={target} count={count}");
        }

        private static int ResolveTarget(PlanetData planet, ShipSpawnPolicyConfig policy)
        {
            int target = policy.BaseTarget;
            if (policy.TargetByRace != null && planet.Race != null
                && policy.TargetByRace.TryGetValue(planet.Race, out var byRace)) target = byRace;
            if (policy.EconomyBonus != null && !string.IsNullOrEmpty(planet.Settlement.EconomyType)
                && policy.EconomyBonus.TryGetValue(planet.Settlement.EconomyType, out var econBonus)) target += econBonus;
            return target;
        }
    }
}
