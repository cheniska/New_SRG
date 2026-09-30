using UnityEngine;
using SRG.Config;
using SRG.Dialog;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.Simulation;

namespace SRG.Ships.Services
{
    /// <summary>Причины успеха/отказа ShipTradeService.</summary>
    public enum ShipTradeRefusalReason
    {
        Accepted = 0,
        BadRelations,   // отношение хуже Normal — не торгуют
        BigDist,        // цель дальше LongDistance — не сговоримся
        War,            // цель под атакой — не до торговли
        NoNeed,         // ни у одной стороны нет груза для сделки
        NoMoney,        // у покупателя не хватает денег
        NoSpace,        // покупатель не может унести (weight — не отслеживаем строго, зарезервировано)
        AlreadyTakeItem,// продавец летит подбирать предмет — «давай как подниму, так и поговорим»
    }

    /// <summary>Результат BuyLargest/SellLargest.</summary>
    public struct ShipTradeResult
    {
        public ShipTradeRefusalReason Reason;
        public string GoodId;
        public int Amount;
        public int Total;
        public bool Accepted => Reason == ShipTradeRefusalReason.Accepted;
        public static ShipTradeResult Refuse(ShipTradeRefusalReason r) => new() { Reason = r };
    }

    /// <summary>
    /// Простая ship-to-ship торговля через диалог: игрок продаёт/покупает крупнейший стек у цели
    /// по средней цене (базовая цена товара). Полноценного UI со слайдерами и ценообразованием
    /// нет — этого достаточно для основного use-case «мирный обмен по пути».
    ///
    /// Цена одной единицы = <see cref="ItemStack.BasePrice"/> у стека (если задана), либо
    /// <see cref="DialogTuning.ShipTradeFallbackPrice"/>.
    /// </summary>
    public static class ShipTradeService
    {
        /// <summary>Проверка «в принципе можем ли торговать» (независимо от направления).</summary>
        public static ShipTradeRefusalReason Preview(ShipData seller, ShipData buyer)
        {
            if (seller == null || buyer == null) return ShipTradeRefusalReason.NoNeed;
            var tuning = GetTuning();
            int rel = Relations.Get(seller, buyer);
            if (rel <= OwnerRaceRelationsManager.BAD_MAX) return ShipTradeRefusalReason.BadRelations;
            float longD = Mathf.Max(1f, tuning?.TruceLongDistance ?? 8f);
            if ((seller.Position - buyer.Position).sqrMagnitude > longD * longD)
                return ShipTradeRefusalReason.BigDist;
            if (!string.IsNullOrEmpty(seller.LastAttackerUid)) return ShipTradeRefusalReason.War;
            if (IsFetchingItem(seller)) return ShipTradeRefusalReason.AlreadyTakeItem;
            return ShipTradeRefusalReason.Accepted;
        }

        /// <summary>Летит ли корабль подбирать вещь: его боевая/целевая метка указывает на
        /// контейнер-предмет в этой же звезде. Такой NPC откладывает торговлю до подбора.</summary>
        private static bool IsFetchingItem(ShipData ship) => FetchedItem(ship) != null;

        private static ShipData FetchedItem(ShipData ship)
        {
            string uid = ship?.Brain?.GetCombatTargetUid();
            if (string.IsNullOrEmpty(uid)) return null;
            var found = ship.CurrentStar?.FindShip(uid);
            return found != null && found.IsItem ? found : null;
        }

        /// <summary>Имя предмета, за которым летит корабль — для реплики отказа
        /// <c>Trade.AnswerAlreadyTakeItem</c> («давай как я подниму …, так и поговорим»).</summary>
        public static string FetchedItemName(ShipData ship) => FetchedItem(ship)?.Name;

