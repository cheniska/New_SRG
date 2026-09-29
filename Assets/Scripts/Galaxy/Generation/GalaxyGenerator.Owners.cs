using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Equipment;
using SRG.NpcAI;
using SRG.NpcAI.Spawning;

namespace SRG.Galaxy.Generation
{
    public partial class GalaxyGenerator
    {
        private void ResolveAllOwners(GalaxyData galaxy)
        {
            foreach (var sector in galaxy.Sectors)
            {
                bool sectorOwnerFixed = OwnerResolver.IsFixedOwner(sector.Owner, _ctx.AvailableOwners);
                bool sectorRaceFixed  = OwnerResolver.IsFixedRace(sector.Race,  _ctx.AvailableRaces);

                foreach (var star in sector.Stars)
                {
                    if (sectorOwnerFixed)
                        star.Owner = sector.Owner;

                    if (sectorRaceFixed)
                        star.Race = sector.Race;

                    bool starOwnerFixed = OwnerResolver.IsFixedOwner(star.Owner, _ctx.AvailableOwners);
                    bool starRaceFixed  = OwnerResolver.IsFixedRace(star.Race,  _ctx.AvailableRaces);

                    if (starOwnerFixed)
                    {
                        foreach (var planet in star.Planets)
                            PropagateStarOwnerToPlanet(planet, star.Owner, star.Race);
                        star.ResolvedOwnerId = star.Owner;
                    }
                    else
                    {
                        ResolveStarOwnerFromPlanets(star);
                    }

                    if (starRaceFixed)
                        foreach (var planet in star.Planets)
                            if (planet.Race != GalaxyConstants.RACE_NONE_KEY)
                            {
                                planet.Race = star.Race;
                                planet.CurrentColor = GenerationHelpers.GetRaceColor(planet.Race, _ctx.AvailableRaces);
                            }
                }

                if (sectorOwnerFixed)
                    sector.ResolvedOwnerId = sector.Owner;
                else
                {
                    sector.Owner = OwnerResolver.ComputeOwnerFromChildren(sector.Stars, s => s.Owner);
                    sector.ResolvedOwnerId = sector.Owner == GalaxyConstants.OWNER_NONE_KEY ? null : sector.Owner;
                }

                if (!sectorRaceFixed)
                    sector.Race = OwnerResolver.ComputeRaceFromChildren(sector.Stars, s => s.Race);
                else
                {
                    bool anyPlanetColonized = false;
                    foreach (var st in sector.Stars)
                    {
                        foreach (var p in st.Planets)
                            if (p.Race == sector.Race) { anyPlanetColonized = true; break; }
                        if (anyPlanetColonized) break;
                    }
                    if (!anyPlanetColonized)
                    {
                        foreach (var star in sector.Stars)
                            if (star.Race == sector.Race)
                                star.Race = OwnerResolver.ComputeRaceFromChildren(star.Planets, p => p.Race);
                        sector.Race = OwnerResolver.ComputeRaceFromChildren(sector.Stars, s => s.Race);
                    }
                }
            }
        }

        private void PostResolutionFinalize(GalaxyData galaxy)
        {
            foreach (var sector in galaxy.Sectors)
            {
                foreach (var star in sector.Stars)
                {
                    foreach (var planet in star.Planets)
                    {
                        ResolvePlanetName(planet, star);
                        ItemFactory.InitPlanetData(planet, _ctx);
                    }

                    if (string.IsNullOrEmpty(star.Name) || star.Name == GalaxyConstants.VAL_UNKNOWN)
                        star.Name = ResolveStarNameFromPlanets(star);

                    star.NameRenderData = OwnershipDisplayResolver.ResolveStarName(star, _ctx);
                    NpcSystemSpawner.PopulateStarWithNpcs(star, _ctx);
                }
                NpcSystemSpawner.PopulateSectorEntities(sector, _ctx);
            }

            // Регистрация политик и пересчёт счётчиков SpawnSystem.
            NpcSystemSpawner.FinalizeAfterGeneration(galaxy, _ctx);
        }

        private void ResolveStarOwnerFromPlanets(StarData star)
        {
            star.Owner = OwnerResolver.ComputeOwnerFromChildren(star.Planets, p => p.Owner);
            if (!OwnerResolver.IsFixedRace(star.Race, _ctx.AvailableRaces))
                star.Race = OwnerResolver.ComputeRaceFromChildren(star.Planets, p => p.Race);
            star.ResolvedOwnerId = star.Owner == GalaxyConstants.OWNER_NONE_KEY ? null : star.Owner;
        }

