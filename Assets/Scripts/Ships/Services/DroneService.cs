using UnityEngine;
using Random = UnityEngine.Random;
using Newtonsoft.Json;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.NpcAI;
using SRG.NpcAI.Actions;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.Ships.Services
{
    /// <summary>
    /// Развёртывание/упаковка дрона. Дрон живёт в двух формах:
    ///  (a) активный корабль в <see cref="StarData.Ships"/>, ведомый партнёрской логикой (PartnerLeaderUid);
    ///  (b) предмет-контейнер в инвентаре хозяина, у которого в <c>ParamStrings[PACKED_KEY]</c>
    ///      лежит сериализованный <see cref="ShipData"/> с полными характеристиками.
    ///
    /// Развёртывание — <see cref="Deploy"/>: item → живой корабль в текущей звезде хозяина.
    /// Возврат — <see cref="Return"/>: живой корабль стыкуется рядом с хозяином, груз и топливо
    /// переливаются, ShipData сериализуется обратно в item, корабль удаляется из звезды.
    /// </summary>
    public static class DroneService
    {
        public const string PACKED_KEY = "PackedShipJson";
        /// <summary>Дистанция, ниже которой возвращающийся дрон считается пристыкованным.</summary>
        private const float DockDistance = 0.6f;
        /// <summary>Отступ спавна дрона от корабля хозяина при Deploy.</summary>
        private const float SpawnOffset = 1.2f;

        /// <summary>Настройки Newtonsoft для сериализации ShipData. Идентичны GalaxySaveManager —
        /// без них Vector2.normalized/magnitude вызывает self-referencing loop.</summary>
        internal static readonly JsonSerializerSettings ShipJsonSettings = new()
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            TypeNameHandling = TypeNameHandling.Auto,
            SerializationBinder = new SRG.Utils.LegacyNamespaceBinder(),
        };

        /// <summary>Разворачивает упакованный дрон-предмет в корабль. Возвращает spawned ShipData или null.</summary>
        public static ShipData Deploy(ShipData host, ItemInstance packedItem)
        {
            if (host == null || packedItem == null) return null;
            if (packedItem.Category != EquipmentCategory.CompanionDrone) return null;
            if (host.CurrentStar == null) return null;

            if (!packedItem.ParamStrings.TryGetValue(PACKED_KEY, out var json) || string.IsNullOrEmpty(json))
            {
                Debug.LogError($"[DroneService] Пакет {packedItem.Uid} не содержит PackedShipJson.");
                return null;
            }

            ShipData drone;
            try { drone = JsonConvert.DeserializeObject<ShipData>(json, ShipJsonSettings); }
            catch (System.Exception e)
            {
                Debug.LogError($"[DroneService] Не удалось распаковать дрон: {e.Message}");
                return null;
            }
            if (drone == null) return null;

            // Позиция рядом с хозяином на окружности радиуса SpawnOffset.
            drone.Position = host.Position + Positions.RandomOnCircle(SpawnOffset);
            drone.TargetPosition = drone.Position;
            drone.CurrentStarUid = host.CurrentStarUid;
            drone.CurrentStar = host.CurrentStar;
            drone.Owner = host.Owner;
            drone.PartnerLeaderUid = host.Uid;
            drone.PartnerContractEndTurn = -1;
            drone.PartnerHiredOnTurn = GameWorld.GeneratedGalaxy?.CurrentTurn ?? 0;
            // Дефолтный приказ для дрона — «за мной». Явный приказ отображается в HUD
            // и не даёт дрону, попавшему в другую звезду, застрять в None-логике.
            drone.PartnerOrder = PartnerOrderKind.FlyToMe;
            drone.PartnerOrderTargetUid = null;
            drone.DroneReturnPending = false;
            drone.LandedPlanetUid = null;
            drone.LandingPlanetUid = null;

            host.CurrentStar.Ships.Add(drone);
            host.PartnerFollowerUids ??= new System.Collections.Generic.List<string>();
            if (!host.PartnerFollowerUids.Contains(drone.Uid))
                host.PartnerFollowerUids.Add(drone.Uid);

            host.Inventory?.Remove(packedItem.Uid);
            // Создаём GameObject/визуал/NpcController — иначе дрон не будет тикать AI и не отреагирует
            // на приказы. Копирует Brain.Personality в ship.Personality.
            GameWorld.View?.EnsureShipVisual(drone);

            Debug.Log($"[DroneService] Deploy {SpriteUtility.ShortId(drone.Uid)} у {SpriteUtility.ShortId(host.Uid)}");
            return drone;
        }

        /// <summary>Начинает возврат: дрон получает приказ <see cref="PartnerOrderKind.FlyToMe"/>
        /// на хозяина и как только дистанция ≤ <see cref="DockDistance"/>, вызывается <see cref="Pack"/>.
        /// Обычно дёргается диалоговым action-ом «Return».</summary>
        public static void RequestReturn(ShipData drone)
        {
            if (drone == null || string.IsNullOrEmpty(drone.PartnerLeaderUid)) return;
            drone.PartnerOrder = PartnerOrderKind.FlyToMe;
            drone.PartnerOrderTargetUid = null;
            // Флаг «стыкуется» — ActionPartnerAttend проверяет дистанцию раз в ход и вызывает Pack.
            drone.DroneReturnPending = true;
        }

        /// <summary>Проверяет, готов ли дрон к упаковке (дистанция до хозяина ≤ порога).
        /// Вызывается из DroneAutoServiceSystem каждый ход. При успехе — упаковывает и возвращает true.</summary>
        public static bool TryFinishReturn(ShipData drone)
        {
            if (drone == null || !drone.DroneReturnPending) return false;

            ShipData host = PartnerService.FindShipInGalaxy(drone.PartnerLeaderUid);
            if (host == null || host.CurrentHull <= 0) return false;
            if (host.CurrentStarUid != drone.CurrentStarUid) return false;
            if ((drone.Position - host.Position).sqrMagnitude > DockDistance * DockDistance) return false;

            Pack(drone, host);
            return true;
        }

        /// <summary>Немедленная упаковка: перелить груз, восстановить топливо, сериализовать в предмет,
        /// удалить корабль из звезды, положить предмет в инвентарь хозяина.</summary>
        public static void Pack(ShipData drone, ShipData host)
        {
            if (drone == null || host == null) return;
            var star = drone.CurrentStar;

            // 1. Груз → хозяин. Простая переливка стеков; вес хозяина не проверяем (будущая доработка).
            if (drone.Inventory?.Stacks != null && host.Inventory != null)
            {
                foreach (var kv in drone.Inventory.Stacks)
                    host.Inventory.AddStack(new ItemStack
                    {
                        ItemId = kv.Key,
                        Category = kv.Value.Category,
                        Name = kv.Value.Name,
                        TotalWeight = kv.Value.TotalWeight,
                        BasePrice = kv.Value.BasePrice,
                        IsGoods = kv.Value.IsGoods
                    });
                drone.Inventory.Stacks.Clear();
            }

            // 2. Топливо восстановлено (fuel = 100% в бак).
            var fuelTank = EquipmentSystem.GetEquipped(drone, SlotKeys.FuelTank);
            if (fuelTank != null) fuelTank.CurrentFuel = (int)fuelTank.GetParam("Capacity", fuelTank.CurrentFuel);

            // 3. Освобождаем ссылки на партнёрство (текущее состояние летящего корабля).
            drone.PartnerOrder = PartnerOrderKind.None;
            drone.PartnerOrderTargetUid = null;
            drone.DroneReturnPending = false;

            // 4. Сериализация ShipData в JSON и запись в предмет.
            string json;
            try { json = JsonConvert.SerializeObject(drone, ShipJsonSettings); }
            catch (System.Exception e)
            {
                Debug.LogError($"[DroneService] Сериализация дрона упала: {e.Message}");
                return;
            }

            // 5. Создать item-обёртку. Пытаемся восстановить исходный ItemId из PackedDroneItemId,
            //    иначе — заводим generic-обёртку с фиксированным именем.
            var packed = new ItemInstance
            {
                Category = EquipmentCategory.CompanionDrone,
                ItemId = string.IsNullOrEmpty(drone.PackedDroneItemId) ? "Item_Drone_Packed" : drone.PackedDroneItemId,
                Name = $"Дрон «{drone.Name ?? "Компаньон"}»",
                Description = "Упакованный дрон-компаньон. Активируйте, чтобы развернуть.",
                Weight = 5,
                Price = 0,
                Activatable = true,
                Durability = 1,
                MaxDurability = 1,
            };
            packed.ParamStrings[PACKED_KEY] = json;

            host.Inventory?.Add(packed);

            // 6. Убрать живой корабль из звезды + очистить follower-список хозяина.
            star?.Ships.Remove(drone);
            host.PartnerFollowerUids?.Remove(drone.Uid);

            Debug.Log($"[DroneService] Pack {SpriteUtility.ShortId(drone.Uid)} → item {SpriteUtility.ShortId(packed.Uid)}");
        }
    }
}
