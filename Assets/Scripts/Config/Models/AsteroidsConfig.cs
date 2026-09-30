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
    public class AsteroidConfigSection
    {
        [JsonProperty("SpawnChancePerTurn")] public float SpawnChancePerTurn { get; set; } = 0.3f;
        [JsonProperty("GravityConst")] public float GravityConst { get; set; } = 500f;
        [JsonProperty("DampingConstant")] public float DampingConstant { get; set; } = 10000f;
        [JsonProperty("Types")] public Dictionary<string, AsteroidTypeConfig> Types { get; set; } = new();
        public string RollRandomTypeId()
        {
            if (Types == null || Types.Count == 0) return null;

            int total = 0;
            foreach (var t in Types.Values) total += t.SpawnWeight;
            if (total <= 0) return null;

            int roll = GameRng.Range(0, total);
            int sum = 0;
            foreach (var kv in Types)
            {
                sum += kv.Value.SpawnWeight;
                if (roll < sum) return kv.Key;
            }
            return null;
        }
    }

    public class AsteroidTypeConfig
    {
        [JsonProperty("Name")] public string Name { get; set; }
        [JsonProperty("MassMin")] public float MassMin { get; set; } = 50f;
        [JsonProperty("MassMax")] public float MassMax { get; set; } = 200f;
        [JsonProperty("SpeedMin")] public float SpeedMin { get; set; } = 1.5f;
        [JsonProperty("SpeedMax")] public float SpeedMax { get; set; } = 4.0f;
        [JsonProperty("SelfRotationSpeedMin")] public float SelfRotationSpeedMin { get; set; } = 20f;
        [JsonProperty("SelfRotationSpeedMax")] public float SelfRotationSpeedMax { get; set; } = 120f;
        [JsonProperty("CollisionRadius")] public float CollisionRadius { get; set; } = 0.3f;
        [JsonProperty("GraphicPath")] public string GraphicPath { get; set; }
        [JsonProperty("ExplosionPath")] public string ExplosionPath { get; set; }
        [JsonProperty("CommonMineralChance")] public float CommonMineralChance { get; set; } = 0.9f;
        [JsonProperty("PreciousMineralChance")] public float PreciousMineralChance { get; set; } = 0.05f;
        /// <summary>Диапазон количества минералов в дропе одного астероида (натуральный вес стека).
        /// Итоговое количество — GameRng.Range(MineralDropMin, MineralDropMax+1).</summary>
        [JsonProperty("MineralDropMin")] public int MineralDropMin { get; set; } = 1;
        [JsonProperty("MineralDropMax")] public int MineralDropMax { get; set; } = 50;
        [JsonProperty("EquipmentDropChance")] public float EquipmentDropChance { get; set; } = 0.02f;
        [JsonProperty("SpawnWeight")] public int SpawnWeight { get; set; } = 50;
        [JsonProperty("AnimFps")] public float AnimFps { get; set; } = 12f;
    }

    public class ExplosionConfig
    {
        [JsonProperty("FPS")] public float FPS { get; set; } = 12f;
        [JsonProperty("Scale")] public float Scale { get; set; } = 1.5f;
    }
}
