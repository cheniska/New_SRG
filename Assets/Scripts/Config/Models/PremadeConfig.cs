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
    public class PremadeConfig
    {
        [JsonProperty("Galaxies")] public Dictionary<string, PremadeGalaxyData> Galaxies { get; set; }
    }

    public class PremadeGalaxyData
    {
        [JsonProperty("Name")] public string Name { get; set; }
        [JsonProperty("Sectors")] public Dictionary<string, FixedSectorData> Sectors { get; set; }
    }

    public class FixedSectorData
    {
        [JsonProperty("Name")] public string Name { get; set; }
        [JsonProperty("StarsCount")] public int? StarsCount { get; set; }
        [JsonProperty("Stars")] public Dictionary<string, FixedStarData> Stars { get; set; }
        [JsonProperty("Owner")] public string Owner;
        [JsonProperty("Race")] public string Race;
        /// <summary>"border" — периферия, "center" — центр, "corner" — угол, "neighbour" — рядом с другим сектором, "random"/null — без ограничений.</summary>
        [JsonProperty("Place")] public string Place { get; set; }
        /// <summary>Ключ соседнего сектора в словаре Sectors (только для Place="neighbour").</summary>
        [JsonProperty("NeighbourSector")] public string NeighbourSector { get; set; }
        /// <summary>Оккупант по умолчанию для всех планет сектора. Переопределяется на уровне звезды/планеты.</summary>
        [JsonProperty("OccupiedBy")] public string OccupiedBy { get; set; }
    }

    public class FixedStarData
    {
        [JsonProperty("Name")] public string Name { get; set; }
        [JsonProperty("Type")] public string Type { get; set; }
        [JsonProperty("Color")] public string Color { get; set; }
        [JsonProperty("PlanetsCount")] public int? PlanetsCount { get; set; }
        [JsonProperty("PlanetsNoneCount")] public string PlanetsNoneCount { get; set; }
        [JsonProperty("GraphVar")] public int? GraphVar;
        [JsonProperty("Planets")] public Dictionary<string, FixedPlanetData> Planets { get; set; }
        [JsonProperty("Owner")] public string Owner;
        [JsonProperty("Race")] public string Race;
        [JsonProperty("MaxAsteroids")] public int? MaxAsteroids { get; set; }
        /// <summary>Оккупант по умолчанию для всех планет звезды. Переопределяется на уровне планеты.</summary>
        [JsonProperty("OccupiedBy")] public string OccupiedBy { get; set; }
    }

    public class FixedPlanetData
    {
        [JsonProperty("Name")] public string Name { get; set; }
        [JsonProperty("Size")] public string Size { get; set; }
        [JsonProperty("Race")] public string Race { get; set; }
        [JsonProperty("CurOwner")] public string CurOwner { get; set; }
        [JsonProperty("Graphic")] public string Graphic { get; set; }
        [JsonProperty("Atmosphere")] public string Atmosphere { get; set; }
        [JsonProperty("OrbitalObjects")] public string OrbitalObjects { get; set; }
        [JsonProperty("SatellitesCount")] public int? SatellitesCount { get; set; }
        [JsonProperty("OrbitIndex")] public int? OrbitIndex { get; set; }
        [JsonProperty("OrbitRadius")] public float? OrbitRadius { get; set; }
        [JsonProperty("OrbitSpeed")] public float? OrbitSpeed { get; set; }
        [JsonProperty("DaySpeed")] public int? DaySpeed { get; set; }
        [JsonProperty("CloudsSpeed")] public int? CloudsSpeed { get; set; }
        [JsonProperty("AxialTilt")] public float? AxialTilt { get; set; }
        [JsonProperty("Satellites")] public Dictionary<string, FixedSatelliteData> Satellites { get; set; }
        [JsonProperty("Type")] public string Type { get; set; }
        [JsonProperty("SurfaceGravity")] public float? SurfaceGravity { get; set; }
        [JsonProperty("Density")] public float? Density { get; set; }
        [JsonProperty("GeoActivity")] public float? GeoActivity { get; set; }
        [JsonProperty("AtmPressure")] public float? AtmPressure { get; set; }
        [JsonProperty("OxygenPercent")] public float? OxygenPercent { get; set; }
        [JsonProperty("WaterAbundance")] public float? WaterAbundance { get; set; }
        [JsonProperty("SurfaceTemp")]      public float? SurfaceTemp      { get; set; }
        [JsonProperty("SurfaceLiquid")]    public float? SurfaceLiquid    { get; set; }
        [JsonProperty("SurfaceMountains")] public float? SurfaceMountains { get; set; }
        [JsonProperty("TotalSurfaceArea")] public float? TotalSurfaceArea { get; set; }
        /// <summary>Оккупант этой планеты. "" явно сбрасывает наследуемое значение со звезды/сектора.</summary>
        [JsonProperty("OccupiedBy")] public string OccupiedBy { get; set; }
        /// <summary>Явная скриптовая блокировка посадки. Если null — авто (Full-оккупант → true).</summary>
        [JsonProperty("LandingBlocked")] public bool? LandingBlocked { get; set; }
    }

    public class FixedSatelliteData
    {
        [JsonProperty("Name")] public string Name { get; set; }
        [JsonProperty("CurOwner")] public string CurOwner { get; set; }
        [JsonProperty("Graphic")] public string Graphic { get; set; }
        [JsonProperty("Atmosphere")] public string Atmosphere { get; set; }
        [JsonProperty("OrbitRadius")] public float? OrbitRadius { get; set; }
        [JsonProperty("OrbitSpeed")] public float? OrbitSpeed { get; set; }
        [JsonProperty("Size")] public string Size { get; set; }
        [JsonProperty("DaySpeed")] public int? DaySpeed { get; set; }
        [JsonProperty("CloudsSpeed")] public int? CloudsSpeed { get; set; }
    }
}
