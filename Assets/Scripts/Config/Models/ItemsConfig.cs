using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SRG.Combat;
using SRG.Dialog;
using SRG.Dialog.PlanetGreetings;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.NpcAI;
using SRG.NpcAI.Spawning;
using SRG.Science;
using SRG.Ships;
using SRG.Ships.Services;
using SRG.Simulation;

namespace SRG.Config
{
    /// <summary>
    /// Kind — единый идентификатор типа предмета. Значения совпадают со слоточными категориями
    /// оборудования (Weapons/Artefacts/Hull/Engine/...) плюс не-слотовые:
    ///   <list type="bullet">
    ///     <item><see cref="MicroModules"/> — встраиваемые (embed).</item>
    ///     <item><see cref="Goods"/> — торговые товары (стекаются, участвуют в TraderAI).</item>
    ///     <item><see cref="Useless"/> — прочие предметы без спец-семантики (Ноды, квестовые находки).
    ///           Стакабельность задаётся флагом <see cref="ItemConfig.Stackable"/>.</item>
    ///   </list>
    /// </summary>
    public static class ItemKind
    {
        public const string Weapons      = "Weapons";
        public const string Artefacts    = "Artefacts";
        public const string Hull         = "Hull";
        public const string Engine       = "Engine";
        public const string FuelTank     = "FuelTank";
        public const string Forsage      = "Forsage";
        public const string Shield       = "Shield";
        public const string Radar        = "Radar";
        public const string Scanner      = "Scanner";
        public const string Droid        = "Droid";
        public const string CargoGrabber = "CargoGrabber";
        public const string BoardingHook = "BoardingHook";
        public const string TowingRig    = "TowingRig";
        public const string MicroModules = "MicroModules";
        public const string Goods        = "Goods";
        /// <summary>Прочие предметы: Ноды, квестовые находки, статуэтки. Не оборудование,
        /// не встраиваются, не участвуют в торговом рейсе. Стакабельность — через
        /// <see cref="ItemConfig.Stackable"/>.</summary>
        public const string Useless      = "Useless";

        public static bool IsEquipment(string kind) =>
            !string.IsNullOrEmpty(kind) && kind != Goods && kind != Useless && kind != MicroModules;
    }

    /// <summary>
    /// Единый конфиг предметов и оборудования. Плоский словарь <see cref="Items"/> (id → <see cref="ItemConfig"/>
    /// c полем <see cref="ItemConfig.Kind"/>), плюс метаданные категорий в <see cref="Categories"/>,
    /// шаблоны процгена <see cref="EquipmentTemplates"/>, серии корпуса <see cref="HullSeries"/>,
    /// тиры встраиваемости <see cref="EmbedTiers"/> и балансовые правила улучшения <see cref="Improvement"/>.
    /// </summary>
    public class ItemsConfig
    {
        /// <summary>Серии корпуса (модификатор) — Universal/Light/Combat/... Ключи с префиксом "HullSeries_".</summary>
        [JsonProperty("HullSeries")] public Dictionary<string, HullSeriesConfig> HullSeries { get; set; } = new();

        /// <summary>Общие параметры категорий (DisplayName, MaxSlots, WearOnX, ContainerGraphic, ShotEffects).
        /// Живёт только для рукописных категорий (Weapons/Artefacts). Для шаблонных категорий
        /// параметры собираются через <see cref="EquipmentTemplate.ToCategoryCommon"/>.</summary>
        [JsonProperty("Categories")] public Dictionary<string, CategoryCommonConfig> Categories { get; set; } = new();

        /// <summary>Все рукописные предметы, сгруппированные по <see cref="ItemKind"/>:
        /// <c>Items[kind][id] = cfg</c>. Kind задаётся ключом словаря первого уровня, поэтому
        /// в самом <see cref="ItemConfig"/> его дублировать не нужно — при загрузке
        /// <see cref="EnsureItemsCache"/> проставляет <see cref="ItemConfig.Kind"/> автоматически.
        /// Шаблонные предметы (Hull/Engine/…) сюда не входят: они генерируются через <see cref="EquipmentTemplates"/>.</summary>
        [JsonProperty("Items")] public Dictionary<string, Dictionary<string, ItemConfig>> Items { get; set; } = new();

        /// <summary>Генеративные шаблоны: Clusters + Hull/Engine/FuelTank/Forsage/Shield/Radar/Scanner/Droid/CargoGrabber.</summary>
        [JsonProperty("EquipmentTemplates")] public JObject EquipmentTemplates { get; set; }

        /// <summary>Балансовая конфигурация улучшения оборудования на научной базе (SB).
        /// См. docs/design/SB_Equipment_Improvement.txt / docs/design/SB_Improvement_Formulas.txt.</summary>
        [JsonProperty("Improvement")] public ImprovementConfig Improvement { get; set; } = new();

