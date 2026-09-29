using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Config;

namespace SRG.Equipment
{
    /// <summary>
    /// Шаблон категории оборудования (Hull/Engine/FuelTank/...).
    /// GTL — словарь T1..T10 с финальными значениями статов для тира (Adj + статы + Price).
    /// Вес: Weight = round(BaseWeight × RandRange(0.6, 2.0)).
    /// Durability: BaseDurability (умножается на расовый множитель в ItemFactory);
    /// у корпусов Durability = Weight (вместимость), расовый множитель не применяется.
    /// Корпуса: дополнительно HullType (формы), HullSlotsHullType (абсолютные слоты по форме),
    /// HullSlotsRace (дельта по расе), HullSizeSmall/Big/Huge (пороги размера для формулы цены).
    /// </summary>
    public class EquipmentTemplate
    {
        // ── общие параметры категории (используются ещё как CategoryCommonConfig) ────────
        [JsonProperty("DisplayName")] public string DisplayName { get; set; }
        [JsonProperty("MaxSlots")] public int MaxSlots { get; set; } = 1;
        [JsonProperty("WearOnMove")] public int WearOnMove { get; set; } = 0;
        [JsonProperty("WearOnHit")] public int WearOnHit { get; set; } = 0;
        [JsonProperty("WearOnShot")] public int WearOnShot { get; set; } = 0;
        [JsonProperty("WearOnTurn")] public int WearOnTurn { get; set; } = 0;
        [JsonProperty("CanBeDamagedByWeapon")] public bool CanBeDamagedByWeapon { get; set; } = false;

