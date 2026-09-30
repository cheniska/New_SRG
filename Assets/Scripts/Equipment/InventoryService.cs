using UnityEngine;
using SRG.Combat;
using SRG.Galaxy;
using SRG.Ships;

namespace SRG.Equipment
{
    /// <summary>
    /// Фасад над <see cref="ShipData.Inventory"/>, <see cref="ShipData.Equipment"/>, <see cref="ShipData.AllItems"/>.
    /// Создан на этапе C рефакторинга: UI-контроллеры (ShipFormView и т.д.) и системы более не лазят
    /// в коллекции корабля напрямую — все мутации проходят через эти методы, которые сами заботятся о
    /// связанных побочных эффектах (rebuild shield state, invalidate weapon cache).
    ///
    /// Поведение каждого метода идентично исходным inline-сценариям из ShipFormView.cs — это чистый
    /// extract-method, без новых проверок или логики.
    /// </summary>
    public static class InventoryService
    {
        // ── Inventory ──────────────────────────────────────────────────────────

        /// <summary>Изъять предмет из инвентаря по UID. Возвращает изъятый ItemInstance либо null.</summary>
        public static ItemInstance TakeFromInventory(ShipData ship, string uid)
            => ship?.Inventory?.TakeByUid(uid);

        /// <summary>Положить предмет в инвентарь. Возвращает успех (false, если nullы).</summary>
        public static bool AddToInventory(ShipData ship, ItemInstance item)
        {
            if (ship?.Inventory == null || item == null) return false;
            ship.Inventory.Add(item);
            return true;
        }

        /// <summary>Положить «свежий» предмет в трюм: <see cref="ShipData.Inventory"/> + регистрация
        /// в <see cref="ShipData.AllItems"/>. Используется всюду, где предмет впервые попадает
        /// к кораблю (дроп, покупка, приз, выдача). Идемпотентно: повторный AllItems[uid]=item
        /// не меняет состояние. Не подходит для перемещения между слотом и трюмом одного корабля
        /// (для этого есть <see cref="EquipToSlot"/>/<see cref="UnequipFromSlot"/>).</summary>
        public static bool PutItem(ShipData ship, ItemInstance item)
        {
            if (ship?.Inventory == null || item == null) return false;
            ship.Inventory.Add(item);
            ship.AllItems[item.Uid] = item;
            return true;
        }

        /// <summary>Удалить предмет из инвентаря (по UID). Используется когда предмет переходит
        /// в «руку» UI или в контейнер; для немедленного удаления навсегда вызывайте
        /// <see cref="RemoveCompletely"/>.</summary>
        public static bool RemoveFromInventory(ShipData ship, string uid)
        {
            if (ship?.Inventory == null || string.IsNullOrEmpty(uid)) return false;
            return ship.Inventory.Remove(uid);
        }

        public static bool InventoryContains(ShipData ship, string uid)
            => ship?.Inventory != null && !string.IsNullOrEmpty(uid) && ship.Inventory.Contains(uid);

        // ── Slots ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Снимает предмет со слота. Удаляет UID из <see cref="ShipData.AllItems"/>,
        /// пересобирает Shield state и сбрасывает кэш оружейных слотов. Возвращает снятый
        /// предмет (null если слот был пуст или ship/equipment отсутствуют).
        /// </summary>
        public static ItemInstance UnequipFromSlot(ShipData ship, string slotKey)
        {
            if (ship?.Equipment == null || string.IsNullOrEmpty(slotKey)) return null;

            string uid = ship.Equipment.Clear(slotKey);
            if (uid == null) return null;
            if (!ship.AllItems.TryGetValue(uid, out var item)) return null;

            ship.AllItems.Remove(uid);
            if (item.Category == EquipmentCategory.Shield)
                WeaponSystem.RebuildShieldState(ship);
            ship.InvalidateWeaponSlotsCache();
            return item;
        }

        /// <summary>
        /// Ставит предмет в слот. Регистрирует его в <see cref="ShipData.AllItems"/>, пересобирает
        /// Shield state (если категория предмета или слота — Shield) и сбрасывает кэш оружейных слотов.
        /// Возвращает предмет, который раньше стоял в слоте (его выводит UI/caller — например, в руку,
        /// в инвентарь или удаляет). Если слота нет — возвращает null и ничего не делает.
        /// </summary>
        public static ItemInstance EquipToSlot(ShipData ship, string slotKey, ItemInstance item)
        {
            if (ship?.Equipment == null || string.IsNullOrEmpty(slotKey) || item == null) return null;
            if (!ship.Equipment.Slots.ContainsKey(slotKey)) return null;

            var displaced = EquipmentSystem.GetEquipped(ship, slotKey);
            ship.Equipment.Set(slotKey, item.Uid);
            ship.AllItems[item.Uid] = item;

            if (displaced != null && displaced.Uid != item.Uid)
                ship.AllItems.Remove(displaced.Uid);

            string slotCategory = EquipmentSystem.SlotCategory(slotKey);
            if (item.Category == EquipmentCategory.Shield || slotCategory == EquipmentCategory.Shield)
                WeaponSystem.RebuildShieldState(ship);
            ship.InvalidateWeaponSlotsCache();
            return displaced;
        }

        /// <summary>
        /// Полностью убрать предмет из корабля (из инвентаря И из реестра AllItems). Используется
        /// когда предмет уходит «в космос» через ContainerFactory.SpawnContainerWithItem.
        /// </summary>
        public static void RemoveCompletely(ShipData ship, ItemInstance item)
        {
            if (ship == null || item == null) return;
            ship.AllItems?.Remove(item.Uid);
            ship.Inventory?.Remove(item.Uid);
        }

        /// <summary>
        /// Перенос предмета между кораблями (например, абордажная добыча). Перемещает предмет
        /// из инвентаря источника в инвентарь получателя (NB: не работает со слотами оборудования —
        /// для них caller должен сначала вызвать <see cref="UnequipFromSlot"/>).
        /// </summary>
        public static bool TransferInventoryItem(ShipData from, ShipData to, string itemUid)
        {
            if (from == null || to == null || string.IsNullOrEmpty(itemUid)) return false;
            var item = from.Inventory?.TakeByUid(itemUid);
            if (item == null) return false;
            to.Inventory.Add(item);
            return true;
        }
    }
}
