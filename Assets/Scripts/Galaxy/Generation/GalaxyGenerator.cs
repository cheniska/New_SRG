using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Config;
using SRG.Galaxy.Politics;

namespace SRG.Galaxy.Generation
{
    public partial class GalaxyGenerator
    {
        private readonly GalaxyGenerationContext _ctx;
        private int _systemSizeMult = 1;
        private int _maxPlanetsPerStar = 12;
        private float _sectorRadius = GalaxyConstants.SECTOR_RADIUS;
        private float _minStarDist   = 0f;
        private float _minStarDistSq = 0f;
        private List<string> _planetaryRaces;

        private const float NearZero = 0.001f;
        private const int MinPlanetsPerStarHardMin = 1;

        public GalaxyGenerator(GalaxyGenerationContext context) => _ctx = context;

        internal void FinalizeStar(StarData star) => FinalizeStarProps(star);
        internal void FinalizePlanet(PlanetData planet, string size) => FinalizePlanetVisuals(planet, size);
        internal void GeneratePlanetSatellites(PlanetData planet) => GenerateSatellites(planet, null);
        internal void AssignOrbitRadiiToList(List<PlanetData> list) => AssignOrbitRadii(list);

        public GalaxyData Generate(int seed, string galaxyKey = null)
        {
            // Дефолт-ключ — из GalaxyConstants (настраивается через GameSettingsConfig.DefaultGalaxyKey),
            // а не хардкод "MilkyWay" в сигнатуре.
            if (string.IsNullOrEmpty(galaxyKey)) galaxyKey = GalaxyConstants.DEFAULT_GALAXY_KEY;
            var galaxyCfg = _ctx.Config.Galaxies?.GetValueOrDefault(galaxyKey)
                ?? throw new Exception($"Galaxy '{galaxyKey}' not found in config");

            var sw = Stopwatch.StartNew();

            for (long attempt = 0; attempt < MaxPlacementAttempts; attempt++)
            {
                long actualSeed = attempt == 0 ? seed : (seed ^ (attempt * 0x9E3779B9));
                UnityEngine.Random.InitState((int)(actualSeed ^ (actualSeed >> 32)));
                // Активная галактика ставится ДО Reset, чтобы RebuildNamePools подхватил
                // per-galaxy пул из TextConfig.Galaxies[galaxyKey].
                _ctx.ActiveGalaxyConfig = galaxyCfg;
                _ctx.ActiveGalaxyKey    = galaxyKey;
                _ctx.Reset();
                _systemSizeMult = _ctx.Config.Settings?.SystemSizeMult ?? 1;
                _planetaryRaces = null;
                if (_ctx.SystemSizeRules.Count > 0)
                {
                    _maxPlanetsPerStar = 0;
                    foreach (var r in _ctx.SystemSizeRules.Values)
                        if (r.MaxPlanets > _maxPlanetsPerStar) _maxPlanetsPerStar = r.MaxPlanets;
                }
                else
                    _maxPlanetsPerStar = GalaxyConstants.FALLBACK_MAX_PLANETS;

                var t0 = sw.ElapsedMilliseconds;
                PreReserveFixedNames(galaxyKey);

                var galaxy = new GalaxyData
                {
                    Key = galaxyKey,
                    Width  = galaxyCfg.GalaxyGridSize[0],
                    Height = galaxyCfg.GalaxyGridSize[1],
                    StartDateString = _ctx.Config.Settings?.InitialDate ?? "01.01.0001"
                };

                ComputeSpacingParams(galaxyCfg, galaxy);

                var tSectors = sw.ElapsedMilliseconds;
                GenerateSectors(galaxy, galaxyKey, galaxyCfg);
                UnityEngine.Debug.Log($"[GalaxyGen] Sectors: {sw.ElapsedMilliseconds - tSectors} ms");

                var tStars = sw.ElapsedMilliseconds;
                GenerateStars(galaxy, galaxyCfg);
                UnityEngine.Debug.Log($"[GalaxyGen] Stars: {sw.ElapsedMilliseconds - tStars} ms");

                var tOwners = sw.ElapsedMilliseconds;
                ResolveAllOwners(galaxy);
                UnityEngine.Debug.Log($"[GalaxyGen] Owners (1st): {sw.ElapsedMilliseconds - tOwners} ms");

                var tPlace = sw.ElapsedMilliseconds;
                if (!TryPlaceSectorPositions(galaxy, galaxyCfg))
                {
                    UnityEngine.Debug.LogWarning($"[GalaxyGenerator] Попытка {attempt + 1}/{MaxPlacementAttempts}: " +
                                     "расстановка секторов не удалась, перезапуск.");
                    continue;
                }
                UnityEngine.Debug.Log($"[GalaxyGen] Placement: {sw.ElapsedMilliseconds - tPlace} ms");

                var tVoronoi = sw.ElapsedMilliseconds;
                BuildVoronoi(galaxy);
                UnityEngine.Debug.Log($"[GalaxyGen] Voronoi: {sw.ElapsedMilliseconds - tVoronoi} ms");

                var tBalance = sw.ElapsedMilliseconds;
                BalanceStarDensityAcrossSectors(galaxy, galaxyCfg);
                UnityEngine.Debug.Log($"[GalaxyGen] Balance: {sw.ElapsedMilliseconds - tBalance} ms");

                var tReassign = sw.ElapsedMilliseconds;
                ReassignStarPositions(galaxy);
                RedistributeTooCloseSystems(galaxy);
                UnityEngine.Debug.Log($"[GalaxyGen] Reassign+Redistribute: {sw.ElapsedMilliseconds - tReassign} ms");

                if (_ctx.Config.Settings?.Expansion == 1)
                {
                    var tExpand = sw.ElapsedMilliseconds;
                    RunExpansion(galaxy);
                    ResolveAllOwners(galaxy);
                    UnityEngine.Debug.Log($"[GalaxyGen] Expansion+Owners: {sw.ElapsedMilliseconds - tExpand} ms");
                }

                var tFinalize = sw.ElapsedMilliseconds;
                PostResolutionFinalize(galaxy);
                galaxy.InitializeLookups();
                // Инициализация кэша CurrentSystemController по звёздам — учитывает раскрытую в
                // ApplyFixedOccupation стартовую оккупацию. Подписчиков в этот момент ещё нет,
                // событий не будет — просто заполняем кэш.
                foreach (var star in galaxy.StarsMap.Values)
                    OccupationService.RecomputeSystemControl(star);

                // Станции: 0–3 на сектор, вне орбит планет (см. план «Космические станции»).
                SRG.NpcAI.Spawning.SpawnSystem.SpawnStationsForGalaxy(galaxy, _ctx);
                UnityEngine.Debug.Log($"[GalaxyGen] Finalize: {sw.ElapsedMilliseconds - tFinalize} ms");

                sw.Stop();
                UnityEngine.Debug.Log($"[GalaxyGen] Total: {sw.ElapsedMilliseconds} ms | seed={seed} attempt={attempt + 1} " +
                          $"sectors={galaxy.Sectors.Count} stars={galaxy.StarsMap.Count} planets={galaxy.PlanetsMap.Count}");
                return galaxy;
            }

            throw new Exception(
                $"[GalaxyGenerator] Не удалось разместить сектора за {MaxPlacementAttempts} попыток.");
        }

