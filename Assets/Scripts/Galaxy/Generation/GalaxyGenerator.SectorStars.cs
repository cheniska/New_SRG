using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Simulation;

namespace SRG.Galaxy.Generation
{
    public partial class GalaxyGenerator
    {
        private void GenerateSectors(GalaxyData galaxy, string galaxyKey, GalaxyConfigData cfg)
        {
            var premadeGalaxy = _ctx.PremadeConfig?.Galaxies?.GetValueOrDefault(galaxyKey);
            if (premadeGalaxy?.Sectors != null)
            {
                var configKeySectors = new Dictionary<string, SectorData>();
                foreach (var (key, fc) in premadeGalaxy.Sectors)
                {
                    var s = CreateSector(galaxy, fc, GetRandomPos(galaxy.Width, galaxy.Height));
                    s.PlaceHint = fc.Place;
                    galaxy.Sectors.Add(s);
                    configKeySectors[key] = s;
                }
                foreach (var (key, fc) in premadeGalaxy.Sectors)
                {
                    if (!string.IsNullOrEmpty(fc.NeighbourSector) &&
                        configKeySectors.TryGetValue(fc.NeighbourSector, out var target))
                        configKeySectors[key].NeighbourSectorUid = target.Uid;
                }
            }

            int remaining = cfg.SectorCount - galaxy.Sectors.Count;
            var tempPos   = JitteredSectorPositions(galaxy.Width, galaxy.Height, remaining);
            for (int i = 0; i < remaining; i++)
                galaxy.Sectors.Add(CreateSector(galaxy, null, tempPos[i]));
        }

        private SectorData CreateSector(GalaxyData galaxy, FixedSectorData fc, Vector2? pos)
        {
            var c = new SectorData(ResolveSectorName(fc?.Name), pos ?? GetRandomPos(galaxy.Width, galaxy.Height));

            if (fc != null)
            {
                c.Owner = fc.Owner ?? GalaxyConstants.OWNER_UNRESOLVED_KEY;
                c.Race  = fc.Race  ?? GalaxyConstants.RACE_NONE_KEY;
                c.FixedStarsCount = (fc.Stars?.Count ?? 0) > 0 ? fc.Stars.Count : fc.StarsCount;
                if (fc.Stars != null)
                    foreach (var fs in fc.Stars.Values)
                    {
                        var star = CreateStar(galaxy, c, fs, c.Owner, c.Race, fc.OccupiedBy);
                        if (!TryAddStar(galaxy, c, star))
                            ForceAddStar(galaxy, c, star);
                    }
            }
            else { c.Owner = GalaxyConstants.OWNER_UNRESOLVED_KEY; c.Race = GalaxyConstants.RACE_NONE_KEY; }

            return c;
        }

        private void GenerateStars(GalaxyData galaxy, GalaxyConfigData cfg)
        {
            int budget = Mathf.Max(0, cfg.StarsCount - galaxy.StarsMap.Count);
            var capacity = new Dictionary<SectorData, int>(galaxy.Sectors.Count);
            foreach (var c in galaxy.Sectors)
                capacity[c] = GetSectorCapacity(c, cfg);

            foreach (var c in galaxy.Sectors)
            {
                int toAdd = Mathf.Min(Mathf.Max(0, cfg.MinStarsPerSector - c.Stars.Count), budget);
                for (int i = 0; i < toAdd; i++)
                {
                    StarData lastStar = null;
                    bool placed = false;
                    for (int attempt = 0; attempt < 200 && !placed; attempt++)
                    {
                        lastStar = CreateStar(galaxy, c);
                        placed = TryAddStar(galaxy, c, lastStar);
                    }
                    if (!placed && lastStar != null)
                    {
                        ForceAddStar(galaxy, c, lastStar);
                        Debug.LogWarning($"[GalaxyGenerator] Сектор '{c.Name}': мин. звезда добавлена принудительно (временная позиция).");
                    }
                    budget--;
                }
            }

            var available = new List<SectorData>(galaxy.Sectors.Count);
            foreach (var c in galaxy.Sectors)
                if (c.Stars.Count < capacity[c]) available.Add(c);

            for (int attempts = 0; budget > 0 && available.Count > 0 && attempts < budget * 5 + 500; attempts++)
            {
                int idx = GameRng.Range(0, available.Count);
                var c = available[idx];
                if (TryAddStar(galaxy, c, CreateStar(galaxy, c)))
                {
                    budget--;
                    attempts = -1;
                    if (c.Stars.Count >= capacity[c])
                    {
                        available[idx] = available[available.Count - 1];
                        available.RemoveAt(available.Count - 1);
                    }
                }
            }
        }

