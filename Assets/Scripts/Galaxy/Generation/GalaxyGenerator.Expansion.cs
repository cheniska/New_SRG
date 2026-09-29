using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Config;

namespace SRG.Galaxy.Generation
{
    public partial class GalaxyGenerator
    {
        private const float ExpansionRadiusStart = 12f;
        private const float ExpansionRadiusStep  =  4f;
        // Не const — переустанавливается в RunExpansion на размер галактики, чтобы волна расселения
        // дотягивалась до изолированных кластеров пригодных звёзд (особенно для Race2/Race4 с узкими
        // условиями обитаемости, которые иначе застревают в углах галактики).
        private float       ExpansionRadiusMax   = 40f;

        private void RunExpansion(GalaxyData galaxy)
        {
            // Радиус волны должен покрывать всю галактику, иначе изолированные кластеры пригодных
            // звёзд недостижимы. Берём диагональ +20% запаса.
            float diag = Mathf.Sqrt(galaxy.Width * galaxy.Width + galaxy.Height * galaxy.Height);
            ExpansionRadiusMax = Mathf.Max(40f, diag * 1.2f);

            var premadeUids = new HashSet<string>();
            foreach (var star in galaxy.StarsMap.Values)
                if (star.IsPremade) premadeUids.Add(star.Uid);

            foreach (var star in galaxy.StarsMap.Values)
            {
                if (star.IsPremade) continue;
                foreach (var planet in star.Planets)
                {
                    planet.Race  = GalaxyConstants.RACE_NONE_KEY;
                    planet.Owner = GalaxyConstants.OWNER_NONE_KEY;
                    planet.CurrentColor = GenerationHelpers.GetRaceColor(GalaxyConstants.RACE_NONE_KEY, _ctx.AvailableRaces);
                    planet.IsTerraformable = false;
                }
            }

            if (!DateTime.TryParseExact(
                    _ctx.Config.Settings?.InitialDate ?? "01.01.0001",
                    "dd.MM.yyyy", null,
                    System.Globalization.DateTimeStyles.None,
                    out DateTime initialDate))
                initialDate = new DateTime(1, 1, 1);

            var entries = new List<(string key, RaceConfig cfg, DateTime startDate, float speed)>();
            var galaxyRaces = GetActiveGalaxyRaceSet();
            if (_ctx.Config.Races != null)
                foreach (var kvp in _ctx.Config.Races)
                {
                    if (kvp.Key == GalaxyConstants.RACE_NONE_KEY) continue;
                    if (kvp.Value.NonPlanetary != 0) continue;
                    // Мульти-галактика: пропускаем расы, не разрешённые текущей галактикой.
                    if (galaxyRaces != null && !galaxyRaces.Contains(kvp.Key)) continue;

                    string rawDate = kvp.Value.ColonizationStartDate
                                     ?? _ctx.Config.Settings?.InitialDate
                                     ?? "01.01.0001";
                    if (!DateTime.TryParseExact(rawDate, "dd.MM.yyyy", null,
                            System.Globalization.DateTimeStyles.None, out DateTime sd))
                        sd = initialDate;

                    entries.Add((kvp.Key, kvp.Value, sd, kvp.Value.ColonizationSpeed));
                }
            entries.Sort((a, b) => a.startDate.CompareTo(b.startDate));

            int raceCount = entries.Count;
            int starsCount = galaxy.StarsMap.Count;

            foreach (var (raceKey, raceCfg, startDate, speed) in entries)
            {
                int budget = ComputeTargetSystems(starsCount, raceCount);
                if (budget <= 0) continue;

                var frontier = new List<StarData>();
                foreach (var star in galaxy.StarsMap.Values)
                    if (star.IsPremade)
                        foreach (var p in star.Planets)
                            if (p.Race == raceKey) { frontier.Add(star); break; }
                if (frontier.Count == 0) continue;

                ExpansionColonizeRace(raceKey, raceCfg, frontier, budget, galaxy, premadeUids, startDate, speed);
            }

            foreach (var sector in galaxy.Sectors)
            {
                foreach (var star in sector.Stars)
                    if (!star.IsPremade)
                        star.Race = OwnerResolver.ComputeRaceFromChildren(star.Planets, p => p.Race);

                if (!OwnerResolver.IsFixedRace(sector.Race, _ctx.AvailableRaces))
                    sector.Race = OwnerResolver.ComputeRaceFromChildren(sector.Stars, s => s.Race);
            }

            LogExpansionStats(galaxy, entries);
        }

