using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Config;

namespace SRG.Galaxy.Generation
{
    public partial class GalaxyGenerator
    {
        private List<PlanetData> CreatePlanetsRandom(string starOwner, string starRace, string starType = null)
        {
            int min = Mathf.Max(MinPlanetsPerStarHardMin, _ctx.ActiveGalaxyConfig.MinPlanetsPerStar);
            int typeMax = (starType != null && _ctx.SystemSizeRules.TryGetValue(starType, out var sizeRule))
                ? sizeRule.MaxPlanets : _maxPlanetsPerStar;
            int total = UnityEngine.Random.Range(min, typeMax + 1);

            var slots = new List<int>(total);
            for (int i = 1; i <= total; i++) slots.Add(i);
            int noneCount = CalculateNoneCount(total);

            var list = new List<PlanetData>(total);
            for (int i = 0; i < total; i++)
            {
                var planet = CreatePlanet(starOwner, starRace, null, forceNone: i < noneCount);
                planet.OrbitIndex = PopRandom(slots);
                list.Add(planet);
            }

            AssignOrbitsAndNames(list, total, new HashSet<int>(), null);
            return list;
        }

        private List<PlanetData> CreatePlanetsMixed(StarData star, FixedStarData fs, string inheritedOccupiedBy = null)
        {
            var (fixedPlanets, usedOrbits, fixedNoneCount) = CollectFixedPlanets(star, fs, inheritedOccupiedBy);

            int targetTotal = ResolveTargetPlanetCount(fs, fixedPlanets.Count);
            int noneNeeded = Mathf.Max(0, ParseNoneCount(fs.PlanetsNoneCount, targetTotal) - fixedNoneCount);
            int toGenerate = targetTotal - fixedPlanets.Count;

            for (int i = 0; i < toGenerate; i++)
                fixedPlanets.Add(CreatePlanet(star.Owner, star.Race, null, forceNone: i < noneNeeded));

            EnsureRacialPlanet(fixedPlanets, star.Race, star.Owner);
            AssignOrbitsAndNames(fixedPlanets, targetTotal, usedOrbits, star);
            return fixedPlanets;
        }

        private void EnsureRacialPlanet(List<PlanetData> planets, string starRace, string starOwner)
        {
            if (string.IsNullOrEmpty(starRace)
                || starRace == GalaxyConstants.RACE_NONE_KEY
                || starRace == GalaxyConstants.RACE_MIXED_KEY
                || IsNonPlanetaryRace(starRace))
                return;

            foreach (var p in planets)
                if (p.Race == starRace) return;

            PlanetData candidate = null;
            foreach (var p in planets)
                if (p.Race == GalaxyConstants.RACE_NONE_KEY) { candidate = p; break; }
            if (candidate == null) return;

            candidate.Race  = starRace;
            if (starOwner != GalaxyConstants.OWNER_NONE_KEY) candidate.Owner = starOwner;
            candidate.CurrentColor = GenerationHelpers.GetRaceColor(starRace, _ctx.AvailableRaces);
            if (string.IsNullOrEmpty(candidate.OrbitalObjects) && UnityEngine.Random.value < GalaxyConstants.ORBITAL_OBJ_CHANCE)
                candidate.OrbitalObjects = GenerationHelpers.GetRandomOrbitalPath();
        }

        private (List<PlanetData> planets, HashSet<int> usedOrbits, int noneCount) CollectFixedPlanets(
            StarData star, FixedStarData fs, string inheritedOccupiedBy = null)
        {
            var list = new List<PlanetData>();
            var usedOrbits = new HashSet<int>();
            int noneCount = 0;

            if (fs.Planets == null) return (list, usedOrbits, noneCount);

            foreach (var fp in fs.Planets.Values)
            {
                string baseRace = fp.Race ?? GalaxyConstants.RACE_NONE_KEY;
                bool isNone = baseRace == GalaxyConstants.RACE_NONE_KEY;
                string resolvedOwner = isNone ? GalaxyConstants.OWNER_NONE_KEY
                    : OwnerResolver.ResolveOwner(fp.CurOwner ?? star.Owner, star.Owner, _ctx.AvailableOwners);

                if (isNone) noneCount++;

                string resolvedName = ResolveFixedPlanetName(fp.Name, baseRace, star.Race);
                var planet = CreatePlanet(resolvedOwner, baseRace, fp, forceNone: false);
                ApplyFixedOccupation(planet, fp, inheritedOccupiedBy);
                planet.Name = resolvedName;
                planet.Type = "None";
                RollGravityDensity(planet, planet.Size, fp.SurfaceGravity, fp.Density, fp.GeoActivity, fp.TotalSurfaceArea);
                if (fp.AtmPressure.HasValue)    planet.AtmPressureFixed    = fp.AtmPressure.Value;
                if (fp.OxygenPercent.HasValue)  planet.OxygenPercentFixed  = fp.OxygenPercent.Value;
                if (fp.WaterAbundance.HasValue) planet.WaterAbundanceFixed = fp.WaterAbundance.Value;
                if (fp.SurfaceTemp.HasValue)    planet.SurfaceTempFixed    = fp.SurfaceTemp.Value;
                if (fp.SurfaceLiquid.HasValue)    planet.SurfaceLiquidFixed    = fp.SurfaceLiquid.Value;
                if (fp.SurfaceMountains.HasValue) planet.SurfaceMountainsFixed = fp.SurfaceMountains.Value;

                if (!string.IsNullOrEmpty(planet.Name) && planet.Name != GalaxyConstants.VAL_UNKNOWN)
                    _ctx.UsedPlanetNames.Add(planet.Name);

                if (fp.OrbitIndex.HasValue && fp.OrbitIndex > 0)
                {
                    planet.OrbitIndex = fp.OrbitIndex.Value;
                    usedOrbits.Add(planet.OrbitIndex);
                }
                list.Add(planet);
            }
            return (list, usedOrbits, noneCount);
        }