        private void ComputeSpacingParams(GalaxyConfigData cfg, GalaxyData galaxy)
        {
            if (cfg.GalaxyGridSize == null || cfg.GalaxyGridSize.Length < 2
                || cfg.MinStarDistanceParsecs <= 0 || cfg.SectorCount <= 0)
                return;

            float gridPerPcX = galaxy.Width  / Mathf.Max(1f, cfg.GalaxyGridSize[0]);
            float gridPerPcY = galaxy.Height / Mathf.Max(1f, cfg.GalaxyGridSize[1]);
            float gridPerPc  = (gridPerPcX + gridPerPcY) * 0.5f;

            _minStarDist   = cfg.MinStarDistanceParsecs * gridPerPc;
            _minStarDistSq = _minStarDist * _minStarDist;

            float cellSize = Mathf.Sqrt((float)galaxy.Width * galaxy.Height / cfg.SectorCount);
            _sectorRadius  = cellSize * 0.5f;
        }

        private Vector2 GetRandomPos(int w, int h, Vector2? center = null, float radius = 0f)
        {
            if (center.HasValue && radius > 0)
            {
                Vector2 p = center.Value + UnityEngine.Random.insideUnitCircle * radius;
                return new Vector2(Mathf.Clamp(p.x, 0, w), Mathf.Clamp(p.y, 0, h));
            }
            return new Vector2(UnityEngine.Random.Range(0f, w), UnityEngine.Random.Range(0f, h));
        }