        /// <summary>Уровни редкости встраиваемых (T1/T2/T3/…).</summary>
        [JsonProperty("EmbedTiers")] public List<EmbedTierConfig> EmbedTiers { get; set; } = new();

        private Dictionary<string, ItemConfig> _itemsById;
        private Dictionary<string, EquipmentTemplate> _templatesCache;
        private Dictionary<string, ClusterConfig> _clustersCache;

        private void EnsureItemsCache()
        {
            if (_itemsById != null) return;
            _itemsById = new();
            if (Items == null) return;
            foreach (var byKind in Items)
            {
                if (byKind.Value == null) continue;
                foreach (var kv in byKind.Value)
                {
                    // Kind проставляем из ключа группы — в JSON он не хранится.
                    if (kv.Value != null) kv.Value.Kind = byKind.Key;
                    if (_itemsById.ContainsKey(kv.Key))
                    {
                        UnityEngine.Debug.LogError($"[ItemsConfig] Duplicate item id '{kv.Key}' across kinds.");
                        continue;
                    }
                    _itemsById[kv.Key] = kv.Value;
                }
            }
        }

        private void EnsureTemplatesCache()
        {
            if (_templatesCache != null) return;
            _templatesCache = new();
            _clustersCache = new();
            if (EquipmentTemplates == null) return;
            var serializer = JsonSerializer.CreateDefault();
            foreach (var entry in EquipmentTemplates.Properties())
            {
                if (entry.Name == "Clusters")
                {
                    try
                    {
                        var clusters = entry.Value.ToObject<Dictionary<string, ClusterConfig>>(serializer);
                        if (clusters != null) _clustersCache = clusters;
                    }
                    catch (System.Exception e) { UnityEngine.Debug.LogError($"[ItemsConfig] Failed to parse EquipmentTemplates.Clusters: {e.Message}"); }
                    continue;
                }
                try
                {
                    var tpl = entry.Value.ToObject<EquipmentTemplate>(serializer);
                    if (tpl != null)
                    {
                        tpl.Category = entry.Name;
                        tpl.OwnerConfig = this;
                        _templatesCache[entry.Name] = tpl;
                    }
                }
                catch (System.Exception e) { UnityEngine.Debug.LogError($"[ItemsConfig] Failed to parse EquipmentTemplates.{entry.Name}: {e.Message}"); }
            }
        }

        /// <summary>Ищет предмет по одному id: сначала в <see cref="Items"/> (перебор всех Kind),
        /// затем — резолв через шаблоны (для сгенерированных id вроде "Hull_Coalition_Combat_T3").</summary>
        public ItemConfig GetItem(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureItemsCache();
            if (_itemsById.TryGetValue(id, out var it)) return it;
            EnsureTemplatesCache();
            foreach (var tpl in _templatesCache.Values)
            {
                var r = tpl.Resolve(id);
                if (r != null) return r;
            }
            return null;
        }

        /// <summary>Ищет предмет с указанной категорией (Kind). Для рукописных предметов —
        /// прямой lookup в <see cref="Items"/>[kind], для сгенерированных — резолв шаблона.</summary>
        public ItemConfig GetItem(string category, string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            if (Items != null && Items.TryGetValue(category, out var byId) &&
                byId != null && byId.TryGetValue(itemId, out var item))
            {
                // Kind в JSON не хранится — гарантируем его наличие даже если EnsureItemsCache
                // ещё не звали.
                if (item != null && string.IsNullOrEmpty(item.Kind)) item.Kind = category;
                return item;
            }

            EnsureTemplatesCache();
            if (_templatesCache.TryGetValue(category, out var tpl))
                return tpl.Resolve(itemId);
            return null;
        }

        /// <summary>Все предметы-оборудование (Kind ∈ Weapons/Artefacts/Hull/Engine/...):
        /// рукописные + резолвы шаблонов. Goods/Useless/MicroModules сюда НЕ входят.</summary>
        public IEnumerable<(string category, string itemId, ItemConfig config)> EnumerateAllItems()
        {
            EnsureItemsCache();
            if (Items != null)
                foreach (var byKind in Items)
                {
                    if (!ItemKind.IsEquipment(byKind.Key) || byKind.Value == null) continue;
                    foreach (var entry in byKind.Value)
                        yield return (byKind.Key, entry.Key, entry.Value);
                }

            EnsureTemplatesCache();
            foreach (var cat in _templatesCache)
                foreach (var id in cat.Value.EnumerateIds())
                {
                    var resolved = GetItem(cat.Key, id);
                    if (resolved != null) yield return (cat.Key, id, resolved);
                }
        }

