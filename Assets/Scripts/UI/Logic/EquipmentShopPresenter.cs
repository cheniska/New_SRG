using System.Collections.Generic;
using UnityEngine;
using SRG.Equipment;
using SRG.Galaxy;

namespace SRG.UI.Logic
{
    /// <summary>Строка магазина оборудования (покупка у планеты или продажа из трюма).</summary>
    public sealed class EquipmentRowModel
    {
        public string ItemUid;
        /// <summary>«[Категория] Название».</summary>
        public string Label;
        /// <summary>«ТУn» для покупки; пусто для продажи.</summary>
        public string TechLevelLabel;
        public int Price;
    }

    /// <summary>
    /// Логика экрана магазина оборудования (без UI). Продажа — за половину цены, продаются
    /// только неустановленные предметы.
    /// </summary>
    public sealed class EquipmentShopPresenter
    {
        public const float SellPriceFactor = 0.5f;

        private readonly ILandingSite _site;

        public EquipmentShopPresenter(ILandingSite site) => _site = site;

        public static int SellPrice(ItemInstance item) => Mathf.RoundToInt(item.Price * SellPriceFactor);

        public List<EquipmentRowModel> BuildBuyRows()
        {
            var rows = new List<EquipmentRowModel>();
            var items = _site?.Settlement?.EquipmentShop?.Items;
            if (items == null) return rows;
            foreach (var kv in items)
            {
                var item = kv.Value;
                rows.Add(new EquipmentRowModel
                {
                    ItemUid = item.Uid,
                    Label = $"[{item.Category}] {item.Name}",
                    TechLevelLabel = $"ТУ{item.TechLevel}",
                    Price = item.Price,
                });
            }
            return rows;
        }

        /// <summary>Неустановленные предметы корабля, которые можно продать.</summary>
        public List<EquipmentRowModel> BuildSellRows(ShipData ship)
        {
            var rows = new List<EquipmentRowModel>();
            if (ship?.AllItems == null) return rows;
            foreach (var kv in ship.AllItems)
            {
                var item = kv.Value;
                if (IsEquipped(ship, item.Uid)) continue;
                rows.Add(new EquipmentRowModel
                {
                    ItemUid = item.Uid,
                    Label = $"[{item.Category}] {item.Name}",
                    TechLevelLabel = "",
                    Price = SellPrice(item),
                });
            }
            return rows;
        }

        public UiActionResult Buy(ShipData ship, string itemUid)
        {
            var items = _site?.Settlement?.EquipmentShop?.Items;
            if (ship == null || items == null || !items.TryGetValue(itemUid, out var item)) return UiActionResult.None();
            if (ship.Money < item.Price) return UiActionResult.Fail("[Магазин] Недостаточно кредитов.");

            ship.Money -= item.Price;
            InventoryService.PutItem(ship, item);
            items.Remove(itemUid);
            return UiActionResult.Ok($"[Магазин] Куплено оборудование: {item.Name} за {item.Price} кр.");
        }

        public UiActionResult Sell(ShipData ship, string itemUid)
        {
            if (ship == null || !ship.AllItems.TryGetValue(itemUid, out var item)) return UiActionResult.None();
            // Установленный предмет продать нельзя (сначала снять) — раньше это проверялось только
            // при построении списка.
            if (IsEquipped(ship, itemUid)) return UiActionResult.Fail("[Магазин] Сначала снимите предмет.");

            int price = SellPrice(item);
            ship.AllItems.Remove(itemUid);
            ship.Inventory.Remove(itemUid);
            ship.Money += price;
            _site.Settlement.EquipmentShop.Items[item.Uid] = item;
            return UiActionResult.Ok($"[Магазин] Продано: {item.Name} за {price} кр.");
        }

        private static bool IsEquipped(ShipData ship, string uid)
        {
            if (ship.Equipment?.Slots == null) return false;
            foreach (var s in ship.Equipment.Slots.Values)
                if (s == uid) return true;
            return false;
        }
    }
}
