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
    public class TechLevelsConfig
    {
        [JsonProperty("Count")] public int Count { get; set; } = 8;
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
}
