using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SRG.Combat;
using SRG.Core;
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

namespace SRG.Config
{
    public class GalaxyConfig
    {
        [JsonProperty("InitialDate")] public string InitialDate { get; set; } = "01.01.0001";
        [JsonProperty("Races")] public Dictionary<string, RaceConfig> Races { get; set; }
        [JsonProperty("Galaxies")] public Dictionary<string, GalaxyConfigData> Galaxies { get; set; }
        [JsonProperty("Stars")] public StarConfigSection Stars { get; set; }
        [JsonProperty("Planets")] public PlanetConfigSection Planets { get; set; }
        [JsonProperty("Satellites")] public SatelliteConfigSection Satellites { get; set; }
        [JsonProperty("Ships")] public ShipConfigSection Ships { get; set; }
        [JsonProperty("Asteroids")] public AsteroidConfigSection Asteroids { get; set; }
        [JsonProperty("Explosion")] public ExplosionConfig Explosion { get; set; }
        [JsonProperty("CustomProperties")] public Dictionary<string, Dictionary<string, CustomPropertyConfig>> CustomProperties { get; set; }
        [JsonProperty("Settings")] public SettingsConfigSection Settings { get; set; }
        [JsonProperty("TechLevels")] public TechLevelsConfig TechLevels { get; set; }
        [JsonProperty("PlanetUI")] public PlanetUIConfig PlanetUI { get; set; } = new();
        [JsonProperty("Goods")] public Dictionary<string, GalaxyGoodConfig> Goods { get; set; }
        [JsonProperty("Trade")] public TradeConfig Trade { get; set; }
        [JsonProperty("Inflation")] public InflationConfig Inflation { get; set; } = new();
        [JsonProperty("Events")] public Dictionary<string, EventConfig> Events { get; set; }
        [JsonProperty("OwnerRelations")] public Dictionary<string, int> OwnerRelations { get; set; }
        [JsonProperty("RaceRelations")] public Dictionary<string, int> RaceRelations { get; set; }
        // Все диалоги/приветствия/пулы живут в DialogsConfig.json (см. <see cref="DialogsConfig"/>).
        // В GalaxyConfig.json секции нет — loader присваивает объект после загрузки Dialogs.
        [JsonIgnore] public DialogsConfig Dialogs { get; set; } = new();
        // Конфиг интерфейса посадки (набор вкладок × тип цели/HullType) — секция GalaxyConfig.json.
        [JsonProperty("LandingUI")] public LandingUIConfig LandingUI { get; set; } = new();
        [JsonProperty("Skills")] public SkillsConfig Skills { get; set; } = new();
        [JsonProperty("Spawn")] public SpawnConfig Spawn { get; set; } = new();
        [JsonProperty("Science")] public ScienceConfig Science { get; set; } = new();
        [JsonProperty("Partners")] public PartnersConfig Partners { get; set; } = new();
        [JsonProperty("Ratings")] public List<ShipRatingConfig> Ratings { get; set; } = new();
    }

    // ──────────────────────────────────────────────
    // Рейтинги кораблей (рейнджерский, пиратский, торговый, …)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Декларативное описание одного рейтинга (GalaxyConfig.json → "Ratings", обрабатывает
    /// <see cref="ShipRatingService"/>). Участники выбираются селекторами ShipTypes/Owners
    /// (объединение по ИЛИ), счёт — взвешенная сумма метрик ShipData. Доступные ключи Score:
    /// KillsDominator, KillsPirate, KillsTransport, KillsRanger, KillsTotal, TradeProfit, CrimeRating.
    /// </summary>
    public class ShipRatingConfig
    {
        [JsonProperty("Id")] public string Id { get; set; }
        [JsonProperty("DisplayName")] public string DisplayName { get; set; }
        [JsonProperty("Enabled")] public bool Enabled { get; set; } = true;
        /// <summary>Участники по ShipTypeId ("Ranger", "Pirate", "Transport", …).</summary>
        [JsonProperty("ShipTypes")] public List<string> ShipTypes { get; set; } = new();
        /// <summary>Участники по Owner ("Pirates", "Dominators", …) — дополняет ShipTypes.</summary>
        [JsonProperty("Owners")] public List<string> Owners { get; set; } = new();
        /// <summary>Метрика → вес. Счёт корабля = Σ вес × значение метрики.</summary>
        [JsonProperty("Score")] public Dictionary<string, float> Score { get; set; } = new();
        /// <summary>Включать ли корабль игрока в список.</summary>
        [JsonProperty("IncludePlayer")] public bool IncludePlayer { get; set; }
        /// <summary>Минимальный счёт для попадания в список (отсекает «нулевых» новичков).</summary>
        [JsonProperty("MinScore")] public float MinScore { get; set; } = 1f;
        /// <summary>Раз в сколько ходов публиковать новость с отличившимися. 0 — не публиковать
        /// (рейтинг остаётся доступен через ShipRatingService.Build, например для UI).</summary>
        [JsonProperty("NewsStrideTurns")] public int NewsStrideTurns { get; set; } = 90;
        /// <summary>Сколько лидеров упоминать в новости.</summary>
        [JsonProperty("TopN")] public int TopN { get; set; } = 3;
        /// <summary>Категория новости; по умолчанию — DisplayName.</summary>
        [JsonProperty("NewsCategory")] public string NewsCategory { get; set; }
    }

    // ──────────────────────────────────────────────
    // Конфиг пилот-скилов
    // ──────────────────────────────────────────────

    /// <summary>
    /// Глобальные настройки системы 6 пилот-скилов (Accuracy/Mobility/Technical/Trader/Charm/Leadership).
    /// База скила хранится в [0..ProgressionCap]; эффективный (с модификаторами) зажат в [EffectiveMin..EffectiveMax].
    /// DamageRollScale — ширина шкалы урона: при cap=10 и scale=15 даже максимально прокачанный скил
    /// не гарантирует max урон сам по себе (нужен дополнительный буст или нулевая Mobility у цели).
    /// </summary>
    public class SkillsConfig
    {
        [JsonProperty("ProgressionCap")] public int ProgressionCap { get; set; } = 10;
        [JsonProperty("EffectiveMin")]   public int EffectiveMin   { get; set; } = -5;
        [JsonProperty("EffectiveMax")]   public int EffectiveMax   { get; set; } = 12;
        [JsonProperty("DamageRollScale")] public int DamageRollScale { get; set; } = 15;

        /// <summary>Константа diminishing-returns: scaled = amount / (catExp * K + 1).
        /// Меньше K → дольше «честный» рост, к десяткам тысяч сходится в ручеёк.</summary>
        [JsonProperty("DiminishingK")] public float DiminishingK { get; set; } = 0.0001f;

        /// <summary>Стоимость повышения скила с уровня L на L+1. Длина массива = ProgressionCap.
        /// UpgradeCosts[0] — цена 0→1, UpgradeCosts[ProgressionCap-1] — цена (cap-1)→cap.</summary>
        [JsonProperty("UpgradeCosts")] public int[] UpgradeCosts { get; set; }
            = { 100, 200, 400, 800, 1600, 3200, 6400, 12800, 25600, 51200 };

        public int GetUpgradeCost(int currentLevel)
        {
            if (UpgradeCosts == null || UpgradeCosts.Length == 0) return int.MaxValue;
            if (currentLevel < 0 || currentLevel >= UpgradeCosts.Length) return int.MaxValue;
            return UpgradeCosts[currentLevel];
        }
    }

    // ──────────────────────────────────────────────
    // Конфиг интерфейса планеты / станции
    // ──────────────────────────────────────────────

    public class PlanetUIConfig
    {
        [JsonProperty("NavBarWidth")]            public float NavBarWidth            { get; set; } = 130f;
        [JsonProperty("ButtonHeight")]           public float ButtonHeight           { get; set; } = 52f;
        [JsonProperty("ButtonSpacing")]          public float ButtonSpacing          { get; set; } = 8f;
        [JsonProperty("TitleBarHeight")]         public float TitleBarHeight         { get; set; } = 42f;
        [JsonProperty("BottomBarHeight")]        public float BottomBarHeight        { get; set; } = 48f;
        [JsonProperty("BackgroundAlpha")]        public float BackgroundAlpha        { get; set; } = 0.97f;
        [JsonProperty("SkipTurnButtonLabel")]    public string SkipTurnButtonLabel   { get; set; } = "Пропустить ход";
        [JsonProperty("LeavePlanetButtonLabel")] public string LeavePlanetButtonLabel{ get; set; } = "Взлететь";
        [JsonProperty("DialogButtonLabel")]      public string DialogButtonLabel     { get; set; } = "Связь";
        [JsonProperty("Screens")]    public Dictionary<string, PlanetScreenConfig>    Screens    { get; set; } = new();
        [JsonProperty("NavButtons")] public List<PlanetNavButtonConfig>               NavButtons { get; set; } = new();
    }

