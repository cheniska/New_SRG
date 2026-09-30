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
    public enum PlanningReason
    {
        PlayerInput,
        RouteCompleted,
        LowHull,
        AsteroidImpact,
        EnemyDetected,
        QuestTrigger,
        HeavyDamage,       // за ход получено >20% MaxHull урона (в режиме автобоя)
        EnemyDestroyed,    // цель следования уничтожена
    }
}