        // ── id / шаблоны имён и графики ─────────────────────────────────────────────────
        [JsonProperty("IdPrefix")] public string IdPrefix { get; set; }
        [JsonProperty("IdSegments", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> IdSegments { get; set; } = new() { "Line" };
        [JsonProperty("NameTemplate")] public string NameTemplate { get; set; }
        [JsonProperty("BaseDesc")] public string BaseDesc { get; set; }
        [JsonProperty("GraphicTemplate")] public string GraphicTemplate { get; set; }
        /// <summary>Шаблон пути к графике корабля в космосе (только для корпусов).</summary>
        [JsonProperty("GraphicCosmicTemplate")] public string GraphicCosmicTemplate { get; set; }
        [JsonProperty("Defaults")] public Dictionary<string, JToken> Defaults { get; set; } = new();

        // ── базовые значения категории ──────────────────────────────────────────────────
        [JsonProperty("BaseWeight")] public int BaseWeight { get; set; }
        [JsonProperty("BaseDurability")] public int BaseDurability { get; set; }
        /// <summary>Базовая цена категории (Hull=2000, остальные=500). Подставляется в формулу стоимости.</summary>
        [JsonProperty("BasePrice")] public int BasePrice { get; set; }

        // ── per-tier данные ─────────────────────────────────────────────────────────────
        [JsonProperty("GTL")] public Dictionary<string, GtlDef> GTL { get; set; } = new();

        // ── только для корпусов ─────────────────────────────────────────────────────────
        [JsonProperty("HullType")] public Dictionary<string, HullTypeDef> HullTypes { get; set; }
        [JsonProperty("HullSlotsHullType")] public Dictionary<string, Dictionary<string, int>> HullSlotsHullType { get; set; }
        [JsonProperty("HullSlotsRace")] public Dictionary<string, Dictionary<string, int>> HullSlotsRace { get; set; }
        /// <summary>Какие расы корпуса принадлежат каждой стороне. Только эти Race+Side комбинации
        /// генерируются в EnumerateIds (предотвращает Coalition+RaceDominators1 и т.п.).</summary>
        [JsonProperty("SideRaces")] public Dictionary<string, List<string>> SideRaces { get; set; }
        /// <summary>Какие HullType-ключи доступны каждой стороне. Аналогично SideRaces.</summary>
        [JsonProperty("SideHullTypes")] public Dictionary<string, List<string>> SideHullTypes { get; set; }
        [JsonProperty("HullSizeSmall")] public int HullSizeSmall { get; set; }
        [JsonProperty("HullSizeBig")] public int HullSizeBig { get; set; }
        [JsonProperty("HullSizeHuge")] public int HullSizeHuge { get; set; }

        // ── runtime (не сериализуется) ──────────────────────────────────────────────────
        [JsonIgnore] public string Category { get; set; }
        [JsonIgnore] public ItemsConfig OwnerConfig { get; set; }

        private static readonly Regex VarRegex = new(@"<(\w+)>", RegexOptions.Compiled);
        private static readonly float[] SizeMultMin = { 0.6f, 2.0f };

        public ItemConfig Resolve(string itemId) => Resolve(itemId, null);

        /// <summary>sideOverride: имя кластера (Coalition, Dominators, ...). Если не задан — извлекается из id.</summary>
        public ItemConfig Resolve(string itemId, string sideOverride)
        {
            var parts = ParseId(itemId);
            if (parts == null) return null;

            parts.TryGetValue("Line", out var lineKey);
            if (lineKey == null || GTL == null || !GTL.TryGetValue(lineKey, out var gtl)) return null;

            int tier = TechLevelFromKey(lineKey);

            parts.TryGetValue("Race", out var raceKey);
            parts.TryGetValue("HullType", out var hullTypeKey);
            parts.TryGetValue("Side", out var sideKey);
            if (sideOverride != null) sideKey = sideOverride;

            HullTypeDef hullType = null;
            if (HullTypes != null && hullTypeKey != null)
                HullTypes.TryGetValue(hullTypeKey, out hullType);

            ClusterConfig cluster = OwnerConfig?.GetCluster(sideKey);

            // Линейка-имя берётся из Clusters[side].Lines[lineKey], иначе lineKey
            string lineName = lineKey;
            if (cluster?.Lines != null && cluster.Lines.TryGetValue(lineKey, out var fromCluster) && fromCluster != null)
                lineName = fromCluster;

            // Размер: round(BaseWeight × RandRange(0.6, 2.0)).
            // Корпус с HullType.CapacityMult считается отдельно:
            // база = BaseWeight × RandRange(мин, макс) типа корпуса, к базе прибавляется 1–20% от неё,
            // затем ГТУ-бонус: линейно от +0% на T1 до +50% итога на T10.
            int size;
            if (IsHullCategory && hullType != null && hullType.IsStation)
            {
                // Станционный корпус: своя формула размера (=вместимость=прочность) — 2000 + tier*250 ± 100.
                size = Mathf.RoundToInt(2000f + tier * 250f + Random.Range(-100f, 101f));
            }
            else if (IsHullCategory && hullType?.CapacityMult != null && hullType.CapacityMult.Length >= 2)
            {
                float baseCap = BaseWeight * Random.Range(hullType.CapacityMult[0], hullType.CapacityMult[1]);
                float cap = baseCap * (1f + Random.Range(0.01f, 0.20f));
                cap *= 1f + 0.5f * (tier - 1) / 9f;
                size = Mathf.RoundToInt(cap);
            }
            else
            {
                size = BaseWeight > 0
                    ? Mathf.RoundToInt(BaseWeight * Random.Range(SizeMultMin[0], SizeMultMin[1]))
                    : 0;
            }

            // Корпус: прочность = вместимость (Weight), см. BaseDesc "Стойкость корпуса <Weight>".
            int durability = IsHullCategory ? size : BaseDurability;

            var item = new ItemConfig
            {
                Kind = Category,
                TechLevel = tier,
                StartGTL = tier,
                EndGTL = GetDefaultInt("EndGTL", 0),
                NoWear = GetDefaultBool("NoWear", false),
                AllowEmbeds = GetDefaultBool("AllowEmbeds", GetDefaultBool("AllowModules", true)),
                MaxEmbeds = GetDefaultInt("MaxEmbeds", 0),
                Activatable = GetDefaultBool("Activatable", false),
                IsEquipment = GetDefaultBool("IsEquipment", true),
                IsImprovable = GetDefaultBool("IsImprovable", true),
                RequiresNodesToImprove = GetDefaultBool("RequiresNodesToImprove", false),
                GraphicByManufacturer = GetDefaultBool("GraphicByManufacturer", false),
                FixedRotation = hullType?.FixedRotation ?? false,
                Manufacturer = new ManufacturerConfig { Race = raceKey, Side = sideKey },
                WeaponPorts = hullType?.WeaponPorts,
                Tails = hullType?.Tails,
                Params = new Dictionary<string, JToken>(),
                Weight = size,
                Durability = durability,
                MaxDurability = durability
            };

            // Перенос GTL-полей (кроме Adj) в Params
            if (gtl.Extras != null)
            {
                foreach (var kv in gtl.Extras)
                {
                    if (kv.Key == "Adj") continue;
                    item.Params[kv.Key] = kv.Value;
                }
            }

            // Defaults → Params: любой ключ из Defaults, не перебитый GTL, идёт в Params предмета.
            // Исключения — флаги конфигурации самого шаблона (AllowModules / NoWear / *GTL), они
            // читаются через GetDefaultBool/Int и не должны попадать в Params экземпляра.
            if (Defaults != null)
            {
                foreach (var kv in Defaults)
                {
                    if (kv.Key == "AllowModules" || kv.Key == "AllowEmbeds" || kv.Key == "MaxEmbeds"
                        || kv.Key == "NoWear"
                        || kv.Key == "Activatable"
                        || kv.Key == "IsEquipment"
                        || kv.Key == "IsImprovable" || kv.Key == "RequiresNodesToImprove"
                        || kv.Key == "GraphicByManufacturer"
                        || kv.Key == "StartGTL" || kv.Key == "EndGTL") continue;
                    if (!item.Params.ContainsKey(kv.Key))
                        item.Params[kv.Key] = kv.Value;
                }
            }

            // FuelTank: Capacity = BaseCapacity + Weight/2
            if (item.Params.TryGetValue("BaseCapacity", out var baseCapTok))
            {
                float baseCap = 0f;
                try { baseCap = baseCapTok.Value<float>(); } catch { }
                item.Params["Capacity"] = JToken.FromObject(Mathf.RoundToInt(baseCap + size / 2f));
                item.Params.Remove("BaseCapacity");
            }

            // Если у кластера флаг GraphicIgnoresTechLevel — графика общая на все техуровни:
            // <TechLevel> подставляем именем стороны (Engine_<TechLevel>_a → Engine_Dominators_a).
            string graphicTechLevelToken = (cluster != null && cluster.GraphicIgnoresTechLevel)
                ? (sideKey ?? tier.ToString())
                : tier.ToString();

            // Подстановки в имя/описание/графику
            var vars = new Dictionary<string, string>
            {
                ["Side"] = sideKey ?? "",
                ["Race"] = raceKey ?? "",
                ["HullType"] = hullTypeKey ?? "",
                ["LineKey"] = lineKey,
                ["T"] = tier.ToString(),
                ["TechLevel"] = graphicTechLevelToken,
                ["TechLevelNum"] = tier.ToString(),
                ["HullTypeAdj"] = hullType?.Adj ?? "",
                ["HullTypeAdjCap"] = Capitalize(hullType?.Adj) ?? "",
                ["HullTypeCode"] = hullType?.Code ?? hullTypeKey ?? "",
                ["LineAdj"] = gtl.Adj ?? "",
                ["LineName"] = lineName,
                ["Weight"] = size.ToString()
            };
            foreach (var kv in item.Params)
            {
                if (kv.Key == "Effects" || kv.Key == "GTLDamageMultipliers") continue;
                try { vars[kv.Key] = kv.Value.ToString(); } catch { }
            }

            item.Name = Substitute(NameTemplate, vars);
            item.Description = Substitute(BaseDesc, vars);
            // Станционный (или иной специфичный) HullType может задать собственный шаблон иконки —
            // категорийный содержит <Race>, что не работает для расо-независимых корпусов.
            item.GraphicPath = Substitute(
                !string.IsNullOrEmpty(hullType?.GraphicTemplate) ? hullType.GraphicTemplate : GraphicTemplate,
                vars);
            // Станционный корпус может переопределить путь к графике тела (Graphics/Items/Equipment/Hull/Stations/<Code>_c).
            item.BodyGraphicPath = Substitute(
                !string.IsNullOrEmpty(hullType?.BodyGraphicTemplate) ? hullType.BodyGraphicTemplate : GraphicCosmicTemplate,
                vars);

            // HullType код в Params (для последующего поиска формы корпуса)
            if (hullTypeKey != null)
                item.Params["HullType"] = hullType?.Code ?? hullTypeKey;
            item.Params["TechLevel"] = tier;

            // Цена: для корпуса — формула v11+sizes; для прочих категорий — формула TL² (race mult в ItemFactory).
            if (IsHullCategory && hullType != null && OwnerConfig != null)
            {
                var slots = GetEffectiveSlotsForCost(hullTypeKey, raceKey);
                int weaponMax = OwnerConfig.GetCategoryCommon(EquipmentCategory.Weapons)?.MaxSlots ?? 5;
                int artMax = OwnerConfig.GetCategoryCommon(EquipmentCategory.Artefacts)?.MaxSlots ?? 4;
                float v11 = ComputeV11(hullType.CostCoef, slots, weaponMax, artMax);
                item.Price = ComputeHullCost(tier, v11, size);
            }
            else
            {
                item.Price = ComputeNonHullCostBase(tier, size);
            }

            return item;
        }

        /// <summary>
        /// Цена без расового множителя: PortionInDiapason(2, 1, 2, 0.5, AverageSize/Size) × TL² × BasePrice.
        /// Расовый множитель применяется в ItemFactory (для категорий кроме Hull/Weapons/Artefacts).
        /// </summary>
        public int ComputeNonHullCostBase(int tier, int size)
        {
            if (BasePrice <= 0 || BaseWeight <= 0 || size <= 0) return 0;
            float averageSize = BaseWeight;
            float sizeRatio = averageSize / size;
            float sizeCoef = PortionInDiapason(2f, 1f, 2f, 0.5f, sizeRatio);
            float tierSquared = tier * (float)tier;
            float raw = sizeCoef * tierSquared * BasePrice;
            return Mathf.RoundToInt(raw / 10f) * 10;
        }

        [JsonIgnore] private bool IsHullCategory => string.Equals(Category, EquipmentCategory.Hull, System.StringComparison.Ordinal);

        public CategoryCommonConfig ToCategoryCommon() => new()
        {
            DisplayName = DisplayName,
            MaxSlots = MaxSlots,
            WearOnMove = WearOnMove,
            WearOnHit = WearOnHit,
            WearOnShot = WearOnShot,
            WearOnTurn = WearOnTurn,
            CanBeDamagedByWeapon = CanBeDamagedByWeapon
        };

        /// <summary>Эффективные слоты корпуса для расчёта цены: HullSlotsHullType + HullSlotsRace (без серии).</summary>
        public Dictionary<string, int> GetEffectiveSlotsForCost(string hullTypeKey, string raceKey)
        {
            var result = new Dictionary<string, int>();
            if (HullSlotsHullType != null && hullTypeKey != null &&
                HullSlotsHullType.TryGetValue(hullTypeKey, out var byType) && byType != null)
            {
                foreach (var kv in byType) result[kv.Key] = kv.Value;
            }
            if (HullSlotsRace != null && raceKey != null &&
                HullSlotsRace.TryGetValue(raceKey, out var byRace) && byRace != null)
            {
                foreach (var kv in byRace)
                {
                    result.TryGetValue(kv.Key, out var prev);
                    result[kv.Key] = prev + kv.Value;
                }
            }
            return result;
        }

        public IEnumerable<string> EnumerateIds()
        {
            if (GTL == null || GTL.Count == 0) yield break;
            if (IdSegments == null || IdSegments.Count == 0) yield break;

            bool hasSide = IdSegments.Contains("Side");
            bool hasRace = IdSegments.Contains("Race");
            bool hasHullType = IdSegments.Contains("HullType") && HullTypes != null;

            // Сторона берётся из OwnerConfig.Clusters; если кластеров нет — пропускаем.
            IEnumerable<string> sides = hasSide
                ? (OwnerConfig?.EnumerateSides() ?? System.Linq.Enumerable.Empty<string>())
                : new[] { (string)null };

            IEnumerable<string> races = hasRace
                ? (HullSlotsRace != null ? HullSlotsRace.Keys : System.Linq.Enumerable.Empty<string>())
                : new[] { (string)null };

            IEnumerable<string> hullTypes = hasHullType
                ? HullTypes.Keys
                : new[] { (string)null };

            foreach (var side in sides)
            {
                // Per-side фильтр для рас и hull-типов: блокирует Coalition+RaceDominators1 и т.п.
                HashSet<string> raceFilter = null;
                HashSet<string> hullTypeFilter = null;
                if (side != null)
                {
                    if (SideRaces != null && SideRaces.TryGetValue(side, out var sr) && sr != null)
                        raceFilter = new HashSet<string>(sr);
                    if (SideHullTypes != null && SideHullTypes.TryGetValue(side, out var sht) && sht != null)
                        hullTypeFilter = new HashSet<string>(sht);
                }

                foreach (var race in races)
                {
                    if (raceFilter != null && race != null && !raceFilter.Contains(race)) continue;
                    foreach (var ht in hullTypes)
                    {
                        if (hullTypeFilter != null && ht != null && !hullTypeFilter.Contains(ht)) continue;
                        foreach (var line in GTL.Keys)
                        {
                            var sb = new System.Text.StringBuilder(IdPrefix);
                            bool first = true;
                            foreach (var seg in IdSegments)
                            {
                                string val = seg switch
                                {
                                    "Side" => side,
                                    "Race" => race,
                                    "HullType" => ht,
                                    "Line" => line,
                                    _ => null
                                };
                                if (val == null) continue;
                                if (!first) sb.Append('_');
                                sb.Append(val);
                                first = false;
                            }
                            yield return sb.ToString();
                        }
                    }
                }
            }
        }

        private Dictionary<string, string> ParseId(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(IdPrefix)) return null;
            if (!itemId.StartsWith(IdPrefix, System.StringComparison.Ordinal)) return null;
            string rest = itemId.Substring(IdPrefix.Length);
            var parts = rest.Split('_');
            if (IdSegments == null || parts.Length != IdSegments.Count) return null;

            var result = new Dictionary<string, string>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
                result[IdSegments[i]] = parts[i];
            return result;
        }

        private static string Substitute(string template, Dictionary<string, string> vars)
        {
            if (string.IsNullOrEmpty(template)) return null;
            return VarRegex.Replace(template, m =>
                vars.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
        }

        private static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpper(s[0]) + s.Substring(1);
        }

        private bool GetDefaultBool(string key, bool def)
        {
            if (Defaults != null && Defaults.TryGetValue(key, out var t))
                try { return t.ToObject<bool>(); } catch { }
            return def;
        }

        private int GetDefaultInt(string key, int def)
        {
            if (Defaults != null && Defaults.TryGetValue(key, out var t))
                try { return t.ToObject<int>(); } catch { }
            return def;
        }

        private static int TechLevelFromKey(string lineKey)
        {
            if (string.IsNullOrEmpty(lineKey)) return 1;
            int start = lineKey.StartsWith("T", System.StringComparison.Ordinal) ? 1 : 0;
            return int.TryParse(lineKey.Substring(start), out var n) ? n : 1;
        }

        // ── формула цены корпуса ─────────────────────────────────────────────────────────
        /// <summary>
        /// Линейная интерполяция с зажимом. Сигнатура: (maxOut, minOut, maxIn, minIn, value).
        /// value ≤ minIn → minOut; value ≥ maxIn → maxOut; иначе линейно.
        /// </summary>
        public static float PortionInDiapason(float maxOut, float minOut, float maxIn, float minIn, float value)
        {
            if (Mathf.Approximately(maxIn, minIn)) return maxOut;
            if (value <= minIn) return minOut;
            if (value >= maxIn) return maxOut;
            return minOut + (value - minIn) / (maxIn - minIn) * (maxOut - minOut);
        }

        private static float ComputeV11(float costCoef, Dictionary<string, int> slots, int weaponMax, int artMax)
        {
            float v = costCoef > 0 ? costCoef : 1f;
            if (SlotCount(slots, EquipmentCategory.Radar) == 0) v *= 0.7f;
            if (SlotCount(slots, EquipmentCategory.Scanner) == 0) v *= 0.9f;
            if (SlotCount(slots, EquipmentCategory.Droid) == 0) v *= 0.7f;
            if (SlotCount(slots, EquipmentCategory.CargoGrabber) == 0) v *= 0.6f;
            if (SlotCount(slots, EquipmentCategory.Shield) == 0) v *= 0.7f;
            if (SlotCount(slots, EquipmentCategory.Forsage) == 0) v *= 0.85f;

            int weaponSlots = SlotCount(slots, EquipmentCategory.Weapons);
            v *= PortionInDiapason(1.0f, 0.5f, weaponMax, 2.0f, weaponSlots);

            int artSlots = SlotCount(slots, EquipmentCategory.Artefacts);
            v *= PortionInDiapason(1.0f, 0.7f, artMax, 0f, artSlots);

            return Mathf.Max(0.2f, v);
        }

        private int ComputeHullCost(int tier, float v11, int size)
        {
            if (HullSizeSmall <= 0 || HullSizeBig <= 0 || HullSizeHuge <= 0) return 0;
            int basePrice = BasePrice > 0 ? BasePrice : 2000;
            float tlPart = PortionInDiapason(8f, 1f, 8f, 1f, tier);
            float sizeMul1 = Mathf.Max(size, HullSizeSmall) * 0.01f - 0.006f;
            float sizeMul2 = Mathf.Max(size, HullSizeBig) * 0.002f - 0.001f;
            float sizeMul3 = Mathf.Max(size, HullSizeHuge) * 0.0005f;
            float raw = tlPart * basePrice * v11 * sizeMul1 * sizeMul2 * sizeMul3;
            return Mathf.RoundToInt(raw / 10f) * 10;
        }

        private static int SlotCount(Dictionary<string, int> slots, string category) =>
            slots != null && slots.TryGetValue(category, out var n) && n > 0 ? n : 0;
    }