        /// <summary>
        /// Целевое число систем на одну расу: round(StarsCount × Rnd(BaseFractionMin..Max) × Multiplier / RaceCount) + Rnd(-Jitter, +Jitter).
        /// Множитель ×2 в дефолте учитывает мультирасовость систем (сумма присутствий превышает число звёзд).
        /// </summary>
        private int ComputeTargetSystems(int starsCount, int raceCount)
        {
            if (raceCount <= 0 || starsCount <= 0) return 0;

            var cfg = _ctx.ActiveGalaxyConfig?.TargetSystemsPerRace;
            float fracMin = cfg?.BaseFractionMin ?? 0.85f;
            float fracMax = cfg?.BaseFractionMax ?? 0.9f;
            float mult    = cfg?.Multiplier      ?? 2.0f;
            int   jitter  = cfg?.Jitter          ?? 2;

            float fraction = UnityEngine.Random.Range(fracMin, fracMax);
            int   baseVal  = Mathf.RoundToInt(starsCount * fraction * mult / raceCount);
            int   target   = baseVal + UnityEngine.Random.Range(-jitter, jitter + 1);
            return Mathf.Max(0, target);
        }

        private void LogExpansionStats(GalaxyData galaxy, List<(string key, RaceConfig cfg, DateTime startDate, float speed)> entries)
        {
            var perRace = new Dictionary<string, int>();
            foreach (var e in entries) perRace[e.key] = 0;

            int emptySystems = 0;
            int habitedSystems = 0;
            int emptyPlanetsInHabitedSystems = 0;
            int planetsInHabitedSystems = 0;

            foreach (var star in galaxy.StarsMap.Values)
            {
                var races = new HashSet<string>();
                int totalPlanets = 0;
                int emptyPlanets = 0;
                foreach (var p in star.Planets)
                {
                    totalPlanets++;
                    if (p.Race == GalaxyConstants.RACE_NONE_KEY) { emptyPlanets++; continue; }
                    races.Add(p.Race);
                }

                if (races.Count == 0)
                {
                    emptySystems++;
                }
                else
                {
                    habitedSystems++;
                    planetsInHabitedSystems += totalPlanets;
                    emptyPlanetsInHabitedSystems += emptyPlanets;
                    foreach (var r in races) if (perRace.ContainsKey(r)) perRace[r]++;
                }
            }

            var sb = new System.Text.StringBuilder();
            sb.Append("[GalaxyGen] Expansion result: ");
            foreach (var kv in perRace) sb.Append($"{kv.Key}={kv.Value} ");
            int totalStars = galaxy.StarsMap.Count;
            float emptyPct = totalStars > 0 ? 100f * emptySystems / totalStars : 0f;
            float emptyPlanetsPct = planetsInHabitedSystems > 0 ? 100f * emptyPlanetsInHabitedSystems / planetsInHabitedSystems : 0f;
            sb.Append($"| empty systems={emptySystems}/{totalStars} ({emptyPct:F1}%) ");
            sb.Append($"| empty planets in habited systems={emptyPlanetsInHabitedSystems}/{planetsInHabitedSystems} ({emptyPlanetsPct:F1}%)");
            UnityEngine.Debug.Log(sb.ToString());
        }

