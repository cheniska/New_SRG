using System.Collections.Generic;
using Newtonsoft.Json;

namespace SRG.Equipment
{
    /// <summary>
    /// Балансовая конфигурация улучшения оборудования на научной базе (SB).
    /// Живёт в <c>ItemsConfig.Improvement</c>. Полный разбор источника —
    /// docs/design/SB_Equipment_Improvement.txt и docs/design/SB_Improvement_Formulas.txt.
    ///
    /// Общая формула цены: <c>money = round10(item.Weight × Tiers[tier])</c>.
    /// Для продвинутого улучшения — то же × <see cref="AdvancedCostMultiplier"/>.
    /// Ноды (если <c>ItemInstance.RequiresNodesToImprove</c>): <c>nodes = round(money × NodeCostRate)</c>.
    ///
    /// Прирост параметра из <see cref="ImprovementAttributeDef"/>:
    /// <c>delta = current × RandRange(BasePct, BasePct + DeltaPct) + AbsBonus</c>.
    /// В обычном улучшении «второй» атрибут категории поднимается на
    /// <see cref="ImprovementCategoryDef.SecondaryGrowthFactor"/> от полного значения.
    /// </summary>
    public class ImprovementConfig
    {
        /// <summary>Множители Weight → base money по тирам. Дефолт из статьи HD: 0.3 / 0.6 / 1.2.</summary>
        [JsonProperty("Tiers")] public Dictionary<string, float> Tiers { get; set; } = new()
        {
            { "Min", 0.3f },
            { "Avg", 0.6f },
            { "Max", 1.2f },
        };

        /// <summary>Множитель к базовой цене для продвинутого улучшения (одна конкретная характеристика,
        /// включая Weight/Durability). Дефолт — 3× стоимости тира Max.</summary>
        [JsonProperty("AdvancedCostMultiplier")] public float AdvancedCostMultiplier { get; set; } = 3f;

        /// <summary>Тир, от цены которого считается стоимость продвинутого апгрейда.</summary>
        [JsonProperty("AdvancedBaseTier")] public string AdvancedBaseTier { get; set; } = "Max";

        /// <summary>Ноды = round(money × NodeCostRate). Из HD: 0.01 (1 нода на 100 монет).</summary>
        [JsonProperty("NodeCostRate")] public float NodeCostRate { get; set; } = 0.01f;

        /// <summary>Категория оборудования → правила прироста атрибутов.
        /// Ключ — <see cref="EquipmentCategory"/>: "Hull"/"Weapons"/"Engine"/... </summary>
        [JsonProperty("Categories")] public Dictionary<string, ImprovementCategoryDef> Categories { get; set; } = new();

        /// <summary>Шанс апгрейда каждой eligible-единицы у NPC при спавне. 0 — никогда.</summary>
        [JsonProperty("NpcSpawnChance")] public float NpcSpawnChance { get; set; } = 0.05f;

        /// <summary>Веса тиров для NPC-спавна. Из ключей выбирается один по весам, если сработал <see cref="NpcSpawnChance"/>.</summary>
        [JsonProperty("NpcTierWeights")] public Dictionary<string, float> NpcTierWeights { get; set; } = new()
        {
            { "Min", 0.6f },
            { "Avg", 0.3f },
            { "Max", 0.1f },
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

        /// <summary>Доля прироста для «второго» атрибута при выборе конкретной характеристики
        /// в Detail-режиме. Дефолт 0.35 — второй параметр качается на 35% от основного.</summary>
        [JsonProperty("SecondaryGrowthFactor")] public float SecondaryGrowthFactor { get; set; } = 0.35f;

        /// <summary>Атрибуты, доступные ТОЛЬКО в продвинутом режиме. Стандартный туда не заглядывает.
        /// Обычно: Weight, MaxDurability, специфичные для категории (Armor у Hull).</summary>
        [JsonProperty("AdvancedTargets")] public Dictionary<string, ImprovementAttributeDef> AdvancedTargets { get; set; } = new();

        /// <summary>Знак прироста: -1 = стат уменьшается при улучшении (например, Consumption у щита).</summary>
        [JsonProperty("Sign")] public int Sign { get; set; } = 1;
    }

    /// <summary>Диапазон прироста для одного атрибута. Формула:
    /// <c>delta = current × RandRange(BasePct, BasePct + DeltaPct) + AbsBonus</c>.
    /// Итог округляется до целого (для целых полей). Итоговое значение = current + Sign × delta.
    /// </summary>
    public class ImprovementAttributeDef
    {
        /// <summary>Отображаемое имя атрибута для UI.</summary>
        [JsonProperty("DisplayName")] public string DisplayName { get; set; }

        /// <summary>Тир → [BasePct, DeltaPct, AbsBonus]. Пример из статьи (Weapon.Damage):
        /// Min=[0.20, 0.05, 1], Avg=[0.30, 0.10, 2], Max=[0.40, 0.10, 3].</summary>
        [JsonProperty("Tiers")] public Dictionary<string, float[]> Tiers { get; set; } = new();

        /// <summary>true — параметр целочисленный (округлять до int). Дефолт true (большинство статов).</summary>
        [JsonProperty("Integer")] public bool Integer { get; set; } = true;
    }
}
