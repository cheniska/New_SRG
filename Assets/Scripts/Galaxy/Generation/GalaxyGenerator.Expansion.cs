using System;
using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Simulation;

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
                        RoleKey = roleKey, Role = roleCfg, InitialDate = initialDate,
                    });
                }
            states.Sort((a, b) => a.StartDate.CompareTo(b.StartDate));

            AssignExpansionTargets(states, galaxy.StarsMap.Count);

            BuildHomeSectors(galaxy, states);

            foreach (var st in states)
            {
                foreach (var star in galaxy.StarsMap.Values)
                    if (star.IsPremade)
                        foreach (var p in star.Planets)
                            if (p.Race == st.Key) { st.Frontier.Add(star); st.Presence.Add(star.Uid); break; }
                bool coverage = st.Role != null && st.Role.UsesCoverage;
                st.Done = (!coverage && st.Target <= 0) || st.Frontier.Count == 0;
            }

            var allCfgs = new List<RaceConfig>(states.Count);
            foreach (var st in states) allCfgs.Add(st.Cfg);

            // Группы по ролям (порядок — по самой ранней дате начала колонизации в группе).
            var groups = new List<List<ExpansionRaceState>>();
            var groupByRole = new Dictionary<string, List<ExpansionRaceState>>();
            foreach (var st in states)
            {
                string gk = st.RoleKey ?? string.Empty;
                if (!groupByRole.TryGetValue(gk, out var g)) { g = new List<ExpansionRaceState>(); groupByRole[gk] = g; groups.Add(g); }
                g.Add(st);
            }

            foreach (var group in groups)
            {
                if (group[0].Role != null && group[0].Role.UsesCoverage)
                    RunCoverageExpansion(group, galaxy, premadeUids, allCfgs);
                else
                    RunTargetExpansion(group, galaxy, premadeUids, allCfgs);
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
            public DateTime InitialDate;
            public float Speed;
            public string RoleKey;
            public GalaxyRaceRoleConfig Role;
            public int Target;
            public int Colonized;
            public int Seeded;
            public float Offset;   // режим покрытия: насколько раса «крупнее» минимальной, в системах
            public bool Done;
            public readonly List<StarData> Frontier = new();
            public readonly HashSet<string> Presence = new();
        }

        /// <summary>
        /// Честная очередь по целям (SystemsShare/Overlap или старая TargetSystemsPerRace): ходит раса с наименьшей
        /// долей выполненной цели (при равенстве — раньше начавшая колонизацию).
        /// </summary>
        private void RunTargetExpansion(List<ExpansionRaceState> group, GalaxyData galaxy,
            HashSet<string> premadeUids, List<RaceConfig> allCfgs)
        {
            while (true)
            {
                ExpansionRaceState cur = null;
                float curRatio = float.MaxValue;
                foreach (var st in group)
                {
                    if (st.Done) continue;
                    float ratio = (float)st.Colonized / Mathf.Max(1, st.Target);
                    if (ratio < curRatio) { curRatio = ratio; cur = st; }
                }
                if (cur == null) break;

                if (!ExpansionStep(cur, galaxy, premadeUids, allCfgs)) cur.Done = true;
                if (cur.Colonized >= cur.Target) cur.Done = true;
            }
        }

        /// <summary>
        /// Режим покрытия: расы роли расселяются, пока доля систем галактики, заселённых ролью, не достигнет
        /// цели (Coverage). Каждая раса получает сдвиг Offset (0..spread систем); ходит раса с наименьшим
        /// Colonized − Offset, поэтому итоговая разница между расами ≈ spread. Раса не может обогнать самую
        /// мелкую (включая исчерпавшие варианты) больше чем на верхнюю границу RaceSpread.
        /// </summary>
        private void RunCoverageExpansion(List<ExpansionRaceState> group, GalaxyData galaxy,
            HashSet<string> premadeUids, List<RaceConfig> allCfgs)
        {
            var role = group[0].Role;
            int starsCount = galaxy.StarsMap.Count;
            float goal   = RollRange(role.Coverage, 0.9f) * starsCount;
            float spread = RollRange(role.RaceSpread, 0.05f) * starsCount;
            float cap    = (role.RaceSpread != null && role.RaceSpread.Length > 1 ? role.RaceSpread[1]
                          : role.RaceSpread != null && role.RaceSpread.Length > 0 ? role.RaceSpread[0] : 0.1f) * starsCount;
            AssignRaceOffsets(group, role, spread);
            foreach (var st in group)
                st.Target = Mathf.RoundToInt(goal * Mathf.Max(1f, role.Overlap) / group.Count + st.Offset);   // оценка — для дат рёбер и лога

            var raceSet = new HashSet<string>();
            foreach (var st in group) raceSet.Add(st.Key);

            while (CountCoveredStars(galaxy, raceSet) < goal)
            {
                int minColonized = int.MaxValue;
                foreach (var st in group) minColonized = Mathf.Min(minColonized, st.Colonized);

                ExpansionRaceState cur = null;
                float best = float.MaxValue;
                foreach (var st in group)
                {
                    if (st.Done || st.Colonized + 1 - minColonized > cap) continue;
                    float key = st.Colonized - st.Offset;
                    if (key < best) { best = key; cur = st; }
                }
                if (cur == null) break;

                if (!ExpansionStep(cur, galaxy, premadeUids, allCfgs)) cur.Done = true;
            }
        }

        private static float RollRange(float[] range, float fallback)
        {
            if (range == null || range.Length == 0) return fallback;
            if (range.Length == 1) return range[0];
            return GameRng.Range(Mathf.Min(range[0], range[1]), Mathf.Max(range[0], range[1]));
        }

        /// <summary>Сдвиги рас 0..spread: по весам роли, а если весов нет или они равны — случайно.</summary>
        private static void AssignRaceOffsets(List<ExpansionRaceState> group, GalaxyRaceRoleConfig role, float spread)
        {
            var raw = new float[group.Count];
            bool weighted = role.Weights != null && role.Weights.Count > 0;
            for (int i = 0; i < group.Count; i++)
                raw[i] = weighted && role.Weights.TryGetValue(group[i].Key, out var w) ? w : 1f;

            float min = float.MaxValue, max = float.MinValue;
            foreach (var v in raw) { min = Mathf.Min(min, v); max = Mathf.Max(max, v); }
            if (max - min < 1e-4f)
            {
                for (int i = 0; i < raw.Length; i++) raw[i] = GameRng.Value;
                min = float.MaxValue; max = float.MinValue;
                foreach (var v in raw) { min = Mathf.Min(min, v); max = Mathf.Max(max, v); }
            }
            for (int i = 0; i < group.Count; i++)
                group[i].Offset = max - min < 1e-4f ? 0f : (raw[i] - min) / (max - min) * spread;
        }

        private static int CountCoveredStars(GalaxyData galaxy, HashSet<string> raceSet)
        {
            int n = 0;
            foreach (var star in galaxy.StarsMap.Values)
                foreach (var p in star.Planets)
                    if (raceSet.Contains(p.Race)) { n++; break; }
            return n;
        }

        // Родные сектора: premade-сектор с расой, у которой там есть premade-звезда. Другие расы его не колонизируют.
        private readonly Dictionary<string, string> _homeSectorRace = new();

        private void BuildHomeSectors(GalaxyData galaxy, List<ExpansionRaceState> states)
        {
            _homeSectorRace.Clear();
            var keys = new HashSet<string>();
            foreach (var st in states) keys.Add(st.Key);
            foreach (var sector in galaxy.Sectors)
            {
                if (string.IsNullOrEmpty(sector.Race) || !keys.Contains(sector.Race)) continue;
                foreach (var star in sector.Stars)
                    if (star.IsPremade) { _homeSectorRace[sector.Uid] = sector.Race; break; }
            }
        }

        private bool IsForeignHomeSector(StarData star, string raceKey) =>
            star.ParentSector != null
            && _homeSectorRace.TryGetValue(star.ParentSector.Uid, out var owner)
            && owner != raceKey;

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
                if (st.Role != null && st.Role.UsesCoverage) continue;   // цели считает RunCoverageExpansion
                if (st.Role != null && st.Role.SystemsShare > 0f)
                {
                    int count  = perRole[st.RoleKey];
                    float share = Mathf.Clamp01(st.Role.SystemsShare);
                    int baseVal = Mathf.RoundToInt(starsCount * share * Mathf.Max(1f, st.Role.Overlap) / count);
                    int jitter  = Mathf.Max(0, st.Role.Jitter);
                    st.Target = Mathf.Max(0, baseVal + GameRng.Range(-jitter, jitter + 1));
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

            float fraction = GameRng.Range(fracMin, fracMax);
            int   baseVal  = Mathf.RoundToInt(starsCount * fraction * mult / raceCount);
            int   target   = baseVal + GameRng.Range(-jitter, jitter + 1);
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
                sb.Append($"{st.Key}[{st.RoleKey ?? "-"}]={perRace[st.Key]}/{st.Target}(offset {st.Offset:F1}, seeded {st.Seeded}) ");
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
                    if (IsForeignHomeSector(star, st.Key)) continue;

                    float minDist = MinDistToFrontier(star, st.Frontier);
                    if (minDist > radius) continue;

                    bool hasHab = false, hasTf = false, occupied = false;
                    int contest = int.MaxValue;
                    foreach (var p in star.Planets)
                    {
                        if (p.Race != GalaxyConstants.RACE_NONE_KEY) { occupied = true; continue; }
                        if (!IsPopulatedSize(p)) continue;
                        bool h = ExpansionIsHabitable(p, st.Cfg);
                        bool t = !h && ExpansionIsTerraformable(p, st.Cfg);
                        if (!h && !t) continue;
                        hasHab |= h; hasTf |= t;
                        contest = Mathf.Min(contest, ExpansionContest(p, allCfgs));
                    }
                    if (!hasHab && !hasTf) continue;
                    // Предпочтение пустых систем: уже заселённая система «спорнее» на OccupiedPenalty.
                    if (occupied) contest += st.Role?.OccupiedPenalty ?? 0;

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
                // Темп из досье (систем/год). Если при нём цель не успевает к стартовой дате игры,
                // шкала сжимается, чтобы все рёбра экспансии были датированы до InitialDate.
                double speed      = System.Math.Max(st.Speed, 0.001);
                double yearOffset = (st.Colonized + 1.0) / speed;
                double fullSpan   = System.Math.Max(1, st.Target) / speed;
                double maxSpan    = (st.InitialDate - st.StartDate).TotalDays / 365.25;
                if (maxSpan > 0 && fullSpan > maxSpan) yearOffset *= maxSpan / fullSpan;
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
        /// Гарантия ниши (крайняя мера, при текущей физике срабатывает редко): берёт свободную каменную планету
        /// заселяемого размера, у которой температура и гравитация уже естественно подходят расе, и «достраивает»
        /// только то, что определяется биосферой — атмосферу (давление, O₂) и гидросферу. Орбита, масса и
        /// температура остаются физическими. Предпочитает ближайшие к фронтиру и ещё незаселённые системы.
        /// </summary>
        private StarData SeedNicheWorld(ExpansionRaceState st, GalaxyData galaxy, HashSet<string> premadeUids)
        {
            var c = st.Cfg?.PlanetConditions;
            if (c == null) return null;
            float tOpt = c.SurfaceTemp?.Optimal?.Length >= 2
                ? (c.SurfaceTemp.Optimal[0] + c.SurfaceTemp.Optimal[1]) * 0.5f
                : (c.SurfaceTempMin.HasValue && c.SurfaceTempMax.HasValue ? (c.SurfaceTempMin.Value + c.SurfaceTempMax.Value) * 0.5f : 288f);

            StarData bestStar = null;
            PlanetData bestPlanet = null;
            float bestScore = float.MaxValue;
            foreach (var star in galaxy.StarsMap.Values)
            {
                if (st.Presence.Contains(star.Uid) || premadeUids.Contains(star.Uid)) continue;
                if (IsForeignHomeSector(star, st.Key)) continue;

                bool inhabited = false;
                PlanetData cand = null;
                float candDt = float.MaxValue;
                foreach (var p in star.Planets)
                {
                    if (p.Race != GalaxyConstants.RACE_NONE_KEY) { inhabited = true; continue; }
                    if (!IsPopulatedSize(p) || p.Density < 2f) continue;
                    if (c.SurfaceTempMin.HasValue && p.SurfaceTemp < c.SurfaceTempMin.Value) continue;
                    if (c.SurfaceTempMax.HasValue && p.SurfaceTemp > c.SurfaceTempMax.Value) continue;
                    if (c.GMin.HasValue && p.SurfaceGravity < c.GMin.Value) continue;
                    if (c.GMax.HasValue && p.SurfaceGravity > c.GMax.Value) continue;
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

        /// <summary>Биосферные параметры «родного» мира: атмосфера и гидросфера в центральной части диапазонов расы.</summary>
        private static void ApplyNativeConditions(PlanetData p, RacePlanetConditionsConfig c)
        {
            static float Mid(float? min, float? max, float fallback) =>
                min.HasValue && max.HasValue ? Mathf.Lerp(min.Value, max.Value, GameRng.Range(0.3f, 0.7f)) : fallback;

            p.AtmPressure    = Mid(c.AtmPressureMin, c.AtmPressureMax, p.AtmPressure);
            p.WaterAbundance = Mid(c.WaterAbundanceMin, c.WaterAbundanceMax, p.WaterAbundance);
            if (p.AtmPressure > 0f && c.OxygenPartialMin.HasValue && c.OxygenPartialMax.HasValue)
            {
                float o2 = Mid(c.OxygenPartialMin, c.OxygenPartialMax, 0.2f);
                p.OxygenPercent = Mathf.Clamp(o2 / p.AtmPressure * 100f, 0f, 100f);
            }
            // Радиация у поверхности зависит от толщины атмосферы — пересчитываем по той же формуле, что в физике.
            p.SurfaceRadiation = p.SolarFlux * 1000f * 1.361f / ((1f + 0.36f * p.AtmPressure) * (1f + 0.36f * p.MagneticField));

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

        private static bool ExpansionIsHabitable(PlanetData p, RaceConfig rc) =>
            rc?.PlanetConditions == null || RaceHabitability.IsHabitable(p, rc.PlanetConditions);

        private static bool ExpansionIsTerraformable(PlanetData p, RaceConfig rc) =>
            rc?.PlanetConditions != null && RaceHabitability.IsTerraformable(p, rc.PlanetConditions);

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
