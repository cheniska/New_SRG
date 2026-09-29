using System.Globalization;
using SRG.Core;
using SRG.Ships;
using SRG.Ships.Services;

namespace SRG.Dialog
{
    /// <summary>
    /// Actions/tags для ship-to-ship торговли (Trade).
    ///
    /// Actions:
    ///   TradePreview      — вычисляет reason (BadRelations/BigDist/War/NoNeed/Accepted).
    ///   TradeBuyLargest   — игрок покупает крупнейший стек у цели за среднюю цену.
    ///   TradeSellLargest  — игрок продаёт крупнейший стек цели за среднюю цену.
    ///
    /// Теги:
    ///   {trade_reason}       — Accepted / BadRelations / BigDist / War / NoNeed / NoMoney / NoSpace.
    ///   {trade_good_name}    — имя товара, участвовавшего в сделке.
    ///   {trade_amount}       — количество единиц.
    ///   {trade_total}        — суммарная цена сделки.
    ///   {can_trade}          — 1, если торговля в принципе возможна (Preview).
    ///   {player_has_cargo}   — 1, если у игрока есть хоть один стек в трюме.
    /// </summary>
    public static class DialogShipTradeActions
    {
        private const string REASON_KEY = "trade_reason";
        private const string NAME_KEY   = "trade_good_name";
        private const string AMT_KEY    = "trade_amount";
        private const string TOTAL_KEY  = "trade_total";
        private const string LIMIT_KEY  = "trade_buy_limit";

        public static void RegisterDefaults()
        {
            DialogService.RegisterAction("TradePreview", (ctx, _) =>
            {
                var (target, player) = ctx.Pair();
                if (target == null || player == null) return;
                var r = ShipTradeService.Preview(target, player);
                ctx.Data[REASON_KEY] = r.ToString();

                // Лимит закупки — для реплики «купить больше чем <Cnt> ед. не смогу»
                // (Trade.TradeOkMayBuyOk против TradeOkMayBuyNo, если платить нечем).
                int limit = ShipTradeService.BuyCapacity(target, player);
                ctx.Data[LIMIT_KEY] = limit > 0 ? limit.ToString(CultureInfo.InvariantCulture) : "";

                // Предмет, за которым летит цель — для Trade.AnswerAlreadyTakeItem.
                if (r == ShipTradeRefusalReason.AlreadyTakeItem)
                    ctx.Data["item_name"] = ShipTradeService.FetchedItemName(target) ?? "";
            });

            DialogService.RegisterAction("TradeBuyLargest", (ctx, _) =>
            {
                var (target, player) = ctx.Pair();
                if (target == null || player == null) return;
                WriteResult(ctx, ShipTradeService.BuyLargestFrom(target, player));
            });

            DialogService.RegisterAction("TradeSellLargest", (ctx, _) =>
            {
                var (target, player) = ctx.Pair();
                if (target == null || player == null) return;
                WriteResult(ctx, ShipTradeService.SellLargestTo(target, player));
            });

            // Вводная реплика NPC: «поторгуем, но больше <Cnt> ед. не куплю» либо «покупать не
            // буду вовсе». Выбор ветки — здесь, а не условными Reply: реплику NPC положено
            // показывать текстом узла, иначе игрок «нажимает» слова собеседника.
            // Тег volatile: выбирает случайную строку из пула, и мемоизировать его нельзя.
            DialogService.RegisterTag("trade_intro", ctx =>
            {
                string lim = ctx.ReadStr(LIMIT_KEY);
                bool canBuy = !string.IsNullOrEmpty(lim) && lim != "0";
                return DialogActions.PickPoolLine(canBuy ? "Trade.TradeOkMayBuyOk" : "Trade.TradeOkMayBuyNo");
            }, isVolatile: true);

            DialogService.RegisterDataTag("trade_reason",    REASON_KEY);
            DialogService.RegisterDataTag("trade_good_name", NAME_KEY);
            DialogService.RegisterDataTag("trade_amount",    AMT_KEY,   "0");
            DialogService.RegisterDataTag("trade_total",     TOTAL_KEY, "0");

            DialogService.RegisterTag("can_trade", ctx =>
            {
                var (target, player) = ctx.Pair();
                if (target == null || player == null) return "0";
                return ShipTradeService.Preview(target, player) == ShipTradeRefusalReason.Accepted ? "1" : "0";
            });

            DialogService.RegisterTag("player_has_cargo", ctx =>
                CargoUtils.HasAnyCargo(ctx?.PlayerShip) ? "1" : "0");
        }

        private static void WriteResult(DialogContext ctx, ShipTradeResult res)
        {
            ctx.Data[REASON_KEY] = res.Reason.ToString();
            ctx.WriteInt(AMT_KEY,   res.Amount);
            ctx.WriteInt(TOTAL_KEY, res.Total);
            if (!string.IsNullOrEmpty(res.GoodId))
            {
                var cfg = GalaxyManager.Instance?.Context?.Config;
                ctx.Data[NAME_KEY] = SRG.Economy.TradeSystem.GetDisplayName(res.GoodId, cfg) ?? res.GoodId;
            }
        }
    }
}
