using UnityEngine;
using SRG.Config;
using SRG.Galaxy;
using SRG.Galaxy.Politics;

namespace SRG.NpcAI.Spawning.Policies
{
    /// <summary>
    /// Top-up per-planet политика для воинов (Warrior).
    /// Target = BaseTarget + RadiusBonus[bucket] + GovernmentBonus[planet.Government]
    ///          + (UseFleetSizeModifier ? race.FleetSizeModifier : 0)
    /// Clamp в нижней границе [1, ∞).
    ///
    /// Бакеты размера системы (фиксированные, без конфига — это техническая классификация):
    ///   SystemSize &lt; 5000   → "Small"
    ///   SystemSize &lt; 10000  → "Medium"
    ///   else                → "Large"
    /// </summary>
    public class WarriorSpawnPolicy : ISpawnPolicy
    {
        public string ShipTypeId => "Warrior";

        public void DailyTick(SpawnTickContext ctx)
        {
            var policy = ctx.Config.Spawn?.GetPolicy(ShipTypeId);
            if (policy == null || policy.BaseChance <= 0f) return;

            foreach (var sector in ctx.Galaxy.Sectors)
                foreach (var star in sector.Stars)
                {
                    if (!SpawnFormulas.StarCanSpawnMore(star, ctx.Settings)) continue;
                    // Квоту пере-проверяем перед КАЖДОЙ планетой: SpawnShipAtPlanet инкрементит
                    // star.SpawnsToday, и правило «≤ MaxShipsSpawnedPerStarPerDay спавнов в систему за день»
                    // должно применяться и внутри одного тика (иначе система с 5 планетами могла бы
                    // выдать до 5 воинов за ход — привет Turn 23 Warrior=37).
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

            // Оккупация: гейт по эффективному контроллёру. На захваченной планете родная сторона
            // больше не выпускает воинов — их место должен занять флот оккупанта (если у него есть policy).
            string effectiveOwner = OccupationService.GetControllingOwner(planet);
            if (policy.AllowedOwners != null && policy.AllowedOwners.Count > 0
                && !policy.AllowedOwners.Contains(effectiveOwner)) return;

            int target = ResolveTarget(planet, star, policy, ctx);
            if (target <= 0) return;

            int count = ctx.Counters.GetShipsAtPlanet(planet.Uid, ShipTypeId);
            if (!SpawnFormulas.ShouldSpawn(count, target, policy.BaseChance)) return;

            SpawnSystem.SpawnShipAtPlanet(ShipTypeId, effectiveOwner, planet.Race, planet, star, ctx.Gen,
                reason: $"target={target} count={count}");
        }

        private static int ResolveTarget(PlanetData planet, StarData star, ShipSpawnPolicyConfig policy, SpawnTickContext ctx)
        {
            int target = policy.BaseTarget;

            // Radius bonus по размеру системы
            if (policy.RadiusBonus != null)
            {
                string bucket = ResolveSizeBucket(star.SystemSize);
                if (policy.RadiusBonus.TryGetValue(bucket, out var radBonus)) target += radBonus;
            }

            // Government bonus
            if (policy.GovernmentBonus != null && !string.IsNullOrEmpty(planet.Settlement.Government)
                && policy.GovernmentBonus.TryGetValue(planet.Settlement.Government, out var govBonus)) target += govBonus;

            // FleetSizeModifier из конфига расы
            if (policy.UseFleetSizeModifier
                && ctx.Config.Races != null
                && ctx.Config.Races.TryGetValue(planet.Race, out var raceCfg) && raceCfg != null)
                target += raceCfg.FleetSizeModifier;

            return Mathf.Max(1, target);
        }

        private static string ResolveSizeBucket(int systemSize)
        {
            if (systemSize < 5000)  return "Small";
            if (systemSize < 10000) return "Medium";
            return "Large";
        }
    }
}
