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
    public class StarConfigSection
    {
        [JsonProperty("Types")] public Dictionary<string, StarTypeData> Types { get; set; }
        [JsonProperty("Colors")] public Dictionary<string, StarColorData> Colors { get; set; }
    }

    public class StarTypeData
    {
        [JsonProperty("GraphicSizeMult")] public float GraphicSizeMult { get; set; }
        [JsonProperty("MaxPlanets")] public int MaxPlanets { get; set; }
        [JsonProperty("MassSolar")] public float MassSolar { get; set; } = 1f;
        [JsonProperty("HZ_SizeMult")] public float HZ_SizeMult { get; set; } = 1f;
    }

    public class StarColorData
    {
        [JsonProperty("VariantsCount")] public int VariantsCount { get; set; }
        [JsonProperty("Path")] public string Path { get; set; }
        [JsonProperty("TempK")] public float TempK { get; set; }
        [JsonProperty("RadiationMult")] public float RadiationMult { get; set; } = 1f;
        [JsonProperty("HabitableZoneMin_u")] public float HabitableZoneMin_u { get; set; }
        [JsonProperty("HabitableZoneMax_u")] public float HabitableZoneMax_u { get; set; }
        [JsonProperty("HabitableZoneMid_u")] public float HabitableZoneMid_u { get; set; }
        [JsonProperty("LuminositySolar")] public float LuminositySolar { get; set; } = 1f;
        [JsonProperty("FlareRisk")] public float FlareRisk { get; set; } = 0f;
    }
}