        private string ResolveDefaultOwner()
        {
            string def = _ctx.Config.Settings?.DefaultOwner;
            if (OwnerResolver.IsFixedOwner(def, _ctx.AvailableOwners)) return def;
            return GalaxyConstants.OWNER_UNRESOLVED_KEY;
        }

        private T PopRandom<T>(List<T> list)
        {
            if (list.Count == 0) return default;
            int i = UnityEngine.Random.Range(0, list.Count);
            T val = list[i]; list.RemoveAt(i);
            return val;
        }

        private static string ToRoman(int n)
        {
            if (n < 1) return string.Empty;
            string[] table = { "I","II","III","IV","V","VI","VII","VIII","IX","X",
                                "XI","XII","XIII","XIV","XV","XVI","XVII","XVIII","XIX","XX" };
            return n <= table.Length ? table[n - 1] : n.ToString();
        }

        private string ResolveValidSize(string candidate, List<string> valid, string fallback)
        {
            if (!string.IsNullOrEmpty(candidate) && valid.Contains(candidate)) return candidate;
            return valid.Count > 0 ? valid[UnityEngine.Random.Range(0, valid.Count)] : fallback;
        }

        private int GetSectorCapacity(SectorData c, GalaxyConfigData cfg) =>
            (c.FixedStarsCount ?? 0) > 0 ? c.FixedStarsCount.Value : cfg.MaxStarsPerSector;

        private int CalculateSystemSize(List<PlanetData> planets) =>
            GenerationHelpers.CalculateSystemSize(planets, _systemSizeMult);

        private static int CalculateNoneCount(int total) =>
            Mathf.RoundToInt(total * UnityEngine.Random.Range(0f, GalaxyConstants.NONE_ORBIT_MAX_FRACTION));

        private int ParseNoneCount(string planetsNone, int total)
        {
            if (string.IsNullOrEmpty(planetsNone)) return CalculateNoneCount(total);
            if (planetsNone.Contains("%") && float.TryParse(planetsNone.Replace("%", ""), out float pct))
                return Mathf.RoundToInt(total * (pct / 100f));
            return int.TryParse(planetsNone, out int n) ? n : 0;
        }

        private int ResolveTargetPlanetCount(FixedStarData fs, int alreadyCreated)
        {
            int min = Mathf.Max(MinPlanetsPerStarHardMin, _ctx.ActiveGalaxyConfig.MinPlanetsPerStar);
            int fromConfig = fs.PlanetsCount ?? UnityEngine.Random.Range(min, _ctx.ActiveGalaxyConfig.MaxPlanetsPerStar + 1);
            return Mathf.Max(fromConfig, alreadyCreated);
        }

        private string ResolveRandomRace(string raceHint)
        {
            if (OwnerResolver.IsFixedRace(raceHint, _ctx.AvailableRaces) && !IsNonPlanetaryRace(raceHint)) return raceHint;
            if (_planetaryRaces == null)
            {
                _planetaryRaces = new List<string>();
                // Мульти-галактика: фильтруем «планетарные» расы по списку, разрешённому текущей галактикой
                // (ActiveGalaxyConfig.Races). Если список пуст/не задан — открытая совместимость: берём всё.
                var galaxyRaces = _ctx.ActiveGalaxyConfig?.Races;
                foreach (var k in _ctx.AvailableRaces.Keys)
                {
                    if (IsNonPlanetaryRace(k)) continue;
                    if (galaxyRaces != null && galaxyRaces.Count > 0 && !galaxyRaces.Contains(k)) continue;
                    _planetaryRaces.Add(k);
                }
                if (_planetaryRaces.Count == 0)
                    UnityEngine.Debug.LogWarning("[GalaxyGenerator] ResolveRandomRace: no planetary races available — returning RACE_NONE_KEY.");
            }
            return _planetaryRaces.Count > 0 ? _planetaryRaces[UnityEngine.Random.Range(0, _planetaryRaces.Count)] : GalaxyConstants.RACE_NONE_KEY;
        }

        /// <summary>Расы, «принадлежащие» текущей галактике по PremadeConfig/GalaxyConfig.Races.
        /// Возвращает null если список не задан (совместимость со старыми конфигами — тогда пропускаем фильтр).</summary>
        internal HashSet<string> GetActiveGalaxyRaceSet()
        {
            var list = _ctx.ActiveGalaxyConfig?.Races;
            if (list == null || list.Count == 0) return null;
            return new HashSet<string>(list);
        }
    }
}