        private void ExpansionColonizeRace(
            string raceKey, RaceConfig raceCfg,
            List<StarData> frontier, int budget,
            GalaxyData galaxy, HashSet<string> premadeUids,
            DateTime startDate, float speed)
        {
            var presence = new HashSet<string>(frontier.Count);
            foreach (var s in frontier) presence.Add(s.Uid);

            for (int step = 0; step < budget; step++)
            {
                var frontierSectors = new HashSet<string>(frontier.Count);
                foreach (var s in frontier)
                    if (s.ParentSector != null) frontierSectors.Add(s.ParentSector.Uid);

                StarData target = null;

                for (float radius = ExpansionRadiusStart;
                     radius <= ExpansionRadiusMax && target == null;
                     radius += ExpansionRadiusStep)
                {
                    var sameFull    = new List<(StarData s, float d)>();
                    var sameTf      = new List<(StarData s, float d)>();
                    var otherFull   = new List<(StarData s, float d)>();
                    var otherTf     = new List<(StarData s, float d)>();

                    foreach (var star in galaxy.StarsMap.Values)
                    {
                        if (presence.Contains(star.Uid))    continue;
                        if (premadeUids.Contains(star.Uid)) continue;

                        float minDist = float.MaxValue;
                        foreach (var fs in frontier)
                        {
                            float d = (star.Position - fs.Position).magnitude;
                            if (d < minDist) minDist = d;
                        }
                        if (minDist > radius) continue;

                        bool hasHab = false, hasTf = false;
                        foreach (var p in star.Planets)
                        {
                            if (p.Race != GalaxyConstants.RACE_NONE_KEY) continue;
                            if (ExpansionIsHabitable(p, raceCfg))     { hasHab = true; break; }
                            if (ExpansionIsTerraformable(p, raceCfg))   hasTf  = true;
                        }
                        if (!hasHab && !hasTf) continue;

                        bool same = frontierSectors.Contains(star.ParentSector?.Uid);
                        var  entry = (star, minDist);

                        if (same) { if (hasHab) sameFull.Add(entry); else sameTf.Add(entry); }
                        else      { if (hasHab) otherFull.Add(entry); else otherTf.Add(entry); }
                    }

                    foreach (var bucket in new[] { sameFull, sameTf, otherFull, otherTf })
                    {
                        if (bucket.Count == 0) continue;
                        bucket.Sort((a, b) => a.d.CompareTo(b.d));
                        target = bucket[0].s;
                        break;
                    }
                }

                if (target == null) break;

                StarData closestFs = null;
                float closestFsDist = float.MaxValue;
                foreach (var fs in frontier)
                {
                    float d = (target.Position - fs.Position).magnitude;
                    if (d < closestFsDist) { closestFsDist = d; closestFs = fs; }
                }
                if (closestFs != null)
                {
                    double yearOffset = (step + 1.0) / System.Math.Max(speed, 0.001);
                    int colonizationYear = startDate.AddDays(yearOffset * 365.25).Year;
                    galaxy.ExpansionEdges.Add(new ExpansionEdge
                    {
                        RaceKey = raceKey,
                        From = closestFs.Position,
                        To = target.Position,
                        ColonizationYear = colonizationYear,
                    });
                }

                ExpansionColonizeStar(target, raceKey, raceCfg);
                presence.Add(target.Uid);
                frontier.Add(target);
            }
        }

        private void ExpansionColonizeStar(StarData star, string raceKey, RaceConfig raceCfg)
        {
            foreach (var planet in star.Planets)
            {
                if (planet.Race != GalaxyConstants.RACE_NONE_KEY) continue;

                bool hab = ExpansionIsHabitable(planet, raceCfg);
                bool tf  = !hab && ExpansionIsTerraformable(planet, raceCfg);
                if (!hab && !tf) continue;

                planet.Race  = raceKey;
                planet.Owner = GalaxyConstants.OWNER_NONE_KEY;
                planet.CurrentColor   = GenerationHelpers.GetRaceColor(raceKey, _ctx.AvailableRaces);
                planet.IsTerraformable = false;

                if (tf) { ExpansionApplyOptimal(planet, raceCfg); planet.IsTerraformed = true; }
            }
        }

        private static bool ExpansionIsHabitable(PlanetData p, RaceConfig rc)
        {
            var c = rc?.PlanetConditions;
            if (c == null) return true;
            float o2 = p.OxygenPercent / 100f * p.AtmPressure;
            if (c.WaterAbundanceMin.HasValue   && p.WaterAbundance   < c.WaterAbundanceMin.Value)   return false;
            if (c.WaterAbundanceMax.HasValue   && p.WaterAbundance   > c.WaterAbundanceMax.Value)   return false;
            if (c.OxygenPartialMin.HasValue    && o2                 < c.OxygenPartialMin.Value)    return false;
            if (c.OxygenPartialMax.HasValue    && o2                 > c.OxygenPartialMax.Value)    return false;
            if (c.AtmPressureMin.HasValue      && p.AtmPressure      < c.AtmPressureMin.Value)      return false;
            if (c.AtmPressureMax.HasValue      && p.AtmPressure      > c.AtmPressureMax.Value)      return false;
            if (c.SurfaceRadiationMin.HasValue && p.SurfaceRadiation < c.SurfaceRadiationMin.Value) return false;
            if (c.SurfaceRadiationMax.HasValue && p.SurfaceRadiation > c.SurfaceRadiationMax.Value) return false;
            if (c.GMin.HasValue                && p.SurfaceGravity   < c.GMin.Value)                return false;
            if (c.GMax.HasValue                && p.SurfaceGravity   > c.GMax.Value)                return false;
            if (c.SurfaceTempMin.HasValue      && p.SurfaceTemp      < c.SurfaceTempMin.Value)      return false;
            if (c.SurfaceTempMax.HasValue      && p.SurfaceTemp      > c.SurfaceTempMax.Value)      return false;
            return true;
        }

