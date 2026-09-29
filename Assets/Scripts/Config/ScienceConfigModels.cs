using System.Collections.Generic;
using Newtonsoft.Json;
using SRG.Economy;
using SRG.Galaxy;

namespace SRG.Config
{
    // ──────────────────────────────────────────────
    // Конфигурация научной системы (диздок: docs/planetary_science_system.md).
    // ──────────────────────────────────────────────

    /// <summary>
    /// Корневой конфиг науки. Берётся из GalaxyConfig.Science.
    /// Категории и биасы — данные продукции поинтов. Inventions — каталог изобретений.
    /// </summary>
    public class ScienceConfig
    {
        [JsonProperty("Categories")] public List<string> Categories { get; set; } = new()
        {
            "Medicine", "Materials", "Propulsion", "Weapons", "Computing", "BioTech", "Energy"
        };

        /// <summary>Базовая ставка поинтов в категорию на единицу населения за ход.</summary>
        [JsonProperty("BasePerCapita")] public float BasePerCapita { get; set; } = 0.0005f;

        /// <summary>Рост социального капитала в ход (база; умножается на расовый MoodGrowthCoef если задан).</summary>
        [JsonProperty("MoodGrowthPerTurn")] public float MoodGrowthPerTurn { get; set; } = 0.5f;

        /// <summary>Стоимость социального капитала на тир изобретения (gate: SocialCapital ≥ Tier × MoodCostPerTier).</summary>
        [JsonProperty("MoodCostPerTier")] public float MoodCostPerTier { get; set; } = 8f;

        /// <summary>Стоимость рывка контрибутора (Tier × этот множитель вычитается из SocialCapital при списании поинтов).</summary>
        [JsonProperty("MoodSpendPerTier")] public float MoodSpendPerTier { get; set; } = 0.5f;

        /// <summary>Раз в сколько ходов проводится распределение накопленных поинтов по активным узлам.</summary>
        [JsonProperty("DistributeStrideTurns")] public int DistributeStrideTurns { get; set; } = 7;

        /// <summary>Сколько credit-ов (MainContributor) нужно для +1 к PTU.</summary>
        [JsonProperty("CreditsPerPtuLevel")] public int CreditsPerPtuLevel { get; set; } = 2;

        /// <summary>Доля поинтов в категорию, после которой планета считается главным контрибьютором (0..1).</summary>
        [JsonProperty("MainContributorThreshold")] public float MainContributorThreshold { get; set; } = 0.4f;

        /// <summary>Лог: писать дневной отчёт по distribute-тикам в EconomicLog (категория SCIENCE).</summary>
        [JsonProperty("LogDistribute")] public bool LogDistribute { get; set; } = false;

        /// <summary>Верхняя граница SocialCapital у поселения (пассивный рост клампится этим значением).</summary>
        [JsonProperty("MoodCap")] public float MoodCap { get; set; } = 100f;

        /// <summary>Прибавка к скорости производства поинтов за каждый уровень TechLevel выше первого:
        /// ptuMul = 1 + этот коэффициент × max(0, TechLevel-1). Раньше — литерал 0.15 в ScienceSystem.</summary>
        [JsonProperty("TechLevelBonusPerStep")] public float TechLevelBonusPerStep { get; set; } = 0.15f;

        /// <summary>Опорное население, при котором ComputePopFactor даёт 1.0. Формула: sqrt(pop / pivot).
        /// Раньше — литерал 1000 в ScienceSystem.</summary>
        [JsonProperty("PopFactorPivot")] public float PopFactorPivot { get; set; } = 1000f;

        /// <summary>Нижняя граница popFactor (защита от очень маленьких колоний).</summary>
        [JsonProperty("PopFactorMin")] public float PopFactorMin { get; set; } = 0.1f;

        /// <summary>Верхняя граница popFactor (защита от гипер-колоний).</summary>
        [JsonProperty("PopFactorMax")] public float PopFactorMax { get; set; } = 5f;

        /// <summary>Каталог изобретений. Ключ — invention id, значение — описание узла.</summary>
        [JsonProperty("Inventions")] public Dictionary<string, InventionNodeConfig> Inventions { get; set; } = new();

        /// <summary>Безопасное чтение per-category мультипликатора из inline-биаса race/economy/government/event.</summary>
        public static float GetCategoryBias(Dictionary<string, float> bias, string category)
        {
            if (bias == null || string.IsNullOrEmpty(category)) return 1f;
            return bias.TryGetValue(category, out var v) ? v : 1f;
        }
    }

    /// <summary>Описание одного изобретения (узла в дереве).</summary>
    public class InventionNodeConfig
    {
        [JsonProperty("DisplayName")] public string DisplayName { get; set; }
        [JsonProperty("Tier")] public int Tier { get; set; } = 1;
        /// <summary>null/пусто = общегалактическое; иначе — только указанная раса вносит вклад и получает эффекты.</summary>
        [JsonProperty("Race")] public string Race { get; set; }
        [JsonProperty("Cost")] public Dictionary<string, int> Cost { get; set; } = new();
        [JsonProperty("Prerequisites")] public List<string> Prerequisites { get; set; } = new();
        [JsonProperty("Effects")] public List<UnlockEffectConfig> Effects { get; set; } = new();
    }