        private StarData CreateStar(GalaxyData galaxy, SectorData parent,
            FixedStarData fs = null, string parentOwner = null, string parentRace = null,
            string parentOccupiedBy = null)
        {
            var star = new StarData
            {
                ParentSector = parent,
                Position = GetRandomPos(galaxy.Width, galaxy.Height, parent.Center, _sectorRadius),
                IsPremade = fs != null
            };

            if (fs != null) ApplyFixedStarProps(star, fs, parentOwner, parentRace, parentOccupiedBy);
            else ApplyRandomStarProps(star);

            FinalizeStarProps(star);
            return star;
        }

        private void ApplyFixedStarProps(StarData star, FixedStarData fs, string parentOwner, string parentRace,
            string parentOccupiedBy)
        {
            if (OwnerResolver.IsFixedOwner(parentOwner, _ctx.AvailableOwners))
                star.Owner = parentOwner;
            else if (OwnerResolver.IsFixedOwner(fs.Owner, _ctx.AvailableOwners))
                star.Owner = fs.Owner;
            else if (fs.Owner == GalaxyConstants.OWNER_NONE_KEY)
                star.Owner = GalaxyConstants.OWNER_NONE_KEY;
            else
                star.Owner = ResolveDefaultOwner();
            star.Race = OwnerResolver.IsFixedRace(parentRace, _ctx.AvailableRaces)
                ? parentRace
                : (fs.Race ?? GalaxyConstants.RACE_NONE_KEY);
            star.Type = fs.Type ?? GalaxyConstants.VAL_UNKNOWN;
            star.Color = fs.Color ?? GalaxyConstants.VAL_UNKNOWN;
            star.GraphVar = fs.GraphVar ?? 0;
            star.Name = ResolveStarFixedName(fs.Name, star.Owner) ?? GalaxyConstants.VAL_UNKNOWN;
            // Приоритет OccupiedBy: fs > parent(sector). PropagateOccupation ниже раскроет на планеты.
            string starOccupiedBy = string.IsNullOrEmpty(fs.OccupiedBy) ? parentOccupiedBy : fs.OccupiedBy;
            star.Planets = CreatePlanetsMixed(star, fs, starOccupiedBy);
            if (fs.MaxAsteroids.HasValue)
                star.MaxAsteroids = fs.MaxAsteroids.Value;
        }

        private void ApplyRandomStarProps(StarData star)
        {
            star.Owner = GalaxyConstants.OWNER_NONE_KEY;
            star.Race  = GalaxyConstants.RACE_NONE_KEY;
            star.Type  = _ctx.AvailableStarTypes[GameRng.Range(0, _ctx.AvailableStarTypes.Count)];
            star.Color = GalaxyConstants.VAL_UNKNOWN;
            star.GraphVar = 0;
            star.Name = GalaxyConstants.VAL_UNKNOWN;
            star.Planets = CreatePlanetsRandom(star.Owner, star.Race, star.Type);
        }

        private bool TryAddStar(GalaxyData galaxy, SectorData c, StarData star)
        {
            if (galaxy.StarsMap.ContainsKey(star.Uid)) return false;

            if (_minStarDistSq > 0f)
            {
                foreach (var existing in galaxy.StarsMap.Values)
                {
                    float dx = existing.Position.x - star.Position.x;
                    float dy = existing.Position.y - star.Position.y;
                    if (dx * dx + dy * dy < _minStarDistSq) return false;
                }
            }

            c.Stars.Add(star);
            galaxy.StarsMap[star.Uid] = star;
            foreach (var planet in star.Planets)
                if (!galaxy.PlanetsMap.ContainsKey(planet.Uid))
                    galaxy.PlanetsMap[planet.Uid] = planet;
            return true;
        }

        private void ForceAddStar(GalaxyData galaxy, SectorData c, StarData star)
        {
            if (galaxy.StarsMap.ContainsKey(star.Uid)) return;
            c.Stars.Add(star);
            galaxy.StarsMap[star.Uid] = star;
            foreach (var planet in star.Planets)
                if (!galaxy.PlanetsMap.ContainsKey(planet.Uid))
                    galaxy.PlanetsMap[planet.Uid] = planet;
        }
    }
}