        private PlanetData CreatePlanet(string owner, string race, FixedPlanetData fp, bool forceNone)
        {
            var planet = new PlanetData();
            string size = RollWeightedPlanetSize();

            if (fp != null) ApplyFixedPlanetProps(planet, fp, owner, race, ref size);
            else ApplyRandomPlanetProps(planet, owner, race, forceNone);

            FinalizePlanetVisuals(planet, size);
            return planet;
        }

        private string RollWeightedPlanetSize()
        {
            var sizes = _ctx.AvailablePlanetSizes;
            var rules = _ctx.PlanetSizeRules;
            int total = 0;
            for (int i = 0; i < sizes.Count; i++)
                total += rules.TryGetValue(sizes[i], out var r) ? Mathf.Max(0, r.Weight) : 1;

            if (total <= 0) return sizes[UnityEngine.Random.Range(0, sizes.Count)];

            int roll = UnityEngine.Random.Range(0, total), sum = 0;
            for (int i = 0; i < sizes.Count; i++)
            {
                int w = rules.TryGetValue(sizes[i], out var r) ? Mathf.Max(0, r.Weight) : 1;
                sum += w;
                if (roll < sum) return sizes[i];
            }
            return sizes[sizes.Count - 1];
        }

        private void ApplyFixedPlanetProps(PlanetData planet, FixedPlanetData fp,
            string resolvedOwner, string resolvedRace, ref string size)
        {
            bool isNone = (fp.Race ?? GalaxyConstants.RACE_NONE_KEY) == GalaxyConstants.RACE_NONE_KEY;
            planet.Race  = fp.Race ?? GalaxyConstants.RACE_NONE_KEY;
            planet.Owner = isNone ? GalaxyConstants.OWNER_NONE_KEY : resolvedOwner;
            planet.Name = string.IsNullOrEmpty(fp.Name) ? GalaxyConstants.VAL_UNKNOWN : fp.Name;

            if (!string.IsNullOrEmpty(fp.Size) && _ctx.AvailablePlanetSizes.Contains(fp.Size)) size = fp.Size;

            planet.Graphic = GenerationHelpers.CleanResourcePath(fp.Graphic);
            planet.OrbitalObjects = GenerationHelpers.CleanResourcePath(fp.OrbitalObjects);
            planet.Atmosphere = GenerationHelpers.CleanResourcePath(fp.Atmosphere);
            planet.OrbitSpeed = fp.OrbitSpeed ?? 0f;
            planet.DaySpeed = fp.DaySpeed ?? 0;
            planet.CloudsSpeed = fp.CloudsSpeed ?? 0;
            if (fp.AxialTilt.HasValue) planet.AxialTilt = Mathf.Clamp(fp.AxialTilt.Value, 0f, 90f);
            planet.OrbitRadius = fp.OrbitRadius ?? 0f;
            planet.HasFixedOrbitRadius = fp.OrbitRadius.HasValue && fp.OrbitRadius.Value > 0f;
            planet.SatellitesCount = fp.SatellitesCount ?? -1;
        }