        /// <summary>Все предметы указанной категории (Kind), только рукописные (без шаблонов).</summary>
        public IEnumerable<KeyValuePair<string, ItemConfig>> EnumerateByKind(string kind)
        {
            EnsureItemsCache();
            if (Items != null && Items.TryGetValue(kind, out var byId) && byId != null)
                foreach (var kv in byId) yield return kv;
        }

        /// <summary>
        /// Общие параметры категории: DisplayName, MaxSlots, WearOnMove/Hit/Shot/Turn, CanBeDamagedByWeapon.
        /// Для Weapons/Artefacts — из <see cref="Categories"/>; для шаблонных категорий — из <see cref="EquipmentTemplate.ToCategoryCommon"/>.
        /// </summary>
        public CategoryCommonConfig GetCategoryCommon(string category)
        {
            if (Categories != null && Categories.TryGetValue(category, out var c)) return c;
            EnsureTemplatesCache();
            if (_templatesCache.TryGetValue(category, out var tpl)) return tpl.ToCategoryCommon();
            return null;
        }

        public HullSeriesConfig GetHullSeries(string seriesId)
        {
            if (HullSeries == null || seriesId == null) return null;
            if (HullSeries.TryGetValue(seriesId, out var s)) return s;
            var withPrefix = "HullSeries_" + seriesId;
            return HullSeries.TryGetValue(withPrefix, out var s2) ? s2 : null;
        }

        public EquipmentTemplate GetTemplate(string category)
        {
            EnsureTemplatesCache();
            return _templatesCache.TryGetValue(category, out var t) ? t : null;
        }

        public ClusterConfig GetCluster(string side)
        {
            EnsureTemplatesCache();
            if (side != null && _clustersCache != null &&
                _clustersCache.TryGetValue(side, out var c)) return c;
            return null;
        }

        /// <summary>Возвращает имя линейки тира для стороны (например, Coalition + T3 → "Импульс"). null если нет.</summary>
        public string GetClusterLine(string side, string tierKey)
        {
            var cluster = GetCluster(side);
            if (cluster?.Lines != null && cluster.Lines.TryGetValue(tierKey, out var n)) return n;
            return null;
        }

        /// <summary>Все известные стороны (Coalition, Dominators, ...). Используется EnumerateIds.</summary>
        public IEnumerable<string> EnumerateSides()
        {
            EnsureTemplatesCache();
            return _clustersCache != null ? _clustersCache.Keys : System.Linq.Enumerable.Empty<string>();
        }

        public HitEffectConfig GetHitEffect(string pattern, string damageType)
        {
            var common = GetCategoryCommon(ItemKind.Weapons);
            if (common?.ShotEffects == null) return null;
            if (common.ShotEffects.TryGetValue(pattern, out var byPattern) &&
                byPattern.TryGetValue(damageType, out var cfg))
                return cfg;
            return null;
        }

        public (string category, string itemId, ItemConfig config)? GetRandomItem()
        {
            var all = new List<(string cat, string id, ItemConfig cfg)>();
            foreach (var x in EnumerateAllItems()) all.Add(x);
            if (all.Count == 0) return null;
            return all[GameRng.Range(0, all.Count)];
        }

        public EmbedTierConfig GetTier(string tierId)
        {
            if (string.IsNullOrEmpty(tierId) || EmbedTiers == null) return null;
            for (int i = 0; i < EmbedTiers.Count; i++)
                if (EmbedTiers[i].Id == tierId) return EmbedTiers[i];
            return null;
        }

        // ─────────── Compat-обёртки для перехода с раздельных секций Goods/MicroModules ───────────

        public ItemConfig GetGood(string id)
        {
            var it = GetItem(id);
            return (it != null && it.Kind == ItemKind.Goods) ? it : null;
        }

        public ItemConfig GetMicroModule(string id)
        {
            var it = GetItem(id);
            return (it != null && it.Kind == ItemKind.MicroModules) ? it : null;
        }
    }

    /// <summary>
    /// Кластер (политическая сторона) — Coalition / Dominators / .... Используется как
    /// сегмент Side в id предмета. Lines связывает техуровни (T1..T10) с именем линейки, которое
    /// подставляется в шаблон имени предмета через &lt;LineName&gt;.
    /// Если GraphicIgnoresTechLevel=true — графика предмета не зависит от техуровня
    /// (&lt;TechLevel&gt; в шаблоне пути графики заменяется на имя стороны), всё оборудование
    /// стороны делит одну картинку.
    /// </summary>
    public class ClusterConfig
    {
        [JsonProperty("Lines")] public Dictionary<string, string> Lines { get; set; } = new();
        [JsonProperty("GraphicIgnoresTechLevel")] public bool GraphicIgnoresTechLevel { get; set; } = false;
        /// <summary>Устаревшее имя <see cref="GraphicIgnoresTechLevel"/>. Сеттер обновляет новое
        /// поле, чтобы старые JSON-конфиги продолжали читаться. Не сериализуется.</summary>
        [JsonProperty("GraphicIgnoresTier")]
        public bool GraphicIgnoresTier { get => GraphicIgnoresTechLevel; set => GraphicIgnoresTechLevel = value; }
        public bool ShouldSerializeGraphicIgnoresTier() => false;
    }

