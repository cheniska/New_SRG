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
}