    /// <summary>
    /// Запись тира в GTL: Adj + произвольные статы (Armor/Speed/JumpRange/BaseCapacity/Price/...).
    /// Extras хранит все поля, кроме Adj — они перенесутся в ItemConfig.Params.
    /// </summary>
    public class GtlDef
    {
        [JsonProperty("Adj")] public string Adj { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JToken> Extras { get; set; } = new();
    }

    /// <summary>
    /// Запись типа корпуса в шаблоне (Hull-template). Ключ словаря HullTypes (например, "P", "PC", "W")
    /// — это сегмент id корпуса. CostCoef — базовый коэффициент v11 в формуле стоимости корпуса.
    /// </summary>
    public class HullTypeDef
    {
        [JsonProperty("Adj")] public string Adj { get; set; }
        [JsonProperty("Code")] public string Code { get; set; }
        [JsonProperty("CostCoef")] public float CostCoef { get; set; } = 1f;
        /// <summary>Диапазон [мин, макс] множителя вместимости корпуса от BaseWeight категории.
        /// Итог: BaseWeight × RandRange(мин, макс) + 1–20% сверху. Если не задан — общая формула размера.</summary>
        [JsonProperty("CapacityMult")] public float[] CapacityMult { get; set; }
        [JsonProperty("WeaponPorts")] public List<float[]> WeaponPorts { get; set; }
        [JsonProperty("Tails")] public List<float[]> Tails { get; set; }
        /// <summary>Путь к спрайту иконки корабля на миникарте/галакарте. Если пуст — рисуется кодом (кружок с обводкой).</summary>
        [JsonProperty("MinimapIconPath")] public string MinimapIconPath { get; set; }