    /// <summary>
    /// Общие свойства категории (применяются ко всем предметам категории независимо от тира/расы):
    /// износ за ход/движение/выстрел/попадание, CanBeDamagedByWeapon, MaxSlots.
    /// Для оружия дополнительно содержит ShotEffects (раньше — HitEffects верхнего уровня).
    /// </summary>
    public class CategoryCommonConfig
    {
        [JsonProperty("DisplayName")] public string DisplayName { get; set; }
        [JsonProperty("MaxSlots")] public int MaxSlots { get; set; } = 1;
        [JsonProperty("WearOnMove")] public int WearOnMove { get; set; } = 0;
        [JsonProperty("WearOnHit")] public int WearOnHit { get; set; } = 0;
        [JsonProperty("WearOnShot")] public int WearOnShot { get; set; } = 0;
        [JsonProperty("WearOnTurn")] public int WearOnTurn { get; set; } = 0;
        [JsonProperty("CanBeDamagedByWeapon")] public bool CanBeDamagedByWeapon { get; set; } = false;
        [JsonProperty("ShotEffects")] public Dictionary<string, Dictionary<string, HitEffectConfig>> ShotEffects { get; set; }

        /// <summary>
        /// Графика контейнера-выброса по умолчанию для всех предметов категории. Можно перебить
        /// для конкретного предмета через Params.ContainerGraphic. Формат: "Container_N" или
        /// абсолютный путь "Graphics/...". См. <see cref="ContainerFactory"/>.
        /// </summary>
        [JsonProperty("ContainerGraphic")] public string ContainerGraphic { get; set; }
    }

    /// <summary>
    /// Серия корпуса (модификатор): абсолютные значения слотов и уязвимостей.
    /// При установке перебивает HullSlotsHullType + HullSlotsRace из шаблона корпуса.
    /// </summary>
    public class HullSeriesConfig
    {
        [JsonProperty("Name")] public string Name { get; set; }
        [JsonProperty("Description")] public string Description { get; set; }

        [JsonProperty("WeaponVulnerability")]
        public Dictionary<string, float> WeaponVulnerability { get; set; } = new();

        /// <summary>Абсолютное число слотов по категориям (Engine, Weapons, Artefacts, ...). Перебивает любые тип/расовые расчёты.</summary>
        [JsonProperty("Slots")]
        public Dictionary<string, int> Slots { get; set; } = new();

        public float GetVulnerability(string damageType) =>
            WeaponVulnerability.TryGetValue(damageType, out var v) ? v : 1.0f;

        public int GetSlotCount(string category) =>
            Slots.TryGetValue(category, out var b) ? b : 0;
    }

    public class ItemConfig
    {
        /// <summary>Тип предмета (Weapons/Artefacts/Hull/…/MicroModules/Goods/Useless). Совпадает
        /// со слоточной категорией для оборудования. См. <see cref="ItemKind"/>. В JSON НЕ хранится —
        /// проставляется из ключа <c>Items[kind]</c> при загрузке (см. <see cref="ItemsConfig.EnsureItemsCache"/>).</summary>
        [JsonIgnore] public string Kind { get; set; }

