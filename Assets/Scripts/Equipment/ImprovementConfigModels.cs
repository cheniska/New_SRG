using System.Collections.Generic;
using Newtonsoft.Json;

namespace SRG.Equipment
{
    /// <summary>
    /// Балансовая конфигурация улучшения оборудования на научной базе.
    /// Живёт в <c>ItemsConfig.Improvement</c>. Дизайн — docs/modules/equipment_improvement.md.
    ///
    /// Цена: <c>money = round10(item.Price × CostShare[tier])</c>; продвинутое улучшение —
    /// то же от <see cref="AdvancedBaseTier"/> × <see cref="AdvancedCostMultiplier"/>.
    /// Нейроядра (если <c>ItemInstance.RequiresNodesToImprove</c>): <c>nodes = ceil(money / CreditsPerNode)</c>.
    /// После улучшения стоимость предмета растёт на <c>money × ValueGainShare</c>.
    ///
    /// Прирост параметра из <see cref="ImprovementAttributeDef"/>:
    /// <c>delta = max(current × Pct × roll, MinStep)</c>, roll — «колокол» 0.75..1.25.
    /// В обычном улучшении с выбранной характеристикой остальные атрибуты категории растут на
    /// <see cref="ImprovementCategoryDef.SecondaryGrowthFactor"/> от полного прироста.
    /// </summary>
    public class ImprovementConfig
    {
        /// <summary>Доля цены предмета, которую стоит улучшение каждого тира.</summary>
        [JsonProperty("CostShare")] public Dictionary<string, float> CostShare { get; set; } = new()
        {
            { "Min", 0.12f },
            { "Avg", 0.25f },
            { "Max", 0.5f },
        };

        /// <summary>Множитель к цене базового тира для продвинутого улучшения (одна конкретная
        /// характеристика, включая Weight/MaxDurability).</summary>
        [JsonProperty("AdvancedCostMultiplier")] public float AdvancedCostMultiplier { get; set; } = 2.5f;

        /// <summary>Тир, от цены которого считается стоимость продвинутого апгрейда.</summary>
        [JsonProperty("AdvancedBaseTier")] public string AdvancedBaseTier { get; set; } = "Max";

        /// <summary>Сколько кредитов стоимости улучшения соответствует одной нейроядру.</summary>
        [JsonProperty("CreditsPerNode")] public int CreditsPerNode { get; set; } = 120;

        /// <summary>Какая доля потраченного на улучшение переходит в стоимость предмета.</summary>
        [JsonProperty("ValueGainShare")] public float ValueGainShare { get; set; } = 0.7f;

        /// <summary>Категория оборудования → правила прироста атрибутов.
        /// Ключ — <see cref="EquipmentCategory"/>: "Hull"/"Weapons"/"Engine"/... </summary>
        [JsonProperty("Categories")] public Dictionary<string, ImprovementCategoryDef> Categories { get; set; } = new();

        /// <summary>Шанс апгрейда каждой eligible-единицы у NPC при спавне. 0 — никогда.</summary>
        [JsonProperty("NpcSpawnChance")] public float NpcSpawnChance { get; set; } = 0.05f;

        /// <summary>Веса тиров для NPC-спавна. Из ключей выбирается один по весам, если сработал <see cref="NpcSpawnChance"/>.</summary>
        [JsonProperty("NpcTierWeights")] public Dictionary<string, float> NpcTierWeights { get; set; } = new()
        {
            { "Min", 0.55f },
            { "Avg", 0.33f },
            { "Max", 0.12f },
        };

        public ImprovementCategoryDef GetCategory(string category)
        {
            if (Categories == null || string.IsNullOrEmpty(category)) return null;
            return Categories.TryGetValue(category, out var def) ? def : null;
        }
    }

    /// <summary>Настройки апгрейда одной категории оборудования.</summary>
    public class ImprovementCategoryDef
    {
        /// <summary>Основные атрибуты, которые улучшает стандартный апгрейд. Порядок задаёт
        /// «главный»/«вторичный»: при выборе конкретной характеристики она получает полный
        /// прирост, остальные — умножаются на <see cref="SecondaryGrowthFactor"/>.</summary>
        [JsonProperty("Attributes")] public Dictionary<string, ImprovementAttributeDef> Attributes { get; set; } = new();

        /// <summary>Доля прироста для остальных атрибутов при выборе конкретной характеристики
        /// в Detail-режиме.</summary>
        [JsonProperty("SecondaryGrowthFactor")] public float SecondaryGrowthFactor { get; set; } = 0.4f;

        /// <summary>Атрибуты, доступные ТОЛЬКО в продвинутом режиме. Стандартный туда не заглядывает.
        /// Обычно: Weight, MaxDurability, специфичные для категории (Armor у Hull).</summary>
        [JsonProperty("AdvancedTargets")] public Dictionary<string, ImprovementAttributeDef> AdvancedTargets { get; set; } = new();

        /// <summary>Знак прироста: -1 = стат уменьшается при улучшении (например, Consumption у щита).</summary>
        [JsonProperty("Sign")] public int Sign { get; set; } = 1;
    }

    /// <summary>Прирост одного атрибута. Формула:
    /// <c>delta = max(current × Pct × roll, MinStep)</c>, roll ∈ [0.75..1.25] с пиком в 1.
    /// Итоговое значение = current + Sign × delta (для целых полей — с округлением).
    /// </summary>
    public class ImprovementAttributeDef
    {
        /// <summary>Отображаемое имя атрибута для UI.</summary>
        [JsonProperty("DisplayName")] public string DisplayName { get; set; }

        /// <summary>Тир → [Pct, MinStep]: доля текущего значения и минимальный шаг прироста.</summary>
        [JsonProperty("Tiers")] public Dictionary<string, float[]> Tiers { get; set; } = new();

        /// <summary>true — параметр целочисленный (округлять до int). Дефолт true (большинство статов).</summary>
        [JsonProperty("Integer")] public bool Integer { get; set; } = true;

        /// <summary>Знак прироста для этого атрибута: −1 — улучшение уменьшает значение
        /// (масса оборудования). 0 — взять знак категории.</summary>
        [JsonProperty("Sign")] public int Sign { get; set; } = 0;

        public int ResolveSign(ImprovementCategoryDef category) => Sign != 0 ? Sign : (category?.Sign ?? 1);
    }
}
