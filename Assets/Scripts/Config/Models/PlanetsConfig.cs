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
}
