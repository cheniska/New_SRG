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
        // ── Goods Shop ─────────────────────────────────────────────

        private void RefreshGoodsShop()
        {
            var shop = _site?.Settlement?.Shop;
            if (_goodsContent == null || shop == null) return;

            var galaxyCfg = GalaxyManager.Instance?.Context?.Config;

            // Если магазин пустой (старый сейв), инициализируем заново
            if (shop.Goods.Count == 0 && galaxyCfg?.Goods != null)
                TradeSystem.InitPlanetShop(_site, galaxyCfg);

            // Пересчитываем цены по текущему стоку перед отображением
            if (galaxyCfg != null)
                TradeSystem.RecalculatePrices(_site, galaxyCfg);

            ClearChildren(_goodsContent);

            var ship = PlayerShip.Instance?.ShipData;

            foreach (var kv in shop.Goods)
            {
                string goodId = kv.Key;
                var entry = kv.Value;
                string goodName = TradeSystem.GetDisplayName(goodId, galaxyCfg);
                bool illegal = galaxyCfg != null && !TradeSystem.IsLegal(_site, goodId, galaxyCfg);

                var row = MakeShopRow(_goodsContent, $"GoodRow_{goodId}", 30f, 8f, new RectOffset(4, 4, 2, 2));

                MakeRowIcon(row, goodId, entry.Stock);

                string label = illegal ? $"<color=#E05050>{goodName}*</color>" : goodName;
                MakeRowLabel(row, label, GOODS_COL_NAME, flexible: true);
                MakeRowLabel(row, $"{entry.BuyPrice} кр.", GOODS_COL_PRICE);
                MakeRowLabel(row, $"{entry.SellPrice} кр.", GOODS_COL_PRICE);
                MakeRowLabel(row, $"{entry.Stock}", GOODS_COL_STOCK);

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
            var ship = PlayerShip.Instance?.ShipData;
            var galaxyCfg = GalaxyManager.Instance?.Context?.Config;
            var entry = ShopService.GetEntry(_site, goodId);
            if (ship == null || entry == null) return;

            if (entry.Stock <= 0)
            {
                GameLog.Add("[Магазин] Товара нет в наличии.");
                return;
            }
            int affordable = entry.BuyPrice > 0 ? ship.Money / entry.BuyPrice : entry.Stock;
            int max = Mathf.Min(entry.Stock, affordable);
            if (max <= 0)
            {
                GameLog.Add("[Магазин] Недостаточно кредитов.");
                return;
            }

            string name = TradeSystem.GetDisplayName(goodId, galaxyCfg);
            int price = entry.BuyPrice;
            _amountDialog.Show($"Купить: {name}", max,
                               amount => BuyGood(goodId, amount),
                               confirmLabel: "Купить",
                               amountFormatter: a => $"{a} / {max}   ({a * price} кр.)");
        }

        /// <summary>Диалог количества для продажи: ползунок 1..вес стека в трюме,
        /// счётчик показывает суммарную выручку по текущей цене.</summary>
        private void OpenSellDialog(string goodId)
        {
            var ship = PlayerShip.Instance?.ShipData;
            var galaxyCfg = GalaxyManager.Instance?.Context?.Config;
            if (ship == null) return;

            if (!ship.Inventory.Stacks.TryGetValue(goodId, out var stack) || stack.TotalWeight <= 0)
            {
                GameLog.Add("[Магазин] Нет такого товара в трюме.");
                return;
            }

            string name = TradeSystem.GetDisplayName(goodId, galaxyCfg);
            int price = ShopService.GetEntry(_site, goodId)?.SellPrice ?? 0;
            int max = stack.TotalWeight;
            _amountDialog.Show($"Продать: {name}", max,
                               amount => SellGood(goodId, amount),
                               confirmLabel: "Продать",
                               amountFormatter: a => $"{a} / {max}   ({a * price} кр.)");
        }

        private void BuyGood(string goodId, int amount)
        {
            var ship = PlayerShip.Instance?.ShipData;
            var galaxyCfg = GalaxyManager.Instance?.Context?.Config;
            var entry = ShopService.GetEntry(_site, goodId);
            if (ship == null || entry == null) return;

            // Контрабанда — штраф к отношениям и отказ если отношения плохие
            if (galaxyCfg != null && !TradeSystem.IsLegal(_site, goodId, galaxyCfg))
            {
                int penalty = galaxyCfg.Trade?.ContrabandRelationPenalty ?? -20;
                GameLog.Add(
                    $"[Магазин] {TradeSystem.GetDisplayName(goodId, galaxyCfg)} — контрабанда. Отношения: {penalty}.");
            }

            // ShopService.TryBuy сам делает: stock-, money-, RecalculatePrices, AddStack.
            if (!ShopService.TryBuy(ship, _site, goodId, amount, galaxyCfg, out int actualAmount, out int totalCost))
            {
                // Различаем «нет товара» и «нет денег» по сравнению с предусчитанной стоимостью.
                int desired = Mathf.Min(amount, entry.Stock);
                if (desired <= 0) return;
                if (ship.Money < desired * entry.BuyPrice)
                    GameLog.Add("[Магазин] Недостаточно кредитов.");
                return;
            }

            string displayName = TradeSystem.GetDisplayName(goodId, galaxyCfg);
            GameLog.Add($"[Магазин] Куплено: {displayName} x{actualAmount} за {totalCost} кр.");
            UpdateMoneyDisplay();
            RefreshGoodsShop();
            RefreshHangar();
        }

        private void SellGood(string goodId, int amount)
        {
            var ship = PlayerShip.Instance?.ShipData;
            var galaxyCfg = GalaxyManager.Instance?.Context?.Config;
            if (ship == null) return;

            var taken = ShopService.TrySell(ship, _site, goodId, amount, galaxyCfg, out int income);
            if (taken == null || taken.TotalWeight == 0)
            {
                GameLog.Add("[Магазин] Нет такого товара в трюме.");
                return;
            }

            GameLog.Add($"[Магазин] Продано: {taken.Name} x{taken.TotalWeight} за {income} кр.");
            UpdateMoneyDisplay();
            RefreshGoodsShop();
            RefreshHangar();
        }
    }
}
