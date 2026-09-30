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
    public class SatelliteData : IGalaxyEntity
    {
        public string Uid { get; set; } = GameRng.NewUid();
        public string Name { get; set; }
        public string Size { get; set; } = "Mid";

        public float OrbitRadius { get; set; }
        public float OrbitSpeed { get; set; }
        public float CurrentAngle { get; set; }
        public float OrbitInclination { get; set; }

        public string Graphic { get; set; }
        public string Atmosphere { get; set; }

        public int DaySpeed { get; set; }
        public int CloudsSpeed { get; set; }
        public float CurrentRotationAngle { get; set; }

        public string Race { get; set; }
        public string Owner { get; set; }
        public Dictionary<string, string> CustomProperties { get; private set; } = new();

        string IGalaxyEntity.Owner => Owner ?? GalaxyConstants.OWNER_NONE_KEY;
        string IGalaxyEntity.Race  => Race  ?? GalaxyConstants.RACE_NONE_KEY;
    }
}
