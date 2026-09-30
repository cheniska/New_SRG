using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Config;
using SRG.Galaxy.Generation;
using SRG.Simulation;

namespace SRG.Galaxy
{
    #region Save / Load


    #endregion

    #region Custom Property Resolver

    public static class OwnerResolver
    {
        /// <summary>Разрешает Owner объекта: если parent зафиксирован — он главнее; иначе child.</summary>
        public static string ResolveOwner(string child, string parent,
            IReadOnlyDictionary<string, OwnerConfig> owners)
        {
            if (child == GalaxyConstants.OWNER_NONE_KEY) return GalaxyConstants.OWNER_NONE_KEY;
            if (IsFixedOwner(parent, owners)) return parent;
            return IsFixedOwner(child, owners) ? child : GalaxyConstants.OWNER_NONE_KEY;
        }

        /// <summary>Разрешает Race объекта: если parent зафиксирован — он главнее; иначе child.</summary>
        public static string ResolveRace(string child, string parent,
            IReadOnlyDictionary<string, RaceInfo> races)
        {
            if (child == GalaxyConstants.RACE_NONE_KEY) return GalaxyConstants.RACE_NONE_KEY;
            if (IsFixedRace(parent, races)) return parent;
            return IsFixedRace(child, races) ? child : GalaxyConstants.RACE_NONE_KEY;
        }

        /// <summary>Обратная совместимость: проверяет по races (биологические расы).</summary>
        public static string Resolve(string child, string parent,
            IReadOnlyDictionary<string, RaceInfo> races)
            => ResolveRace(child, parent, races);

        public static bool IsFixedOwner(string owner, IReadOnlyDictionary<string, OwnerConfig> owners) =>
            !string.IsNullOrEmpty(owner)
            && owner != GalaxyConstants.OWNER_NONE_KEY
            && owner != GalaxyConstants.OWNER_UNRESOLVED_KEY
            && owner != GalaxyConstants.OWNER_MIXED_KEY
            && owners != null && owners.ContainsKey(owner);

        public static bool IsFixed(string owner, IReadOnlyDictionary<string, RaceInfo> races) =>
            !string.IsNullOrEmpty(owner)
            && owner != GalaxyConstants.RACE_NONE_KEY
            && owner != GalaxyConstants.RACE_COMMON_KEY
            && races.ContainsKey(owner);

        public static bool IsFixedRace(string race, IReadOnlyDictionary<string, RaceInfo> races) =>
            IsFixed(race, races);

        /// <summary>Вычисляет агрегированный Owner из дочерних: единственный → он, несколько → Mixed, ноль → None.</summary>
        public static string ComputeOwnerFromChildren(IEnumerable<string> childOwners)
        {
            string single = null;
            bool multiple = false;
            foreach (var o in childOwners)
            {
                if (string.IsNullOrEmpty(o)
                    || o == GalaxyConstants.OWNER_NONE_KEY
                    || o == GalaxyConstants.OWNER_UNRESOLVED_KEY) continue;
                if (single == null) single = o;
                else if (o != single) { multiple = true; break; }
            }
            if (single == null) return GalaxyConstants.OWNER_NONE_KEY;
            return multiple ? GalaxyConstants.OWNER_MIXED_KEY : single;
        }

        /// <summary>Вычисляет агрегированную Race из дочерних: единственная → она, несколько → Mixed, ноль → None.</summary>
        public static string ComputeRaceFromChildren(IEnumerable<string> childRaces)
        {
            string single = null;
            bool multiple = false;
            foreach (var r in childRaces)
            {
                if (string.IsNullOrEmpty(r)
                    || r == GalaxyConstants.RACE_NONE_KEY
                    || r == GalaxyConstants.RACE_COMMON_KEY) continue;
                if (single == null) single = r;
                else if (r != single) { multiple = true; break; }
            }
            if (single == null) return GalaxyConstants.RACE_NONE_KEY;
            return multiple ? GalaxyConstants.RACE_MIXED_KEY : single;
        }

        public static string ComputeFromChildren(IEnumerable<string> childOwners)
            => ComputeOwnerFromChildren(childOwners);

        public static string ComputeOwnerFromChildren<T>(List<T> items, Func<T, string> selector)
        {
            string single = null;
            bool multiple = false;
            foreach (var item in items)
            {
                var o = selector(item);
                if (string.IsNullOrEmpty(o)
                    || o == GalaxyConstants.OWNER_NONE_KEY
                    || o == GalaxyConstants.OWNER_UNRESOLVED_KEY) continue;
                if (single == null) single = o;
                else if (o != single) { multiple = true; break; }
            }
            if (single == null) return GalaxyConstants.OWNER_NONE_KEY;
            return multiple ? GalaxyConstants.OWNER_MIXED_KEY : single;
        }

