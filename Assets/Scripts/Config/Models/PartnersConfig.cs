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
