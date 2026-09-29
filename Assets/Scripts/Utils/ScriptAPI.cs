using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using System.Linq;
using SRG.Config;
using SRG.Galaxy;
using SRG.Galaxy.Generation;

namespace SRG.Utils
{
    public class StarParams
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Color { get; set; }
        public int? GraphVar { get; set; }
        public string Owner { get; set; }
        public string Race { get; set; }
        public List<PlanetData> Planets { get; set; }
        public int? PlanetsCount { get; set; }
    }

    public class PlanetParams
    {
        public string Name { get; set; }
        public string Size { get; set; }
        public string Type { get; set; }
        public string Race { get; set; }
        public string Owner { get; set; }
        public string Graphic { get; set; }
        public string Atmosphere { get; set; }
        public string OrbitalObjects { get; set; }
        public float? OrbitRadius { get; set; }
        public float? OrbitSpeed { get; set; }
        public int? OrbitIndex { get; set; }
        public int? DaySpeed { get; set; }
        public int? CloudsSpeed { get; set; }
        public int? SatellitesCount { get; set; }
    }
    public class ScriptAPI
    {
        public static ScriptAPI Instance { get; private set; }

        public static void Initialize(GalaxyGenerationContext context, GalaxyData galaxy) =>
            Instance = new ScriptAPI(context, galaxy);

        private readonly GalaxyGenerationContext _ctx;
        private readonly GalaxyData _galaxy;
        private readonly GalaxyGenerator _gen;

        private ScriptAPI(GalaxyGenerationContext context, GalaxyData galaxy)
        {
            _ctx = context;
            _galaxy = galaxy;
            _gen = new GalaxyGenerator(context);
        }

        public StarData UserCreateStar(SectorData sector, StarParams p = null)
        {
            if (sector == null)
                throw new System.ArgumentNullException(nameof(sector), "UserCreateStar: сектор обязателен");

            var star = new StarData { ParentSector = sector };
            ApplyStarParams(star, p, sector);
            _gen.FinalizeStar(star);
            AttachStarToGalaxy(star, sector);
            return star;
        }

        public PlanetData UserCreatePlanet(StarData star, PlanetParams p = null)
        {
            if (star == null)
                throw new System.ArgumentNullException(nameof(star), "UserCreatePlanet: звезда обязательна");

            string size = ResolveSize(p?.Size);
            var planet = new PlanetData();
            ApplyPlanetParams(planet, p, star, size);
            _gen.FinalizePlanet(planet, size);
            AssignOrbitToNewPlanet(planet, star, p?.OrbitRadius);
            AttachPlanetToStar(planet, star);
            return planet;
        }

        private void ApplyStarParams(StarData star, StarParams p, SectorData sector)
        {
            star.Type     = ResolveStarType(p?.Type);
            star.Color    = p?.Color ?? GalaxyConstants.VAL_UNKNOWN;
            star.GraphVar = p?.GraphVar ?? 0;
            star.Owner    = p?.Owner ?? sector.Owner ?? GalaxyConstants.OWNER_UNRESOLVED_KEY;
            star.Race     = p?.Race ?? GalaxyConstants.RACE_NONE_KEY;
            star.Name     = p?.Name ?? GalaxyConstants.VAL_UNKNOWN;
            star.Position = sector.Center + Random.insideUnitCircle * GalaxyConstants.SECTOR_RADIUS;

            if (p?.Planets != null && p.Planets.Count > 0)
            {
                star.Planets = new List<PlanetData>(p.Planets);
                foreach (var planet in star.Planets) planet.ParentStar = star;
            }
            else
            {
                star.Planets = GeneratePlanetsForNewStar(star, p?.PlanetsCount);
            }
        }

        private void ApplyPlanetParams(PlanetData planet, PlanetParams p, StarData star, string size)
        {
            if (p != null)
            {
                string race = !string.IsNullOrEmpty(p.Race) ? p.Race : GalaxyConstants.RACE_NONE_KEY;
                bool isNone = race == GalaxyConstants.RACE_NONE_KEY;

                planet.Race  = race;
                planet.Owner = isNone
                    ? GalaxyConstants.OWNER_NONE_KEY
                    : (!string.IsNullOrEmpty(p.Owner) ? p.Owner : star.Owner);
                planet.Name          = p.Name ?? GalaxyConstants.VAL_UNKNOWN;
                planet.Type          = !string.IsNullOrEmpty(p.Type) ? p.Type : RandomPlanetType();
                planet.Graphic       = GenerationHelpers.CleanResourcePath(p.Graphic);
                planet.Atmosphere    = GenerationHelpers.CleanResourcePath(p.Atmosphere);
                planet.OrbitalObjects = GenerationHelpers.CleanResourcePath(p.OrbitalObjects);
                planet.OrbitSpeed    = p.OrbitSpeed ?? 0f;
                planet.DaySpeed      = p.DaySpeed ?? 0;
                planet.CloudsSpeed   = p.CloudsSpeed ?? 0;
                planet.SatellitesCount = p.SatellitesCount ?? -1;
                planet.OrbitIndex    = p.OrbitIndex ?? 0;
            }
            else
            {
                string starRace = star.Race;
                string race = OwnerResolver.IsFixedRace(starRace, _ctx.AvailableRaces)
                    ? starRace : GetRandomRaceKey();
                bool isNone = Random.value < 0.2f;

                planet.Race  = isNone ? GalaxyConstants.RACE_NONE_KEY : race;
                planet.Owner = isNone ? GalaxyConstants.OWNER_NONE_KEY : star.Owner;
                planet.Name  = GalaxyConstants.VAL_UNKNOWN;
                planet.Type  = RandomPlanetType();
                planet.OrbitSpeed = 0f;
                planet.SatellitesCount = -1;
            }
        }
        private void AssignOrbitToNewPlanet(PlanetData planet, StarData star, float? forcedRadius)
        {
            int mult = _ctx.Config.Settings?.SystemSizeMult ?? 1;

            if (forcedRadius.HasValue && forcedRadius.Value > 0f)
            {
                planet.OrbitRadius = forcedRadius.Value;
            }
            else
            {
                float maxExisting = star.Planets.Count > 0 ? star.Planets.Max(pl => pl.OrbitRadius) : 0f;
                float gap = Random.Range(GalaxyConstants.ORBIT_GAP_MIN, GalaxyConstants.ORBIT_GAP_MAX) * mult;
                planet.OrbitRadius = maxExisting > 0f ? maxExisting + gap : Random.Range(GalaxyConstants.FIRST_ORBIT_MIN, GalaxyConstants.FIRST_ORBIT_MAX);
            }

            if (planet.OrbitIndex <= 0)
                planet.OrbitIndex = star.Planets.Count > 0 ? star.Planets.Max(pl => pl.OrbitIndex) + 1 : 1;

            star.SystemSize = Mathf.RoundToInt(
                star.Planets.Append(planet).Max(pl => pl.OrbitRadius) + GalaxyConstants.SYSTEM_PADDING * mult);
        }

        private List<PlanetData> GeneratePlanetsForNewStar(StarData star, int? forcedCount)
        {
            int min = Mathf.Max(1, _ctx.ActiveGalaxyConfig?.MinPlanetsPerStar ?? 1);
            int max = _ctx.ActiveGalaxyConfig?.MaxPlanetsPerStar ?? 8;
            int total = forcedCount ?? Random.Range(min, max + 1);

            var list = new List<PlanetData>(total);
            for (int i = 0; i < total; i++)
            {
                var planet = new PlanetData();
                string size = ResolveSize(null);
                ApplyPlanetParams(planet, null, star, size);
                _gen.FinalizePlanet(planet, size);
                _gen.GeneratePlanetSatellites(planet);
                planet.OrbitIndex = i + 1;
                list.Add(planet);
            }

            _gen.AssignOrbitRadiiToList(list);
            return list;
        }

        private void AttachStarToGalaxy(StarData star, SectorData sector)
        {
            sector.Stars.Add(star);
            _galaxy.StarsMap[star.Uid] = star;
            foreach (var planet in star.Planets)
            {
                planet.ParentStar = star;
                if (!_galaxy.PlanetsMap.ContainsKey(planet.Uid)) _galaxy.PlanetsMap[planet.Uid] = planet;
            }
        }

        private void AttachPlanetToStar(PlanetData planet, StarData star)
        {
            planet.ParentStar = star;
            star.Planets.Add(planet);
            _galaxy.PlanetsMap[planet.Uid] = planet;
        }

        private string ResolveStarType(string requested)
        {
            if (!string.IsNullOrEmpty(requested) && _ctx.AvailableStarTypes.Contains(requested))
                return requested;
            if (_ctx.AvailableStarTypes.Count == 0) return requested ?? "";
            return _ctx.AvailableStarTypes[Random.Range(0, _ctx.AvailableStarTypes.Count)];
        }

        private string ResolveSize(string requested)
        {
            if (!string.IsNullOrEmpty(requested) && _ctx.AvailablePlanetSizes.Contains(requested))
                return requested;
            if (_ctx.AvailablePlanetSizes.Count == 0) return requested ?? "";
            return _ctx.AvailablePlanetSizes[Random.Range(0, _ctx.AvailablePlanetSizes.Count)];
        }

        private string RandomPlanetType()
        {
            if (_ctx.AvailablePlanetTypes.Count == 0) return "";
            return _ctx.AvailablePlanetTypes[Random.Range(0, _ctx.AvailablePlanetTypes.Count)];
        }

        private string GetRandomRaceKey()
        {
            int count = _ctx.AvailableRaces.Count;
            return count > 0
                ? _ctx.AvailableRaces.Keys.ElementAt(Random.Range(0, count))
                : GalaxyConstants.RACE_NONE_KEY;
        }
    }
}