    /// <summary>
    /// Конфиг одного эффекта. Тип определяет, какие поля имеют смысл (см. ScienceUnlockEffects).
    /// Поля «зонтичные», лишние просто игнорируются для текущего Type.
    /// </summary>
    public class UnlockEffectConfig
    {
        [JsonProperty("Type")] public string Type { get; set; }

        // Generic / общие
        [JsonProperty("Message")] public string Message { get; set; }
        [JsonProperty("NewsKey")] public string NewsKey { get; set; }

        // Item template effects
        [JsonProperty("Category")] public string Category { get; set; }
        [JsonProperty("TemplateId")] public string TemplateId { get; set; }
        [JsonProperty("SpawnWeight")] public float SpawnWeight { get; set; } = 1f;
        [JsonProperty("MinTL")] public int MinTL { get; set; } = 1;
        [JsonProperty("MaxTL")] public int MaxTL { get; set; } = 10;
        [JsonProperty("OwnerRaceFilter")] public string OwnerRaceFilter { get; set; }

        // Goods
        [JsonProperty("GoodId")] public string GoodId { get; set; }
        [JsonProperty("DisplayName")] public string DisplayName { get; set; }
        [JsonProperty("BasePrice")] public int BasePrice { get; set; }

        // Multipliers (events / good price)
        [JsonProperty("Multiplier")] public float Multiplier { get; set; } = 1f;
        [JsonProperty("EventId")] public string EventId { get; set; }
    }

    // ──────────────────────────────────────────────
    // Runtime state галактической науки.
    // Сохраняется в JSON вместе с GalaxyData.
    // ──────────────────────────────────────────────

    public class GalacticResearchState
    {
        /// <summary>Id уже завершённых изобретений.</summary>
        [JsonProperty("Completed")] public HashSet<string> Completed { get; set; } = new();

        /// <summary>Активные изобретения, по которым идёт накопление поинтов.</summary>
        [JsonProperty("Active")] public Dictionary<string, InventionProgress> Active { get; set; } = new();

        /// <summary>Глобальные множители цен товаров. Применяются в TradeSystem.ComputeBasePrice.</summary>
        [JsonProperty("GoodPriceMultipliers")] public Dictionary<string, float> GoodPriceMultipliers { get; set; } = new();

        /// <summary>Множители к ChancePerDay у RandomChance-триггеров событий.</summary>
        [JsonProperty("EventChanceMultipliers")] public Dictionary<string, float> EventChanceMultipliers { get; set; } = new();

        /// <summary>Новые товары, добавленные в каталог изобретениями (расширяют cfg.Goods на лету).</summary>
        [JsonProperty("AddedGoods")] public Dictionary<string, GalaxyGoodConfig> AddedGoods { get; set; } = new();

        /// <summary>Товары, которые сняты с производства. TradeSystem пропускает их в свежих магазинах,
        /// но уже существующий сток продолжает обращаться (естественное вымывание).</summary>
        [JsonProperty("RemovedGoods")] public HashSet<string> RemovedGoods { get; set; } = new();

        /// <summary>Шаблоны оборудования, заблокированные для появления в магазинах (SpawnWeight ≤ 0).</summary>
        [JsonProperty("DisabledTemplates")] public HashSet<string> DisabledTemplates { get; set; } = new();

        /// <summary>Дополнительные ограничения по новым шаблонам (минимальный TL, фильтр расы).</summary>
        [JsonProperty("TemplateGates")] public Dictionary<string, TemplateGate> TemplateGates { get; set; } = new();

        public float GetGoodPriceMultiplier(string goodId)
        {
            if (string.IsNullOrEmpty(goodId)) return 1f;
            return GoodPriceMultipliers != null && GoodPriceMultipliers.TryGetValue(goodId, out var m) ? m : 1f;
        }

        public float GetEventChanceMultiplier(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return 1f;
            return EventChanceMultipliers != null && EventChanceMultipliers.TryGetValue(eventId, out var m) ? m : 1f;
        }

        public bool IsGoodRemoved(string goodId)
            => goodId != null && RemovedGoods != null && RemovedGoods.Contains(goodId);

        public bool IsTemplateDisabled(string templateId)
            => templateId != null && DisabledTemplates != null && DisabledTemplates.Contains(templateId);
    }

    public class TemplateGate
    {
        [JsonProperty("MinTL")] public int MinTL { get; set; } = 1;
        [JsonProperty("MaxTL")] public int MaxTL { get; set; } = 10;
        [JsonProperty("OwnerRaceFilter")] public string OwnerRaceFilter { get; set; }
    }

    public class InventionProgress
    {
        /// <summary>Сколько уже потрачено в каждую категорию (из Cost).</summary>
        [JsonProperty("Spent")] public Dictionary<string, float> Spent { get; set; } = new();
        /// <summary>Суммарный вклад planet.Uid → потраченные поинты (любая категория).</summary>
        [JsonProperty("Contributors")] public Dictionary<string, float> Contributors { get; set; } = new();
    }
}