    public class PlanetScreenConfig
    {
        [JsonProperty("Title")]  public string Title  { get; set; }
        [JsonProperty("BgPath")] public string BgPath { get; set; }
    }

    public class PlanetNavButtonConfig
    {
        [JsonProperty("Id")]    public string Id    { get; set; }
        [JsonProperty("Label")] public string Label { get; set; }
    }

    public class TextConfig
    {
        /// <summary>Общие для всех галактик поля.</summary>
        [JsonProperty("RaceNames")] public Dictionary<string, string> RaceNames { get; set; }

        /// <summary>
        /// Сообщения для экрана смерти. Ключ — название PlayerDeathCause
        /// (Weapon, Missile, Asteroid, Unknown) либо "Default" как фолбэк.
        /// Допустимые плейсхолдеры: {killer}, {star}.
        /// </summary>
        [JsonProperty("DeathMessages")] public Dictionary<string, string> DeathMessages { get; set; }

        /// <summary>Локализуемые тексты новостей галактики (категории + шаблоны).
        /// Читаются через <see cref="SRG.Galaxy.Politics.NewsTexts"/>.</summary>
        [JsonProperty("News")] public NewsTextsConfig News { get; set; }

        /// <summary>Фразы диалоговой системы: пулы строк, тексты приветствий, дефолты тегов,
        /// имена говорящих. DialogsConfig.json держит только структуру и условия.
        /// Связывается с ней на загрузке через <see cref="SRG.Dialog.DialogTexts.Link"/>.</summary>
        [JsonProperty("Dialog")] public SRG.Dialog.DialogTextsConfig Dialog { get; set; } = new();

        /// <summary>Per-galaxy пулы имён (ключ = ключ галактики в GalaxyConfig.Galaxies).
        /// Генератор берёт пулы отсюда по активной галактике.</summary>
        [JsonProperty("Galaxies")] public Dictionary<string, TextConfigGalaxyData> Galaxies { get; set; }

        /// <summary>Legacy-fallback: если галактика не найдена в <see cref="Galaxies"/>,
        /// генератор использует эти поля. Оставлены для обратной совместимости.</summary>
        [JsonProperty("SectorNames")] public Dictionary<string, string> SectorNames { get; set; }
        [JsonProperty("StarsNames")] public Dictionary<string, RaceNamesData> StarsNames { get; set; }
        [JsonProperty("PlanetsNames")] public Dictionary<string, RaceNamesData> PlanetsNames { get; set; }
    }

    /// <summary>Тексты и шаблоны новостей для лент/панелей.
    /// Категории (Categories) — ключ→отображаемое имя. Шаблоны (Templates) — ключ→формат-строка
    /// с именованными плейсхолдерами вида <c>{starName}</c>. Fallback при отсутствии ключа —
    /// сам ключ (заметно в UI, но не падает).</summary>
    public class NewsTextsConfig
    {
        [JsonProperty("Categories")] public Dictionary<string, string> Categories { get; set; } = new();
        [JsonProperty("Templates")]  public Dictionary<string, string> Templates  { get; set; } = new();
    }

    /// <summary>Per-galaxy контейнер имён — то, что раньше жило в корне TextConfig.
    /// Формат подсекций идентичен legacy-полям TextConfig.</summary>
    public class TextConfigGalaxyData
    {
        [JsonProperty("SectorNames")] public Dictionary<string, string> SectorNames { get; set; }
        [JsonProperty("StarsNames")] public Dictionary<string, RaceNamesData> StarsNames { get; set; }
        [JsonProperty("PlanetsNames")] public Dictionary<string, RaceNamesData> PlanetsNames { get; set; }
    }

    public class RaceNamesData
    {
        [JsonProperty("Fixed")] public Dictionary<string, string> Fixed { get; set; } = new();
        [JsonProperty("Pool")] public List<string> Pool { get; set; } = new();
    }

    public class PremadeConfig
    {
        [JsonProperty("Galaxies")] public Dictionary<string, PremadeGalaxyData> Galaxies { get; set; }
    }

    public class PremadeGalaxyData
    {
        [JsonProperty("Name")] public string Name { get; set; }
        [JsonProperty("Sectors")] public Dictionary<string, FixedSectorData> Sectors { get; set; }
    }

    public class GalaxyConfigData
    {
        /// <summary>Отображаемое название галактики.</summary>
        [JsonProperty("Name")] public string Name { get; set; }

        /// <summary>Расы, присутствующие в этой галактике (ключи из GalaxyConfig.Races).</summary>
        [JsonProperty("Races")] public List<string> Races { get; set; }

        /// <summary>Размер галактики в единицах внутренней сетки [x, y].</summary>
        [JsonProperty("GalaxyGridSize")] public int[] GalaxyGridSize { get; set; }

        [JsonProperty("StarsCount")] public int StarsCount { get; set; }
        [JsonProperty("ConstCount")] public int SectorCount { get; set; }
        [JsonProperty("MinStarsPerConstellation")] public int MinStarsPerSector { get; set; }
        [JsonProperty("MaxStarsPerConstellation")] public int MaxStarsPerSector { get; set; }
        [JsonProperty("MinPlanetsPerStar")] public int MinPlanetsPerStar { get; set; }
        [JsonProperty("MaxPlanetsPerStar")] public int MaxPlanetsPerStar { get; set; }

        [JsonProperty("StartingStarName")] public string StartingStarName { get; set; }

        [JsonProperty("MinStarDistanceParsecs")] public float MinStarDistanceParsecs { get; set; } = 6f;

        [JsonProperty("MapPixelsPerParsec")] public float MapPixelsPerParsec { get; set; } = 4f;
        [JsonProperty("VoronoiBorderColor")] public string VoronoiBorderColor { get; set; } = "90,100,130";
        [JsonProperty("GTU")] public GtuConfig GTU { get; set; }
        /// <summary>Целевой охват одной расы в режиме Expansion: round(StarsCount × Rnd(BaseFractionMin..Max) × 2 / RaceCount) ± Jitter.
        /// Множитель ×2 учитывает, что системы могут быть мультирасовыми.</summary>
        [JsonProperty("TargetSystemsPerRace")] public TargetSystemsPerRaceConfig TargetSystemsPerRace { get; set; }
    }

    public class TargetSystemsPerRaceConfig
    {
        [JsonProperty("BaseFractionMin")] public float BaseFractionMin { get; set; } = 0.85f;
        [JsonProperty("BaseFractionMax")] public float BaseFractionMax { get; set; } = 0.9f;
        [JsonProperty("Multiplier")]      public float Multiplier      { get; set; } = 2.0f;
        [JsonProperty("Jitter")]          public int   Jitter          { get; set; } = 2;
    }

    public class StarConfigSection
    {
        [JsonProperty("Types")] public Dictionary<string, StarTypeData> Types { get; set; }
        [JsonProperty("Colors")] public Dictionary<string, StarColorData> Colors { get; set; }
    }

    public class SettingsConfigSection
    {
        [JsonProperty("SystemSizeMult")] public int SystemSizeMult { get; set; }
        [JsonProperty("InitialDate")] public string InitialDate { get; set; } = "01.01.0001";
        [JsonProperty("JumpFuelCostPerUnit")] public float JumpFuelCostPerUnit { get; set; } = 1.0f;
        /// <summary>Единиц дистанции пк, за которые корабль тратит 1 ход в фазе HyperArrive
        /// («болтание в гипертоннеле»). Итог: длительность = <c>ceil(distance / этот параметр)</c>,
        /// минус модификатор от Гипергенератора (<c>Hyperjump.TurnsDelta</c>). Минимум — 1 ход.</summary>
        [JsonProperty("HyperCrossPerTurnDistance")] public float HyperCrossPerTurnDistance { get; set; } = 30f;
        [JsonProperty("DefaultOwner")] public string DefaultOwner { get; set; }
        /// <summary>1 — режим расселения: расы начинают только с premadeConfig-планет и колонизируют галактику.</summary>
        [JsonProperty("Expansion")] public int Expansion { get; set; } = 0;
    }

    public class StarTypeData
    {
        [JsonProperty("GraphicSizeMult")] public float GraphicSizeMult { get; set; }
        [JsonProperty("MaxPlanets")] public int MaxPlanets { get; set; }
        [JsonProperty("MassSolar")] public float MassSolar { get; set; } = 1f;
        [JsonProperty("HZ_SizeMult")] public float HZ_SizeMult { get; set; } = 1f;
    }

