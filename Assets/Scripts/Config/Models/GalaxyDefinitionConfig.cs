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
    public class GalaxyConfigData
    {
        /// <summary>Отображаемое название галактики.</summary>
        [JsonProperty("Name")] public string Name { get; set; }

        /// <summary>Расы, присутствующие в этой галактике (ключи из GalaxyConfig.Races).</summary>
        [JsonProperty("Races")] public List<string> Races { get; set; }

        /// <summary>Размер галактики в единицах внутренней сетки [x, y].</summary>
        [JsonProperty("GalaxyGridSize")] public int[] GalaxyGridSize { get; set; }

        [JsonProperty("StarsCount")] public int StarsCount { get; set; }
        [JsonProperty("ConstCount")] public int SectorCount { get; set; }
        [JsonProperty("MinStarsPerConstellation")] public int MinStarsPerSector { get; set; }
        [JsonProperty("MaxStarsPerConstellation")] public int MaxStarsPerSector { get; set; }
        [JsonProperty("MinPlanetsPerStar")] public int MinPlanetsPerStar { get; set; }
        [JsonProperty("MaxPlanetsPerStar")] public int MaxPlanetsPerStar { get; set; }

        [JsonProperty("StartingStarName")] public string StartingStarName { get; set; }

        [JsonProperty("MinStarDistanceParsecs")] public float MinStarDistanceParsecs { get; set; } = 6f;

        [JsonProperty("MapPixelsPerParsec")] public float MapPixelsPerParsec { get; set; } = 4f;
        [JsonProperty("VoronoiBorderColor")] public string VoronoiBorderColor { get; set; } = "90,100,130";
        [JsonProperty("GTU")] public GtuConfig GTU { get; set; }
        /// <summary>Целевой охват одной расы в режиме Expansion: round(StarsCount × Rnd(BaseFractionMin..Max) × 2 / RaceCount) ± Jitter.
        /// Множитель ×2 учитывает, что системы могут быть мультирасовыми.</summary>
        [JsonProperty("TargetSystemsPerRace")] public TargetSystemsPerRaceConfig TargetSystemsPerRace { get; set; }
    }

    public class TargetSystemsPerRaceConfig
    {
        [JsonProperty("BaseFractionMin")] public float BaseFractionMin { get; set; } = 0.85f;
        [JsonProperty("BaseFractionMax")] public float BaseFractionMax { get; set; } = 0.9f;
        [JsonProperty("Multiplier")]      public float Multiplier      { get; set; } = 2.0f;
        [JsonProperty("Jitter")]          public int   Jitter          { get; set; } = 2;
    }

    public class SettingsConfigSection
    {
        [JsonProperty("SystemSizeMult")] public int SystemSizeMult { get; set; }
        [JsonProperty("InitialDate")] public string InitialDate { get; set; } = "01.01.0001";
        [JsonProperty("JumpFuelCostPerUnit")] public float JumpFuelCostPerUnit { get; set; } = 1.0f;
        /// <summary>Единиц дистанции пк, за которые корабль тратит 1 ход в фазе HyperArrive
        /// («болтание в гипертоннеле»). Итог: длительность = <c>ceil(distance / этот параметр)</c>,
        /// минус модификатор от Гипергенератора (<c>Hyperjump.TurnsDelta</c>). Минимум — 1 ход.</summary>
        [JsonProperty("HyperCrossPerTurnDistance")] public float HyperCrossPerTurnDistance { get; set; } = 30f;
        [JsonProperty("DefaultOwner")] public string DefaultOwner { get; set; }
        /// <summary>1 — режим расселения: расы начинают только с premadeConfig-планет и колонизируют галактику.</summary>
        [JsonProperty("Expansion")] public int Expansion { get; set; } = 0;
    }
}