        [JsonProperty("Name")] public string Name { get; set; }
        [JsonProperty("Description")] public string Description { get; set; }
        [JsonProperty("Weight")] public int Weight { get; set; } = 1;
        [JsonProperty("Price")] public int Price { get; set; }
        /// <summary>Алиас для <see cref="Price"/> — исторически использовался в Goods/MicroModules/Useless JSON.</summary>
        [JsonProperty("BasePrice")]
        public int BasePrice { get => Price; set => Price = value; }
        public bool ShouldSerializeBasePrice() => false;
        [JsonProperty("Durability")] public int Durability { get; set; }
        [JsonProperty("MaxDurability")] public int MaxDurability { get; set; }
        [JsonProperty("TechLevel")] public int TechLevel { get; set; } = 1;
        /// <summary>Минимальный ГТУ (Галактический Технологический Уровень), с которого встречается это оборудование (1..10). 0 = без нижней границы.</summary>
        [JsonProperty("StartGTL")] public int StartGTL { get; set; } = 1;
        /// <summary>Максимальный ГТУ, до которого встречается это оборудование (1..10). 0 = производится до конца игры.</summary>
        [JsonProperty("EndGTL")] public int EndGTL { get; set; } = 0;
        /// <summary>Путь к иконке предмета в инвентаре/магазине (Resources, без расширения). Для корпусов — расовый рисунок.</summary>
        [JsonProperty("GraphicPath")] public string GraphicPath { get; set; }
        /// <summary>Базовый путь к графике корабля в космосе (только для корпусов). Резолвится в {path}/{Owner|Race|Default}.</summary>
        [JsonProperty("BodyGraphicPath")] public string BodyGraphicPath { get; set; }
        /// <summary>Только для корпусов. Если true — графика корабля резолвится по расе/стороне
        /// ПРОИЗВОДИТЕЛЯ корпуса (<see cref="ManufacturerConfig"/>), а не по Owner/Race текущего корабля.
        /// Используется, чтобы корпус, купленный другой расой, сохранял «фирменный» внешний вид.</summary>
        [JsonProperty("GraphicByManufacturer")] public bool GraphicByManufacturer { get; set; } = false;
        /// <summary>Только для корпусов станций. true — графика тела не поворачивается по курсу.</summary>
        [JsonProperty("FixedRotation")] public bool FixedRotation { get; set; } = false;
        [JsonProperty("NoWear")] public bool NoWear { get; set; } = false;
        /// <summary>Носитель разрешает встраивать в себя микромодули/встраиваемые артефакты.
        /// Раньше называлось AllowModules — сохранена совместимость через <see cref="AllowModules"/>.</summary>
        [JsonProperty("AllowEmbeds")] public bool AllowEmbeds { get; set; } = true;
        /// <summary>Устаревшее имя <see cref="AllowEmbeds"/>. Сеттер обновляет новое поле, чтобы
        /// старые конфиги/сейвы (до июля 2026) продолжали работать. Не сериализуется.</summary>
        [JsonProperty("AllowModules")]
        public bool AllowModules { get => AllowEmbeds; set => AllowEmbeds = value; }
        public bool ShouldSerializeAllowModules() => false;
        /// <summary>Максимальное число встроенных предметов у носителя. 0 = «по умолчанию»:
        /// у оборудования 1 (если AllowEmbeds=true), у остального 0. Явное значение перебивает.</summary>
        [JsonProperty("MaxEmbeds")] public int MaxEmbeds { get; set; } = 0;
        /// <summary>Предмет является встраиваемым (микромодуль или встраиваемый артефакт).</summary>
        [JsonProperty("IsEmbeddable")] public bool IsEmbeddable { get; set; } = false;
        /// <summary>Блок встраиваемости (Tier/Priority/Compat/Bonuses/…). null у обычных предметов.</summary>
        [JsonProperty("Embed")] public EmbedConfig Embed { get; set; }
        /// <summary>Встроенные в само оборудование бонусы, применяются на любой экземпляр этого предмета,
        /// не занимая слот встраивания. Аналог intrinsic-микромодуля. Полосу появления в галактике задают
        /// стандартные <see cref="StartGTL"/>/<see cref="EndGTL"/> у оборудования.
        /// См. <see cref="SRG.Equipment.EmbedService.RecomputeCarrier"/>.</summary>
        [JsonProperty("IntrinsicEmbed")] public EffectConfig IntrinsicEmbed { get; set; }

        /// <summary>Пассивный код артефакта (или иного оборудования): пока предмет установлен
        /// в слот и <see cref="ItemInstance.IsWorking"/>=true, все Bonuses / SlotBonuses /
        /// AddedWeaponEffects / WeaponFlags применяются к носителю и (через ShipScope) к другим
        /// слотам корабля. См. <see cref="SRG.Equipment.EmbedService.RecomputeCarrier"/>.</summary>
        [JsonProperty("SlotCode")] public EffectConfig SlotCode { get; set; }

        /// <summary>Идентификатор C#-скрипта, вызываемого на сабтёрнах из <see cref="Subturns"/>
        /// для любого предмета, у которого он задан — и в слоте, и в трюме. Работает пока
        /// <see cref="ItemInstance.IsWorking"/>=true. Скрипт САМ решает, нужен ли ему только
        /// «экипированный» тик — через <see cref="ItemInstance.IsEquipped"/>
        /// (в Lua: <c>item.IsEquipped</c>). null — тика нет.
        /// Игнорируется, если задан <see cref="TurnScript"/> (Lua-скрипт перебивает C#-код).</summary>
        [JsonProperty("TurnCode")] public string TurnCode { get; set; }