        /// <summary>Сколько ещё единиц товара цель способна купить: ограничено её деньгами.
        /// 0 — покупать не может вовсе (реплика <c>Trade.TradeOkMayBuyNo</c>).</summary>
        public static int BuyCapacity(ShipData buyer, ShipData seller)
        {
            if (buyer == null) return 0;
            var (id, _) = CargoUtils.GetLargestCargo(seller);
            int unit = 0;
            if (!string.IsNullOrEmpty(id) && seller.Inventory != null
                && seller.Inventory.Stacks.TryGetValue(id, out var stack) && stack != null)
                unit = stack.BasePrice;
            if (unit <= 0) unit = Mathf.Max(1, GetTuning()?.ShipTradeFallbackPrice ?? 100);
            return Mathf.Max(0, buyer.Money / unit);
        }

        /// <summary>Игрок покупает крупнейший стек у цели.</summary>
        public static ShipTradeResult BuyLargestFrom(ShipData target, ShipData player)
        {
            var pv = Preview(target, player);
            if (pv != ShipTradeRefusalReason.Accepted) return ShipTradeResult.Refuse(pv);

            var (id, amount) = CargoUtils.GetLargestCargo(target);
            if (string.IsNullOrEmpty(id) || amount <= 0) return ShipTradeResult.Refuse(ShipTradeRefusalReason.NoNeed);
            if (!target.Inventory.Stacks.TryGetValue(id, out var stack) || stack == null || stack.TotalWeight <= 0)
                return ShipTradeResult.Refuse(ShipTradeRefusalReason.NoNeed);

            int unitPrice = stack.BasePrice > 0
                ? stack.BasePrice
                : Mathf.Max(1, GetTuning()?.ShipTradeFallbackPrice ?? 100);
            int total = unitPrice * amount;

            if (player.Money < total)
            {
                // Возьмём столько, сколько может унести кошелёк.
                int afford = player.Money / unitPrice;
                if (afford <= 0) return ShipTradeResult.Refuse(ShipTradeRefusalReason.NoMoney);
                amount = afford;
                total  = amount * unitPrice;
            }

            var taken = target.Inventory.TakeStack(id, amount);
            if (taken == null || taken.TotalWeight <= 0) return ShipTradeResult.Refuse(ShipTradeRefusalReason.NoNeed);

            player.Money -= total;
            target.Money += total;
            player.Inventory.AddStack(taken);

            return new ShipTradeResult { Reason = ShipTradeRefusalReason.Accepted, GoodId = id, Amount = taken.TotalWeight, Total = total };
        }

        /// <summary>Игрок продаёт крупнейший стек цели.</summary>
        public static ShipTradeResult SellLargestTo(ShipData target, ShipData player)
        {
            var pv = Preview(player, target);
            if (pv != ShipTradeRefusalReason.Accepted) return ShipTradeResult.Refuse(pv);

            var (id, amount) = CargoUtils.GetLargestCargo(player);
            if (string.IsNullOrEmpty(id) || amount <= 0) return ShipTradeResult.Refuse(ShipTradeRefusalReason.NoNeed);
            if (!player.Inventory.Stacks.TryGetValue(id, out var stack) || stack == null || stack.TotalWeight <= 0)
                return ShipTradeResult.Refuse(ShipTradeRefusalReason.NoNeed);

            int unitPrice = stack.BasePrice > 0
                ? stack.BasePrice
                : Mathf.Max(1, GetTuning()?.ShipTradeFallbackPrice ?? 100);
            int total = unitPrice * amount;

            if (target.Money < total)
            {
                int afford = target.Money / unitPrice;
                if (afford <= 0) return ShipTradeResult.Refuse(ShipTradeRefusalReason.NoMoney);
                amount = afford;
                total  = amount * unitPrice;
            }

            var taken = player.Inventory.TakeStack(id, amount);
            if (taken == null || taken.TotalWeight <= 0) return ShipTradeResult.Refuse(ShipTradeRefusalReason.NoNeed);

            target.Money -= total;
            player.Money += total;
            target.Inventory.AddStack(taken);

            return new ShipTradeResult { Reason = ShipTradeRefusalReason.Accepted, GoodId = id, Amount = taken.TotalWeight, Total = total };
        }

        private static DialogTuning GetTuning() =>
            GameWorld.Context?.Config?.Dialogs?.Tuning;
    }
}