        /// <summary>
        /// Раскрытие OccupiedBy/LandingBlocked из premade на PlanetData.
        /// Приоритет OccupiedBy: FixedPlanetData &gt; наследованное со звезды/сектора.
        /// Пустая строка на планете (fp.OccupiedBy=="") — явный сброс: планета не оккупирована,
        /// даже если родитель оккупирован.
        /// LandingBlocked: если задано явно на планете — используем; иначе автомат по режиму оккупанта
        /// (Full ⇒ true, Partial ⇒ false).
        /// </summary>
        private void ApplyFixedOccupation(PlanetData planet, FixedPlanetData fp, string inheritedOccupiedBy)
        {
            string occup;
            if (fp.OccupiedBy == null) occup = inheritedOccupiedBy;   // не задан — наследуем
            else if (fp.OccupiedBy == "") occup = null;                // явно очищено
            else occup = fp.OccupiedBy;

            if (string.IsNullOrEmpty(occup))
            {
                planet.Settlement.OccupiedByOwner = null;
                planet.Settlement.OccupationStartTurn = 0;
                planet.Settlement.LandingBlocked = fp.LandingBlocked ?? false;
                return;
            }

            planet.Settlement.OccupiedByOwner = occup;
            planet.Settlement.OccupationStartTurn = 0;   // стартовая оккупация задана в конфиге, «ход установки» = 0
            // LandingBlocked: явное значение из конфига > дефолт по режиму оккупанта.
            if (fp.LandingBlocked.HasValue) planet.Settlement.LandingBlocked = fp.LandingBlocked.Value;
            else
            {
                string mode = null;
                if (_ctx.AvailableOwners != null && _ctx.AvailableOwners.TryGetValue(occup, out var oc))
                    mode = oc.OccupationMode;
                planet.Settlement.LandingBlocked = string.Equals(mode, "Full", System.StringComparison.OrdinalIgnoreCase);
            }
        }

        private bool IsNonPlanetaryRace(string raceKey) =>
            !string.IsNullOrEmpty(raceKey)
            && _ctx.Config?.Races != null
            && _ctx.Config.Races.TryGetValue(raceKey, out var rc)
            && rc.NonPlanetary != 0;

        private void ApplyRandomPlanetProps(PlanetData planet, string owner, string race, bool forceNone)
        {
            bool isNone = forceNone || owner == GalaxyConstants.OWNER_NONE_KEY || IsNonPlanetaryRace(race);
            planet.Race  = isNone ? GalaxyConstants.RACE_NONE_KEY : ResolveRandomRace(race);
            planet.Owner = isNone ? GalaxyConstants.OWNER_NONE_KEY : owner;
            planet.Name = GalaxyConstants.VAL_UNKNOWN;
            planet.Type = "None";
            planet.OrbitSpeed = 0f;
            planet.SatellitesCount = -1;
            planet.AxialTilt = UnityEngine.Random.Range(0f, 90f);
        }

        private void FinalizePlanetVisuals(PlanetData planet, string size)
        {
            planet.Size = size;
            planet.CurrentColor = GenerationHelpers.GetRaceColor(planet.Race, _ctx.AvailableRaces);

            if (string.IsNullOrEmpty(planet.Graphic)) planet.Graphic = GenerationHelpers.GetRandomPlanetGraphic(size);
            if (string.IsNullOrEmpty(planet.MaskGraphic)) planet.MaskGraphic = GenerationHelpers.GetMaskPath(size);
            if (string.IsNullOrEmpty(planet.Atmosphere)) planet.Atmosphere = GenerationHelpers.GenerateAtmosphere(size, _ctx.PlanetSizeRules);

            if (Mathf.Abs(planet.OrbitSpeed) < NearZero)
                planet.OrbitSpeed = UnityEngine.Random.Range(GalaxyConstants.PLANET_ORBIT_SPEED_MIN, GalaxyConstants.PLANET_ORBIT_SPEED_MAX)
                    * (UnityEngine.Random.value > 0.5f ? 1f : -1f);

            if (planet.DaySpeed <= 0) planet.DaySpeed = UnityEngine.Random.Range(GalaxyConstants.PLANET_DAY_SPEED_MIN, GalaxyConstants.PLANET_DAY_SPEED_MAX + 1);
            if (planet.CloudsSpeed <= 0) planet.CloudsSpeed = Mathf.RoundToInt(
                planet.DaySpeed * UnityEngine.Random.Range(GalaxyConstants.CLOUD_SPEED_FACTOR_MIN, GalaxyConstants.CLOUD_SPEED_FACTOR_MAX));

            planet.InitialAngle = planet.CurrentAngle = UnityEngine.Random.Range(0f, 360f);

            if (string.IsNullOrEmpty(planet.OrbitalObjects)
                && planet.Race != GalaxyConstants.RACE_NONE_KEY
                && UnityEngine.Random.value < GalaxyConstants.ORBITAL_OBJ_CHANCE)
                planet.OrbitalObjects = GenerationHelpers.GetRandomOrbitalPath();

            if (planet.SatellitesCount == -1) planet.SatellitesCount = GenerationHelpers.CalculateSatelliteCount(size, _ctx.PlanetSizeRules);

            var cfg = _ctx.Config.Planets;
            planet.OrbitEccentricity = UnityEngine.Random.Range(cfg.EccentricityMin, cfg.EccentricityMax);
            planet.OrbitTiltDeg = UnityEngine.Random.Range(0f, 360f);

            RollGravityDensity(planet, size);
        }