        /// <summary>Идентификатор C#-скрипта активации: вызывается при активации предмета
        /// (см. <see cref="Activatable"/>). Скрипты — в <see cref="SRG.Equipment.ArtefactActionRegistry"/>.
        /// null — активация уходит в дефолтную ветку (Forsage/CompanionDrone).
        /// Игнорируется, если задан <see cref="UseScript"/>.</summary>
        [JsonProperty("UseCode")] public string UseCode { get; set; }

        /// <summary>Номера сабтёрнов (1..<see cref="SRG.Galaxy.GalaxyData.SubTurnsPerTurn"/> = 10),
        /// на которых срабатывает <see cref="TurnCode"/>/<see cref="TurnScript"/>. Дефолт [1] —
        /// один раз в начале хода. Для боевых эффектов (ПРО, локальные сканеры) задаётся
        /// конкретный сабтёрн вроде [5]; для многофазных артефактов — список вроде [1,4,8].
        /// Пустой список / null трактуется как [1].</summary>
        [JsonProperty("Subturns")] public int[] Subturns { get; set; }

        /// <summary>Тело Lua-скрипта тик-эффекта. Переменные, доступные внутри:
        /// <c>ship</c> (<see cref="SRG.Galaxy.ShipData"/>), <c>item</c> (<see cref="SRG.Equipment.ItemInstance"/>),
        /// <c>cfg</c> (<see cref="ItemsConfig"/>), <c>subTurn</c> (int, 1..10) и все глобалы
        /// <see cref="SRG.Scripting.LuaBindings"/> (включая <see cref="SRG.Equipment.ArtefactApi"/>
        /// под именем <c>Api</c>). Вызывается на сабтёрнах из <see cref="Subturns"/>.
        /// Компилируется один раз при загрузке конфига в замыкание. null — тика нет.</summary>
        [JsonProperty("TurnScript")] public string TurnScript { get; set; }

        /// <summary>Тело Lua-скрипта для активации. Должен вернуть таблицу-результат
        /// <c>{ ok=true, message="…", consume=false, wear=0 }</c>. Отсутствие возврата = ok=true
        /// без побочек. Компилируется один раз при загрузке конфига.</summary>
        [JsonProperty("UseScript")] public string UseScript { get; set; }

        /// <summary>Свободный JSON-конфиг для TurnCode/UseCode и их Lua-двойников:
        /// радиусы, стороны призыва, счётчики и т.д. Читается конкретной реализацией скрипта.</summary>
        [JsonProperty("ScriptParams")] public JObject ScriptParams { get; set; }

        /// <summary>Проверка «срабатывает ли предмет на этом сабтёрне». Пустой/null <see cref="Subturns"/>
        /// трактуется как [1].</summary>
        public bool RunsOnSubturn(int subTurn)
        {
            var s = Subturns;
            if (s == null || s.Length == 0) return subTurn == 1;
            for (int i = 0; i < s.Length; i++)
                if (s[i] == subTurn) return true;
            return false;
        }
        /// <summary>Классификационный флаг: экземпляр является предметом оборудования
        /// (может быть установлен в слот корабля, ремонтируется, апгрейдится). Задаётся в
        /// <c>Defaults["IsEquipment"] = true</c> в шаблоне категории. Всё, что не помечено
        /// ни <c>IsEquipment</c>, ни (для стеков) <c>IsGoods</c> — считается «useless»
        /// (находки, статуэтки, квестовые предметы).</summary>
        [JsonProperty("IsEquipment")] public bool IsEquipment { get; set; } = false;
        /// <summary>Предмет можно «активировать»: положить в псевдо-слот активации (справа от радара)
        /// и выполнить его активируемый код. У форсажа активация включает режим форсажа корабля,
        /// в будущем здесь будет произвольный скрипт.</summary>
        [JsonProperty("Activatable")] public bool Activatable { get; set; } = false;
        /// <summary>Экземпляр разрешено улучшать на научной базе (SB). По умолчанию true для оборудования
        /// (задаётся в <c>Defaults["IsImprovable"]</c> шаблона категории). Сбрасывается в false после
        /// первого апгрейда или встраивания микромодуля. См. docs/design/SB_Equipment_Improvement.txt.</summary>
        [JsonProperty("IsImprovable")] public bool IsImprovable { get; set; } = true;
        /// <summary>При улучшении требуется расходовать Ноды (стеки Node в трюме или нод-счёт корабля).
        /// Обычно ставится для доминаторского/трофейного оборудования.</summary>
        [JsonProperty("RequiresNodesToImprove")] public bool RequiresNodesToImprove { get; set; } = false;
        [JsonProperty("Manufacturer")] public ManufacturerConfig Manufacturer { get; set; } = new();
        [JsonProperty("WeaponPorts")] public List<float[]> WeaponPorts { get; set; }
        [JsonProperty("Tails")] public List<float[]> Tails { get; set; }
        [JsonProperty("Params")] public Dictionary<string, JToken> Params { get; set; } = new();
        [JsonProperty("Missile")] public MissileConfig Missile { get; set; }
        [JsonProperty("Visual")] public WeaponVisualConfig Visual { get; set; }

