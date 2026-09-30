using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using SRG.Config;
using SRG.Core;
using SRG.Dialog;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Presentation.Common;
using SRG.Controllers;
using SRG.Ships.Services;
using SRG.UI.Common;
using SRG.UI.HUD;
using SRG.Utils;

namespace SRG.UI.Screens
{
    public partial class PlanetUIController
    {
        // ── Equipment Shop ─────────────────────────────────────────

        private void RefreshEquipShop()
        {
            if (_equipContent == null || _site?.Settlement?.EquipmentShop == null) return;
            ClearChildren(_equipContent);

            var ship = PlayerShip.Instance?.ShipData;

            foreach (var kv in _site.Settlement.EquipmentShop.Items)
            {
                var item = kv.Value;
                var row = MakeShopRow(_equipContent, $"EquipRow_{item.Uid}", 28f, 4f, new RectOffset(2, 2, 1, 1));

                MakeRowLabel(row, $"[{item.Category}] {item.Name}", EQUIP_COL_NAME, flexible: true);
                MakeRowLabel(row, $"ТУ{item.TechLevel}", EQUIP_COL_TU);
                MakeRowLabel(row, $"{item.Price} кр.", EQUIP_COL_PRICE);

                if (ship != null)
                {
                    string uid = item.Uid;
                    MakeSmallButton(row, "Купить", ColGreen, () => BuyEquipment(uid));
                }
            }

            // Продажа из инвентаря
            if (ship?.AllItems != null)
            {
                var sep = new GameObject("Sep");
                sep.transform.SetParent(_equipContent, false);
                sep.AddComponent<LayoutElement>().preferredHeight = 20f;
                var sepTxt = MakeText(sep.transform, "SepText", "── Продать из трюма ──", 11, FontStyle.Italic, ColAccent);
                SetAnchors(sepTxt.gameObject, 0f, 0f, 1f, 1f);

                foreach (var kv in ship.AllItems)
                {
                    var item = kv.Value;
                    bool equipped = false;
                    foreach (var s in ship.Equipment.Slots.Values)
                        if (s == item.Uid) { equipped = true; break; }
                    if (equipped) continue;

                    var row2 = MakeShopRow(_equipContent, $"SellRow_{item.Uid}", 26f, 4f, new RectOffset(2, 2, 1, 1));

                    int sellPrice = Mathf.RoundToInt(item.Price * 0.5f);
                    MakeRowLabel(row2, $"[{item.Category}] {item.Name}", EQUIP_COL_NAME, flexible: true);
                    MakeRowLabel(row2, "", EQUIP_COL_TU); // пустая колонка ТУ — для выравнивания с секцией покупки
                    MakeRowLabel(row2, $"{sellPrice} кр.", EQUIP_COL_PRICE);
                    string uid = item.Uid;
                    MakeSmallButton(row2, "Продать", ColRed, () => SellEquipment(uid));
                }
            }
        }

        private void BuyEquipment(string itemUid)
        {
            var ship = PlayerShip.Instance?.ShipData;
            if (ship == null || !_site.Settlement.EquipmentShop.Items.TryGetValue(itemUid, out var item)) return;

            if (ship.Money < item.Price)
            {
                GameLog.Add("[Магазин] Недостаточно кредитов.");
                return;
            }

            ship.Money -= item.Price;
            InventoryService.PutItem(ship, item);
            _site.Settlement.EquipmentShop.Items.Remove(itemUid);

            GameLog.Add($"[Магазин] Куплено оборудование: {item.Name} за {item.Price} кр.");
            UpdateMoneyDisplay();
            RefreshEquipShop();
            RefreshHangar();
        }

        private void SellEquipment(string itemUid)
        {
            var ship = PlayerShip.Instance?.ShipData;
            if (ship == null || !ship.AllItems.TryGetValue(itemUid, out var item)) return;

            int price = Mathf.RoundToInt(item.Price * 0.5f);
            ship.AllItems.Remove(itemUid);
            ship.Inventory.Remove(itemUid);
            ship.Money += price;

            _site.Settlement.EquipmentShop.Items[item.Uid] = item;

            GameLog.Add($"[Магазин] Продано: {item.Name} за {price} кр.");
            UpdateMoneyDisplay();
            RefreshEquipShop();
            RefreshHangar();
        }
    }
}
