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

            var galaxyCfg   = _ctx.ActiveGalaxyConfig;
            var galaxyRaces = GetActiveGalaxyRaceSet();
            var states = new List<ExpansionRaceState>();
            if (_ctx.Config.Races != null)
                foreach (var kvp in _ctx.Config.Races)
                {
                    if (kvp.Key == GalaxyConstants.RACE_NONE_KEY) continue;
                    if (kvp.Value.NonPlanetary != 0) continue;
                    // Мульти-галактика: пропускаем расы, не разрешённые текущей галактикой.
                    if (galaxyRaces != null && !galaxyRaces.Contains(kvp.Key)) continue;

                    GalaxyRaceRoleConfig roleCfg = null;
                    string roleKey = galaxyCfg != null ? galaxyCfg.FindRaceRole(kvp.Key, out roleCfg) : null;
                    if (roleCfg != null && !roleCfg.Colonize) continue;

                    string rawDate = kvp.Value.ColonizationStartDate
                                     ?? _ctx.Config.Settings?.InitialDate
                                     ?? "01.01.0001";
                    if (!DateTime.TryParseExact(rawDate, "dd.MM.yyyy", null,
                            System.Globalization.DateTimeStyles.None, out DateTime sd))
                        sd = initialDate;

                    states.Add(new ExpansionRaceState
                    {
                        Key = kvp.Key, Cfg = kvp.Value, StartDate = sd, Speed = kvp.Value.ColonizationSpeed,
                        RoleKey = roleKey, Role = roleCfg,
                    });
                }
            states.Sort((a, b) => a.StartDate.CompareTo(b.StartDate));

            AssignExpansionTargets(states, galaxy.StarsMap.Count);

            foreach (var st in states)
            {
                foreach (var star in galaxy.StarsMap.Values)
                    if (star.IsPremade)
                        foreach (var p in star.Planets)
                            if (p.Race == st.Key) { st.Frontier.Add(star); st.Presence.Add(star.Uid); break; }
                st.Done = st.Target <= 0 || st.Frontier.Count == 0;
            }

            // Честная очередь: на каждом шаге ходит раса с наименьшей долей выполненной цели
            // (при равенстве — раньше начавшая колонизацию). Раньше расы расселялись строго по очереди,
            // и поздние/узкоспециализированные (напр. Race4) получали только объедки за «универсалами».
            var allCfgs = new List<RaceConfig>(states.Count);
            foreach (var st in states) allCfgs.Add(st.Cfg);
            while (true)
            {
                ExpansionRaceState cur = null;
                float curRatio = float.MaxValue;
                foreach (var st in states)
                {
                    if (st.Done) continue;
                    float ratio = (float)st.Colonized / Mathf.Max(1, st.Target);
                    if (ratio < curRatio) { curRatio = ratio; cur = st; }   // states отсортированы по дате — tie-break
                }
                if (cur == null) break;

                if (!ExpansionStep(cur, galaxy, premadeUids, allCfgs)) cur.Done = true;
                if (cur.Colonized >= cur.Target) cur.Done = true;
            }

            foreach (var sector in galaxy.Sectors)
            {
                foreach (var star in sector.Stars)
                    if (!star.IsPremade)
                        star.Race = OwnerResolver.ComputeRaceFromChildren(star.Planets, p => p.Race);

                if (!OwnerResolver.IsFixedRace(sector.Race, _ctx.AvailableRaces))
                    sector.Race = OwnerResolver.ComputeRaceFromChildren(sector.Stars, s => s.Race);
            }

            LogExpansionStats(galaxy, states);
        }

        private sealed class ExpansionRaceState
        {
            public string Key;
            public RaceConfig Cfg;
            public DateTime StartDate;
            public float Speed;
            public string RoleKey;
            public GalaxyRaceRoleConfig Role;
            public int Target;
            public int Colonized;
            public int Seeded;
            public bool Done;
            public readonly List<StarData> Frontier = new();
            public readonly HashSet<string> Presence = new();
        }

        /// <summary>
        /// Цели по системам. Расы с ролью, у которой задан SystemsShare, получают одинаковую цель внутри роли:
        /// round(StarsCount × SystemsShare × Overlap / RaceCountInRole) ± Jitter. Остальные — старая формула
        /// <see cref="ComputeTargetSystems"/>.
        /// </summary>
        private void AssignExpansionTargets(List<ExpansionRaceState> states, int starsCount)
        {
            var perRole = new Dictionary<string, int>();
            foreach (var st in states)
                if (st.Role != null && st.Role.SystemsShare > 0f)
                    perRole[st.RoleKey] = perRole.TryGetValue(st.RoleKey, out var n) ? n + 1 : 1;

            foreach (var st in states)
            {
                if (st.Role != null && st.Role.SystemsShare > 0f)
                {
                    int count  = perRole[st.RoleKey];
                    float share = Mathf.Clamp01(st.Role.SystemsShare);
                    int baseVal = Mathf.RoundToInt(starsCount * share * Mathf.Max(1f, st.Role.Overlap) / count);
                    int jitter  = Mathf.Max(0, st.Role.Jitter);
                    st.Target = Mathf.Max(0, baseVal + UnityEngine.Random.Range(-jitter, jitter + 1));
                }
                else
                    st.Target = ComputeTargetSystems(starsCount, states.Count);
            }
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

        private void LogExpansionStats(GalaxyData galaxy, List<ExpansionRaceState> states)
        {
            var perRace = new Dictionary<string, int>();
            foreach (var st in states) perRace[st.Key] = 0;

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
            foreach (var st in states)
                sb.Append($"{st.Key}[{st.RoleKey ?? "-"}]={perRace[st.Key]}/{st.Target}(seeded {st.Seeded}) ");
            int totalStars = galaxy.StarsMap.Count;
            float emptyPct = totalStars > 0 ? 100f * emptySystems / totalStars : 0f;
            float emptyPlanetsPct = planetsInHabitedSystems > 0 ? 100f * emptyPlanetsInHabitedSystems / planetsInHabitedSystems : 0f;
            sb.Append($"| empty systems={emptySystems}/{totalStars} ({emptyPct:F1}%) ");
            sb.Append($"| empty planets in habited systems={emptyPlanetsInHabitedSystems}/{planetsInHabitedSystems} ({emptyPlanetsPct:F1}%)");
            UnityEngine.Debug.Log(sb.ToString());
        }

        /// <summary>Один шаг расселения расы: найти ближайшую подходящую систему (или создать нишу) и колонизировать.</summary>
        private bool ExpansionStep(ExpansionRaceState st, GalaxyData galaxy, HashSet<string> premadeUids, List<RaceConfig> allCfgs)
        {
            var frontierSectors = new HashSet<string>(st.Frontier.Count);
            foreach (var s in st.Frontier)
                if (s.ParentSector != null) frontierSectors.Add(s.ParentSector.Uid);

            StarData target = null;

            for (float radius = ExpansionRadiusStart;
                 radius <= ExpansionRadiusMax && target == null;
                 radius += ExpansionRadiusStep)
            {
                var sameFull    = new List<(StarData s, float d, int c)>();
                var sameTf      = new List<(StarData s, float d, int c)>();
                var otherFull   = new List<(StarData s, float d, int c)>();
                var otherTf     = new List<(StarData s, float d, int c)>();

                foreach (var star in galaxy.StarsMap.Values)
                {
                    if (st.Presence.Contains(star.Uid)) continue;
                    if (premadeUids.Contains(star.Uid)) continue;

                    float minDist = MinDistToFrontier(star, st.Frontier);
                    if (minDist > radius) continue;

                    bool hasHab = false, hasTf = false;
                    int contest = int.MaxValue;
                    foreach (var p in star.Planets)
                    {
                        if (p.Race != GalaxyConstants.RACE_NONE_KEY || !IsPopulatedSize(p)) continue;
                        bool h = ExpansionIsHabitable(p, st.Cfg);
                        bool t = !h && ExpansionIsTerraformable(p, st.Cfg);
                        if (!h && !t) continue;
                        hasHab |= h; hasTf |= t;
                        contest = Mathf.Min(contest, ExpansionContest(p, allCfgs));
                    }
                    if (!hasHab && !hasTf) continue;

                    bool same = frontierSectors.Contains(star.ParentSector?.Uid);
                    var  entry = (star, minDist, contest);

                    if (same) { if (hasHab) sameFull.Add(entry); else sameTf.Add(entry); }
                    else      { if (hasHab) otherFull.Add(entry); else otherTf.Add(entry); }
                }

                foreach (var bucket in new[] { sameFull, sameTf, otherFull, otherTf })
                {
                    if (bucket.Count == 0) continue;
                    // Сначала наименее «спорные» миры (которые подходят меньшему числу рас), затем ближайшие:
                    // универсалы не выедают ниши узкоспециализированных рас.
                    bucket.Sort((a, b) => a.c != b.c ? a.c.CompareTo(b.c) : a.d.CompareTo(b.d));
                    target = bucket[0].s;
                    break;
                }
            }

            if (target == null && st.Role != null && st.Role.GuaranteeNiche)
            {
                target = SeedNicheWorld(st, galaxy, premadeUids);
                if (target != null) st.Seeded++;
            }

            if (target == null) return false;

            StarData closestFs = null;
            float closestFsDist = float.MaxValue;
            foreach (var fs in st.Frontier)
            {
                float d = (target.Position - fs.Position).magnitude;
                if (d < closestFsDist) { closestFsDist = d; closestFs = fs; }
            }
            if (closestFs != null)
            {
                double yearOffset = (st.Colonized + 1.0) / System.Math.Max(st.Speed, 0.001);
                int colonizationYear = st.StartDate.AddDays(yearOffset * 365.25).Year;
                galaxy.ExpansionEdges.Add(new ExpansionEdge
                {
                    RaceKey = st.Key,
                    From = closestFs.Position,
                    To = target.Position,
                    ColonizationYear = colonizationYear,
                });
            }

            ExpansionColonizeStar(target, st.Key, st.Cfg, allCfgs);
            st.Presence.Add(target.Uid);
            st.Frontier.Add(target);
            st.Colonized++;
            return true;
        }

        private static float MinDistToFrontier(StarData star, List<StarData> frontier)
        {
            float minDist = float.MaxValue;
            foreach (var fs in frontier)
            {
                float d = (star.Position - fs.Position).magnitude;
                if (d < minDist) minDist = d;
            }
            return minDist;
        }

        /// <summary>Планета размера, у которого в конфиге есть население (Population[1] &gt; 0).
        /// Колонии на Tiny/Giant получали бы население 0 — ни торговли, ни NPC-логики.</summary>
        private bool IsPopulatedSize(PlanetData p)
        {
            if (_ctx.PlanetSizeRules.Count == 0) return true;
            return _ctx.PlanetSizeRules.TryGetValue(p.Size ?? string.Empty, out var sd)
                   && sd.Population != null && sd.Population.Length >= 2 && sd.Population[1] > 0;
        }

        /// <summary>Сколько колонизирующих рас галактики могут жить на планете (полностью или через терраформ).</summary>
        private static int ExpansionContest(PlanetData p, List<RaceConfig> allCfgs)
        {
            int n = 0;
            foreach (var rc in allCfgs)
                if (ExpansionIsHabitable(p, rc) || ExpansionIsTerraformable(p, rc)) n++;
            return n;
        }

        /// <summary>
        /// Гарантия ниши: превращает свободную планету подходящего размера (температура в пределах
        /// Terraformable расы) в родной мир расы. Предпочитает ближайшие к фронтиру и ещё незаселённые системы.
        /// </summary>
        private StarData SeedNicheWorld(ExpansionRaceState st, GalaxyData galaxy, HashSet<string> premadeUids)
        {
            var c = st.Cfg?.PlanetConditions;
            if (c == null) return null;
            float tMin = c.SurfaceTempTfMin ?? c.SurfaceTempMin ?? float.MinValue;
            float tMax = c.SurfaceTempTfMax ?? c.SurfaceTempMax ?? float.MaxValue;
            float tOpt = c.SurfaceTemp?.Optimal?.Length >= 2
                ? (c.SurfaceTemp.Optimal[0] + c.SurfaceTemp.Optimal[1]) * 0.5f
                : (c.SurfaceTempMin.HasValue && c.SurfaceTempMax.HasValue ? (c.SurfaceTempMin.Value + c.SurfaceTempMax.Value) * 0.5f : 288f);

            StarData bestStar = null;
            PlanetData bestPlanet = null;
            float bestScore = float.MaxValue;
            foreach (var star in galaxy.StarsMap.Values)
            {
                if (st.Presence.Contains(star.Uid) || premadeUids.Contains(star.Uid)) continue;

                bool inhabited = false;
                PlanetData cand = null;
                float candDt = float.MaxValue;
                foreach (var p in star.Planets)
                {
                    if (p.Race != GalaxyConstants.RACE_NONE_KEY) { inhabited = true; continue; }
                    if (!IsPopulatedSize(p) || p.SurfaceTemp < tMin || p.SurfaceTemp > tMax) continue;
                    float dt = Mathf.Abs(p.SurfaceTemp - tOpt);
                    if (dt < candDt) { candDt = dt; cand = p; }
                }
                if (cand == null) continue;

                float score = MinDistToFrontier(star, st.Frontier) * (inhabited ? 1.5f : 1f);
                if (score < bestScore) { bestScore = score; bestStar = star; bestPlanet = cand; }
            }

            if (bestPlanet == null) return null;
            ApplyNativeConditions(bestPlanet, c);
            return bestStar;
        }

        /// <summary>Параметры «родного» мира: случайная точка в центральной части допустимых диапазонов расы.</summary>
        private static void ApplyNativeConditions(PlanetData p, RacePlanetConditionsConfig c)
        {
            static float Mid(float? min, float? max, float fallback) =>
                min.HasValue && max.HasValue ? Mathf.Lerp(min.Value, max.Value, UnityEngine.Random.Range(0.3f, 0.7f)) : fallback;

            if (c.SurfaceTemp?.Optimal?.Length >= 2)
                p.SurfaceTemp = UnityEngine.Random.Range(c.SurfaceTemp.Optimal[0], c.SurfaceTemp.Optimal[1]);
            else
                p.SurfaceTemp = Mid(c.SurfaceTempMin, c.SurfaceTempMax, p.SurfaceTemp);

            p.AtmPressure      = Mid(c.AtmPressureMin, c.AtmPressureMax, p.AtmPressure);
            p.WaterAbundance   = Mid(c.WaterAbundanceMin, c.WaterAbundanceMax, p.WaterAbundance);
            p.SurfaceRadiation = Mid(c.SurfaceRadiationMin, c.SurfaceRadiationMax, p.SurfaceRadiation);
            if (c.GMin.HasValue) p.SurfaceGravity = Mathf.Max(p.SurfaceGravity, c.GMin.Value);
            if (c.GMax.HasValue) p.SurfaceGravity = Mathf.Min(p.SurfaceGravity, c.GMax.Value);
            if (p.AtmPressure > 0f && c.OxygenPartialMin.HasValue && c.OxygenPartialMax.HasValue)
            {
                float o2 = Mid(c.OxygenPartialMin, c.OxygenPartialMax, 0.2f);
                p.OxygenPercent = Mathf.Clamp(o2 / p.AtmPressure * 100f, 0f, 100f);
            }

            RecomputeHydrologyState(p);
            RollSurfaceTypes(p);
        }

        /// <summary>
        /// Колонизирует систему: лучшую подходящую планету (полная пригодность, наименее спорная), плюс остальные
        /// полностью пригодные планеты, которые не нужны ни одной другой расе. Остальное остаётся другим расам —
        /// так появляются мультирасовые системы, а «универсалы» не забирают всю систему целиком.
        /// </summary>
        private void ExpansionColonizeStar(StarData star, string raceKey, RaceConfig raceCfg, List<RaceConfig> allCfgs)
        {
            PlanetData best = null;
            bool bestHab = false;
            int bestContest = int.MaxValue;
            var extra = new List<PlanetData>();

            foreach (var planet in star.Planets)
            {
                if (planet.Race != GalaxyConstants.RACE_NONE_KEY || !IsPopulatedSize(planet)) continue;

                bool hab = ExpansionIsHabitable(planet, raceCfg);
                bool tf  = !hab && ExpansionIsTerraformable(planet, raceCfg);
                if (!hab && !tf) continue;

                int contest = ExpansionContest(planet, allCfgs);
                if (hab && contest <= 1) extra.Add(planet);
                if (best == null || (hab && !bestHab) || (hab == bestHab && contest < bestContest))
                {
                    best = planet; bestHab = hab; bestContest = contest;
                }
            }
            if (best == null) return;

            ColonizePlanet(best, raceKey, raceCfg, !bestHab);
            foreach (var p in extra)
                if (p != best) ColonizePlanet(p, raceKey, raceCfg, false);
        }

        private void ColonizePlanet(PlanetData planet, string raceKey, RaceConfig raceCfg, bool terraform)
        {
            planet.Race  = raceKey;
            planet.Owner = GalaxyConstants.OWNER_NONE_KEY;
            planet.CurrentColor   = GenerationHelpers.GetRaceColor(raceKey, _ctx.AvailableRaces);
            planet.IsTerraformable = false;

            if (terraform) { ExpansionApplyOptimal(planet, raceCfg); planet.IsTerraformed = true; }
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