    public class StarColorData
    {
        [JsonProperty("VariantsCount")] public int VariantsCount { get; set; }
        [JsonProperty("Path")] public string Path { get; set; }
        [JsonProperty("TempK")] public float TempK { get; set; }
        [JsonProperty("RadiationMult")] public float RadiationMult { get; set; } = 1f;
        [JsonProperty("HabitableZoneMin_u")] public float HabitableZoneMin_u { get; set; }
        [JsonProperty("HabitableZoneMax_u")] public float HabitableZoneMax_u { get; set; }
        [JsonProperty("HabitableZoneMid_u")] public float HabitableZoneMid_u { get; set; }
        [JsonProperty("LuminositySolar")] public float LuminositySolar { get; set; } = 1f;
        [JsonProperty("FlareRisk")] public float FlareRisk { get; set; } = 0f;
    }

    public class PlanetConfigSection
    {
        [JsonProperty("OrbitsEccentricity")] public float[] OrbitsEccentricity = { 0f, 0f };
        [JsonProperty("Sizes")] public Dictionary<string, PlanetSizeData> Sizes { get; set; }
        [JsonProperty("Types")] public List<string> Types { get; set; }
        [JsonProperty("GovernmentTypes")] public Dictionary<string, GovernmentTypeConfig> GovernmentTypes { get; set; } = new();
        [JsonProperty("EconomyTypes")] public Dictionary<string, EconomyTypeConfig> EconomyTypes { get; set; } = new();
        [JsonProperty("PTU")] public PtuConfig PTU { get; set; }

        public float EccentricityMin => OrbitsEccentricity?.Length > 0 ? OrbitsEccentricity[0] : 0f;
        public float EccentricityMax => OrbitsEccentricity?.Length > 1 ? OrbitsEccentricity[1] : 0f;
    }

    public class GovernmentTypeConfig
    {
        [JsonProperty("TechGrowthCoef")] public float TechGrowthCoef { get; set; } = 1f;
        [JsonProperty("AlwaysLegal")] public bool AlwaysLegal { get; set; } = false;
        [JsonProperty("GoodPriceModifiers")] public Dictionary<string, float> GoodPriceModifiers { get; set; } = new();
        /// <summary>Множитель к ежедневной выработке/потреблению товара (см. TradeSystem.TickDaily). 1.0 = базовый.</summary>
        [JsonProperty("GoodProductionMultiplier")] public Dictionary<string, float> GoodProductionMultiplier { get; set; } = new();
        /// <summary>Сдвиг целевого числа предметов в магазине по категории (см. EquipmentShopSystem). −2..+2 разумно.</summary>
        [JsonProperty("EquipmentShopMods")] public Dictionary<string, int> EquipmentShopMods { get; set; } = new();
        /// <summary>Множители выработки научных поинтов по категориям (см. ScienceSystem). 1.0 = базовый.</summary>
        [JsonProperty("ScienceBias")] public Dictionary<string, float> ScienceBias { get; set; } = new();
    }

    public class EconomyTypeConfig
    {
        [JsonProperty("TechGrowthCoef")] public float TechGrowthCoef { get; set; } = 1f;
        [JsonProperty("Trade")] public EconomyTradeConfig Trade { get; set; }
        /// <summary>Сдвиг целевого числа предметов в магазине по категории (см. EquipmentShopSystem). −2..+2 разумно.</summary>
        [JsonProperty("EquipmentShopMods")] public Dictionary<string, int> EquipmentShopMods { get; set; } = new();
        /// <summary>Множители выработки научных поинтов по категориям (см. ScienceSystem). 1.0 = базовый.</summary>
        [JsonProperty("ScienceBias")] public Dictionary<string, float> ScienceBias { get; set; } = new();
    }

    public class EconomyTradeConfig
    {
        [JsonProperty("GoodPriceModifiers")] public Dictionary<string, float> GoodPriceModifiers { get; set; } = new();
        [JsonProperty("GoodStockModifiers")] public Dictionary<string, float> GoodStockModifiers { get; set; } = new();
        /// <summary>Базовая ежедневная выработка/потребление товара (единиц/ход). Положительное — производство, отрицательное — потребление.</summary>
        [JsonProperty("GoodProductionDelta")] public Dictionary<string, float> GoodProductionDelta { get; set; } = new();
    }

    public class PlanetSizeData
    {
        [JsonProperty("BaseScale")] public float BaseScale { get; set; }
        [JsonProperty("MaxSatellites")] public int MaxSatellites { get; set; }
        [JsonProperty("AtmosphereChance")] public int AtmosphereChance { get; set; }
        [JsonProperty("SatOrbitPixelRadius")] public float PixelRadius { get; set; }
        // Диапазон населения [min, max] для данного размера планеты
        [JsonProperty("Population")] public int[] Population { get; set; } = new[] { 0, 0 };
        [JsonProperty("GravityRange")] public float[] GravityRange { get; set; } = new[] { 0.5f, 1.5f };
        [JsonProperty("DensityRange")] public float[] DensityRange { get; set; } = new[] { 5.0f, 6.0f };
        [JsonProperty("GeoK")] public float GeoK { get; set; } = 0f;
        [JsonProperty("SurfaceArea")] public float[] SurfaceAreaRange { get; set; } = new[] { 480f, 520f };
        /// <summary>Вес при случайном выборе размера планеты (взвешенный rand). По умолчанию 1.</summary>
        [JsonProperty("Weight")] public int Weight { get; set; } = 1;

        public float GravityMin     => GravityRange?.Length     > 0 ? GravityRange[0]     : 0.5f;
        public float GravityMax     => GravityRange?.Length     > 1 ? GravityRange[1]     : 1.5f;
        public float DensityMin     => DensityRange?.Length     > 0 ? DensityRange[0]     : 1.0f;
        public float DensityMax     => DensityRange?.Length     > 1 ? DensityRange[1]     : 5.0f;
        public float SurfaceAreaMin => SurfaceAreaRange?.Length > 0 ? SurfaceAreaRange[0] : 480f;
        public float SurfaceAreaMax => SurfaceAreaRange?.Length > 1 ? SurfaceAreaRange[1] : 520f;
    }

    public class SatelliteConfigSection
    {
        [JsonProperty("Sizes")] public Dictionary<string, SatelliteSizeData> Sizes { get; set; }
    }

    public class SatelliteSizeData
    {
        [JsonProperty("BaseScale")] public float BaseScale { get; set; }
    }

    public class AsteroidConfigSection
    {
        [JsonProperty("SpawnChancePerTurn")] public float SpawnChancePerTurn { get; set; } = 0.3f;
        [JsonProperty("GravityConst")] public float GravityConst { get; set; } = 500f;
        [JsonProperty("DampingConstant")] public float DampingConstant { get; set; } = 10000f;
        [JsonProperty("Types")] public Dictionary<string, AsteroidTypeConfig> Types { get; set; } = new();
        public string RollRandomTypeId()
        {
            if (Types == null || Types.Count == 0) return null;

            int total = 0;
            foreach (var t in Types.Values) total += t.SpawnWeight;
            if (total <= 0) return null;

            int roll = UnityEngine.Random.Range(0, total);
            int sum = 0;
            foreach (var kv in Types)
            {
                sum += kv.Value.SpawnWeight;
                if (roll < sum) return kv.Key;
            }
            return null;
        }
    }

    public class AsteroidTypeConfig
    {
        [JsonProperty("Name")] public string Name { get; set; }
        [JsonProperty("MassMin")] public float MassMin { get; set; } = 50f;
        [JsonProperty("MassMax")] public float MassMax { get; set; } = 200f;
        [JsonProperty("SpeedMin")] public float SpeedMin { get; set; } = 1.5f;
        [JsonProperty("SpeedMax")] public float SpeedMax { get; set; } = 4.0f;
        [JsonProperty("SelfRotationSpeedMin")] public float SelfRotationSpeedMin { get; set; } = 20f;
        [JsonProperty("SelfRotationSpeedMax")] public float SelfRotationSpeedMax { get; set; } = 120f;
        [JsonProperty("CollisionRadius")] public float CollisionRadius { get; set; } = 0.3f;
        [JsonProperty("GraphicPath")] public string GraphicPath { get; set; }
        [JsonProperty("ExplosionPath")] public string ExplosionPath { get; set; }
        [JsonProperty("CommonMineralChance")] public float CommonMineralChance { get; set; } = 0.9f;
        [JsonProperty("PreciousMineralChance")] public float PreciousMineralChance { get; set; } = 0.05f;
        /// <summary>Диапазон количества минералов в дропе одного астероида (натуральный вес стека).
        /// Итоговое количество — Random.Range(MineralDropMin, MineralDropMax+1).</summary>
        [JsonProperty("MineralDropMin")] public int MineralDropMin { get; set; } = 1;
        [JsonProperty("MineralDropMax")] public int MineralDropMax { get; set; } = 50;
        [JsonProperty("EquipmentDropChance")] public float EquipmentDropChance { get; set; } = 0.02f;
        [JsonProperty("SpawnWeight")] public int SpawnWeight { get; set; } = 50;
        [JsonProperty("AnimFps")] public float AnimFps { get; set; } = 12f;
    }