        // ─── Поля для не-оборудования (Goods/Useless/MicroModules) ───
        /// <summary>Инвентарная иконка стека (Resources-путь). Для оборудования используется
        /// <see cref="GraphicPath"/>; у товаров/находок/микромодулей исторически «Icon».</summary>
        [JsonProperty("Icon")] public string Icon { get; set; }
        /// <summary>Классификационный флаг для стеков-товаров (Kind=Goods). Стеки без флага
        /// в торговом рейсе TraderAI не участвуют.</summary>
        [JsonProperty("IsGoods")] public bool IsGoods { get; set; }
        /// <summary>Товар нелегальный (Kind=Goods).</summary>
        [JsonProperty("Illegal")] public bool Illegal { get; set; }
        /// <summary>Предмет объединяется в стек. Задаётся явно в JSON: обычно true для Goods
        /// и стакабельных Useless (Ноды); false для оборудования, микромодулей и квестовых уникумов.</summary>
        [JsonProperty("Stackable")] public bool Stackable { get; set; } = false;
        /// <summary>Минимальное число единиц в дропе (Ноды/астероидные находки). 1 если не задан.</summary>
        [JsonProperty("DropWeightMin")] public int DropWeightMin { get; set; } = 1;
        /// <summary>Максимальное число единиц в дропе (Ноды/астероидные находки). 50 если не задан.</summary>
        [JsonProperty("DropWeightMax")] public int DropWeightMax { get; set; } = 50;
        /// <summary>Базовый путь к спрайтшитам стакабельной находки в космосе (легаси Tier_N). Для
        /// современных стеков используется <see cref="GraphicSteps"/>.</summary>
        [JsonProperty("SpritePath")] public string SpritePath { get; set; }
        /// <summary>Ступени графики стакабельных предметов по количеству — приоритет над Icon/SpritePath.</summary>
        [JsonProperty("GraphicSteps")] public List<StackGraphicStep> GraphicSteps { get; set; }
        /// <summary>Правила появления встраиваемого предмета в мире (дроп, магазины, центр рейнджеров).</summary>
        [JsonProperty("Sources")] public ItemSources Sources { get; set; }
        /// <summary>Графика контейнера-выброса (переопределяет <see cref="CategoryCommonConfig.ContainerGraphic"/>).</summary>
        [JsonProperty("ContainerGraphic")] public string ContainerGraphic { get; set; }

        public float GetParam(string key, float defaultValue = 0f)
        {
            if (Params != null && Params.TryGetValue(key, out var token))
            {
                try { return token.ToObject<float>(); } catch { }
            }
            return defaultValue;
        }

        public string GetParamString(string key, string defaultValue = null)
        {
            if (Params != null && Params.TryGetValue(key, out var token))
            {
                try
                {
                    if (token.Type == Newtonsoft.Json.Linq.JTokenType.Array)
                        return string.Join(",", token.ToObject<string[]>());
                    return token.ToObject<string>();
                }
                catch { }
            }
            return defaultValue;
        }
        public string HullType => GetParamString("HullType");
    }

    public class MissileConfig
    {
        [JsonProperty("Hp")]                   public float  Hp          { get; set; } = 30f;
        [JsonProperty("Speed")]                public float  Speed       { get; set; } = 1.5f;
        [JsonProperty("Lifedays")]             public int    Lifedays    { get; set; } = 5;
        [JsonProperty("GraphicPath")]          public string GraphicPath { get; set; }
        [JsonProperty("Scale")]                public float  Scale       { get; set; } = 0.07f;
        [JsonProperty("ReturnsOnTargetDeath")] public bool   ReturnsOnTargetDeath { get; set; } = false;
        /// <summary>Сколько ракет вылетает за один выстрел (залп). По SR2-формуле:
        /// угол отклонения i-й ракеты = 60° / (N+3) × ⌈i/2⌉ с чередованием знака.</summary>
        [JsonProperty("SalvoCount")]           public int    SalvoCount  { get; set; } = 1;
        /// <summary>Максимальная угловая скорость поворота ракеты в градусах/ход.
        /// Используется в кинематическом шаге: каждый сабтёрн ракета может довернуть
        /// до TurnDeg/SubTurnsPerTurn градусов в сторону цели (как у кораблей).</summary>
        [JsonProperty("TurnDeg")]              public float  TurnDeg     { get; set; } = 720f;

        // ── Дополнения по SR2-модели (Missile_Trajectory.txt §§2.1, 3.A–D, 4) ──

