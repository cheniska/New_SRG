using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Config;
using SRG.Galaxy;
using SRG.Galaxy.Politics;

namespace SRG.NpcAI.Spawning.Policies
{
    /// <summary>
    /// Top-up per-galaxy политика для свободных пиратов (не клановых).
    ///   target = NormalSystemsCount × TargetFromNormalsMultiplier × CrimeMultiplier
    /// где CrimeMultiplier = clamp(AvgCrime / Divisor, Min, Max).
    ///
    /// Спавн на одной из планет коалиц. системы. Приоритет — звёзды с высоким Crime (сектор).
    /// </summary>
    public class PirateSpawnPolicy : ISpawnPolicy
    {
        public string ShipTypeId => "Pirate";

        public void DailyTick(SpawnTickContext ctx)
        {
            var policy = ctx.Config.Spawn?.GetPolicy(ShipTypeId);
            if (policy == null || policy.BaseChance <= 0f) return;

            float crimeMult = ResolveCrimeMultiplier(ctx, policy);
            int target = Mathf.RoundToInt(ctx.Counters.NormalSystemsCount * policy.TargetFromNormalsMultiplier * crimeMult);
            if (target <= 0) return;

            int count = ctx.Counters.GetShips(ShipTypeId);
            if (!SpawnFormulas.ShouldSpawn(count, target, policy.BaseChance)) return;

            TrySpawnNearCoalition(policy, ctx, target, count);
        }

        private static float ResolveCrimeMultiplier(SpawnTickContext ctx, ShipSpawnPolicyConfig policy)
        {
            var cm = policy.CrimeMultiplier;
            if (cm == null || cm.Divisor <= 0f) return 1f;
            float raw = ctx.Counters.AverageCrimeRating / cm.Divisor;
            return Mathf.Clamp(raw, cm.Min, cm.Max);
        }

        private void TrySpawnNearCoalition(ShipSpawnPolicyConfig policy, SpawnTickContext ctx, int target, int count)
        {
            var candidates = CollectCandidates(ctx);
            if (candidates.Count == 0) return;

            // Свободно владеют пиратами — но раса корабля = раса родной планеты, чтобы AI/диалоги работали.
            var (planet, star) = candidates[Random.Range(0, candidates.Count)];
            string pirateOwner = "Pirates";

            SpawnSystem.SpawnShipAtPlanet(ShipTypeId, pirateOwner, planet.Race, planet, star, ctx.Gen,
                reason: $"target={target} count={count}");
        }

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
                        // Оккупированные планеты не порождают локальных пиратов — там другая власть.
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
