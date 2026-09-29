using System.Collections.Generic;
using UnityEngine;
using System;
using SRG.Config;
using SRG.NpcAI;

namespace SRG.Galaxy.Generation
{
    public class RaceNamePool
    {
        public Dictionary<string, string> Fixed { get; } = new();
        public List<string> Pool { get; } = new();
    }

    public class SystemSizeDef { public int MaxPlanets; }

    public class GalaxyGenerationContext
    {
        public GalaxyConfig Config { get; }
        public TextConfig TextConfig { get; }
        public PremadeConfig PremadeConfig { get; }
        public ItemsConfig ItemsConfig { get; set; }

        public GalaxyConfigData ActiveGalaxyConfig { get; set; }
        /// <summary>Ключ активной галактики (из <see cref="GalaxyConfig.Galaxies"/>). Используется в
        /// <see cref="RebuildNamePools"/> для выбора per-galaxy пула из <see cref="TextConfig.Galaxies"/>.
        /// Ставится в <see cref="Generation.GalaxyGenerator.Generate"/> перед <see cref="Reset"/>.</summary>
        public string ActiveGalaxyKey { get; set; }
        public List<string> AvailablePlanetSizes { get; private set; }
        public List<string> AvailablePlanetTypes { get; private set; }
        public List<string> AvailableStarTypes { get; private set; }
        public List<string> AvailableSatelliteSizes { get; private set; }

        public Dictionary<string, RaceInfo> AvailableRaces { get; } = new();
        public Dictionary<string, OwnerConfig> AvailableOwners { get; private set; } = new();
        public Dictionary<string, ShipTypeConfig> AvailableShipTypes { get; private set; } = new();
        public List<string> AvailableShipTypeKeys { get; private set; } = new();
        /// <summary>RaceKey → список ShipTypeId, доступных этой расе (источник: RaceConfig.AvailableShipTypes).</summary>
        public Dictionary<string, List<string>> RaceLineups { get; private set; } = new();
        public Dictionary<string, SystemSizeDef> SystemSizeRules { get; private set; } = new();
        public Dictionary<string, PlanetSizeData> PlanetSizeRules { get; private set; } = new();

        public Dictionary<string, RaceNamePool> StarNamesByRace { get; } = new();
        public Dictionary<string, RaceNamePool> PlanetNamesByRace { get; } = new();
        public RaceNamePool CommonStarNames { get; private set; } = new();
        public RaceNamePool CommonPlanetNames { get; private set; } = new();
        public List<string> SectorNamePool { get; } = new();

        /// <summary>Активный (для текущей галактики) словарь имён секторов: сначала берётся из
        /// <see cref="TextConfig.Galaxies"/>[<see cref="ActiveGalaxyKey"/>].SectorNames, при отсутствии
        /// — из legacy-корневого <see cref="TextConfig.SectorNames"/>. Используется в
        /// <see cref="Generation.GalaxyGenerator.ResolveSectorName"/> и PreReserveFixedNames.</summary>
        public Dictionary<string, string> ActiveSectorNames { get; private set; }

        public HashSet<string> UsedStarNames { get; } = new();
        public HashSet<string> UsedPlanetNames { get; } = new();

        public int UnnamedSectorCounter = 0;
        public int UnnamedStarCounter = 0;
        public int UnnamedPlanetCounter = 0;

        /// <summary>Сквозной счётчик имён NPC при стартовой раздаче (см. NpcSystemSpawner).</summary>
        public int NextNpcIndex = 0;

        public GalaxyGenerationContext(GalaxyConfig cfg, TextConfig txt, PremadeConfig pre,
            ItemsConfig itemsConfig = null)
        {
            Config = cfg;
            TextConfig = txt;
            PremadeConfig = pre;
            ItemsConfig = itemsConfig;
            RebuildPools();
        }

        public void Reset()
        {
            UnnamedSectorCounter = UnnamedStarCounter = UnnamedPlanetCounter = 0;
            UsedStarNames.Clear();
            UsedPlanetNames.Clear();
            RebuildPools();
        }

        private void RebuildPools()
        {
            RebuildSizeLists();
            RebuildSystemSizeRules();
            RebuildRaces();
            RebuildNamePools();
            RebuildShips();
        }