        // ── Станции ──────────────────────────────────────────────────────────────
        /// <summary>true — корпус станции: графика не поворачивается по курсу (флаг переносится
        /// в <see cref="ItemInstance.FixedRotation"/>).</summary>
        [JsonProperty("FixedRotation")] public bool FixedRotation { get; set; }
        /// <summary>true — корпус станции: размер считается по станционной формуле
        /// (2000 + tier*250 ± 100), а не по CapacityMult.</summary>
        [JsonProperty("IsStation")] public bool IsStation { get; set; }
        /// <summary>Переопределение шаблона пути к графике корпуса в космосе. Если задан — имеет
        /// приоритет над общешаблонным GraphicCosmicTemplate (у станций указывает на Graphics/Items/Equipment/Hull/Stations/&lt;Code&gt;_c).</summary>
        [JsonProperty("BodyGraphicTemplate")] public string BodyGraphicTemplate { get; set; }
        /// <summary>Переопределение шаблона пути к «плоской» инвентарной/иконочной графике корпуса.
        /// Если задан — имеет приоритет над категорийным <c>GraphicTemplate</c>. У станций указывает
        /// на Graphics/Items/Equipment/Hull/Stations/&lt;Code&gt;_a — общая иконка не подходит,
        /// так как в имя категорийного шаблона входит placeholder <c>&lt;Race&gt;</c>, а станции
        /// расово-независимы.</summary>
        [JsonProperty("GraphicTemplate")] public string GraphicTemplate { get; set; }

        // ── Абордаж/буксир ──────────────────────────────────────────────────────
        /// <summary>Точка крепления абордируемой цели в локальных координатах корпуса (обычно сбоку).</summary>
        [JsonProperty("BoardAnchor")] public float[] BoardAnchor { get; set; }
        /// <summary>Точки крепления буксируемых объектов: Front/Back/Left/Right → [x,y] в локальных координатах.
        /// Если корпус не задаёт точку — буксир по этому направлению невозможен.</summary>
        [JsonProperty("TowAnchors")] public Dictionary<string, float[]> TowAnchors { get; set; }
        /// <summary>Порядок использования точек буксира (например, ["Back","Left","Right","Front"]). Если не задан — порядок ключей TowAnchors.</summary>
        [JsonProperty("TowOrder")] public List<string> TowOrder { get; set; }
    }
}