        private void ResolveSectorOwnerFromStars(SectorData sector)
        {
            sector.Owner = OwnerResolver.ComputeOwnerFromChildren(sector.Stars, s => s.Owner);
            if (!OwnerResolver.IsFixedRace(sector.Race, _ctx.AvailableRaces))
                sector.Race = OwnerResolver.ComputeRaceFromChildren(sector.Stars, s => s.Race);
            sector.ResolvedOwnerId = sector.Owner == GalaxyConstants.OWNER_NONE_KEY ? null : sector.Owner;
        }

        private void PropagateStarOwnerToPlanet(PlanetData planet, string starOwner, string starRace)
        {
            if (planet.Race == GalaxyConstants.RACE_NONE_KEY) return;
            if (planet.Owner != GalaxyConstants.OWNER_UNRESOLVED_KEY
                && !string.IsNullOrEmpty(planet.Owner)
                && planet.Owner != GalaxyConstants.OWNER_NONE_KEY) return;
            planet.Owner = starOwner;
            if (string.IsNullOrEmpty(planet.Race) || planet.Race == GalaxyConstants.RACE_NONE_KEY)
                planet.Race = starRace;
            planet.CurrentColor = GenerationHelpers.GetRaceColor(planet.Race, _ctx.AvailableRaces);
        }

        private void PreReserveFixedNames(string galaxyKey)
        {
            var premadeGalaxy = _ctx.PremadeConfig?.Galaxies?.GetValueOrDefault(galaxyKey);
            if (premadeGalaxy?.Sectors == null) return;

            var reservedSectorNames = new HashSet<string>();

            foreach (var fc in premadeGalaxy.Sectors.Values)
            {
                if (!string.IsNullOrEmpty(fc.Name)
                    && _ctx.ActiveSectorNames?.TryGetValue(fc.Name, out string sectorName) == true)
                    reservedSectorNames.Add(sectorName);

                if (fc.Stars == null) continue;

                foreach (var fs in fc.Stars.Values)
                {
                    string owner = OwnerResolver.ResolveOwner(fs.Owner ?? GalaxyConstants.OWNER_NONE_KEY, fc.Owner, _ctx.AvailableOwners);
                    string starName = ResolveStarFixedName(fs.Name, owner);
                    if (!string.IsNullOrEmpty(starName)) _ctx.UsedStarNames.Add(starName);

                    if (fs.Planets == null) continue;
                    string starRace = OwnerResolver.IsFixedRace(fc.Race, _ctx.AvailableRaces)
                        ? fc.Race
                        : (fs.Race ?? GalaxyConstants.RACE_NONE_KEY);
                    foreach (var fp in fs.Planets.Values)
                    {
                        string pName = ResolveFixedPlanetName(fp.Name, fp.Race, starRace);
                        if (!string.IsNullOrEmpty(pName) && pName != GalaxyConstants.VAL_UNKNOWN)
                            _ctx.UsedPlanetNames.Add(pName);
                    }
                }
            }
            _ctx.SectorNamePool.RemoveAll(name => reservedSectorNames.Contains(name));
        }

        private string ResolveSectorName(string key)
        {
            if (string.IsNullOrEmpty(key)) return PopSectorName();
            if (_ctx.ActiveSectorNames?.TryGetValue(key, out string resolved) == true) return resolved;
            return key;
        }

        private string PopSectorName()
        {
            string name = PopRandom(_ctx.SectorNamePool);
            return name ?? $"UNNAMED_S_{++_ctx.UnnamedSectorCounter}";
        }

        private string ResolveStarFixedName(string key, string race)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!string.IsNullOrEmpty(race) && _ctx.StarNamesByRace.TryGetValue(race, out var racePool)
                && racePool.Fixed.TryGetValue(key, out string byRace)) return byRace;
            if (_ctx.CommonStarNames.Fixed.TryGetValue(key, out string common)) return common;
            foreach (var pool in _ctx.StarNamesByRace.Values)
                if (pool.Fixed.TryGetValue(key, out string any)) return any;
            return key;
        }

        private string ResolveStarNameFromPlanets(StarData star) => _ctx.ResolveStarNameFromPlanets(star);

        private string ResolveFixedPlanetName(string key, string race, string starOwner)
        {
            if (string.IsNullOrEmpty(key)) return GalaxyConstants.VAL_UNKNOWN;
            return TryGetFixedPlanetName(key, starOwner) ?? TryGetFixedPlanetName(key, race) ?? key;
        }

        private string TryGetFixedPlanetName(string key, string race)
        {
            if (string.IsNullOrEmpty(race) || race == GalaxyConstants.RACE_NONE_KEY) return null;
            return _ctx.PlanetNamesByRace.TryGetValue(race, out var pool)
                && pool.Fixed.TryGetValue(key, out string name) ? name : null;
        }

        private void ResolvePlanetName(PlanetData planet, StarData star) => _ctx.ResolvePlanetName(planet, star.Race);
    }
}
