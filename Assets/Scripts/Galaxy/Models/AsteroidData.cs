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
    [Serializable] public class AsteroidData
    {
        public string Uid { get; set; } = GameRng.NewUid();
        public string TypeId { get; set; }
        public Vector2 Position { get; set; }
        public Vector2 PreviousPosition { get; set; }
        public Vector2 Velocity { get; set; }
        public float Mass { get; set; }
        public float CollisionRadius { get; set; }
        public string GraphicPath { get; set; }
        public string ExplosionPath { get; set; }
        public float SelfRotationSpeed { get; set; }
        public float AnimFps { get; set; } = 12f;

        [JsonIgnore] public bool IsDestroyed { get; set; }
        [JsonIgnore] public AsteroidSubTurnFrames SubTurnFrames = new AsteroidSubTurnFrames();

        /// <summary>UID планеты, к которой астероид приближается на «опасное» расстояние в текущем
        /// ходу (пересечение траектории с зоной поражения радиусом ~<see cref="AsteroidSystem"/>
        /// DangerCloseRadius). Заполняется при спавне и каждый ход в TickAsteroids. null = не угрожает.
        /// Используется для награды/новостей: астероид считается «сбитым по заказу» планеты, если
        /// в момент уничтожения это поле не null.</summary>
        [JsonIgnore] public string PredictedDangerPlanetUid;
    }
}
