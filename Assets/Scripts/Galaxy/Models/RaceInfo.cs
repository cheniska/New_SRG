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
    public class RaceInfo
    {
        public string Name { get; private set; }
        public string Color { get; private set; }
        public string EmblemPath { get; private set; }

        public RaceInfo(string name, string color, string emblemPath)
        {
            Name = name;
            Color = color?.ToLowerInvariant() ?? GalaxyConstants.DEFAULT_COLOR;
            EmblemPath = emblemPath;
        }
    }
}