        private void RollGravityDensity(PlanetData planet, string size,
            float? fixedGravity = null, float? fixedDensity = null, float? fixedGeoActivity = null,
            float? fixedSurfaceArea = null)
        {
            _ctx.PlanetSizeRules.TryGetValue(size ?? planet.Size ?? string.Empty, out var sizeData);

            if (fixedGravity.HasValue)
                planet.SurfaceGravity = fixedGravity.Value;
            else if (sizeData != null)
                planet.SurfaceGravity = UnityEngine.Random.Range(sizeData.GravityMin, sizeData.GravityMax);

            if (fixedDensity.HasValue)
                planet.Density = fixedDensity.Value;
            else if (UnityEngine.Random.value < GalaxyConstants.HIGH_DENSITY_CHANCE)
                planet.Density = UnityEngine.Random.Range(1f, 2f);
            else if (sizeData != null)
                planet.Density = UnityEngine.Random.Range(sizeData.DensityMin, sizeData.DensityMax);

            if (fixedGeoActivity.HasValue)
                planet.GeoActivity = Mathf.Clamp(fixedGeoActivity.Value, 0f, 1f);
            else if (sizeData != null)
            {
                float geoBase = Mathf.Clamp(sizeData.GeoK * Mathf.Pow(planet.Density, 0.6f), 0f, 1f);
                planet.GeoActivity = Mathf.Clamp(geoBase * UnityEngine.Random.Range(0.4f, 1.6f) + planet.SatellitesCount * 0.05f, 0f, 1f);
            }

            if (fixedSurfaceArea.HasValue)
                planet.TotalSurfaceArea = fixedSurfaceArea.Value;
            else if (sizeData != null)
                planet.TotalSurfaceArea = UnityEngine.Random.Range(sizeData.SurfaceAreaMin, sizeData.SurfaceAreaMax);
            else
                planet.TotalSurfaceArea = 500f;
        }

        private void AssignOrbitsAndNames(List<PlanetData> list, int total,
            HashSet<int> usedOrbits, StarData star)
        {
            var freeSlots = new List<int>(total);
            for (int i = 1; i <= total; i++)
                if (!usedOrbits.Contains(i)) freeSlots.Add(i);

            foreach (var planet in list)
            {
                planet.CurrentAngle = UnityEngine.Random.Range(0f, 360f);
                if (planet.OrbitIndex <= 0)
                    planet.OrbitIndex = freeSlots.Count > 0 ? PopRandom(freeSlots) : GalaxyConstants.ORBIT_FALLBACK;

                GenerateSatellites(planet, null);
                CustomPropertyResolver.ResolveAndApplyProperties(planet, planet.CustomProperties, "Planets", _ctx.Config);
            }

            AssignOrbitRadii(list);
        }

        private void AssignOrbitRadii(List<PlanetData> list)
        {
            list.Sort((a, b) => a.OrbitIndex.CompareTo(b.OrbitIndex));
            float prevApocenter = 0f;

            for (int i = 0; i < list.Count; i++)
            {
                var planet = list[i];
                float e = Mathf.Clamp(planet.OrbitEccentricity, 0f, 0.99f);

                if (planet.OrbitRadius > GalaxyConstants.MIN_ORBIT_RADIUS)
                {
                    if (!planet.HasFixedOrbitRadius)
                        planet.OrbitRadius = planet.OrbitRadius / Mathf.Max(1f - e, 0.01f);
                }
                else
                {
                    float gap = UnityEngine.Random.Range(GalaxyConstants.ORBIT_GAP_MIN, GalaxyConstants.ORBIT_GAP_MAX) * _systemSizeMult;
                    float peri = prevApocenter + gap;
                    float minPeri = i == 0
                        ? UnityEngine.Random.Range(_systemSizeMult * GalaxyConstants.FIRST_ORBIT_MIN, _systemSizeMult * GalaxyConstants.FIRST_ORBIT_MAX)
                        : peri;
                    peri = Mathf.Max(peri, minPeri);
                    planet.OrbitRadius = peri / Mathf.Max(1f - e, 0.01f);
                }

                prevApocenter = planet.OrbitRadius * (1f + e);
            }
        }
    }
}