        public static string ComputeRaceFromChildren<T>(List<T> items, Func<T, string> selector)
        {
            string single = null;
            bool multiple = false;
            foreach (var item in items)
            {
                var r = selector(item);
                if (string.IsNullOrEmpty(r)
                    || r == GalaxyConstants.RACE_NONE_KEY
                    || r == GalaxyConstants.RACE_COMMON_KEY) continue;
                if (single == null) single = r;
                else if (r != single) { multiple = true; break; }
            }
            if (single == null) return GalaxyConstants.RACE_NONE_KEY;
            return multiple ? GalaxyConstants.RACE_MIXED_KEY : single;
        }
    }


    public static class CustomPropertyResolver
    {
        private static readonly Dictionary<Type, Dictionary<string, Func<object, string>>> _cache = new();
        public static void ResolveAndApplyProperties(
            object entity,
            Dictionary<string, string> customProperties,
            string category,
            GalaxyConfig config)
        {
            if (config.CustomProperties == null ||
                !config.CustomProperties.TryGetValue(category, out var props)) return;

            foreach (var pair in props)
                customProperties[pair.Key] = ResolveValue(entity, pair.Value);
        }

        private static string ResolveValue(object entity, CustomPropertyConfig propDef)
        {
            if (propDef.Default == null) return GetFallbackValue(propDef);
            if (propDef.Default.Type == JTokenType.String)
                return propDef.Default.ToString();
            if (propDef.Default.Type == JTokenType.Object)
            {
                var mappingObject = (JObject)propDef.Default;
                foreach (var dependency in mappingObject)
                {
                    string entityValue = GetEntityValue(entity, dependency.Key);
                    if (dependency.Value is JObject map &&
                        map.TryGetValue(entityValue, StringComparison.OrdinalIgnoreCase, out var result))
                        return result.ToString();
                }
                return GetFallbackValue(propDef);
            }
            if (propDef.Default.Type == JTokenType.Array)
            {
                var rules = propDef.Default.ToObject<List<CustomPropertyRule>>();
                if (rules == null) return GetFallbackValue(propDef);

                foreach (var rule in rules)
                {
                    if (!MatchesConditions(entity, rule.Conditions)) continue;

                    if (string.Equals(rule.Result, "Random", StringComparison.OrdinalIgnoreCase))
                        return GetRandomValue(propDef);

                    return rule.Result;
                }
            }

            return GetFallbackValue(propDef);
        }

        private static bool MatchesConditions(object entity,
            Dictionary<string, List<string>> conditions)
        {
            if (conditions == null || conditions.Count == 0) return true;

            foreach (var condition in conditions)
            {
                string entityValue = GetEntityValue(entity, condition.Key);
                bool anyMatch = false;
                foreach (var v in condition.Value)
                    if (string.Equals(v, entityValue, StringComparison.OrdinalIgnoreCase)) { anyMatch = true; break; }
                if (!anyMatch) return false;
            }
            return true;
        }

        private static string GetEntityValue(object entity, string fieldName)
        {
            var accessors = GetAccessors(entity.GetType());
            return accessors.TryGetValue(fieldName.ToLowerInvariant(), out var getter)
                ? getter(entity) ?? string.Empty
                : string.Empty;
        }

        private static Dictionary<string, Func<object, string>> GetAccessors(Type type)
        {
            if (_cache.TryGetValue(type, out var cached)) return cached;

            var map = new Dictionary<string, Func<object, string>>();

            foreach (var prop in type.GetProperties(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0) continue;
                var p = prop;
                map[p.Name.ToLowerInvariant()] = obj =>
                    p.GetValue(obj)?.ToString() ?? string.Empty;
            }

            foreach (var field in type.GetFields(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance))
            {
                var f = field;
                map[f.Name.ToLowerInvariant()] = obj =>
                    f.GetValue(obj)?.ToString() ?? string.Empty;
            }

            _cache[type] = map;
            return map;
        }

        private static string GetFallbackValue(CustomPropertyConfig propDef)
        {
            if (propDef.Values == null || propDef.Values.Count == 0)
                return GalaxyConstants.VAL_UNKNOWN;
            if (propDef.Values.Contains("No")) return "No";
            if (propDef.Values.Contains("None")) return "None";
            return propDef.Values[0];
        }

        private static string GetRandomValue(CustomPropertyConfig propDef)
        {
            if (propDef.Values == null || propDef.Values.Count == 0)
                return GalaxyConstants.VAL_UNKNOWN;
            return propDef.Values[UnityEngine.Random.Range(0, propDef.Values.Count)];
        }
    }

    #endregion

    #region Logger

    public static class GalaxyLogger
    {
        private const int MaxConsoleChunk = 15000;
        private static string LogDirectory => SRG.Utils.RuntimePaths.LogsDir;
        private static readonly Regex RichTagRegex = new Regex("<.*?>", RegexOptions.Compiled);

