using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System;
using SRG.Combat;
using SRG.Config;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy.Generation;
using SRG.Galaxy.Politics;
using SRG.Galaxy.Simulation;
using SRG.NpcAI;
using SRG.NpcAI.Actions;
using SRG.NpcAI.Spawning;
using SRG.Science;
using SRG.Ships;
using SRG.Ships.Movement;
using SRG.Ships.Services;
using SRG.Simulation;

namespace SRG.Galaxy
{
    public class PlanetData : IGalaxyEntity, ILandingSite
    {
        LandingSiteKind ILandingSite.Kind => LandingSiteKind.Planet;
        // Позиция и радиус посадки — единый геометрический контракт ILandingSite. Делегируем
        // в существующие утилиты, чтобы формулы (BaseScale, LandingZoneMargin) оставались
        // в одном месте (PlanetGeometry / OrbitMath).
        Vector2 ILandingSite.CenterPosition => SRG.Utils.OrbitMath.GetPlanetWorldPosition(this);
        float ILandingSite.LandingRadius => SRG.Utils.PlanetGeometry.GetLandingRadius(this);

        public string Uid { get; set; } = GameRng.NewUid();
        public string Name { get; set; }
        public string Size { get; set; }
        public int OrbitIndex { get; set; }
        public float OrbitRadius { get; set; }
        public float OrbitSpeed { get; set; }
        public float InitialAngle { get; set; }
        public float OrbitEccentricity;
        public float OrbitTiltDeg;
        public string Race { get; set; }
        public string Owner { get; set; }
        public string Type { get; set; }
        public string CurrentColor { get; set; }
        public string Graphic { get; set; }
        public string MaskGraphic { get; set; }
        public string Atmosphere { get; set; }
        public string OrbitalObjects { get; set; }
        public int SatellitesCount { get; set; }
        public int DaySpeed { get; set; }
        public int CloudsSpeed { get; set; }
        public float AxialTilt { get; set; } = 0f;
        public float CurrentAngle { get; set; }
        public float PreviousAngle { get; set; }
        /// <summary>Обитаемая инфраструктура планеты (правительство/экономика/магазины/наука/оккупация).
        /// Общая сущность с станциями — см. <see cref="SettlementData"/>.</summary>
        public SettlementData Settlement { get; set; } = new();
        public float SurfaceGravity { get; set; }
        public float Density { get; set; }

        // Физические и экологические характеристики
        public float GeoActivity { get; set; }
        public float SolarFlux { get; set; }
        public float MagneticField { get; set; }
        public float SurfaceRadiation { get; set; }
        public float AtmPressure { get; set; }
        [JsonIgnore] public float? AtmPressureFixed { get; set; }
        public string AtmComposition { get; set; }
        public float OxygenPercent { get; set; }
        [JsonIgnore] public float? OxygenPercentFixed { get; set; }
        [JsonIgnore] public bool HasFixedOrbitRadius;
        public float SurfaceTemp { get; set; }
        [JsonIgnore] public float? SurfaceTempFixed { get; set; }
        public float WaterAbundance { get; set; }
        [JsonIgnore] public float? WaterAbundanceFixed { get; set; }
        public int HydrologyState { get; set; }

        // Типы поверхности (% от общей площади, сумма = 100)
        public float SurfaceLiquid { get; set; }
        public float SurfacePlains { get; set; }
        public float SurfaceMountains { get; set; }
        // Общая площадь поверхности, условные млн км²
        public float TotalSurfaceArea { get; set; }

        [JsonIgnore] public float AreaLiquid    => TotalSurfaceArea * SurfaceLiquid    / 100f;
        [JsonIgnore] public float AreaPlains    => TotalSurfaceArea * SurfacePlains    / 100f;
        [JsonIgnore] public float AreaMountains => TotalSurfaceArea * SurfaceMountains / 100f;

        [JsonIgnore] public float? SurfaceLiquidFixed    { get; set; }
        [JsonIgnore] public float? SurfaceMountainsFixed { get; set; }

        // Планета в обитаемом диапазоне расы, но требует терраформирования для колонизации
        public bool IsTerraformable { get; set; }
        // Параметры планеты были приведены к оптимальным значениям расы-колонизатора
        public bool IsTerraformed { get; set; }
        // Исходные значения параметров до терраформирования (ключ — имя свойства)
        public Dictionary<string, float> TerraformOriginals { get; set; } = new();

        public Dictionary<string, string> CustomProperties { get; private set; } = new();
        public List<SatelliteData> Satellites { get; set; } = new();

        public string MinimapIconPath { get; set; }

        /// <summary>Является ли планета «столицей» своей расы (мир-родина). Ставится генератором
        /// или сценарием; используется в приветствиях планет (CurPlanetIsHomeworld).</summary>
        public bool IsHomeworld { get; set; }

        /// <summary>Является ли планета источником активного задания (квестовая).
        /// Ставится системой квестов; используется в приветствиях планет (QuestGiver).</summary>
        public bool IsQuestGiver { get; set; }

        [JsonIgnore] public StarData ParentStar { get; set; }

        private const float NearZero = 0.001f;
        public void PlanetNextDay()
        {
            PreviousAngle = CurrentAngle;
            CurrentAngle = AdvanceAngle(CurrentAngle, OrbitSpeed);

            foreach (var sat in Satellites)
            {
                sat.CurrentAngle = AdvanceAngle(sat.CurrentAngle, sat.OrbitSpeed);
                if (sat.DaySpeed > 0) sat.CurrentRotationAngle += 360f / sat.DaySpeed;
            }
        }

        public void InitRotation() { CurrentAngle = PreviousAngle = InitialAngle; }

        string IGalaxyEntity.Owner => Owner ?? GalaxyConstants.OWNER_NONE_KEY;
        string IGalaxyEntity.Race  => Race  ?? GalaxyConstants.RACE_NONE_KEY;

        private static float AdvanceAngle(float current, float speed)
        {
            if (Mathf.Abs(speed) < NearZero) return current;
            float next = current + 360f / speed;
            return Mathf.Repeat(next, 360f);
        }
    }
}