        private void RebuildSizeLists()
        {
            PlanetSizeRules.Clear();
            if (Config.Planets?.Sizes == null)
                throw new InvalidOperationException("[GalaxyGenerationContext] Config missing Planets.Sizes.");

            AvailablePlanetSizes = new List<string>(Config.Planets.Sizes.Count);
            foreach (var kvp in Config.Planets.Sizes)
            {
                AvailablePlanetSizes.Add(kvp.Key);
                PlanetSizeRules[kvp.Key] = kvp.Value;
            }

            AvailablePlanetTypes = Config.Planets?.Types ?? new List<string>();

            if (Config.Stars?.Types == null)
                throw new InvalidOperationException("[GalaxyGenerationContext] Config missing Stars.Types.");

            AvailableStarTypes = new List<string>(Config.Stars.Types.Keys);

            if (Config.Satellites?.Sizes == null)
                throw new InvalidOperationException("[GalaxyGenerationContext] Config missing Satellites.Sizes.");

            AvailableSatelliteSizes = new List<string>(Config.Satellites.Sizes.Keys);
        }

        private void RebuildSystemSizeRules()
        {
            SystemSizeRules.Clear();
            if (Config.Stars?.Types != null)
                foreach (var kvp in Config.Stars.Types)
                    SystemSizeRules[kvp.Key] = new SystemSizeDef { MaxPlanets = kvp.Value.MaxPlanets };

            if (SystemSizeRules.Count == 0)
                throw new InvalidOperationException("[GalaxyGenerationContext] Stars.Types is empty — cannot build SystemSizeRules. Check GalaxyConfig.");
        }

        private void RebuildRaces()
        {
            AvailableRaces.Clear();
            if (Config.Races == null) return;

            foreach (var kvp in Config.Races)
            {
                if (kvp.Key == GalaxyConstants.RACE_NONE_KEY) continue;
                string name = TextConfig?.RaceNames?.TryGetValue(kvp.Key, out string n) == true ? n : kvp.Key;
                string color = kvp.Value.Color ?? GalaxyConstants.DEFAULT_COLOR;
                AvailableRaces[kvp.Key] = new RaceInfo(name, color, kvp.Value.EmblemPath ?? GalaxyConstants.FALLBACK_EMBLEM_PATH);
            }
        }

        private void RebuildNamePools()
        {
            StarNamesByRace.Clear(); PlanetNamesByRace.Clear(); SectorNamePool.Clear();
            CommonStarNames = new RaceNamePool();
            CommonPlanetNames = new RaceNamePool();

            // Мульти-галактика: пул имён — сначала из TextConfig.Galaxies[ActiveGalaxyKey] (per-galaxy),
            // при отсутствии — из legacy-корневых TextConfig.SectorNames/StarsNames/PlanetsNames.
            TextConfigGalaxyData galaxyText = null;
            if (!string.IsNullOrEmpty(ActiveGalaxyKey) && TextConfig?.Galaxies != null)
                TextConfig.Galaxies.TryGetValue(ActiveGalaxyKey, out galaxyText);

            ActiveSectorNames = galaxyText?.SectorNames ?? TextConfig?.SectorNames;
            var starsSrc      = galaxyText?.StarsNames   ?? TextConfig?.StarsNames;
            var planetsSrc    = galaxyText?.PlanetsNames ?? TextConfig?.PlanetsNames;

            if (ActiveSectorNames != null) SectorNamePool.AddRange(ActiveSectorNames.Values);
            ParseNameSection(starsSrc,   StarNamesByRace,   CommonStarNames);
            ParseNameSection(planetsSrc, PlanetNamesByRace, CommonPlanetNames);
        }

        private static void ParseNameSection(Dictionary<string, RaceNamesData> source,
            Dictionary<string, RaceNamePool> target, RaceNamePool commonPool)
        {
            if (source == null) return;
            foreach (var (race, data) in source)
            {
                var pool = new RaceNamePool();
                if (data.Fixed != null) foreach (var kv in data.Fixed) pool.Fixed[kv.Key] = kv.Value;
                if (data.Pool != null) pool.Pool.AddRange(data.Pool);

                if (race == GalaxyConstants.RACE_COMMON_KEY)
                {
                    foreach (var kv in pool.Fixed) commonPool.Fixed[kv.Key] = kv.Value;
                    commonPool.Pool.AddRange(pool.Pool);
                }
                else target[race] = pool;
            }
        }