        private static bool ExpansionIsTerraformable(PlanetData p, RaceConfig rc)
        {
            var c = rc?.PlanetConditions;
            if (c == null) return false;
            float o2 = p.OxygenPercent / 100f * p.AtmPressure;
            if (c.WaterAbundanceTfMin.HasValue   && p.WaterAbundance   < c.WaterAbundanceTfMin.Value)   return false;
            if (c.WaterAbundanceTfMax.HasValue   && p.WaterAbundance   > c.WaterAbundanceTfMax.Value)   return false;
            if (c.OxygenPartialTfMin.HasValue    && o2                 < c.OxygenPartialTfMin.Value)    return false;
            if (c.OxygenPartialTfMax.HasValue    && o2                 > c.OxygenPartialTfMax.Value)    return false;
            if (c.AtmPressureTfMin.HasValue      && p.AtmPressure      < c.AtmPressureTfMin.Value)      return false;
            if (c.AtmPressureTfMax.HasValue      && p.AtmPressure      > c.AtmPressureTfMax.Value)      return false;
            if (c.SurfaceRadiationTfMin.HasValue && p.SurfaceRadiation < c.SurfaceRadiationTfMin.Value) return false;
            if (c.SurfaceRadiationTfMax.HasValue && p.SurfaceRadiation > c.SurfaceRadiationTfMax.Value) return false;
            if (c.GTfMin.HasValue                && p.SurfaceGravity   < c.GTfMin.Value)                return false;
            if (c.GTfMax.HasValue                && p.SurfaceGravity   > c.GTfMax.Value)                return false;
            if (c.SurfaceTempTfMin.HasValue      && p.SurfaceTemp      < c.SurfaceTempTfMin.Value)      return false;
            if (c.SurfaceTempTfMax.HasValue      && p.SurfaceTemp      > c.SurfaceTempTfMax.Value)      return false;
            return true;
        }

        private static void ExpansionApplyOptimal(PlanetData p, RaceConfig rc)
        {
            var c = rc?.PlanetConditions;
            if (c == null) return;

            if (c.SurfaceTemp?.Optimal?.Length >= 2)
            {
                p.TerraformOriginals["SurfaceTemp"] = p.SurfaceTemp;
                p.SurfaceTemp = (c.SurfaceTemp.Optimal[0] + c.SurfaceTemp.Optimal[1]) * 0.5f;
            }
            else if (c.SurfaceTempMin.HasValue && c.SurfaceTempMax.HasValue)
            {
                p.TerraformOriginals["SurfaceTemp"] = p.SurfaceTemp;
                p.SurfaceTemp = (c.SurfaceTempMin.Value + c.SurfaceTempMax.Value) * 0.5f;
            }

            if (c.AtmPressureMin.HasValue && c.AtmPressureMax.HasValue)
            {
                p.TerraformOriginals["AtmPressure"] = p.AtmPressure;
                p.AtmPressure = (c.AtmPressureMin.Value + c.AtmPressureMax.Value) * 0.5f;
            }

            if (c.WaterAbundanceMin.HasValue && c.WaterAbundanceMax.HasValue)
            {
                p.TerraformOriginals["WaterAbundance"] = p.WaterAbundance;
                p.WaterAbundance = (c.WaterAbundanceMin.Value + c.WaterAbundanceMax.Value) * 0.5f;
            }

            if (c.OxygenPartialMin.HasValue && c.OxygenPartialMax.HasValue && p.AtmPressure > 0f)
            {
                p.TerraformOriginals["OxygenPercent"] = p.OxygenPercent;
                float targetO2 = (c.OxygenPartialMin.Value + c.OxygenPartialMax.Value) * 0.5f;
                p.OxygenPercent = Mathf.Clamp(targetO2 / p.AtmPressure * 100f, 0f, 100f);
            }

            if (c.SurfaceRadiationMin.HasValue && c.SurfaceRadiationMax.HasValue)
            {
                p.TerraformOriginals["SurfaceRadiation"] = p.SurfaceRadiation;
                p.SurfaceRadiation = (c.SurfaceRadiationMin.Value + c.SurfaceRadiationMax.Value) * 0.5f;
            }

            RecomputeHydrologyState(p);
        }
    }
}
