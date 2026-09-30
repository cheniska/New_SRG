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
    // Конфиг интерфейса планеты / станции
    // ──────────────────────────────────────────────

    public class PlanetUIConfig
    {
        [JsonProperty("NavBarWidth")]            public float NavBarWidth            { get; set; } = 130f;
        [JsonProperty("ButtonHeight")]           public float ButtonHeight           { get; set; } = 52f;
        [JsonProperty("ButtonSpacing")]          public float ButtonSpacing          { get; set; } = 8f;
        [JsonProperty("TitleBarHeight")]         public float TitleBarHeight         { get; set; } = 42f;
        [JsonProperty("BottomBarHeight")]        public float BottomBarHeight        { get; set; } = 48f;
        [JsonProperty("BackgroundAlpha")]        public float BackgroundAlpha        { get; set; } = 0.97f;
        [JsonProperty("SkipTurnButtonLabel")]    public string SkipTurnButtonLabel   { get; set; } = "Пропустить ход";
        [JsonProperty("LeavePlanetButtonLabel")] public string LeavePlanetButtonLabel{ get; set; } = "Взлететь";
        [JsonProperty("DialogButtonLabel")]      public string DialogButtonLabel     { get; set; } = "Связь";
        [JsonProperty("Screens")]    public Dictionary<string, PlanetScreenConfig>    Screens    { get; set; } = new();
        [JsonProperty("NavButtons")] public List<PlanetNavButtonConfig>               NavButtons { get; set; } = new();
    }

    public class PlanetScreenConfig
    {
        [JsonProperty("Title")]  public string Title  { get; set; }
        [JsonProperty("BgPath")] public string BgPath { get; set; }
    }

    public class PlanetNavButtonConfig
    {
        [JsonProperty("Id")]    public string Id    { get; set; }
        [JsonProperty("Label")] public string Label { get; set; }
    }
}