    public class ExplosionConfig
    {
        [JsonProperty("FPS")] public float FPS { get; set; } = 12f;
        [JsonProperty("Scale")] public float Scale { get; set; } = 1.5f;
    }

    public class TechLevelsConfig
    {
        [JsonProperty("Count")] public int Count { get; set; } = 8;
    }

    public class ShipConfigSection
    {
        [JsonProperty("Owners")] public Dictionary<string, OwnerConfig> Owners { get; set; }

        /// <summary>Именованные профили характера NPC: имя → диапазоны 6 черт
        /// (см. <see cref="ShipPersonality"/>). На профиль ссылается <see cref="ShipTypeConfig.Personality"/>.
        /// Профиль "Default" применяется к типам без явной ссылки (и при неизвестном имени).</summary>
        [JsonProperty("PersonalityProfiles")] public Dictionary<string, PersonalityProfileConfig> PersonalityProfiles { get; set; }

        [JsonProperty("ShipTypes")] public Dictionary<string, ShipTypeConfig> ShipTypes { get; set; }
    }

    /// <summary>Профиль характера NPC в конфиге. Каждая черта — массив [min, max] в шкале 0..100;
    /// отсутствующая черта берёт нейтральный дефолт (см. PersonalityRange.Fallback).</summary>
    public class PersonalityProfileConfig
    {
        [JsonProperty("Aggression")] public float[] Aggression { get; set; }
        [JsonProperty("Caution")] public float[] Caution { get; set; }
        [JsonProperty("Greed")] public float[] Greed { get; set; }
        [JsonProperty("Discipline")] public float[] Discipline { get; set; }
        [JsonProperty("Tribalism")] public float[] Tribalism { get; set; }
        [JsonProperty("Vendetta")] public float[] Vendetta { get; set; }
    }

    public class OwnerConfig
    {
        [JsonProperty("DefaultInternalRelation")] public int DefaultInternalRelation { get; set; }
        [JsonProperty("Color")] public string Color { get; set; }
        [JsonProperty("ColorPriority")] public bool ColorPriority { get; set; }
        [JsonProperty("EquipmentManufacturing")] public EquipmentManufacturingConfig EquipmentManufacturing { get; set; }

        /// <summary>Считать ли доминацию по каждой расе стороны отдельно. true для Coalition (расы воюют между
        /// собой за территорию). false для Pirates/Dominators (стороны однородные). Влияет на выбор ступени
        /// SubtypeTables для расовых кораблей.</summary>
        [JsonProperty("SeparateRaceDomination")] public bool SeparateRaceDomination { get; set; } = false;

        /// <summary>
        /// Режим оккупации: "Full" | "Partial". Используется только авто-правилом захвата:
        /// Full — для захвата системы в ней не должно быть ни одного корабля кроме своих;
        /// Partial — достаточно, чтобы не было чужих боевых (мирные транспорты/лайнеры допускаются).
        /// При Full-захвате авто-правило дополнительно ставит <see cref="PlanetData.LandingBlocked"/>=true.
        /// Скриптовые вызовы <see cref="OccupationService.OccupyPlanet"/> этот режим игнорируют
        /// — блокировка ставится отдельным <see cref="OccupationService.BlockLanding"/>.
        /// </summary>
        [JsonProperty("OccupationMode")] public string OccupationMode { get; set; } = "Partial";

        /// <summary>
        /// Если true — у стороны отдельный <see cref="FactionHighCommand"/> для каждой Race (пример: Dominators,
        /// у которых 3 расы = 3 генштаба). Если false — один генштаб на весь Owner (Coalition).
        /// Не путать с <see cref="SeparateRaceDomination"/> (та про SubtypeTables/спавн).
        /// </summary>
        [JsonProperty("SeparatePerRaceHighCommand")] public bool SeparatePerRaceHighCommand { get; set; } = false;

        /// <summary>
        /// Стратегия ГШ по умолчанию для этой стороны (WeakestNearby/WeakestAnywhere/Encirclement/HighValue).
        /// Используется только когда SeparatePerRaceHighCommand=false. Если пусто — WeakestNearby.
        /// </summary>
        [JsonProperty("HighCommandStrategy")] public string HighCommandStrategy { get; set; }

        /// <summary>
        /// Имя кластера диалогов для кораблей этой стороны. Резолвер диалогов
        /// (<see cref="SRG.Dialog.DialogService.ResolveShipDialogId"/>) вставит его между
        /// «поиск по Race» и «поиск общего Ship_default» — ищет <c>Ship_{Cluster}_{suffix}</c>.
        /// Пусто — берётся сам Owner-id (обратно совместимо с текущим Ship_Dominators_default).
        /// <br/>
        /// Пример: две союзные Owner-стороны Coalition и Alliance могут указать одинаковый
        /// <c>DialogCluster=Coalition</c> и обе разговаривать через один набор Ship_Coalition_*
        /// диалогов. Отдельная фракция Arx с своим кластером — задаёт <c>DialogCluster=Arx</c>
        /// и добавляет в DialogsConfig.json ключи <c>Ship_Arx_default</c>, <c>Ship_Arx_space</c> и т.д.
        /// Если для кластера нет ни одного ключа — резолвер естественным образом упадёт в общий
        /// <c>Ship_default</c>.
        /// </summary>
        [JsonProperty("DialogCluster")] public string DialogCluster { get; set; }
    }

    public class ShipTypeConfig
    {
        /// <summary>Русское display-имя типа для UI и подстановок в диалогах (Транспорт/Лайнер/…).
        /// Используется рендерером приветствий: <c>&lt;FullShip&gt;</c> → «{DisplayName} «{Name}»».</summary>
        [JsonProperty("DisplayName")] public string DisplayName { get; set; }
        [JsonProperty("MinimapIconPath")] public string MinimapIconPath { get; set; }
        [JsonProperty("Animations")] public Dictionary<string, ShipAnimationClip> Animations { get; set; }
        [JsonProperty("Thrusters")] public List<ShipThrusterConfig> Thrusters { get; set; }
        [JsonProperty("StarterKit")] public Dictionary<string, string> StarterKit { get; set; }
        [JsonProperty("StarterWeapons")] public List<string> StarterWeapons { get; set; }

        /// <summary>Боевой класс типа: строковое имя <see cref="global::CombatClass"/>
        /// (Civilian/Mercenary/Military/Pirate), регистронезависимо. Определяет ветку AI-логики,
        /// участие в директивах ГШ, учёт в боевой силе фракций и оккупации.
        /// Пусто/неизвестное имя → Mercenary.</summary>
        [JsonProperty("CombatClass")] public string CombatClass { get; set; }

        /// <summary>Имя профиля характера из <see cref="ShipConfigSection.PersonalityProfiles"/>
        /// (Military/Pirate/Trader/…): диапазоны 6 черт для генерации <see cref="ShipPersonality"/>.
        /// Пусто или неизвестное имя → профиль "Default".</summary>
        [JsonProperty("Personality")] public string Personality { get; set; }

        /// <summary>Стартовые значения 6 скилов в порядке SkillType:
        /// [Accuracy, Mobility, Technical, Trader, Charm, Leadership]. Используется ShipFactory
        /// при создании ShipData этого типа. По умолчанию все скилы = 2.</summary>
        [JsonProperty("BaseSkills")] public int[] BaseSkills { get; set; }

        /// <summary>Флаг «этот тип — дрон». Дрон живёт в двух формах (корабль/предмет), не имеет
        /// самостоятельной AI-жизни (только PartnerAttend), не считается в MaxPartners по умолчанию.
        /// Через отдельный ShipType можно легко добавить Drone_Heavy, Drone_Scout и т.п.</summary>
        [JsonProperty("IsDrone")] public bool IsDrone { get; set; }

        /// <summary>Веса приоритетов апгрейда по категориям оборудования для NpcOutfitter.
        /// Ключ = <see cref="EquipmentCategory"/> (Weapons/Hull/Shield/Engine/FuelTank/Radar/Scanner/...).
        /// Значение = int 0..10 (0 = не апгрейдим). Используется как множитель к базовому скору
        /// предмета при выборе, что покупать: score(item) × EquipmentPriority[category].
        /// Также определяет очередь ремонта в RepairService: категории с большим весом чинятся первыми.
        /// Отсутствие ключа = вес 1 (нейтрально).</summary>
        [JsonProperty("EquipmentPriority")] public Dictionary<string, int> EquipmentPriority { get; set; }

