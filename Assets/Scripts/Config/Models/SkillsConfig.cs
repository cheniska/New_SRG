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
}
