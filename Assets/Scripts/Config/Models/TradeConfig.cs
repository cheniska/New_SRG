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
}
