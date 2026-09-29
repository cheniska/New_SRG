using SRG.Ships.Services;

namespace SRG.Dialog
{
    /// <summary>
    /// Actions/tags для ветки вымогательства денег (Money).
    /// Механика торга — общая (<see cref="DialogBargain"/>), здесь только её параметры.
    ///
    /// Actions:
    ///   ExtortInit    — ctx.Data["extort_demand"] = ExtortionService.SeedDemand(target).
    ///   ExtortLess    — /2 (не ниже 1).
    ///   ExtortMore    — ×2.
    ///   ExtortDemand  — ExtortionService.TryDemand; сохраняет reason/fee.
    ///
    /// Теги:
    ///   {extort_demand}  — текущая требуемая сумма.
    ///   {extort_reason}  — код причины отказа (Accepted / SumIsVeryBig / LongDistance / ShipRefuses / NotEnoughMoney).
    ///   {extort_fee}     — реально списанная сумма (или максимально приемлемая при SumIsVeryBig).
    ///   {extort_verdict} — живой вердикт по текущей сумме, без совершения сделки.
    ///   {can_extort}     — 1, если цель в принципе может расстаться с деньгами (не Warrior/Ranger/Pirate/Dominator).
    /// </summary>
    public static class DialogExtortionActions
    {
        private const string DEMAND_KEY = "extort_demand";
        private const string FEE_KEY    = "extort_fee";
        private const string REASON_KEY = "extort_reason";

        public static void RegisterDefaults()
        {
            DialogBargain.Register(new BargainSpec
            {
                InitAction   = "ExtortInit",
                LessAction   = "ExtortLess",
                MoreAction   = "ExtortMore",
                CommitAction = "ExtortDemand",

                OfferKey  = DEMAND_KEY, FeeKey = FEE_KEY, ReasonKey = REASON_KEY,
                OfferTag  = "extort_demand",
                FeeTag    = "extort_fee",
                ReasonTag = "extort_reason",
                VerdictTag = "extort_verdict",

                // Платит цель, а не игрок: ни потолка по кошельку игрока, ни самого игрока
                // в расчёте стартовой суммы нет — вымогатель смотрит на возможности жертвы.
                CapMoreByPlayerMoney = false,
                SeedNeedsPlayer      = false,
                Seed = (target, _) => ExtortionService.SeedDemand(target),

                // Здесь чужой кошелёк смотреть законно — платит сама цель, это её деньги,
                // поэтому NotEnoughMoney остаётся честным отказом и в Accepted не переводится.
                Verdict = (target, player, demand) =>
                    ExtortionService.Preview(target, player, demand).Reason.ToString(),

                Commit = (target, player, demand) =>
                {
                    var r = ExtortionService.TryDemand(target, player, demand);
                    return (r.Reason.ToString(), r.Fee);
                },
            });

            DialogService.RegisterTag("can_extort", ctx =>
            {
                var (target, player) = ctx.Pair();
                if (target == null || player == null) return "0";
                var preview = ExtortionService.Preview(target, player, 1);
                // Показываем пункт диалога, если корабль в принципе может согласиться (не гордый и не в пакте).
                return preview.Reason != ExtortRefusalReason.ShipRefuses
                    && preview.Reason != ExtortRefusalReason.WeAlreadyHavePact ? "1" : "0";
            });
        }
    }
}