        public static void LogGalaxy(GalaxyData galaxy, int seed, GalaxyConfig config, Dictionary<string, RaceInfo> races)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<color=cyan><b>=== ОТЧЕТ О ГЕНЕРАЦИИ ГАЛАКТИКИ ===</b></color>");
            sb.AppendLine($"<color=white>Seed: {seed} | Секторов: {galaxy.Sectors.Count} | Звезд: {galaxy.StarsMap.Count}</color>");
            sb.AppendLine(new string('-', 50));

            foreach (var c in galaxy.Sectors.OrderBy(x => x.Name))
                AppendSectorBlock(sb, c, races, config);

            AppendGenerationSummary(sb, galaxy, seed, config);
            AppendDistanceTable(sb, galaxy, config);

            string log = sb.ToString();
            PrintToConsole(log);
            SaveLogToFile(log, seed);
        }

        private static void AppendGenerationSummary(StringBuilder sb, GalaxyData galaxy, int seed, GalaxyConfig config)
        {
            // Мультигалактика: берём конфиг именно ЭТОЙ галактики (по galaxy.Key), а не дефолтной —
            // иначе сводка сравнивает StarsCount/GridSize 2-й галактики с ожидаемыми из MilkyWay.
            string galKey = !string.IsNullOrEmpty(galaxy?.Key) ? galaxy.Key : GalaxyConstants.DEFAULT_GALAXY_KEY;
            var galCfg = config?.Galaxies?.GetValueOrDefault(galKey);
            int expSectors  = galCfg?.SectorCount        ?? -1;
            int expStars    = galCfg?.StarsCount          ?? -1;
            int minPerSect  = galCfg?.MinStarsPerSector   ?? 0;
            int maxPerSect  = galCfg?.MaxStarsPerSector   ?? 0;
            int gridW       = galCfg?.GalaxyGridSize?[0]  ?? galaxy.Width;
            int gridH       = galCfg?.GalaxyGridSize?[1]  ?? galaxy.Height;
            float minStarDistPc = galCfg?.MinStarDistanceParsecs ?? 0f;

            float cellSize     = expSectors > 0 ? Mathf.Sqrt((float)gridW * gridH / expSectors) : 0f;
            float sectorRadius = expSectors > 0 ? cellSize * 0.5f : 0f;
            float minStarDist  = minStarDistPc;

            sb.AppendLine();
            sb.AppendLine("<color=cyan><b>=== СВОДКА ГЕНЕРАЦИИ ===</b></color>");
            sb.AppendLine($"<color=white>Размер галактики : {gridW} x {gridH} пк</color>");
            sb.AppendLine($"<color=white>MinStarDist      : {minStarDistPc:F1} пк  |  sectorRadius (расч.): {sectorRadius:F2} пк  |  minStarDist (grid): {minStarDist:F2}</color>");

            string sectStatus = galaxy.Sectors.Count == expSectors ? "<color=green>OK</color>"
                : $"<color=red>НЕСОВПАДЕНИЕ</color>";
            string starStatus = galaxy.StarsMap.Count == expStars ? "<color=green>OK</color>"
                : $"<color=yellow>НЕСОВПАДЕНИЕ</color>";
            sb.AppendLine($"<color=white>Секторов : {galaxy.Sectors.Count} / {expSectors} (ожидалось) {sectStatus}</color>");
            sb.AppendLine($"<color=white>Звёзд    : {galaxy.StarsMap.Count} / {expStars} (ожидалось) {starStatus}  |  на сектор: {minPerSect}–{maxPerSect}</color>");

            sb.AppendLine();
            sb.AppendLine($"<color=white>{"№",-4} {"Сектор",-22} {"Звёзд",6} {"Мин.",6}  Статус</color>");
            sb.AppendLine(new string('-', 50));

            int idx = 1;
            foreach (var c in galaxy.Sectors.OrderBy(x => x.Name))
            {
                int count  = c.Stars.Count;
                string sta = count >= minPerSect
                    ? "<color=green>OK</color>"
                    : $"<color=red>МАЛО ({count} < {minPerSect})</color>";
                sb.AppendLine($"<color=white>{idx,-4} {c.Name,-22} {count,6} {minPerSect,6}  {sta}</color>");
                idx++;
            }
        }

        private static void AppendDistanceTable(StringBuilder sb, GalaxyData galaxy, GalaxyConfig config)
        {
            var stars = galaxy.StarsMap.Values.OrderBy(s => s.Name).ToList();
            if (stars.Count == 0) return;

            sb.AppendLine();
            sb.AppendLine("<color=cyan><b>=== РАССТОЯНИЯ МЕЖДУ ЗВЁЗДАМИ (пк) ===</b></color>");
            sb.AppendLine($"<color=white>{"№",-4} {"Звезда",-20}  Соседи по возрастанию дистанции</color>");
            sb.AppendLine(new string('-', 90));

            for (int i = 0; i < stars.Count; i++)
            {
                var a = stars[i];
                // Сортируем остальные по расстоянию
                var neighbors = stars
                    .Where(b => b != a)
                    .Select(b => {
                        float dx = a.Position.x - b.Position.x;
                        float dy = a.Position.y - b.Position.y;
                        return (star: b, dist: Mathf.Sqrt(dx*dx + dy*dy));
                    })
                    .OrderBy(t => t.dist)
                    .ToList();

                var parts = new StringBuilder();
                foreach (var (star, dist) in neighbors)
                    parts.Append($"{star.Name}({dist:F1})  ");

                sb.AppendLine($"<color=white>{i+1,-4} {a.Name,-20}  {parts}</color>");
            }
        }