        private void RebuildShips()
        {
            AvailableOwners.Clear();
            AvailableShipTypes.Clear(); AvailableShipTypeKeys.Clear(); RaceLineups.Clear();
            var ships = Config.Ships;
            if (ships != null)
            {
                if (ships.Owners != null) foreach (var kvp in ships.Owners) AvailableOwners[kvp.Key] = kvp.Value;
                if (ships.ShipTypes != null) foreach (var kvp in ships.ShipTypes) { AvailableShipTypes[kvp.Key] = kvp.Value; AvailableShipTypeKeys.Add(kvp.Key); }
            }
            if (Config.Races != null)
                foreach (var kvp in Config.Races)
                {
                    var list = kvp.Value?.AvailableShipTypes;
                    if (list != null && list.Count > 0) RaceLineups[kvp.Key] = list;
                }
        }

        public string ResolveStarNameFromPlanets(StarData star)
        {
            string singleRace = null;
            bool multiple = false;
            foreach (var planet in star.Planets)
            {
                if (planet.Race == GalaxyConstants.RACE_NONE_KEY) continue;
                if (singleRace == null) singleRace = planet.Race;
                else if (planet.Race != singleRace) { multiple = true; break; }
            }
            string name = (!multiple && singleRace != null) ? PopStarName(singleRace) : null;
            return name ?? PopStarName(null);
        }

        private string PopStarName(string raceKey)
        {
            if (!string.IsNullOrEmpty(raceKey)
                && StarNamesByRace.TryGetValue(raceKey, out var racePool) && racePool.Pool.Count > 0)
            {
                string n = GenerationHelpers.PopRandomUnused(racePool.Pool, UsedStarNames);
                if (n != null) return n;
            }
            if (CommonStarNames.Pool.Count > 0)
            {
                string n = GenerationHelpers.PopRandomUnused(CommonStarNames.Pool, UsedStarNames);
                if (n != null) return n;
            }
            return $"UNNAMED_S_{++UnnamedStarCounter}";
        }

        public void ResolvePlanetName(PlanetData planet, string starRace = null, bool forceRename = false)
        {
            if (!forceRename && planet.Name != GalaxyConstants.VAL_UNKNOWN && !planet.Name.StartsWith("UNNAMED")) return;

            string raceKey;
            if (planet.Race != GalaxyConstants.RACE_NONE_KEY)
                raceKey = planet.Race;
            else if (!string.IsNullOrEmpty(starRace)
                && starRace != GalaxyConstants.RACE_NONE_KEY
                && starRace != GalaxyConstants.RACE_MIXED_KEY)
                raceKey = starRace;
            else
                raceKey = GalaxyConstants.RACE_COMMON_KEY;

            string n = PopPlanetName(raceKey);
            planet.Name = n ?? $"UNNAMED_P_{++UnnamedPlanetCounter}";
        }

        public void ColonizePlanet(PlanetData planet, string raceKey)
        {
            planet.Race = raceKey;
            ResolvePlanetName(planet, forceRename: true);
        }

        private string PopPlanetName(string raceKey)
        {
            if (raceKey != GalaxyConstants.RACE_COMMON_KEY
                && PlanetNamesByRace.TryGetValue(raceKey, out var racePool)
                && racePool.Pool.Count > 0)
            {
                string poolEntry;
                while ((poolEntry = GenerationHelpers.PopRandomUnused(racePool.Pool, UsedPlanetNames)) != null)
                {
                    string display = racePool.Fixed.TryGetValue(poolEntry, out string d) ? d : poolEntry;
                    if (UsedPlanetNames.Add(display))
                        return display;
                }
            }

            string key;
            while ((key = GenerationHelpers.PopRandomUnused(CommonPlanetNames.Pool, UsedPlanetNames)) != null)
            {
                string displayName = CommonPlanetNames.Fixed.TryGetValue(key, out string dn) ? dn : key;
                if (UsedPlanetNames.Add(displayName))
                    return displayName;
            }
            return null;
        }
    }
}
