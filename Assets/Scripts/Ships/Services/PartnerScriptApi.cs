using UnityEngine;
using Random = UnityEngine.Random;
using System.Collections.Generic;
using Newtonsoft.Json;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.NpcAI;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.Ships.Services
{
    /// <summary>
    /// Публичный скриптовый API системы партнёрства и дронов. Используется:
    ///   * из консоли (GameConsoleController.RegisterCommands)
    ///   * из квестовых скриптов / модов
    ///   * из будущих UI-кнопок магазина дронов
    ///
    /// Ничего не проверяет из «геймплейных» условий (attitude/Aggression/PowerRatio) —
    /// эти проверки живут в <see cref="PartnerService.TryHire"/>. API force-путь: то что
    /// заведено, то и работает. Ошибки идут в консольный лог.
    /// </summary>
    public static class PartnerScriptApi
    {
        /// <summary>Дефолтный ShipTypeId дрона. Меняется если добавите Drone_Heavy/Drone_Scout.</summary>
        public const string DefaultDroneShipType = "Drone";
        /// <summary>Дефолтная раса-производитель дрона, если не передана явно (для графики Ranger Race1).</summary>
        public const string DefaultDroneRace = "Race1";

        // ── Дрон: спавн активного корабля ────────────────────────────────────────

        /// <summary>Создаёт полностью экипированный ShipData дрона, ставит рядом с хостом,
        /// делает партнёром. Возвращает spawned drone или null при ошибке.</summary>
        public static ShipData SpawnDrone(ShipData host, string manufacturerRace = null, string shipTypeId = null)
        {
            if (host == null) return null;
            var star = host.CurrentStar;
            if (star == null) { Debug.LogError("[PartnerScriptApi] У хоста нет CurrentStar."); return null; }

            var drone = BuildDroneShip(manufacturerRace, shipTypeId, host.Owner);
            if (drone == null) return null;

            drone.Position = host.Position + Positions.RandomOnCircle(1.2f);
            drone.TargetPosition = drone.Position;
            drone.CurrentStarUid = host.CurrentStarUid;
            drone.CurrentStar    = star;
            drone.PartnerLeaderUid = host.Uid;
            drone.PartnerHiredOnTurn = GameWorld.GeneratedGalaxy?.CurrentTurn ?? 0;
            drone.PartnerContractEndTurn = -1;
            drone.PartnerOrder = PartnerOrderKind.FlyToMe;
            drone.PartnerOrderTargetUid = null;

            star.Ships.Add(drone);
            host.PartnerFollowerUids ??= new List<string>();
            host.PartnerFollowerUids.Add(drone.Uid);

            // GameObject/визуал/NpcController — иначе дрон не тикает AI и не отреагирует на приказы.
            GameWorld.View?.EnsureShipVisual(drone);

            return drone;
        }

        // ── Дрон: выдача упакованного предмета ───────────────────────────────────

        /// <summary>Создаёт свёрнутый дрон в виде <see cref="ItemInstance"/> и кладёт в инвентарь получателя.
        /// Стартовое состояние — как только что произведённый (полное HP, полный бак, пустой трюм).</summary>
        public static ItemInstance GiveDronePackage(ShipData recipient, string manufacturerRace = null, string shipTypeId = null)
        {
            if (recipient == null) return null;
            if (recipient.Inventory == null) { Debug.LogError("[PartnerScriptApi] У получателя нет Inventory."); return null; }

            var drone = BuildDroneShip(manufacturerRace, shipTypeId, recipient.Owner);
            if (drone == null) return null;

            // Полный бак и HP уже выставлены в BuildDroneShip → EquipStarterKit.
            // Просто сериализуем в JSON и заворачиваем в предмет.
            string json;
            try { json = JsonConvert.SerializeObject(drone, DroneService.ShipJsonSettings); }
            catch (System.Exception e)
            {
                Debug.LogError($"[PartnerScriptApi] Serialize failed: {e.Message}");
                return null;
            }

            var packed = new ItemInstance
            {
                Category = EquipmentCategory.CompanionDrone,
                ItemId   = "Item_Drone_Packed",
                Name     = $"Дрон «{drone.Name ?? shipTypeId ?? DefaultDroneShipType}»",
                Description = "Упакованный дрон-компаньон. Активируйте, чтобы развернуть.",
                Weight   = 5,
                Price    = 0,
                Activatable = true,
                Durability = 1,
                MaxDurability = 1,
                ManufacturerRace = manufacturerRace ?? DefaultDroneRace,
            };
            packed.ParamStrings[DroneService.PACKED_KEY] = json;
            InventoryService.PutItem(recipient, packed);
            return packed;
        }

        // ── Партнёрство: принудительное ──────────────────────────────────────────

        /// <summary>Устанавливает партнёрство без проверок цены/attitude/аграссии (используется
        /// квестами или командами консоли). Возвращает true если связь установлена.
        /// Учитывает MaxPartners нанимателя — превышение не даёт установить связь.</summary>
        public static bool ForceHire(ShipData target, ShipData leader, int contractYears = -1)
        {
            if (target == null || leader == null) return false;
            if (!string.IsNullOrEmpty(target.PartnerLeaderUid))
            {
                Debug.LogWarning($"[PartnerScriptApi] {SpriteUtility.ShortId(target.Uid)} уже под контрактом.");
                return false;
            }
            int currentCount = leader.PartnerFollowerUids?.Count ?? 0;
            int max = PartnerService.MaxPartners(leader);
            if (currentCount >= max)
            {
                Debug.LogWarning($"[PartnerScriptApi] Свита {SpriteUtility.ShortId(leader.Uid)} полна ({currentCount}/{max}).");
                return false;
            }

            int currentTurn = GameWorld.GeneratedGalaxy?.CurrentTurn ?? 0;
            int turnsPerYear = GameWorld.Context?.Config?.Partners?.Global?.TurnsPerYear ?? 365;
            int endTurn = contractYears > 0 ? currentTurn + contractYears * turnsPerYear : -1;

            PartnerService.SetPartner(target, leader, currentTurn, endTurn);
            return true;
        }

        // ── Разрыв ───────────────────────────────────────────────────────────────

        /// <summary>Разрывает контракт follower→leader с указанной причиной (по умолчанию PlayerDismiss).</summary>
        public static void BreakContract(ShipData follower, BreakReason reason = BreakReason.PlayerDismiss)
        {
            if (follower == null) return;
            PartnerService.Break(follower, reason);
        }

        // ── Общий строитель дрона ────────────────────────────────────────────────

        /// <summary>Создаёт готовый ShipData дрона: тип, раса, оборудование, HP. Не привязывает
        /// к звезде и не устанавливает партнёрство.</summary>
        private static ShipData BuildDroneShip(string manufacturerRace, string shipTypeId, string owner)
        {
            var ctx = GameWorld.Context;
            if (ctx == null) { Debug.LogError("[PartnerScriptApi] Context не готов."); return null; }
            if (ctx.AvailableShipTypes == null) { Debug.LogError("[PartnerScriptApi] AvailableShipTypes пусты."); return null; }

            string type = string.IsNullOrEmpty(shipTypeId) ? DefaultDroneShipType : shipTypeId;
            if (!ctx.AvailableShipTypes.TryGetValue(type, out var typeCfg))
            {
                Debug.LogError($"[PartnerScriptApi] ShipType '{type}' не найден.");
                return null;
            }
            if (!typeCfg.IsDrone)
                Debug.LogWarning($"[PartnerScriptApi] ShipType '{type}' не помечен IsDrone. Всё равно спавним.");

            string race = string.IsNullOrEmpty(manufacturerRace) ? DefaultDroneRace : manufacturerRace;
            var ship = ShipFactory.BuildShipData(type, owner ?? string.Empty, race,
                                                 ctx.AvailableShipTypes, ctx);
            if (ship == null) return null;

            ship.Name = $"{type}_{SpriteUtility.ShortId(ship.Uid, 4)}";
            if ((typeCfg.StarterKit != null || typeCfg.StarterWeapons != null) && ctx.ItemsConfig != null)
                ItemFactory.EquipStarterKit(ship, typeCfg.StarterKit, ctx.ItemsConfig, ctx.Config, typeCfg.StarterWeapons);
            else
            {
                ship.MaxHull = 100;
                ship.CurrentHull = ship.MaxHull;
            }

            // Полный бак после сборки.
            var tank = EquipmentSystem.GetEquipped(ship, SlotKeys.FuelTank);
            if (tank != null) tank.CurrentFuel = (int)tank.GetParam("Capacity", tank.CurrentFuel);

            // ShipFactory уже поставил ship.Race = race (см. BuildShipData). Дрон использует
            // race для графики корпуса, а Owner — для отношений/цвета.
            return ship;
        }

        // ── Утилиты поиска (для консоли) ─────────────────────────────────────────

        /// <summary>Ищет ближайший к игроку корабль в текущей звезде, удовлетворяющий фильтру.
        /// Возвращает null если нет подходящих.</summary>
        public static ShipData FindNearestShip(ShipData player, System.Func<ShipData, bool> filter)
        {
            if (player?.CurrentStar == null) return null;
            ShipData best = null;
            float bestSq = float.MaxValue;
            foreach (var s in player.CurrentStar.Ships)
            {
                if (s == player || s.CurrentHull <= 0) continue;
                if (filter != null && !filter(s)) continue;
                float d = (s.Position - player.Position).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = s; }
            }
            return best;
        }
    }
}