        /// <summary>Флаг «этот тип не садится ради заправки/ремонта/апгрейда».
        /// true — корабль игнорирует триггеры LandResupply (fuel/hull/money) и остаётся в бою.
        /// Проектно для доминаторов, которые «не заходят на планеты».
        /// Смертельно опасные ситуации (пустой бак → застревание) конфигуратор должен закладывать сам.</summary>
        [JsonProperty("SkipsResupplyLandings")] public bool SkipsResupplyLandings { get; set; }

        /// <summary>Флаг «на корабль этого типа можно садиться» (стыковка, аналог посадки на планету):
        /// пристыкованный корабль скрыт, следует за носителем, не участвует в бою.
        /// По умолчанию false; в конфиге true только у линкоров. См. <see cref="ShipDockingService"/>.</summary>
        [JsonProperty("CanBeLandedOn")] public bool CanBeLandedOn { get; set; }
    }

    public class ShipThrusterConfig
    {
        [JsonProperty("OffsetX")] public float OffsetX { get; set; }
        [JsonProperty("OffsetY")] public float OffsetY { get; set; }
        [JsonProperty("SpritesheetPath")] public string SpritesheetPath { get; set; }
        [JsonProperty("FPS")] public float FPS { get; set; } = 12f;
    }

    public class EquipmentManufacturingConfig
    {
        [JsonProperty("PriceMult")] public float PriceMult { get; set; } = 1.0f;
        [JsonProperty("DurabilityMult")] public float DurabilityMult { get; set; } = 1.0f;
    }

    public class RaceConfig
    {
        [JsonProperty("EmblemPath")] public string EmblemPath { get; set; }
        [JsonProperty("Color")] public string Color { get; set; }
        [JsonProperty("EquipmentManufacturing")] public EquipmentManufacturingConfig EquipmentManufacturing { get; set; }
        [JsonProperty("PriceCoef")] public float PriceCoef { get; set; } = 1f;
        [JsonProperty("TechGrowthCoef")] public float TechGrowthCoef { get; set; } = 1f;
        [JsonProperty("Trade")] public RaceTradeConfig Trade { get; set; }
        /// <summary>Множители выработки научных поинтов по категориям (см. ScienceSystem). 1.0 = базовый.</summary>
        [JsonProperty("ScienceBias")] public Dictionary<string, float> ScienceBias { get; set; } = new();
        [JsonProperty("PlanetConditions")] public RacePlanetConditionsConfig PlanetConditions { get; set; }
        [JsonProperty("NonPlanetary")] public int NonPlanetary { get; set; }
        /// <summary>Дата начала колонизации для режима Expansion (формат dd.MM.yyyy).</summary>
        [JsonProperty("ColonizationStartDate")] public string ColonizationStartDate { get; set; }
        /// <summary>Скорость колонизации: число систем в год (для режима Expansion).</summary>
        [JsonProperty("ColonizationSpeed")] public float ColonizationSpeed { get; set; } = 1f;
        /// <summary>Список ShipTypes (ключи из Ships.ShipTypes), которые спавнятся у этой расы.</summary>
        [JsonProperty("AvailableShipTypes")] public List<string> AvailableShipTypes { get; set; } = new();

        /// <summary>Модификатор размера военного флота расы (-1..+1). Прибавляется к target воинов на планетах
        /// этой расы. Применяется ТОЛЬКО к Warrior — гражданские/линкоры/доминаторы используют свои таблицы.</summary>
        [JsonProperty("FleetSizeModifier")] public int FleetSizeModifier { get; set; } = 0;

        /// <summary>Признак, что раса использует таблицу подтипов (SubtypeTables) для выбора конкретного
        /// типа корабля при спавне. true для рас доминаторов (RaceDominators1..3) — у них одна точка спавна
        /// «доминатор», конкретный Dom1..Dom6 выбирается из таблицы по ступени доминации.
        /// false (по умолчанию) — спавнится тип, указанный в политике напрямую (нет выбора подтипа).</summary>
        [JsonProperty("UseShipTypeTable")] public bool UseShipTypeTable { get; set; } = false;

        /// <summary>
        /// Стратегия ГШ для этой расы (актуально, когда <see cref="OwnerConfig.SeparatePerRaceHighCommand"/>=true).
        /// Значения: WeakestNearby / WeakestAnywhere / Encirclement / HighValue. Пусто — WeakestNearby.
        /// </summary>
        [JsonProperty("HighCommandStrategy")] public string HighCommandStrategy { get; set; }
    }

    public class TempLevelsConfig
    {
        [JsonProperty("Optimal")]       public float[] Optimal       { get; set; }
        [JsonProperty("Acceptable")]    public float[] Acceptable    { get; set; }
        [JsonProperty("Limit")]         public float[] Limit         { get; set; }
        // Максимальный диапазон, доступный расе через терраформирование
        [JsonProperty("Terraformable")] public float[] Terraformable { get; set; }
    }

    public class RacePlanetConditionsConfig
    {
        [JsonProperty("WaterAbundance")]    public float[] WaterAbundance    { get; set; }
        // Диапазон парциального давления кислорода [min, max] в атм (= OxygenPercent/100 * AtmPressure)
        [JsonProperty("OxygenPartialPressure")] public float[] OxygenPartialPressure { get; set; }
        [JsonProperty("g")]                 public float[] G                 { get; set; }
        [JsonProperty("AtmPressure")]       public float[] AtmPressure       { get; set; }
        [JsonProperty("SurfaceRadiation")]  public float[] SurfaceRadiation  { get; set; }
        [JsonProperty("SurfaceTemp")]       public TempLevelsConfig SurfaceTemp { get; set; }

        // Широкие диапазоны, которые раса способна стабилизировать терраформированием
        [JsonProperty("WaterAbundanceTerraformable")]       public float[] WaterAbundanceTerraformable       { get; set; }
        [JsonProperty("OxygenPartialPressureTerraformable")] public float[] OxygenPartialPressureTerraformable { get; set; }
        [JsonProperty("gTerraformable")]                    public float[] GTerraformable                    { get; set; }
        [JsonProperty("AtmPressureTerraformable")]          public float[] AtmPressureTerraformable          { get; set; }
        [JsonProperty("SurfaceRadiationTerraformable")]     public float[] SurfaceRadiationTerraformable     { get; set; }

        // Habitable
        public float? WaterAbundanceMin       => WaterAbundance?.Length > 0 ? WaterAbundance[0] : (float?)null;
        public float? WaterAbundanceMax       => WaterAbundance?.Length > 1 ? WaterAbundance[1] : (float?)null;
        public float? OxygenPartialMin        => OxygenPartialPressure?.Length > 0 ? OxygenPartialPressure[0] : (float?)null;
        public float? OxygenPartialMax        => OxygenPartialPressure?.Length > 1 ? OxygenPartialPressure[1] : (float?)null;
        public float? GMin                    => G?.Length > 0 ? G[0] : (float?)null;
        public float? GMax                    => G?.Length > 1 ? G[1] : (float?)null;
        public float? AtmPressureMin          => AtmPressure?.Length > 0 ? AtmPressure[0] : (float?)null;
        public float? AtmPressureMax          => AtmPressure?.Length > 1 ? AtmPressure[1] : (float?)null;
        public float? SurfaceRadiationMin     => SurfaceRadiation?.Length > 0 ? SurfaceRadiation[0] : (float?)null;
        public float? SurfaceRadiationMax     => SurfaceRadiation?.Length > 1 ? SurfaceRadiation[1] : (float?)null;
        // Acceptable используется для проверки пригодности
        public float? SurfaceTempMin          => SurfaceTemp?.Acceptable?.Length > 0 ? SurfaceTemp.Acceptable[0] : (float?)null;
        public float? SurfaceTempMax          => SurfaceTemp?.Acceptable?.Length > 1 ? SurfaceTemp.Acceptable[1] : (float?)null;
        // Limit — внешняя граница выживания (для штрафов)
        public float? SurfaceTempLimitMin     => SurfaceTemp?.Limit?.Length > 0 ? SurfaceTemp.Limit[0] : (float?)null;
        public float? SurfaceTempLimitMax     => SurfaceTemp?.Limit?.Length > 1 ? SurfaceTemp.Limit[1] : (float?)null;

