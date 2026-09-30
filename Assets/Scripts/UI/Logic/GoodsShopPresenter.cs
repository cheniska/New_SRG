using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Economy;
using SRG.Galaxy;

namespace SRG.UI.Logic
{
    /// <summary>Строка магазина товаров.</summary>
    public sealed class GoodsRowModel
    {
        public string GoodId;
        public string DisplayName;
        /// <summary>Товар запрещён расой/правительством посадочной цели (контрабанда).</summary>
        public bool IsIllegal;
        public int BuyPrice;
        public int SellPrice;
        public int Stock;
        /// <summary>Подпись с разметкой: контрабанда — красным и со звёздочкой.</summary>
        public string Label => IsIllegal ? $"<color=#E05050>{DisplayName}*</color>" : DisplayName;
    }

    /// <summary>
    /// Логика экрана магазина товаров (без UI): строки, лимиты диалогов, покупка/продажа.
    /// Экран (<c>PlanetUIController</c>) только рисует строки и пишет сообщения в лог.
    /// </summary>
    public sealed class GoodsShopPresenter
    {
        private readonly ILandingSite _site;
        private readonly GalaxyConfig _cfg;

        public GoodsShopPresenter(ILandingSite site, GalaxyConfig cfg)
        {
            _site = site;
            _cfg = cfg;
        }

        /// <summary>Строки магазина с актуальными ценами. Пустой магазин старого сейва
        /// инициализируется, цены пересчитываются по текущему стоку.</summary>
        public List<GoodsRowModel> BuildRows()
        {
            var rows = new List<GoodsRowModel>();
            var shop = _site?.Settlement?.Shop;
            if (shop == null) return rows;

            if (shop.Goods.Count == 0 && _cfg?.Goods != null)
                TradeSystem.InitPlanetShop(_site, _cfg);
            if (_cfg != null)
                TradeSystem.RecalculatePrices(_site, _cfg);

            foreach (var kv in shop.Goods)
            {
                rows.Add(new GoodsRowModel
                {
                    GoodId = kv.Key,
                    DisplayName = TradeSystem.GetDisplayName(kv.Key, _cfg),
                    IsIllegal = _cfg != null && !TradeSystem.IsLegal(_site, kv.Key, _cfg),
                    BuyPrice = kv.Value.BuyPrice,
                    SellPrice = kv.Value.SellPrice,
                    Stock = kv.Value.Stock,
                });
            }
            return rows;
        }

        /// <summary>Диалог покупки: максимум = min(сток, на сколько хватает денег).
        /// null и сообщение — если покупать нечего/не на что.</summary>
        public QuantityPrompt PrepareBuy(ShipData ship, string goodId, out string error)
        {
            error = null;
            var entry = ShopService.GetEntry(_site, goodId);
            if (ship == null || entry == null) return null;

            if (entry.Stock <= 0) { error = "[Магазин] Товара нет в наличии."; return null; }
            int affordable = entry.BuyPrice > 0 ? ship.Money / entry.BuyPrice : entry.Stock;
            int max = Mathf.Min(entry.Stock, affordable);
            if (max <= 0) { error = "[Магазин] Недостаточно кредитов."; return null; }

            return new QuantityPrompt($"Купить: {TradeSystem.GetDisplayName(goodId, _cfg)}", max, entry.BuyPrice, "Купить");
        }

        /// <summary>Диалог продажи: максимум = вес стека в трюме.</summary>
        public QuantityPrompt PrepareSell(ShipData ship, string goodId, out string error)
        {
            error = null;
            if (ship == null) return null;
            if (!ship.Inventory.Stacks.TryGetValue(goodId, out var stack) || stack.TotalWeight <= 0)
            {
                error = "[Магазин] Нет такого товара в трюме.";
                return null;
            }
            int price = ShopService.GetEntry(_site, goodId)?.SellPrice ?? 0;
            return new QuantityPrompt($"Продать: {TradeSystem.GetDisplayName(goodId, _cfg)}", stack.TotalWeight, price, "Продать");
        }

        public UiActionResult Buy(ShipData ship, string goodId, int amount)
        {
            var entry = ShopService.GetEntry(_site, goodId);
            if (ship == null || entry == null) return UiActionResult.None();

            var messages = new List<string>();
            if (_cfg != null && !TradeSystem.IsLegal(_site, goodId, _cfg))
            {
                int penalty = _cfg.Trade?.ContrabandRelationPenalty ?? -20;
                messages.Add($"[Магазин] {TradeSystem.GetDisplayName(goodId, _cfg)} — контрабанда. Отношения: {penalty}.");
            }

            // Стоимость до покупки — чтобы отличить «нет товара» от «нет денег» при отказе.
            int desired = Mathf.Min(amount, entry.Stock);
            int unitPrice = entry.BuyPrice;
            if (!ShopService.TryBuy(ship, _site, goodId, amount, _cfg, out int actualAmount, out int totalCost))
            {
                if (desired > 0 && ship.Money < desired * unitPrice)
                    messages.Add("[Магазин] Недостаточно кредитов.");
                return UiActionResult.Fail(messages.ToArray());
            }

            messages.Add($"[Магазин] Куплено: {TradeSystem.GetDisplayName(goodId, _cfg)} x{actualAmount} за {totalCost} кр.");
            return UiActionResult.Ok(messages.ToArray());
        }

        public UiActionResult Sell(ShipData ship, string goodId, int amount)
        {
            if (ship == null) return UiActionResult.None();
            var taken = ShopService.TrySell(ship, _site, goodId, amount, _cfg, out int income);
            if (taken == null || taken.TotalWeight == 0)
                return UiActionResult.Fail("[Магазин] Нет такого товара в трюме.");
            return UiActionResult.Ok($"[Магазин] Продано: {taken.Name} x{taken.TotalWeight} за {income} кр.");
        }
    }
}
