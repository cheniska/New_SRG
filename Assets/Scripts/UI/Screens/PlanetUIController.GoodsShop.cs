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
        // ── Goods Shop ─────────────────────────────────────────────
        // Логика — GoodsShopPresenter (SRG.UI.Logic); здесь только отрисовка и лог.

        private GoodsShopPresenter GoodsShop()
            => new GoodsShopPresenter(_site, GalaxyManager.Instance?.Context?.Config);

        private void RefreshGoodsShop()
        {
            if (_goodsContent == null || _site?.Settlement?.Shop == null) return;
            var rows = GoodsShop().BuildRows();

            ClearChildren(_goodsContent);
            var ship = PlayerShip.Instance?.ShipData;

            foreach (var r in rows)
            {
                string goodId = r.GoodId;
                var row = MakeShopRow(_goodsContent, $"GoodRow_{goodId}", 30f, 8f, new RectOffset(4, 4, 2, 2));
                MakeRowIcon(row, goodId, r.Stock);
                MakeRowLabel(row, r.Label, GOODS_COL_NAME, flexible: true);
                MakeRowLabel(row, $"{r.BuyPrice} кр.", GOODS_COL_PRICE);
                MakeRowLabel(row, $"{r.SellPrice} кр.", GOODS_COL_PRICE);
                MakeRowLabel(row, $"{r.Stock}", GOODS_COL_STOCK);

                if (ship != null)
                {
                    MakeSmallButton(row, "Купить",  ColGreen, () => OpenBuyDialog(goodId));
                    MakeSmallButton(row, "Продать", ColRed,   () => OpenSellDialog(goodId));
                }
            }
        }

        /// <summary>Диалог количества для покупки: ползунок 1..min(сток, на сколько хватает денег),
        /// счётчик показывает суммарную стоимость по текущей цене.</summary>
        private void OpenBuyDialog(string goodId)
        {
            var prompt = GoodsShop().PrepareBuy(PlayerShip.Instance?.ShipData, goodId, out string error);
            ShowQuantityPrompt(prompt, error, amount => BuyGood(goodId, amount));
        }

        /// <summary>Диалог количества для продажи: ползунок 1..вес стека в трюме,
        /// счётчик показывает суммарную выручку по текущей цене.</summary>
        private void OpenSellDialog(string goodId)
        {
            var prompt = GoodsShop().PrepareSell(PlayerShip.Instance?.ShipData, goodId, out string error);
            ShowQuantityPrompt(prompt, error, amount => SellGood(goodId, amount));
        }

        private void ShowQuantityPrompt(QuantityPrompt prompt, string error, System.Action<int> onConfirm)
        {
            if (error != null) GameLog.Add(error);
            if (prompt == null) return;
            _amountDialog.Show(prompt.Title, prompt.Max, onConfirm,
                               confirmLabel: prompt.ConfirmLabel,
                               amountFormatter: prompt.FormatAmount);
        }

        private void BuyGood(string goodId, int amount)
            => ApplyShopResult(GoodsShop().Buy(PlayerShip.Instance?.ShipData, goodId, amount), RefreshGoodsShop);

        private void SellGood(string goodId, int amount)
            => ApplyShopResult(GoodsShop().Sell(PlayerShip.Instance?.ShipData, goodId, amount), RefreshGoodsShop);

        /// <summary>Сообщения действия — в лог; при успехе обновить деньги, экран и ангар.</summary>
        private void ApplyShopResult(UiActionResult result, System.Action refreshScreen)
        {
            foreach (var m in result.Messages) GameLog.Add(m);
            if (!result.Success) return;
            UpdateMoneyDisplay();
            refreshScreen();
            RefreshHangar();
        }
    }
}