        // Terraformable
        public float? WaterAbundanceTfMin       => WaterAbundanceTerraformable?.Length > 0 ? WaterAbundanceTerraformable[0] : (float?)null;
        public float? WaterAbundanceTfMax       => WaterAbundanceTerraformable?.Length > 1 ? WaterAbundanceTerraformable[1] : (float?)null;
        public float? OxygenPartialTfMin        => OxygenPartialPressureTerraformable?.Length > 0 ? OxygenPartialPressureTerraformable[0] : (float?)null;
        public float? OxygenPartialTfMax        => OxygenPartialPressureTerraformable?.Length > 1 ? OxygenPartialPressureTerraformable[1] : (float?)null;
        public float? GTfMin                    => GTerraformable?.Length > 0 ? GTerraformable[0] : (float?)null;
        public float? GTfMax                    => GTerraformable?.Length > 1 ? GTerraformable[1] : (float?)null;
        public float? AtmPressureTfMin          => AtmPressureTerraformable?.Length > 0 ? AtmPressureTerraformable[0] : (float?)null;
        public float? AtmPressureTfMax          => AtmPressureTerraformable?.Length > 1 ? AtmPressureTerraformable[1] : (float?)null;
        public float? SurfaceRadiationTfMin     => SurfaceRadiationTerraformable?.Length > 0 ? SurfaceRadiationTerraformable[0] : (float?)null;
        public float? SurfaceRadiationTfMax     => SurfaceRadiationTerraformable?.Length > 1 ? SurfaceRadiationTerraformable[1] : (float?)null;
        public float? SurfaceTempTfMin          => SurfaceTemp?.Terraformable?.Length > 0 ? SurfaceTemp.Terraformable[0] : (float?)null;
        public float? SurfaceTempTfMax          => SurfaceTemp?.Terraformable?.Length > 1 ? SurfaceTemp.Terraformable[1] : (float?)null;
    }

    public class RaceTradeConfig
    {
        [JsonProperty("GoodPriceModifiers")] public Dictionary<string, float> GoodPriceModifiers { get; set; } = new();
        [JsonProperty("BannedGoodsByGovernment")] public Dictionary<string, List<string>> BannedGoodsByGovernment { get; set; } = new();
        /// <summary>Модификатор ежедневной выработки товара для расы (прибавляется к Economy-базе).</summary>
        [JsonProperty("GoodProductionDelta")] public Dictionary<string, float> GoodProductionDelta { get; set; } = new();
    }

    public class CustomPropertyConfig
    {
        [JsonProperty("Values")] public List<string> Values { get; set; }
        [JsonProperty("Default")] public JToken Default { get; set; }
    }

    public class CustomPropertyRule
    {
        [JsonProperty("Conditions")] public Dictionary<string, List<string>> Conditions { get; set; } = new();
        [JsonProperty("Result")] public string Result { get; set; }
    }

    public class FixedSectorData
    {
        [JsonProperty("Name")] public string Name { get; set; }
        [JsonProperty("StarsCount")] public int? StarsCount { get; set; }
        [JsonProperty("Stars")] public Dictionary<string, FixedStarData> Stars { get; set; }
        [JsonProperty("Owner")] public string Owner;
        [JsonProperty("Race")] public string Race;
        /// <summary>"border" — периферия, "center" — центр, "corner" — угол, "neighbour" — рядом с другим сектором, "random"/null — без ограничений.</summary>
        [JsonProperty("Place")] public string Place { get; set; }
        /// <summary>Ключ соседнего сектора в словаре Sectors (только для Place="neighbour").</summary>
        [JsonProperty("NeighbourSector")] public string NeighbourSector { get; set; }
        /// <summary>Оккупант по умолчанию для всех планет сектора. Переопределяется на уровне звезды/планеты.</summary>
        [JsonProperty("OccupiedBy")] public string OccupiedBy { get; set; }
    }

    public class FixedStarData
    {
        [JsonProperty("Name")] public string Name { get; set; }
        [JsonProperty("Type")] public string Type { get; set; }
        [JsonProperty("Color")] public string Color { get; set; }
        [JsonProperty("PlanetsCount")] public int? PlanetsCount { get; set; }
        [JsonProperty("PlanetsNoneCount")] public string PlanetsNoneCount { get; set; }
        [JsonProperty("GraphVar")] public int? GraphVar;
        [JsonProperty("Planets")] public Dictionary<string, FixedPlanetData> Planets { get; set; }
        [JsonProperty("Owner")] public string Owner;
        [JsonProperty("Race")] public string Race;
        [JsonProperty("MaxAsteroids")] public int? MaxAsteroids { get; set; }
        /// <summary>Оккупант по умолчанию для всех планет звезды. Переопределяется на уровне планеты.</summary>
        [JsonProperty("OccupiedBy")] public string OccupiedBy { get; set; }
    }

    public class FixedPlanetData
    {
        [JsonProperty("Name")] public string Name { get; set; }
        [JsonProperty("Size")] public string Size { get; set; }
        [JsonProperty("Race")] public string Race { get; set; }
        [JsonProperty("CurOwner")] public string CurOwner { get; set; }
        [JsonProperty("Graphic")] public string Graphic { get; set; }
        [JsonProperty("Atmosphere")] public string Atmosphere { get; set; }
        [JsonProperty("OrbitalObjects")] public string OrbitalObjects { get; set; }
        [JsonProperty("SatellitesCount")] public int? SatellitesCount { get; set; }
        [JsonProperty("OrbitIndex")] public int? OrbitIndex { get; set; }
        [JsonProperty("OrbitRadius")] public float? OrbitRadius { get; set; }
        [JsonProperty("OrbitSpeed")] public float? OrbitSpeed { get; set; }
        [JsonProperty("DaySpeed")] public int? DaySpeed { get; set; }
        [JsonProperty("CloudsSpeed")] public int? CloudsSpeed { get; set; }
        [JsonProperty("AxialTilt")] public float? AxialTilt { get; set; }
        [JsonProperty("Satellites")] public Dictionary<string, FixedSatelliteData> Satellites { get; set; }
        [JsonProperty("Type")] public string Type { get; set; }
        [JsonProperty("SurfaceGravity")] public float? SurfaceGravity { get; set; }
        [JsonProperty("Density")] public float? Density { get; set; }
        [JsonProperty("GeoActivity")] public float? GeoActivity { get; set; }
        [JsonProperty("AtmPressure")] public float? AtmPressure { get; set; }
        [JsonProperty("OxygenPercent")] public float? OxygenPercent { get; set; }
        [JsonProperty("WaterAbundance")] public float? WaterAbundance { get; set; }
        [JsonProperty("SurfaceTemp")]      public float? SurfaceTemp      { get; set; }
        [JsonProperty("SurfaceLiquid")]    public float? SurfaceLiquid    { get; set; }
        [JsonProperty("SurfaceMountains")] public float? SurfaceMountains { get; set; }
        [JsonProperty("TotalSurfaceArea")] public float? TotalSurfaceArea { get; set; }
        /// <summary>Оккупант этой планеты. "" явно сбрасывает наследуемое значение со звезды/сектора.</summary>
        [JsonProperty("OccupiedBy")] public string OccupiedBy { get; set; }
        /// <summary>Явная скриптовая блокировка посадки. Если null — авто (Full-оккупант → true).</summary>
        [JsonProperty("LandingBlocked")] public bool? LandingBlocked { get; set; }
    }

    public class FixedSatelliteData
    {
        [JsonProperty("Name")] public string Name { get; set; }
        [JsonProperty("CurOwner")] public string CurOwner { get; set; }
        [JsonProperty("Graphic")] public string Graphic { get; set; }
        [JsonProperty("Atmosphere")] public string Atmosphere { get; set; }
        [JsonProperty("OrbitRadius")] public float? OrbitRadius { get; set; }
        [JsonProperty("OrbitSpeed")] public float? OrbitSpeed { get; set; }
        [JsonProperty("Size")] public string Size { get; set; }
        [JsonProperty("DaySpeed")] public int? DaySpeed { get; set; }
        [JsonProperty("CloudsSpeed")] public int? CloudsSpeed { get; set; }
    }

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
        /// См. docs/modules/equipment_improvement.md.</summary>
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
            return all[UnityEngine.Random.Range(0, all.Count)];
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
        /// первого апгрейда или встраивания микромодуля. См. docs/modules/equipment_improvement.md.</summary>
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
        /// <summary>Сколько ракет вылетает за один выстрел (залп).</summary>
        [JsonProperty("SalvoCount")]           public int    SalvoCount  { get; set; } = 1;
        /// <summary>Максимальная угловая скорость поворота ракеты в градусах/ход.
        /// Каждый сабтёрн ракета может довернуть до TurnDeg/SubTurnsPerTurn градусов.</summary>
        [JsonProperty("TurnDeg")]              public float  TurnDeg     { get; set; } = 720f;

        // ── Залп ──