        private static void AppendSectorBlock(StringBuilder sb, SectorData c, Dictionary<string, RaceInfo> races, GalaxyConfig config)
        {
            string owner = string.IsNullOrEmpty(c.Owner) ? GalaxyConstants.LOG_NEUTRAL_OWNER_LABEL : c.Owner;
            sb.AppendLine($"\n<color=yellow>Сектор: {GetColoredName(c.Name, c.Owner, races)}</color> (Владелец: {owner})");

            foreach (var star in c.Stars.OrderBy(x => x.Name))
            {
                string starOwner = string.IsNullOrEmpty(star.Owner) ? GalaxyConstants.LOG_NEUTRAL_OWNER_LABEL : star.Owner;
                string starNameColored = star.NameRenderData?.ToRichText() ?? star.Name;

                sb.AppendLine($"  <color=white>[Звезда]</color> {starNameColored} " +
                              $"(Тип: {star.Type}, Цвет: {star.Color}, Var: {star.GraphVar}, " +
                              $"Владелец: {starOwner}, Раса: {star.Race})" +
                              FormatProperties(star.CustomProperties));

                foreach (var planet in star.Planets.OrderBy(x => x.OrbitIndex))
                {
                    string planetNameColored = GetColoredPlanetName(planet, races);

                    string tfTag = planet.IsTerraformed ? " <color=lime>[Терраформирована]</color>" : "";
                    sb.AppendLine($"    <color=silver>></color> Планета: {planetNameColored}{tfTag} " +
                                  $"(Орбита: {planet.OrbitIndex}, Радиус: {planet.OrbitRadius:F2}, " +
                                  $"Размер: {planet.Size}, Раса: {planet.Race}, Владелец: {planet.Owner}, " +
                                  $"Плотность: {planet.Density:F2}, Гравитация: {planet.SurfaceGravity:F2}, Геоактивность: {planet.GeoActivity:F2})" +
                                  FormatProperties(planet.CustomProperties));
                    sb.AppendLine($"      <color=gray>StarFlux: {planet.SolarFlux:F1}  EquilTemp: {planet.SurfaceTemp:F1} K  " +
                                  $"MagField: {planet.MagneticField:F2}  SurfRad: {planet.SurfaceRadiation:F0} Вт/м²  AtmP: {planet.AtmPressure:F2} атм  " +
                                  $"O₂: {planet.OxygenPercent:F1}% (pO₂={planet.OxygenPercent / 100f * planet.AtmPressure:F3} атм)  Вода: {planet.WaterAbundance:F2}</color>");
                    if (planet.IsTerraformed && planet.TerraformOriginals.Count > 0)
                    {
                        var changes = string.Join("  ", planet.TerraformOriginals.Select(kv =>
                        {
                            float cur = kv.Key switch {
                                "SurfaceTemp"     => planet.SurfaceTemp,
                                "AtmPressure"     => planet.AtmPressure,
                                "WaterAbundance"  => planet.WaterAbundance,
                                "OxygenPercent"   => planet.OxygenPercent,
                                "SurfaceRadiation"=> planet.SurfaceRadiation,
                                _ => kv.Value
                            };
                            string arrow = cur > kv.Value + 0.001f ? "↑" : cur < kv.Value - 0.001f ? "↓" : "=";
                            return $"{kv.Key}: {kv.Value:F2}→{cur:F2}{arrow}";
                        }));
                        sb.AppendLine($"      <color=lime>Терраформирование: {changes}</color>");
                    }
                    string hab = FormatRaceHabitability(planet, config?.Races, races, richText: true);
                    if (!string.IsNullOrEmpty(hab))
                        sb.AppendLine($"      <color=white>Пригодность: {hab}</color>");
                }
            }
        }

        private static string GetColoredPlanetName(PlanetData planet, Dictionary<string, RaceInfo> races)
        {
            string color;
            if (planet.Race == GalaxyConstants.RACE_NONE_KEY || string.IsNullOrEmpty(planet.Race))
                color = GalaxyConstants.LOG_FALLBACK_STAR_COLOR;
            else
                color = races.TryGetValue(planet.Race, out var info) ? info.Color : GalaxyConstants.DEFAULT_COLOR;

            return $"<b><color={color}>{planet.Name}</color></b>";
        }

