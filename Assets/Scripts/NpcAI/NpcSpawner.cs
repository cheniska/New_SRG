using UnityEngine;
using System.Collections.Generic;
using SRG.Config;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.NpcAI.Spawning;
using SRG.NpcAI.Spawning.Policies;
using SRG.Ships;
using SRG.Utils;

namespace SRG.NpcAI
{
    /// <summary>
    /// Стартовая раздача NPC при генерации галактики.
    ///   • <see cref="PopulateStarWithNpcs"/> — спавн per-planet и per-system типов из InitialDistribution.
    ///   • <see cref="PopulateSectorEntities"/> — спавн per-sector типов (линкоры).
    ///   • <see cref="FinalizeAfterGeneration"/> — финальный пересчёт счётчиков и инициализация SpawnSystem.
    ///
    /// Числа берутся из <see cref="InitialDistributionConfig"/> (секция Spawn.InitialDistribution).
    /// </summary>
    public static class NpcSystemSpawner
    {

        // ──────────────────────────────────────────
        // Per-star: гражданские + воины (per-planet) + вольные пилоты/пираты (per-system)
        // ──────────────────────────────────────────

        public static void PopulateStarWithNpcs(StarData star, GalaxyGenerationContext ctx)
        {
            if (star == null || ctx == null) return;
            var initCfg = ctx.Config?.Spawn?.InitialDistribution;
            if (initCfg == null) return;

            bool hasInhabitable = false;
            foreach (var planet in star.Planets)
                if (IsInhabitableForSpawn(planet, ctx)) { hasInhabitable = true; break; }
            if (!hasInhabitable) return;

            // 1) PerHabitablePlanet: для каждой обитаемой планеты — N штук каждого типа.
            if (initCfg.PerHabitablePlanet != null)
                foreach (var planet in star.Planets)
                {
                    if (!IsInhabitableForSpawn(planet, ctx)) continue;
                    foreach (var kv in initCfg.PerHabitablePlanet)
                    {
                        string shipTypeId = kv.Key;
                        int count = kv.Value;
                        for (int i = 0; i < count; i++)
                            SpawnOneAtPlanet(shipTypeId, planet, star, ctx, landed: true);
                    }
                }

            // 2) PerSystem: на звезду — N штук каждого типа, у случайной обитаемой планеты.
            if (initCfg.PerSystem != null && initCfg.PerSystem.Count > 0)
            {
                var habitable = new List<PlanetData>();
                foreach (var p in star.Planets) if (IsInhabitableForSpawn(p, ctx)) habitable.Add(p);
                if (habitable.Count > 0)
                    foreach (var kv in initCfg.PerSystem)
                    {
                        string shipTypeId = kv.Key;
                        int count = kv.Value;
                        for (int i = 0; i < count; i++)
                        {
                            var planet = habitable[i % habitable.Count];
                            SpawnOneAtPlanet(shipTypeId, planet, star, ctx, landed: true);
                        }
                    }
            }
        }

        // ──────────────────────────────────────────
        // Per-sector: линкоры
        // ──────────────────────────────────────────

        public static void PopulateSectorEntities(SectorData sector, GalaxyGenerationContext ctx)
        {
            if (sector == null || ctx == null) return;
            var initCfg = ctx.Config?.Spawn?.InitialDistribution;
            if (initCfg?.PerSector == null || initCfg.PerSector.Count == 0) return;

            // Выбираем sector capital — обитаемую планету с максимальным населением.
            PlanetData best = null; StarData bestStar = null; int bestPop = -1;
            foreach (var star in sector.Stars)
                foreach (var planet in star.Planets)
                {
                    if (!IsInhabitableForSpawn(planet, ctx)) continue;
                    if (planet.Settlement.Population <= bestPop) continue;
                    best = planet; bestStar = star; bestPop = planet.Settlement.Population;
                }
            if (best == null || bestStar == null) return;

            foreach (var kv in initCfg.PerSector)
            {
                string shipTypeId = kv.Key;
                int count = kv.Value;
                for (int i = 0; i < count; i++)
                    SpawnOneAtPlanet(shipTypeId, best, bestStar, ctx, landed: true);
            }
        }

        /// <summary>Финализация после стартовой раздачи: регистрация политик и пересчёт счётчиков.</summary>
        public static void FinalizeAfterGeneration(GalaxyData galaxy, GalaxyGenerationContext ctx)
        {
            if (galaxy == null || ctx?.Config == null) return;
            SpawnSystem.RegisterDefaultPolicies();
            SpawnSystem.RecountFromGalaxy(galaxy, ctx.Config);
        }

        // ──────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────

        /// <summary>Планета пригодна для стартового спавна NPC. Делегирует в <see cref="PlanetPredicates.IsInitialSpawnViable"/>.</summary>
        private static bool IsInhabitableForSpawn(PlanetData planet, GalaxyGenerationContext ctx)
            => PlanetPredicates.IsInitialSpawnViable(planet, ctx?.Config);

        /// <summary>Создаёт один корабль у планеты через общий SpawnSystem.SpawnShipAtPlanet,
        /// но подменяет имя на initial-именование (GenerateNpcName). Логирует как INITIAL_SPAWN.</summary>
        private static void SpawnOneAtPlanet(string shipTypeId, PlanetData planet, StarData star, GalaxyGenerationContext ctx, bool landed)
        {
            if (string.IsNullOrEmpty(shipTypeId) || planet == null || star == null) return;
            if (!ctx.AvailableShipTypes.ContainsKey(shipTypeId)) return;

            var ship = SpawnSystem.SpawnShipAtPlanet(shipTypeId, planet.Owner, planet.Race, planet, star, ctx,
                reason: "initial_distribution", landed: landed);
            if (ship == null) return;
            ship.Name = GenerateNpcName(shipTypeId, ctx.NextNpcIndex++);
        }

        private static string GenerateNpcName(string shipTypeId, int index)
        {
            return shipTypeId switch
            {
                "Pirate"    => $"Пират-{index + 1}",
                "Transport" => $"Транспорт-{index + 1}",
                "Liner"     => $"Лайнер-{index + 1}",
                "Diplomat"  => $"Дипломат-{index + 1}",
                "Warrior"   => $"Воин-{index + 1}",
                "Linkor"    => $"Линкор-{index + 1}",
                "Ranger"    => $"Вольный пилот-{index + 1}",
                "Dom1" or "Dom2" or "Dom3" or "Dom4" or "Dom5" or "Dom6" => $"Синтет-{index + 1}",
                _ => $"NPC-{index + 1}"
            };
        }
    }
}
