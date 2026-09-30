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
    public class GalaxyConfig
    {
        [JsonProperty("InitialDate")] public string InitialDate { get; set; } = "01.01.0001";
        [JsonProperty("Races")] public Dictionary<string, RaceConfig> Races { get; set; }
        [JsonProperty("Galaxies")] public Dictionary<string, GalaxyConfigData> Galaxies { get; set; }
        [JsonProperty("Stars")] public StarConfigSection Stars { get; set; }
        [JsonProperty("Planets")] public PlanetConfigSection Planets { get; set; }
        [JsonProperty("Satellites")] public SatelliteConfigSection Satellites { get; set; }
        [JsonProperty("Ships")] public ShipConfigSection Ships { get; set; }
        [JsonProperty("Asteroids")] public AsteroidConfigSection Asteroids { get; set; }
        [JsonProperty("Explosion")] public ExplosionConfig Explosion { get; set; }
        [JsonProperty("CustomProperties")] public Dictionary<string, Dictionary<string, CustomPropertyConfig>> CustomProperties { get; set; }
        [JsonProperty("Settings")] public SettingsConfigSection Settings { get; set; }
        [JsonProperty("TechLevels")] public TechLevelsConfig TechLevels { get; set; }
        [JsonProperty("PlanetUI")] public PlanetUIConfig PlanetUI { get; set; } = new();
        [JsonProperty("Goods")] public Dictionary<string, GalaxyGoodConfig> Goods { get; set; }
        [JsonProperty("Trade")] public TradeConfig Trade { get; set; }
        [JsonProperty("Inflation")] public InflationConfig Inflation { get; set; } = new();
        [JsonProperty("Events")] public Dictionary<string, EventConfig> Events { get; set; }
        [JsonProperty("OwnerRelations")] public Dictionary<string, int> OwnerRelations { get; set; }
        [JsonProperty("RaceRelations")] public Dictionary<string, int> RaceRelations { get; set; }
        // Все диалоги/приветствия/пулы живут в DialogsConfig.json (см. <see cref="DialogsConfig"/>).
        // В GalaxyConfig.json секции нет — loader присваивает объект после загрузки Dialogs.
        [JsonIgnore] public DialogsConfig Dialogs { get; set; } = new();
        // Конфиг интерфейса посадки (набор вкладок × тип цели/HullType) — секция GalaxyConfig.json.
        [JsonProperty("LandingUI")] public LandingUIConfig LandingUI { get; set; } = new();
        [JsonProperty("Skills")] public SkillsConfig Skills { get; set; } = new();
        [JsonProperty("Spawn")] public SpawnConfig Spawn { get; set; } = new();
        [JsonProperty("Science")] public ScienceConfig Science { get; set; } = new();
        [JsonProperty("Partners")] public PartnersConfig Partners { get; set; } = new();
        [JsonProperty("Ratings")] public List<ShipRatingConfig> Ratings { get; set; } = new();
    }
}