        private static string FormatProperties(Dictionary<string, string> props)
        {
            if (props == null || props.Count == 0) return string.Empty;
            return " | Свойства: " + string.Join(", ", props.Select(kv => $"{kv.Key}={kv.Value}"));
        }

        private static void PrintToConsole(string log)
        {
            for (int i = 0; i < log.Length; i += MaxConsoleChunk)
                Debug.Log(log.Substring(i, Math.Min(MaxConsoleChunk, log.Length - i)));
        }

        private static void SaveLogToFile(string content, int seed)
        {
            try
            {
                if (!Directory.Exists(LogDirectory)) Directory.CreateDirectory(LogDirectory);

                string clean = RichTagRegex.Replace(content, string.Empty);
                string fileName = $"GalaxyLog_Seed_{seed}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
                File.WriteAllText(Path.Combine(LogDirectory, fileName), clean);
                Debug.Log($"<color=green>Лог сохранён: {fileName}</color>");
            }
            catch (Exception e)
            {
                Debug.LogError($"[GalaxyLogger] Не удалось сохранить лог: {e.Message}");
            }
        }

        public static string GetColoredStarName(StarData star, Dictionary<string, RaceInfo> races)
        {
            if (string.IsNullOrEmpty(star.Name)) return $"{GalaxyConstants.VAL_UNKNOWN} Star";

            var groupCounts = new Dictionary<string, int>();
            int settledCount = 0;
            foreach (var p in star.Planets)
            {
                if (p.Race == GalaxyConstants.RACE_NONE_KEY) continue;
                groupCounts.TryGetValue(p.Race, out int cur);
                groupCounts[p.Race] = cur + 1;
                settledCount++;
            }

            if (groupCounts.Count == 0)
            {
                string fallbackColor = races.TryGetValue(GalaxyConstants.RACE_NONE_KEY, out var rNone)
                    ? rNone.Color : GalaxyConstants.LOG_FALLBACK_STAR_COLOR;
                return $"<color={fallbackColor}>{star.Name}</color>";
            }

            var sorted = new List<KeyValuePair<string, int>>(groupCounts);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));

            var sb = new StringBuilder(star.Name.Length * 2);
            int nameLength = star.Name.Length;
            int charIndex = 0;

            for (int i = 0; i < sorted.Count; i++)
            {
                var kv = sorted[i];
                int charCount = i == sorted.Count - 1
                    ? nameLength - charIndex
                    : Mathf.RoundToInt((float)kv.Value / settledCount * nameLength);

                charCount = Mathf.Clamp(charCount, 0, nameLength - charIndex);
                if (charCount <= 0) continue;

                string color = races.TryGetValue(kv.Key, out var info) ? info.Color : GalaxyConstants.DEFAULT_COLOR;
                sb.Append($"<color={color}>{star.Name.Substring(charIndex, charCount)}</color>");
                charIndex += charCount;
            }

            return sb.ToString();
        }

        public static string FormatRaceHabitability(PlanetData planet, Dictionary<string, RaceConfig> raceConfigs,
            Dictionary<string, RaceInfo> raceInfos, bool richText)
        {
            if (raceConfigs == null) return string.Empty;
            var parts = new List<string>();

            foreach (var kvp in raceConfigs)
            {
                if (kvp.Key == GalaxyConstants.RACE_NONE_KEY) continue;
                var cond = kvp.Value.PlanetConditions;
                if (cond == null) continue;

                RaceInfo ri = null;
                raceInfos?.TryGetValue(kvp.Key, out ri);
                string name  = ri?.Name  ?? kvp.Key;
                string color = ri?.Color ?? GalaxyConstants.DEFAULT_COLOR;

                var failed = new List<string>();
                float partialO2 = (planet.OxygenPercent / 100f) * planet.AtmPressure;
                AppendConditionLabel(planet.WaterAbundance,   cond.WaterAbundanceMin,   cond.WaterAbundanceMax,   "Вода", failed);
                AppendConditionLabel(partialO2,               cond.OxygenPartialMin,    cond.OxygenPartialMax,    "pO₂", failed);
                AppendConditionLabel(planet.AtmPressure,      cond.AtmPressureMin,      cond.AtmPressureMax,      "P",   failed);
                AppendConditionLabel(planet.SurfaceRadiation, cond.SurfaceRadiationMin, cond.SurfaceRadiationMax, "Rad", failed);
                AppendConditionLabel(planet.SurfaceGravity,   cond.GMin,                cond.GMax,                "g",   failed);
                AppendConditionLabel(planet.SurfaceTemp,      cond.SurfaceTempMin,      cond.SurfaceTempMax,      "T",   failed);

                bool fit = failed.Count == 0;
                string entry = richText
                    ? (fit ? $"<color={color}>{name} ✓</color>"
                           : $"<color=red>{name} ✗({string.Join(",", failed)})</color>")
                    : (fit ? $"{name} ✓" : $"{name} ✗({string.Join(",", failed)})");
                string.Join("\r\n",entry);
                parts.Add(entry);
            }

            return string.Join("\n", parts);
        }

        private static void AppendConditionLabel(float value, float? min, float? max, string label, List<string> failed)
        {
            if (min.HasValue && value < min.Value) { failed.Add(label + "↓"); return; }
            if (max.HasValue && value > max.Value) { failed.Add(label + "↑"); }
        }

        private static string GetColoredName(string name, string owner, Dictionary<string, RaceInfo> races)
        {
            if (!string.IsNullOrEmpty(owner) && races.TryGetValue(owner, out var info))
                return $"<color={info.Color}>{name}</color>";
            return name;
        }
    }
    #endregion
    #region GenerationHelpers

    public static class GenerationHelpers
    {
        private static readonly Dictionary<string, string[]> _resourceNameCache = new();

        private static string[] GetResourceNames(string path)
        {
            if (_resourceNameCache.TryGetValue(path, out var cached)) return cached;
            var textures = Resources.LoadAll<Texture2D>(path);
            var names = System.Array.ConvertAll(textures, t => t.name);
            _resourceNameCache[path] = names;
            return names;
        }


        public static string GetRandomPlanetGraphic(string size)
        {
            string p1 = $"{GalaxyConstants.PATH_PLANET_TEXTURES_BASE}/{size}";
            var names1 = GetResourceNames(p1);
            if (names1.Length > 0) return $"{p1}/{names1[UnityEngine.Random.Range(0, names1.Length)]}";
            var names2 = GetResourceNames(GalaxyConstants.PATH_COMMON_TEXTURES);
            if (names2.Length > 0) return $"{GalaxyConstants.PATH_COMMON_TEXTURES}/{names2[UnityEngine.Random.Range(0, names2.Length)]}";
            return GalaxyConstants.PATH_PLANET_FALLBACK;
        }

        public static string GetRandomSatelliteGraphic()
        {
            var names1 = GetResourceNames(GalaxyConstants.PATH_SAT_TEXTURES);
            if (names1.Length > 0) return $"{GalaxyConstants.PATH_SAT_TEXTURES}/{names1[UnityEngine.Random.Range(0, names1.Length)]}";
            var names2 = GetResourceNames(GalaxyConstants.PATH_COMMON_TEXTURES);
            if (names2.Length > 0) return $"{GalaxyConstants.PATH_COMMON_TEXTURES}/{names2[UnityEngine.Random.Range(0, names2.Length)]}";
            return GalaxyConstants.PATH_SATELLITE_FALLBACK;
        }

        public static string GetRandomOrbitalPath()
        {
            var names = GetResourceNames(GalaxyConstants.PATH_ORBITAL);
            return names.Length > 0 ? $"{GalaxyConstants.PATH_ORBITAL}/{names[UnityEngine.Random.Range(0, names.Length)]}" : null;
        }

        public static string BuildStarMapIcon(string color, int variant)
        {
            string basePath = $"{GalaxyConstants.PATH_STAR_MINIMAP_ICONS}/{color}";
            string variantPath = $"{basePath}/Var{variant}_minimap";
            return Resources.Load(variantPath) != null ? variantPath : $"{basePath}/minimap";
        }

        public static void ApplyWeightedStarColor(StarData star, GalaxyConfig config)
        {
            var colors = config.Stars.Colors;
            if (colors == null || colors.Count == 0)
            {
                star.Color = GalaxyConstants.FALLBACK_STAR_COLOR;
                star.GraphVar = 1;
                return;
            }

            int total = colors.Values.Sum(d => d?.VariantsCount ?? 1);
            int roll = UnityEngine.Random.Range(0, total), sum = 0;

            foreach (var (key, data) in colors)
            {
                sum += data?.VariantsCount ?? 1;
                if (roll >= sum) continue;
                star.Color = key;
                star.GraphVar = UnityEngine.Random.Range(1, (data?.VariantsCount ?? 1) + 1);
                return;
            }
        }

        public static int GetRandomColorVariant(string color, GalaxyConfig config)
        {
            var colors = config.Stars?.Colors;
            if (colors != null && colors.TryGetValue(color, out var d) && d != null)
                return UnityEngine.Random.Range(1, Mathf.Max(1, d.VariantsCount) + 1);
            return 1;
        }

        public static string GenerateAtmosphere(string size, Dictionary<string, PlanetSizeData> sizeRules)
        {
            if (string.IsNullOrEmpty(size)) return null;
            if (sizeRules.TryGetValue(size, out var rule))
                return UnityEngine.Random.Range(0, 100) < rule.AtmosphereChance
                    ? $"{GalaxyConstants.PATH_CLOUDS}/{UnityEngine.Random.Range(1, GalaxyConstants.CLOUD_VARIANTS_COUNT + 1)}" : null;
            Debug.LogWarning($"[GenerationHelpers] Planet size '{size}' not found in PlanetSizeRules — no atmosphere generated.");
            return null;
        }

        public static float GetPlanetRadiusInPixels(string size, Dictionary<string, PlanetSizeData> sizeRules)
        {
            return sizeRules.TryGetValue(size, out var rule) && rule.PixelRadius > 0
                ? rule.PixelRadius : GalaxyConstants.FALLBACK_PLANET_RADIUS;
        }

        public static int CalculateSatelliteCount(string size, Dictionary<string, PlanetSizeData> sizeRules)
        {
            if (string.IsNullOrEmpty(size)) return 0;
            if (sizeRules.TryGetValue(size, out var rule))
                return rule.MaxSatellites > 0 ? UnityEngine.Random.Range(0, rule.MaxSatellites + 1) : 0;
            Debug.LogWarning($"[GenerationHelpers] Planet size '{size}' not found in PlanetSizeRules — 0 satellites.");
            return 0;
        }

        public static int CalculateSystemSize(List<PlanetData> planets, int systemSizeMult)
        {
            if (planets == null || planets.Count == 0) return (int)(GalaxyConstants.SYSTEM_PADDING * systemSizeMult);
            float maxExtent = 0f;
            foreach (var p in planets)
            {
                float e = Mathf.Clamp(p.OrbitEccentricity, 0f, 0.99f);
                float apocenter = p.OrbitRadius * (1f + e);
                if (apocenter > maxExtent) maxExtent = apocenter;
            }
            return Mathf.RoundToInt(maxExtent + GalaxyConstants.SYSTEM_PADDING * systemSizeMult);
        }

        public static string GetMaskPath(string size) => GalaxyConstants.PATH_PLANET_MASK;

        public static string CleanResourcePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return path.Replace(".png", "").Replace(".jpg", "");
        }

        public static string PopRandomUnused(List<string> pool, HashSet<string> used)
        {
            int count = pool.Count;
            while (count > 0)
            {
                int idx = UnityEngine.Random.Range(0, count);
                string candidate = pool[idx];
                pool[idx] = pool[count - 1];
                pool.RemoveAt(count - 1);
                count--;
                if (used.Add(candidate)) return candidate;
            }
            return null;
        }

        public static string GetRaceColor(string key, Dictionary<string, RaceInfo> races) =>
            races.TryGetValue(key, out var race) ? race.Color : GalaxyConstants.DEFAULT_COLOR;
    }

    #endregion

    [System.Serializable]
    public class StarNameSegment
    {
        public string Text;
        public Color Color;

        public StarNameSegment(string text, Color color) { Text = text; Color = color; }
    }

    [System.Serializable]
    public class StarNameRenderData
    {
        public List<StarNameSegment> Segments = new();
        public bool HasIcon;
        public string IconPath;
        public string ToRichText()
        {
            if (Segments == null || Segments.Count == 0) return string.Empty;
            var sb = new System.Text.StringBuilder();
            foreach (var seg in Segments)
                sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(seg.Color)}>{seg.Text}</color>");
            return sb.ToString();
        }

        public string ToPlainText()
        {
            if (Segments == null || Segments.Count == 0) return string.Empty;
            var sb = new System.Text.StringBuilder();
            foreach (var seg in Segments) sb.Append(seg.Text);
            return sb.ToString();
        }
    }

    public static class OwnershipDisplayResolver
    {
        public static Color ParseRgbString(string rgb, Color fallback)
        {
            if (string.IsNullOrEmpty(rgb)) return fallback;
            var parts = rgb.Split(',');
            if (parts.Length < 3) return fallback;
            if (float.TryParse(parts[0].Trim(), out float r) &&
                float.TryParse(parts[1].Trim(), out float g) &&
                float.TryParse(parts[2].Trim(), out float b))
                return new Color(r / 255f, g / 255f, b / 255f);
            return fallback;
        }

        public static Color ResolveShipMinimapColor(ShipData ship, GalaxyGenerationContext ctx)
        {
            Color fallback = GetDefaultMinimapColor(ctx);
            if (ship == null || ctx?.Config?.Ships == null) return fallback;

            if (TryGetOwnerColor(ship.Owner, ctx.Config.Ships, fallback, out Color ownerColor))
                return ownerColor;
            if (TryGetRaceColor(ship.Race, ctx, fallback, out Color raceColor))
                return raceColor;
            return fallback;
        }

        public static Color ResolvePlanetMinimapColor(PlanetData planet, GalaxyGenerationContext ctx)
        {
            Color fallback = GetDefaultMinimapColor(ctx);
            if (planet == null || ctx?.Config == null) return fallback;

            if (!string.IsNullOrEmpty(planet.Owner)
                && ctx.Config.Ships?.Owners?.TryGetValue(planet.Owner, out var ownerCfg) == true
                && ownerCfg.ColorPriority
                && !string.IsNullOrEmpty(ownerCfg.Color))
                return ParseRgbString(ownerCfg.Color, fallback);

            if (TryGetRaceColor(planet.Race, ctx, fallback, out Color raceColor))
                return raceColor;
            return fallback;
        }

        public static StarNameRenderData ResolveStarName(StarData star, GalaxyGenerationContext ctx)
        {
            var result = new StarNameRenderData();
            if (star == null || ctx?.Config?.Ships == null) return result;

            string name = star.Name ?? string.Empty;
            if (name.Length == 0) return result;

            Color defaultColor = GetDefaultStarNameColor(ctx);

            OwnerConfig ownerCfg = null;
            ctx.Config.Ships.Owners?.TryGetValue(star.Owner ?? string.Empty, out ownerCfg);

            if (ownerCfg != null && ownerCfg.ColorPriority && !string.IsNullOrEmpty(ownerCfg.Color))
            {
                Color ownerColor = ParseRgbString(ownerCfg.Color, defaultColor);
                result.Segments.Add(new StarNameSegment(name, ownerColor));
                return result;
            }

            BuildRaceGradientSegments(result, name, star, ctx, defaultColor);

            if (result.Segments.Count == 0)
                result.Segments.Add(new StarNameSegment(name, defaultColor));

            return result;
        }

        private static Color GetDefaultMinimapColor(GalaxyGenerationContext ctx)
        {
            var settings = GetSettings();
            return ParseRgbString(settings?.DefaultMinimapColor, Color.white);
        }

        private static Color GetDefaultStarNameColor(GalaxyGenerationContext ctx)
        {
            var settings = GetSettings();
            return ParseRgbString(settings?.DefaultStarNameColor, new Color(0.78f, 0.78f, 0.78f));
        }

        private static GameSettingsConfig GetSettings() => GameWorld.Settings;

        private static bool TryGetOwnerColor(string ownerId, ShipConfigSection ships,
            Color fallback, out Color color)
        {
            color = fallback;
            if (string.IsNullOrEmpty(ownerId) || ships?.Owners == null) return false;
            if (!ships.Owners.TryGetValue(ownerId, out var cfg)) return false;
            if (string.IsNullOrEmpty(cfg.Color)) return false;
            color = ParseRgbString(cfg.Color, fallback);
            return true;
        }

        private static bool TryGetRaceColor(string raceId, GalaxyGenerationContext ctx,
            Color fallback, out Color color)
        {
            color = fallback;
            if (string.IsNullOrEmpty(raceId)
                || raceId == GalaxyConstants.RACE_NONE_KEY
                || ctx.Config?.Races == null) return false;
            if (!ctx.Config.Races.TryGetValue(raceId, out var cfg)) return false;
            if (string.IsNullOrEmpty(cfg.Color)) return false;
            color = ParseRgbString(cfg.Color, fallback);
            return true;
        }

        private static string GetDominantRaceIconPath(StarData star, GalaxyGenerationContext ctx)
        {
            string dominantRace = null;
            int maxCount = 0;
            var counts = new Dictionary<string, int>();
            foreach (var planet in star.Planets)
            {
                if (string.IsNullOrEmpty(planet.Race) || planet.Race == GalaxyConstants.RACE_NONE_KEY) continue;
                counts.TryGetValue(planet.Race, out int cur);
                int next = cur + 1;
                counts[planet.Race] = next;
                if (next > maxCount) { maxCount = next; dominantRace = planet.Race; }
            }

            if (string.IsNullOrEmpty(dominantRace) || ctx.Config?.Races == null) return null;
            return ctx.Config.Races.TryGetValue(dominantRace, out var raceCfg) ? raceCfg.EmblemPath : null;
        }

        private static void BuildRaceGradientSegments(StarNameRenderData result, string name,
            StarData star, GalaxyGenerationContext ctx, Color defaultColor)
        {
            var orderedRaces = new List<string>();
            var seen = new HashSet<string>();
            foreach (var planet in star.Planets)
            {
                if (string.IsNullOrEmpty(planet.Race) || planet.Race == GalaxyConstants.RACE_NONE_KEY) continue;
                if (seen.Add(planet.Race)) orderedRaces.Add(planet.Race);
            }

            if (orderedRaces.Count == 0)
            {
                result.Segments.Add(new StarNameSegment(name, defaultColor));
                return;
            }

            int n = orderedRaces.Count;
            int nameLen = name.Length;
            int charIndex = 0;

            for (int i = 0; i < n; i++)
            {
                int charCount = i == n - 1
                    ? nameLen - charIndex
                    : Mathf.RoundToInt((float)(i + 1) / n * nameLen) - charIndex;

                charCount = Mathf.Clamp(charCount, 0, nameLen - charIndex);
                if (charCount <= 0) continue;

                Color raceColor = defaultColor;
                TryGetRaceColor(orderedRaces[i], ctx, defaultColor, out raceColor);

                result.Segments.Add(new StarNameSegment(name.Substring(charIndex, charCount), raceColor));
                charIndex += charCount;
            }
        }
    }
}
