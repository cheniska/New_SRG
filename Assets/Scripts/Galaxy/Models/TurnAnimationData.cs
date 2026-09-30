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
    public class TurnAnimationData
    {
        // Симуляция: позиции на каждом субходе (для коллизий, NPC, интерполяции в SystemViewManager)
        public Dictionary<string, ShipSubTurnFrames> ShipFrames = new();
        public Dictionary<string, AsteroidSubTurnFrames> AsteroidFrames = new();
        public Dictionary<string, MissileSubTurnFrames> MissileFrames = new();

        // Рендер: плотный путь вдоль кривой Безье, заполняется на каждом вейпоинте (только для визуала)
        public Dictionary<string, List<Vector2>> ShipRenderPaths = new();

        public Dictionary<string, (float From, float To)> PlanetAngles = new();
        public List<ShotEvent> Shots = new();
        public List<string> DeathUids = new();
        public List<PlanningReason> PlanningReasons = new();
        public List<AsteroidSpawnEvent> AsteroidSpawns = new();
        public List<AsteroidDestroyEvent> AsteroidDestroys = new();
        // Ракета считается «мёртвой» начиная с этого сабтёрна (попадание/возврат/таймаут).
        // Спрайт показывается во время анимации сабтёрна смерти (подлёт к цели), затем
        // полностью гасится — иначе он висел бы до конца хода в точке impact'а.
        public Dictionary<string, int> MissileDeathUids = new();
        // Подмножество MissileDeathUids: ракеты, которые исчезли «тихо», без визуального взрыва
        // (торпеда вернулась к стрелявшему и восстановила боезапас). По умолчанию (uid отсутствует
        // в этом наборе) ракета на смерти показывает анимацию взрыва.
        public HashSet<string> MissileSilentDeathUids = new();
    }

    public class AsteroidSubTurnFrames
    {
        public Vector2[] SubTurns = new Vector2[11];
    }

    public class MissileSubTurnFrames
    {
        public Vector2[] SubTurns = new Vector2[11];
    }

    public class ShipSubTurnFrames
    {
        public Vector2[] SubTurns = new Vector2[11];
    }

    public struct ShotEvent
    {
        public string AttackerUid;
        public string TargetUid;
        public string WeaponId;
        public int DamageDealt;
        public int SubTurn;
        public int ShotDuration;
        public HitPattern HitPattern;
        public DamageType DamageType;
        public WeaponVisualConfig Visual;    // палитра или путь к спрайту; null = умолчание по DamageType
        public HitEffectConfig    HitEffect; // параметры эффекта попадания; null = умолчание по паттерну
    }

    public struct AsteroidSpawnEvent
    {
        public string AsteroidUid;
    }

    public struct AsteroidDestroyEvent
    {
        public string AsteroidUid;
        public Vector2 Position;
        public List<ItemInstance> DroppedItems;
        public List<ItemStack> DroppedStacks;
        public bool HitPlayer;
        public AsteroidCollisionType CollisionType;
        public int CollisionSubTurn;
        public float CollisionT;
        public string ExplosionPath;
    }
}
