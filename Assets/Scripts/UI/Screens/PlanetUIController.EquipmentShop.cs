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
using SRG.UI.Logic;

namespace SRG.UI.Screens
{
    public partial class PlanetUIController
    {
        // ── Equipment Shop ─────────────────────────────────────────
        // Логика — EquipmentShopPresenter (SRG.UI.Logic); здесь только отрисовка.

        private void RefreshEquipShop()
        {
            if (_equipContent == null || _site?.Settlement?.EquipmentShop == null) return;
            ClearChildren(_equipContent);

            var presenter = new EquipmentShopPresenter(_site);
            var ship = PlayerShip.Instance?.ShipData;

            foreach (var r in presenter.BuildBuyRows())
            {
                var row = MakeShopRow(_equipContent, $"EquipRow_{r.ItemUid}", 28f, 4f, new RectOffset(2, 2, 1, 1));
                MakeRowLabel(row, r.Label, EQUIP_COL_NAME, flexible: true);
                MakeRowLabel(row, r.TechLevelLabel, EQUIP_COL_TU);
                MakeRowLabel(row, $"{r.Price} кр.", EQUIP_COL_PRICE);
                if (ship != null)
                {
                    string uid = r.ItemUid;
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

                foreach (var r in presenter.BuildSellRows(ship))
                {
                    var row2 = MakeShopRow(_equipContent, $"SellRow_{r.ItemUid}", 26f, 4f, new RectOffset(2, 2, 1, 1));
                    MakeRowLabel(row2, r.Label, EQUIP_COL_NAME, flexible: true);
                    MakeRowLabel(row2, r.TechLevelLabel, EQUIP_COL_TU); // пустая колонка ТУ — для выравнивания
                    MakeRowLabel(row2, $"{r.Price} кр.", EQUIP_COL_PRICE);
                    string uid = r.ItemUid;
                    MakeSmallButton(row2, "Продать", ColRed, () => SellEquipment(uid));
                }
            }
        }

        private void BuyEquipment(string itemUid)
            => ApplyShopResult(new EquipmentShopPresenter(_site).Buy(PlayerShip.Instance?.ShipData, itemUid), RefreshEquipShop);

        private void SellEquipment(string itemUid)
            => ApplyShopResult(new EquipmentShopPresenter(_site).Sell(PlayerShip.Instance?.ShipData, itemUid), RefreshEquipShop);
    }
}