        /// <summary>Полная ширина веера залпа в градусах: крайние ракеты уходят на ±SpreadDeg/2.</summary>
        [JsonProperty("SpreadDeg")]            public float  SpreadDeg   { get; set; } = 24f;
        /// <summary>Расстояние между соседними направляющими поперёк корпуса (мировые единицы).</summary>
        [JsonProperty("RailSpacing")]          public float  RailSpacing { get; set; } = 0.06f;
        /// <summary>Вынос точки старта вперёд по курсу стрелка (мировые единицы).</summary>
        [JsonProperty("NoseOffset")]           public float  NoseOffset  { get; set; } = 0.05f;

        // ── Двигатель ──

        /// <summary>Доля скорости стрелка, которую ракета наследует на старте. 0 = без инерции.</summary>
        [JsonProperty("InertiaFactor")]        public float  InertiaFactor { get; set; } = 0.5f;

        /// <summary>Множитель шага на разгонном участке (ход запуска), чтобы ракета сразу
        /// отрывалась от стрелка. 1.0 = без буста.</summary>
        [JsonProperty("LaunchSpeedMultiplier")] public float  LaunchSpeedMultiplier { get; set; } = 2.5f;

        /// <summary>Если &gt; 0 — ракета стартует с половины Speed и разгоняется до Speed
        /// на SpeedRampPerTurn единиц/ход. 0 = мгновенный разгон.</summary>
        [JsonProperty("SpeedRampPerTurn")]     public float  SpeedRampPerTurn { get; set; } = 0f;

        // ── Наведение ──

        /// <summary>При проскоке цели ракета сначала отходит на дистанцию разворота и только потом
        /// разворачивается (петля), а не кружит вокруг цели.</summary>
        [JsonProperty("OvershootExtend")]      public bool   OvershootExtend { get; set; } = true;

        /// <summary>Сколько ходов подряд цель может отдаляться от ракеты, прежде чем захват
        /// будет потерян и ракета самоликвидируется. 0 = не терять захват.</summary>
        [JsonProperty("MaxRecedingTurns")]     public int    MaxRecedingTurns { get; set; } = 2;

        /// <summary>Головка самонаведения: после потери цели ракета ищет нового врага стрелка
        /// в радиусе ReacquireRadius и в конусе SeekerConeDeg перед носом.
        /// По умолчанию выключено: цель остаётся фиксированной.</summary>
        [JsonProperty("AutoReacquire")]        public bool   AutoReacquire { get; set; } = false;
        [JsonProperty("ReacquireRadius")]      public float  ReacquireRadius { get; set; } = 5f;
        [JsonProperty("SeekerConeDeg")]        public float  SeekerConeDeg { get; set; } = 120f;
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

    // ──────────────────────────────────────────────
    // Товары и торговля
    // ──────────────────────────────────────────────

    public class GalaxyGoodConfig
    {
        [JsonProperty("DisplayName")] public string DisplayName { get; set; }
        [JsonProperty("BasePrice")] public int BasePrice { get; set; }
    }

    public class TradeConfig
    {
        [JsonProperty("ContrabandPriceMultiplier")] public float ContrabandPriceMultiplier { get; set; } = 3f;
        [JsonProperty("MinPriceMultiplier")] public float MinPriceMultiplier { get; set; } = 0.4f;
        [JsonProperty("MaxPriceMultiplier")] public float MaxPriceMultiplier { get; set; } = 3f;
        [JsonProperty("PriceStockCurve")] public string PriceStockCurve { get; set; } = "Hyperbolic";
        [JsonProperty("ContrabandRelationPenalty")] public int ContrabandRelationPenalty { get; set; } = -20;
        [JsonProperty("PrisonSentenceMonths")] public int PrisonSentenceMonths { get; set; } = 6;
        [JsonProperty("BaseStockBySize")] public Dictionary<string, int> BaseStockBySize { get; set; }
        [JsonProperty("SpecialLegality")] public SpecialLegalityConfig SpecialLegality { get; set; }
        /// <summary>Потолок стока товара = BaseStock × этот множитель. Без него планеты копили бы товар бесконечно.</summary>
        [JsonProperty("MaxStockMultiplier")] public float MaxStockMultiplier { get; set; } = 5f;
    }

    public class SpecialLegalityConfig
    {
        [JsonProperty("StationsAlwaysLegal")] public bool StationsAlwaysLegal { get; set; }
        [JsonProperty("NewMolizonContrabandGoods")] public List<string> NewMolizonContrabandGoods { get; set; } = new();
    }

    // ──────────────────────────────────────────────
    // Инфляция: линейный множитель на все цены в галактике
    // ──────────────────────────────────────────────

    public class InflationConfig
    {
        /// <summary>Линейный прирост InflationFactor за месяц (30 ходов). 0.0021 → +1.0 за 40 лет.</summary>
        [JsonProperty("MonthlyDelta")] public float MonthlyDelta { get; set; } = 0.0021f;
        /// <summary>Жёсткий потолок InflationFactor.</summary>
        [JsonProperty("MaxFactor")] public float MaxFactor { get; set; } = 5.0f;
        /// <summary>Разовый bump при объявлении войны.</summary>
        [JsonProperty("WarBoost")] public float WarBoost { get; set; } = 0.02f;
        /// <summary>Разовый bump при революции.</summary>
        [JsonProperty("RevolutionBoost")] public float RevolutionBoost { get; set; } = 0.01f;
    }

    // ──────────────────────────────────────────────
    // События планеты
    // ──────────────────────────────────────────────

    public class EventConfig
    {
        [JsonProperty("DisplayName")] public string DisplayName { get; set; }
        /// <summary>Длительность активной фазы в ХОДАХ (несмотря на имя — дни). DurationMonths[0..1] = диапазон.</summary>
        [JsonProperty("DurationMonths")] public int[] DurationMonths { get; set; }
        [JsonProperty("NewsMessage")] public string NewsMessage { get; set; }
        [JsonProperty("Trade")] public EventTradeConfig Trade { get; set; }
        [JsonProperty("PTU")] public EventPtuConfig PTU { get; set; }
        /// <summary>Множители выработки научных поинтов по категориям во время события (см. ScienceSystem).
        /// Эпидемия может, например, стимулировать Medicine ×1.3 — отражает приток ресурсов в дисциплину.</summary>
        [JsonProperty("Science")] public Dictionary<string, float> Science { get; set; } = new();

        // ── Расширения для PlanetaryEventSystem (этапы 4-5) ──────────────────────
        /// <summary>Условия автоматического срабатывания. Если пустой — событие может быть запущено только вручную или через ChainOnExpire.</summary>
        [JsonProperty("Triggers")] public List<EventTriggerConfig> Triggers { get; set; } = new();
        /// <summary>"And" (все триггеры должны сработать) или "Or" (любой). По умолчанию And.</summary>
        [JsonProperty("TriggerLogic")] public string TriggerLogic { get; set; } = "And";
        /// <summary>После завершения события — сколько ходов оно не может сработать снова. 0 = без кулдауна.</summary>
        [JsonProperty("Cooldown")] public int Cooldown { get; set; } = 0;
        /// <summary>При естественном завершении — шанс запустить другое событие.</summary>
        [JsonProperty("ChainOnExpire")] public EventChainConfig ChainOnExpire { get; set; }
        /// <summary>Одноразовые действия при старте события: смена правительства, шок инфляции, и т.д.</summary>
        [JsonProperty("ImmediateActions")] public List<EventActionConfig> ImmediateActions { get; set; } = new();
    }

    public class EventTriggerConfig
    {
        /// <summary>"PriceAbove" | "PriceBelow" | "StockBelow" | "StockAbove" | "DaysSinceLast" | "GovernmentIs" | "GTUAbove" | "RandomChance" | "EventActive" | "EventJustExpired".</summary>
        [JsonProperty("Type")] public string Type { get; set; }
        [JsonProperty("GoodId")] public string GoodId { get; set; }
        /// <summary>Для Price*: множитель к BasePrice (1.0 = базовая цена).</summary>
        [JsonProperty("MultiplierVsBase")] public float MultiplierVsBase { get; set; } = 1f;
        /// <summary>Для Stock*: абсолютный порог в единицах. Для других — общий параметр-число.</summary>
        [JsonProperty("Threshold")] public float Threshold { get; set; } = 0f;
        /// <summary>Для Price*/Stock*: сколько ходов подряд условие должно держаться.</summary>
        [JsonProperty("ForDays")] public int ForDays { get; set; } = 1;
        /// <summary>Для DaysSinceLast/EventActive/EventJustExpired: id события-ссылки.</summary>
        [JsonProperty("EventId")] public string EventId { get; set; }
        /// <summary>Для DaysSinceLast/EventJustExpired: пороги в ходах.</summary>
        [JsonProperty("Days")] public int Days { get; set; } = 0;
        /// <summary>Для GovernmentIs.</summary>
        [JsonProperty("Government")] public string Government { get; set; }
        /// <summary>Для GTUAbove.</summary>
        [JsonProperty("Level")] public int Level { get; set; }
        /// <summary>Для RandomChance: вероятность срабатывания за ход (0..1).</summary>
        [JsonProperty("ChancePerDay")] public float ChancePerDay { get; set; }
    }

