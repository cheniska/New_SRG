using UnityEngine;
using SRG.Config;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Ships;

namespace SRG.Economy
{
    /// <summary>
    /// Фасад над магазином товаров планеты (<see cref="PlanetData.Shop"/>).
    /// Создан на этапе C рефакторинга: UI (<see cref="PlanetUIController"/>) и NPC-торговцы
    /// перестают править <c>Shop.Goods[id].Stock/BuyPrice/SellPrice</c> напрямую — мутации
    /// проходят через эти методы, которые сами пересчитывают цены после изменения стока.
    ///
    /// Поведение идентично исходным inline-сценариям из BuyGood/SellGood. Никакой бизнес-логики
    /// не добавлено — только инкапсуляция.
    /// </summary>
    public static class ShopService
    {
        /// <summary>Возвращает запись магазина по goodId либо null.</summary>
        public static ShopGoodEntry GetEntry(ILandingSite site, string goodId)
        {
            if (site?.Settlement?.Shop?.Goods == null || string.IsNullOrEmpty(goodId)) return null;
            return site.Settlement.Shop.Goods.TryGetValue(goodId, out var entry) ? entry : null;
        }

        /// <summary>
        /// Купить amount единиц товара у планеты. Списывает стоимость, уменьшает Stock и пересчитывает
        /// цены. Возвращает true, если покупка состоялась; false при отсутствии товара/денег/планеты.
        /// Stack добавляется в инвентарь покупателя через ItemStack с актуальной BasePrice.
        /// Не печатает сообщения в консоль — это делает UI (для разных контекстов разные тексты).
        /// </summary>
        public static bool TryBuy(
            ShipData buyer, ILandingSite site, string goodId, int amount,
            GalaxyConfig galaxyCfg, out int actualAmount, out int totalCost)
        {
            actualAmount = 0;
            totalCost = 0;
            if (buyer == null) return false;
            var entry = GetEntry(site, goodId);
            if (entry == null) return false;

            amount = Mathf.Min(amount, entry.Stock);
            if (amount <= 0) return false;

            int cost = amount * entry.BuyPrice;
            if (buyer.Money < cost) return false;

            string displayName = TradeSystem.GetDisplayName(goodId, galaxyCfg);
            buyer.Money -= cost;
            entry.Stock -= amount;
            RecalculatePrices(entry, site, goodId, galaxyCfg);

            buyer.Inventory.AddStack(ItemGrantService.BuildStack(
                goodId, CargoUtils.CargoCategory, displayName, entry.SellPrice, amount, isGoods: true));

            actualAmount = amount;
            totalCost = cost;
            return true;
        }

        /// <summary>
        /// Продать amount единиц товара планете. Изымает stack из инвентаря продавца, начисляет
        /// деньги, увеличивает Stock и пересчитывает цены. Возвращает изъятый <see cref="ItemStack"/>
        /// (для UI-сообщения) либо null, если такого товара в трюме не оказалось.
        /// </summary>
        public static ItemStack TrySell(
            ShipData seller, ILandingSite site, string goodId, int amount,
            GalaxyConfig galaxyCfg, out int incomeMoney)
        {
            incomeMoney = 0;
            if (seller == null) return null;
            var entry = GetEntry(site, goodId);
            if (entry == null) return null;

            var taken = seller.Inventory?.TakeStack(goodId, amount);
            if (taken == null || taken.TotalWeight == 0) return null;

            int income = taken.TotalWeight * entry.SellPrice;
            seller.Money += income;
            entry.Stock += taken.TotalWeight;
            RecalculatePrices(entry, site, goodId, galaxyCfg);

            incomeMoney = income;
            return taken;
        }

        /// <summary>
        /// Пересчитывает BuyPrice/SellPrice записи по текущему Stock через TradeSystem.
        /// </summary>
        public static void RecalculatePrices(ShopGoodEntry entry, ILandingSite site, string goodId, GalaxyConfig galaxyCfg)
        {
            if (entry == null || galaxyCfg == null) return;
            entry.BuyPrice  = TradeSystem.CalculateBuyPrice(site, goodId, entry.Stock, galaxyCfg);
            entry.SellPrice = TradeSystem.CalculateSellPrice(site, goodId, entry.Stock, galaxyCfg);
        }
    }
}
