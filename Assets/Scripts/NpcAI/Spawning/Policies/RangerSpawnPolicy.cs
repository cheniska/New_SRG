using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Config;
using SRG.Galaxy;
using SRG.Galaxy.Politics;

namespace SRG.NpcAI.Spawning.Policies
{
    /// <summary>
    /// Top-up per-galaxy политика для вольных пилотов.
    /// Один ролл в день на всю галактику.
    ///   target = min(NormalSystemsCount × TargetFromNormalsMultiplier, TargetGlobalCap)
    /// При срабатывании спавн у случайной обитаемой коалиц. планеты с населением > 0.
    /// Игрок-вольный пилот не учитывается (счётчик не считает IsPlayer).
    /// </summary>
    public class RangerSpawnPolicy : ISpawnPolicy
    {
        public string ShipTypeId => "Ranger";

        public void DailyTick(SpawnTickContext ctx)
        {
            var policy = ctx.Config.Spawn?.GetPolicy(ShipTypeId);
            if (policy == null || policy.BaseChance <= 0f) return;

            int target = Mathf.RoundToInt(ctx.Counters.NormalSystemsCount * policy.TargetFromNormalsMultiplier);
            if (policy.TargetGlobalCap > 0) target = Mathf.Min(target, policy.TargetGlobalCap);
            if (target <= 0) return;

            int count = ctx.Counters.GetShips(ShipTypeId);
            if (!SpawnFormulas.ShouldSpawn(count, target, policy.BaseChance)) return;

            TrySpawnAtRandomCoalitionPlanet(policy, ctx, target, count);
        }

        private void TrySpawnAtRandomCoalitionPlanet(ShipSpawnPolicyConfig policy, SpawnTickContext ctx, int target, int count)
        {
            var candidates = CollectCandidates(ctx);
            if (candidates.Count == 0) return;
            var (planet, star) = candidates[Random.Range(0, candidates.Count)];
            SpawnSystem.SpawnShipAtPlanet(ShipTypeId, planet.Owner, planet.Race, planet, star, ctx.Gen,
                reason: $"target={target} count={count}");
        }

        /// <summary>Все обитаемые коалиц. планеты с населением, на которых cooldown звезды готов.</summary>
        private static List<(PlanetData, StarData)> CollectCandidates(SpawnTickContext ctx)
        {
            var result = new List<(PlanetData, StarData)>();
            foreach (var sector in ctx.Galaxy.Sectors)
                foreach (var star in sector.Stars)
                {
                    if (!SpawnFormulas.StarCanSpawnMore(star, ctx.Settings)) continue;
                    foreach (var planet in star.Planets)
                    {
                        if (planet == null) continue;
                        // Оккупированные планеты выпадают: вольные пилоты Содружества не спавнятся под чужим флагом.
                        if (OccupationService.GetControllingOwner(planet) != "Coalition") continue;
                        if (planet.Settlement.Population <= 0) continue;
                        if (string.IsNullOrEmpty(planet.Race)
                            || planet.Race == GalaxyConstants.RACE_NONE_KEY
                            || planet.Race == GalaxyConstants.RACE_MIXED_KEY) continue;
                        result.Add((planet, star));
                    }
                }
            return result;
        }
    }
}