    public class EventActionConfig
    {
        /// <summary>"ChangeGovernment" | "InflationShock" | "AddNotification".</summary>
        [JsonProperty("Type")] public string Type { get; set; }
        /// <summary>Для ChangeGovernment: "Random" | "Weighted".</summary>
        [JsonProperty("Mode")] public string Mode { get; set; }
        /// <summary>Для ChangeGovernment Weighted: словарь Gov→вес.</summary>
        [JsonProperty("Weights")] public Dictionary<string, float> Weights { get; set; }
        /// <summary>Для InflationShock: "War" | "Revolution".</summary>
        [JsonProperty("ShockType")] public string ShockType { get; set; }
        /// <summary>Для AddNotification: текст (если null — берётся NewsMessage события).</summary>
        [JsonProperty("Message")] public string Message { get; set; }
    }

    public class EventChainConfig
    {
        [JsonProperty("EventId")] public string EventId { get; set; }
        /// <summary>Вероятность (0..1) запустить связанное событие при истечении этого.</summary>
        [JsonProperty("Chance")] public float Chance { get; set; }
    }

    public class EventTradeConfig
    {
        [JsonProperty("GoodPriceModifiers")] public Dictionary<string, float> GoodPriceModifiers { get; set; } = new();
        /// <summary>Прибавка к ежедневной выработке товара во время действия события (см. TradeSystem.TickDaily).</summary>
        [JsonProperty("GoodStockDelta")] public Dictionary<string, float> GoodStockDelta { get; set; } = new();
    }

    public class EventPtuConfig
    {
        [JsonProperty("GrowthMultiplier")] public float GrowthMultiplier { get; set; } = 1f;
        [JsonProperty("PopulationEffect")] public float PopulationEffect { get; set; } = 0f;
        [JsonProperty("PTUPenalty")] public int PTUPenalty { get; set; } = 0;
    }

    // ──────────────────────────────────────────────
    // Планетарный технический уровень (ПТУ)
    // ──────────────────────────────────────────────

    public class PtuConfig
    {
        [JsonProperty("TickMonths")] public int TickMonths { get; set; } = 1;
        [JsonProperty("BaseGrowthRate")] public float BaseGrowthRate { get; set; } = 1f;
        [JsonProperty("LagBonus")] public float LagBonus { get; set; } = 1.5f;
        [JsonProperty("DifficultyEquipmentBonus")] public float DifficultyEquipmentBonus { get; set; } = 1f;
        [JsonProperty("Research")] public PtuResearchConfig Research { get; set; } = new();
    }

    public class PtuResearchConfig
    {
        [JsonProperty("ProgressPerLevel")] public float ProgressPerLevel { get; set; } = 100f;
    }

    // ──────────────────────────────────────────────
    // Галактический технический уровень (ГТУ)
    // ──────────────────────────────────────────────

    public class GtuConfig
    {
        [JsonProperty("MinValue")] public int MinValue { get; set; } = 1;
        [JsonProperty("MaxValue")] public int MaxValue { get; set; } = 8;
        [JsonProperty("Levels")] public List<GtuLevelConfig> Levels { get; set; } = new();
        [JsonProperty("HDMode")] public GtuHDModeConfig HDMode { get; set; } = new();
        [JsonProperty("WeaponUnlockThresholds")] public Dictionary<string, int> WeaponUnlockThresholds { get; set; } = new();
    }

    public class GtuLevelConfig
    {
        [JsonProperty("Level")] public int Level { get; set; }
        [JsonProperty("RequiredPlanets")] public int RequiredPlanets { get; set; }
    }

    public class GtuHDModeConfig
    {
        [JsonProperty("Enabled")] public bool Enabled { get; set; }
        [JsonProperty("BlockUseAboveGTU")] public bool BlockUseAboveGTU { get; set; }
        [JsonProperty("BlockRepairAboveGTU")] public bool BlockRepairAboveGTU { get; set; }
    }

    // ──────────────────────────────────────────────
    // Партнёрство: наём NPC-кораблей в свиту игрока/лидера
    // ──────────────────────────────────────────────

    /// <summary>Конфиг системы партнёрства. Хранит глобальные параметры формулы найма
    /// и список ShipTypeId, которые вообще можно нанять (и на каких условиях).</summary>
    public class PartnersConfig
    {
        [JsonProperty("Global")] public PartnersGlobalConfig Global { get; set; } = new();

        /// <summary>Ключ = ShipTypeId (совпадает с ключом из <see cref="ShipConfigSection.ShipTypes"/>).
        /// Если типа нет в этом словаре — корабль не поддаётся найму (Diplomat/Liner/Warrior/Dom* и т.п.).</summary>
        [JsonProperty("PartnerableShipTypes")] public Dictionary<string, PartnerableShipTypeConfig> PartnerableShipTypes { get; set; } = new();
    }

    public class PartnersGlobalConfig
    {
        /// <summary>Базовая цена контракта в кредитах, до умножения на PriceMultiplier типа.</summary>
        [JsonProperty("BasePrice")] public int BasePrice { get; set; } = 5000;

        /// <summary>Минимальная доля от BasePrice × PriceMultiplier, ниже — «оскорбление» (OfferTooLow).</summary>
        [JsonProperty("MinFeeRatio")] public float MinFeeRatio { get; set; } = 0.3f;

        /// <summary>Сколько пунктов требуемого attitude снижает 1 уровень Leadership игрока.</summary>
        [JsonProperty("AttitudePerLeadership")] public float AttitudePerLeadership { get; set; } = 2.0f;

        /// <summary>Максимальная скидка на цену контракта от полного Charm (100 = 100% Charm).</summary>
        [JsonProperty("CharmFeeReductionMax")] public float CharmFeeReductionMax { get; set; } = 0.5f;

        /// <summary>База и множитель на Leadership для расчёта MaxPartners нанимателя.
        /// MaxPartners = Base + floor(Leadership × Multiplier).</summary>
        [JsonProperty("MaxPartnersBase")] public int MaxPartnersBase { get; set; } = 0;
        [JsonProperty("MaxPartnersPerLeadership")] public float MaxPartnersPerLeadership { get; set; } = 1.0f;

        /// <summary>Пороги риота: RiotThreshold = BaseRiot − (Discipline−50) × DisciplineScale.
        /// При attitude(target→leader) ≤ RiotThreshold контракт расторгается.</summary>
        [JsonProperty("RiotBaseThreshold")] public float RiotBaseThreshold { get; set; } = 30f;
        [JsonProperty("RiotDisciplineScale")] public float RiotDisciplineScale { get; set; } = 0.3f;

        /// <summary>Как часто (в ходах) LoyaltyTracker проверяет условия Riot.</summary>
        [JsonProperty("LoyaltyCheckPeriodTurns")] public int LoyaltyCheckPeriodTurns { get; set; } = 5;

        /// <summary>Сколько ходов в году (для перевода ContractTermYears в номер хода истечения).</summary>
        [JsonProperty("TurnsPerYear")] public int TurnsPerYear { get; set; } = 365;
    }

    public class PartnerableShipTypeConfig
    {
        /// <summary>Множитель к BasePrice для этого типа.</summary>
        [JsonProperty("PriceMultiplier")] public float PriceMultiplier { get; set; } = 1.0f;

        /// <summary>Максимум CombatPower(target) / CombatPower(hirer), при котором цель согласится.
        /// Больше единицы → цель может быть чуть сильнее нанимателя.</summary>
        [JsonProperty("PowerRatio")] public float PowerRatio { get; set; } = 1.5f;

        /// <summary>Базовое требуемое attitude(target→hirer). Уменьшается за счёт Leadership.</summary>
        [JsonProperty("BaseAttitudeRequired")] public float BaseAttitudeRequired { get; set; } = 50f;

        /// <summary>Длина контракта в игровых годах. -1 = бессрочно.</summary>
        [JsonProperty("ContractTermYears")] public int ContractTermYears { get; set; } = -1;

        /// <summary>Дополнительные условия по личности цели. null поле = условие не проверяется.</summary>
        [JsonProperty("Requires")] public PartnerRequires Requires { get; set; } = new();
    }

    public class PartnerRequires
    {
        /// <summary>Минимальная Aggression у цели (для найма пиратов). null = не проверяется.</summary>
        [JsonProperty("AggressionMin")] public float? AggressionMin { get; set; }
        /// <summary>Максимальная Aggression у цели (для найма мирных). null = не проверяется.</summary>
        [JsonProperty("AggressionMax")] public float? AggressionMax { get; set; }
    }
}