        /// <summary>SR2-смещение точки спавна вдоль угла, чтобы ракеты залпа не пересекались.
        /// В наших единицах ≈ 0.08 (соответствует 8 у SR2). 0 = выключено.</summary>
        [JsonProperty("SpawnOffset")]          public float  SpawnOffset { get; set; } = 0.08f;

        /// <summary>Доля скорости стрелка, наследуемой как «инерция» на старте.
        /// Σ скорости стрелка × InertiaFactor + Speed = стартовая скорость ракеты.
        /// 0 = без инерции (обратно совместимо).</summary>
        [JsonProperty("InertiaFactor")]        public float  InertiaFactor { get; set; } = 0.5f;

        /// <summary>Множитель скорости только на launch-фазе (день запуска). Действует на step
        /// в LaunchPhase, чтобы ракета визуально «вылетала» из ствола сразу, не зависая на
        /// одной скорости со стрелком. После сброса LaunchPhase используется обычный Speed/SpeedMax.
        /// 1.0 = без буста (обратно совместимо).</summary>
        [JsonProperty("LaunchSpeedMultiplier")] public float  LaunchSpeedMultiplier { get; set; } = 2.5f;

        /// <summary>Если &gt; 0 — стартовая скорость ниже Speed, потом разгоняется
        /// до Speed со скоростью SpeedRampPerTurn единиц/ход. 0 = мгновенный разгон.</summary>
        [JsonProperty("SpeedRampPerTurn")]     public float  SpeedRampPerTurn { get; set; } = 0f;

        /// <summary>SR2 §3.B/§9.4: при отдалении от цели ракета входит в фазу «петли
        /// промаха» — летит в зеркальном направлении до 0.05·Lifedays. Параметр
        /// отключает механику, если в конкретной ракете это нежелательно.</summary>
        [JsonProperty("MissJitter")]           public bool   MissJitter { get; set; } = true;

        /// <summary>SR2 §3.D: ракета умирает раньше Lifedays, если для долёта до цели
        /// нужно более MaxRangeFactor × оставшегося срока. 0 = выключено.</summary>
        [JsonProperty("MaxRangeFactor")]       public float  MaxRangeFactor { get; set; } = 2f;

        /// <summary>SR2 §4: автозамена цели (homing class 5). После потери цели ракета
        /// ищет ближайшего hostile в радиусе ReacquireRadius. Если не нашла — наводится
        /// на стрелка. По умолчанию выключено: цель остаётся фиксированной.</summary>
        [JsonProperty("AutoReacquire")]        public bool   AutoReacquire { get; set; } = false;
        [JsonProperty("ReacquireRadius")]      public float  ReacquireRadius { get; set; } = 5f;
    }

    /// <summary>
    /// Визуальный режим оружия. Mode: "Code" — процедурная графика с палитрой цветов;
    /// "Sprite" — текстурный спрайт по пути SpritePath (Resources/).
    /// </summary>
    public class WeaponVisualConfig
    {
        [JsonProperty("Mode")]       public string Mode      { get; set; } = "Code";
        /// <summary>
        /// Палитра выстрела: HitEffectConfig-схема (Shape/Count/Radius/ColorMain/ColorSpark).
        /// ColorMain — основной цвет снаряда, ColorSpark — цвет искр/попадания.
        /// </summary>
        [JsonProperty("Palette")]    public HitEffectConfig Palette { get; set; }
        [JsonProperty("SpritePath")] public string SpritePath { get; set; }
        public bool IsSprite => string.Equals(Mode, "Sprite", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Параметры визуального эффекта попадания для пары (HitPattern, DamageType).
    /// Shape: "Sparks" (расходящиеся точки), "Ring" (кольцо фиксированного радиуса),
    /// "Burst" (расширяющееся кольцо от центра), "Flash" (одна пульсирующая точка).
    /// ColorMain/ColorSpark — hex-строки (#RRGGBB[AA]), null = цвет из DamageType-умолчания.
    /// </summary>
    public class HitEffectConfig
    {
        [JsonProperty("Shape")]      public string Shape      { get; set; } = "Sparks";
        [JsonProperty("Count")]      public int    Count      { get; set; } = 5;
        [JsonProperty("Radius")]     public float  Radius     { get; set; } = 0.30f;
        [JsonProperty("ColorMain")]  public string ColorMain  { get; set; }
        [JsonProperty("ColorSpark")] public string ColorSpark { get; set; }
    }

    public class ManufacturerConfig
    {
        [JsonProperty("Race")] public string Race { get; set; }
        /// <summary>Сторона-производитель (Coalition, Dominators, Pirates, ...). Раньше называлось Owner/Faction.</summary>
        [JsonProperty("Side")] public string Side { get; set; }
    }
}
