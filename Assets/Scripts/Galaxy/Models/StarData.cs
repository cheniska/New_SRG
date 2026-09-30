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
    public class StarData : IGalaxyEntity
    {
        public string Uid { get; set; } = GameRng.NewUid();
        public string Name { get; set; }
        public string Type { get; set; }
        public string Color { get; set; }
        public Vector2 Position { get; set; }
        public string Owner { get; set; }
        public string Race { get; set; }
        public int GraphVar { get; set; }

        /// <summary>
        /// Доминирующий владелец звезды. Вычисляется при генерации, не сохраняется в JSON.
        /// Mixed если планеты принадлежат разным владельцам.
        /// </summary>
        [JsonIgnore] public string ResolvedOwnerId { get; set; }

        /// <summary>
        /// Кэш «эффективного контроллёра системы» — если все населённые планеты управляются одним владельцем,
        /// хранится он; иначе null. Пересчитывается <see cref="OccupationService.RecomputeSystemControl"/>
        /// при изменении оккупации любой планеты системы. Слушать переходы —
        /// через <see cref="OccupationService.OnSystemControlChanged"/>.
        /// </summary>
        [JsonIgnore] public string CurrentSystemController { get; set; }

        public int SystemSize { get; set; }
        public int BackgroundSeed { get; set; }
        public string MapIcon { get; set; }
        public string PlanetsNoneCount { get; set; }
        public float StarRadius { get; set; }

        public float MassSolar { get; set; } = 1f;
        public float RadiationMult { get; set; } = 1f;
        public float HabitableZoneMin { get; set; }
        public float HabitableZoneMax { get; set; }
        public float HabitableZoneMid { get; set; }
        public float FlareRisk { get; set; } = 0f;

        public List<PlanetData> Planets { get; set; } = new();
        public List<ShipData> Ships { get; set; } = new();
        // Графа связей между системами нет: соседство и маршруты AI считает HyperNavigation
        // по физическим координатам (бывший ConnectedStarUids никогда не заполнялся — удалён).
        public Dictionary<string, string> CustomProperties { get; private set; } = new();

        // Пользовательские препятствия для планировщика траектории — задаются вручную (по координатам).
        // Корабли в этой системе будут обходить их, как и саму звезду.
        public List<CircleObstacle> CircleObstacles { get; set; } = new();
        public List<PolygonObstacle> PolygonObstacles { get; set; } = new();

        public StarNameRenderData NameRenderData { get; set; }
        public List<AsteroidData> Asteroids { get; set; } = new();
        public int MaxAsteroids { get; set; } = -1;
        public List<ActiveMissile> ActiveMissiles { get; set; } = new();
        /// <summary>Активные червоточины в этой системе. Управляются <see cref="Simulation.WormholeSystem"/>.
        /// Пустой список у большинства систем. Ходит вместе с сейвом.</summary>
        public List<WormholeData> Wormholes { get; set; } = new();

        [JsonIgnore] public SectorData ParentSector { get; set; }
        [JsonIgnore] public bool IsPremade { get; set; }

        /// <summary>Сколько кораблей уже заспавнилось в этой звезде за текущий игровой день.
        /// Сбрасывается в 0 в начале хода (SpawnSystem.DailyTick), инкрементируется в RegisterSpawn.
        /// Гейт «не больше N спавнов в системе в день» настраивается через
        /// <see cref="Config.GameSettingsConfig.MaxShipsSpawnedPerStarPerDay"/>.</summary>
        [JsonIgnore] public int SpawnsToday;

        /// <summary>Сила кораблей одного Owner с разбивкой на боевую (не-Civilian по
        /// <see cref="NpcBrain.ResolveCombatClass"/>) и гражданскую составляющие.</summary>
        public struct OwnerPower
        {
            public float Combat;
            public float Civilian;
            public float Total => Combat + Civilian;
        }

        // Кеш суммарной «силы» (NpcBrain.CalculateStrength) кораблей по Owner + агрегаты звезды
        // (криминал, пиратская/военная сила) для реактивных директив. Пересчитывается раз в день
        // в StarNextDay; читается NpcBrain (системный flee), ГШ и DirectiveManager.
        [JsonIgnore] public Dictionary<string, OwnerPower> PowerByOwner { get; private set; } = new();
        [JsonIgnore] public int PowerCacheTurn { get; set; } = -1;
        [JsonIgnore] public float CrimeSum { get; private set; }
        [JsonIgnore] public float PiratePower { get; private set; }
        [JsonIgnore] public float MilitaryPower { get; private set; }
        /// <summary>Owner первого военного корабля звезды (не None/Mixed) — адресат авто-директив.</summary>
        [JsonIgnore] public string FirstMilitaryOwner { get; private set; }

        public void RebuildPowerCache(int currentTurn)
        {
            if (PowerCacheTurn == currentTurn) return;
            PowerByOwner.Clear();
            CrimeSum = 0f;
            PiratePower = 0f;
            MilitaryPower = 0f;
            FirstMilitaryOwner = null;
            for (int i = 0; i < Ships.Count; i++)
            {
                var ship = Ships[i];
                if (ship.CurrentHull <= 0) continue;
                string owner = ship.Owner ?? GalaxyConstants.OWNER_NONE_KEY;
                float strength = NpcBrain.CalculateStrength(ship);
                var cls = NpcBrain.ResolveCombatClass(ship.ShipTypeId);

                PowerByOwner.TryGetValue(owner, out var current);
                if (cls == CombatClass.Civilian) current.Civilian += strength;
                else current.Combat += strength;
                PowerByOwner[owner] = current;

                CrimeSum += ship.CrimeRating; // включая игрока — его криминал тоже триггерит SuppressPiracy
                if (cls == CombatClass.Pirate) PiratePower += strength;
                else if (cls == CombatClass.Military)
                {
                    MilitaryPower += strength;
                    if (FirstMilitaryOwner == null && !string.IsNullOrEmpty(ship.Owner)
                        && ship.Owner != GalaxyConstants.OWNER_NONE_KEY
                        && ship.Owner != GalaxyConstants.OWNER_MIXED_KEY)
                        FirstMilitaryOwner = ship.Owner;
                }
            }
            PowerCacheTurn = currentTurn;
        }

        /// <summary>Суммарная сила враждебных Owner'ов. combatOnly=true — только боевые корабли
        /// (Military/Pirate/Mercenary), без транспортов и прочих гражданских.</summary>
        public float GetHostilePower(string myOwner, string myRace, bool combatOnly = false)
        {
            var rel = OwnerRaceRelationsManager.Instance;
            if (rel == null) return 0f;
            float total = 0f;
            foreach (var kv in PowerByOwner)
            {
                if (kv.Key == myOwner) continue;
                if (rel.AreHostile(myOwner, kv.Key, myRace, null))
                    total += combatOnly ? kv.Value.Combat : kv.Value.Total;
            }
            return total;
        }

        /// <summary>Суммарная сила своих и не-враждебных Owner'ов. combatOnly=true — только боевые.</summary>
        public float GetFriendlyPower(string myOwner, string myRace, bool combatOnly = false)
        {
            var rel = OwnerRaceRelationsManager.Instance;
            if (rel == null)
            {
                PowerByOwner.TryGetValue(myOwner ?? "", out var ownOnly);
                return combatOnly ? ownOnly.Combat : ownOnly.Total;
            }
            float total = 0f;
            foreach (var kv in PowerByOwner)
            {
                if (kv.Key != myOwner && rel.AreHostile(myOwner, kv.Key, myRace, null)) continue;
                total += combatOnly ? kv.Value.Combat : kv.Value.Total;
            }
            return total;
        }

        /// <summary>Единый поиск корабля звезды по UID (вместо локальных копий в NpcAction/NpcOrder/NpcBrain).</summary>
        public ShipData FindShip(string uid, bool aliveOnly = false)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            for (int i = 0; i < Ships.Count; i++)
            {
                var s = Ships[i];
                if (s.Uid != uid) continue;
                if (aliveOnly && s.CurrentHull <= 0) return null;
                return s;
            }
            return null;
        }

        /// <summary>Планета этой звезды по UID; null если нет.</summary>
        public PlanetData FindPlanet(string uid)
        {
            if (string.IsNullOrEmpty(uid) || Planets == null) return null;
            for (int i = 0; i < Planets.Count; i++)
                if (Planets[i].Uid == uid) return Planets[i];
            return null;
        }

        // Симуляция дневного хода вынесена в StarSimulator (этап T1 рефакторинга, июнь 2026):
        // StarData теперь содержит только данные и связанные с ними утилиты (Power-кэш для NpcBrain).
        public void StarNextDay(TurnAnimationData anim, GalaxyGenerationContext ctx = null)
            => StarSimulator.SimulateTurn(this, anim, ctx);
    }
}
