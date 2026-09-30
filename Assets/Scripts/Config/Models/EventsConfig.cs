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
}
