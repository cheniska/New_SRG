using SRG.Core;
using SRG.Ships.Services;

namespace SRG.Dialog
{
    /// <summary>
    /// Actions и tags для диалоговой ветки перемирия/выкупа (Truce).
    /// Механика торга — общая (<see cref="DialogBargain"/>), здесь только её параметры.
    ///
    /// Actions:
    ///   TruceOfferInit — ctx.Data["truce_offer"] = минимальный ожидаемый выкуп (Preview.Fee для OfferTooLow, иначе base).
    ///   TruceOfferLess — /2 (не ниже 1).
    ///   TruceOfferMore — ×2 (кэп по деньгам игрока).
    ///   TrucePropose   — TruceService.TryPropose; сохраняет reason/fee в ctx.Data.
    ///
    /// Теги:
    ///   {truce_offer}   — текущая сумма предложения.
    ///   {truce_fee}     — счёт после Propose (списанная сумма или требуемый минимум при OfferTooLow).
    ///   {truce_reason}  — код причины отказа (Accepted / LongDistance / OfferTooLow / …).
    ///   {truce_verdict} — живой вердикт по текущей сумме, без совершения сделки.
    ///   {needs_truce}   — 1, если корабль сейчас враждебен игроку (или у него свежая обида) — можно показать пункт «Предложить перемирие».
    /// </summary>
    public static class DialogTruceActions
    {
        private const string OFFER_KEY  = "truce_offer";
        private const string FEE_KEY    = "truce_fee";
        private const string REASON_KEY = "truce_reason";

        public static void RegisterDefaults()
        {
            DialogBargain.Register(new BargainSpec
            {
                InitAction   = "TruceOfferInit",
                LessAction   = "TruceOfferLess",
                MoreAction   = "TruceOfferMore",
                CommitAction = "TrucePropose",

                OfferKey  = OFFER_KEY, FeeKey  = FEE_KEY,  ReasonKey  = REASON_KEY,
                OfferTag  = "truce_offer",
                FeeTag    = "truce_fee",
                ReasonTag = "truce_reason",
                VerdictTag = "truce_verdict",

                // Выкуп платит сам игрок, поэтому здесь потолок по кошельку уместен —
                // в отличие от найма, где сумма всего лишь обещание.
                CapMoreByPlayerMoney = true,

                Seed = (target, player) =>
                {
                    var preview = TruceService.Preview(target, player, int.MaxValue);
                    return preview.Fee > 0
                        ? preview.Fee
                        : TruceService.CalcMinFee(target,
                            GalaxyManager.Instance?.Context?.Config?.Dialogs?.Tuning);
                },

                // NotEnoughMoney (у игрока нет столько) — не предмет разговора с целью: она видит
                // предложение, а не чужой счёт. Нехватка всплывёт при самой попытке, в узле result.
                Verdict = (target, player, offer) =>
                {
                    var reason = TruceService.Preview(target, player, offer).Reason;
                    return reason == TruceRefusalReason.NotEnoughMoney
                        ? nameof(TruceRefusalReason.Accepted) : reason.ToString();
                },

                Commit = (target, player, offer) =>
                {
                    var r = TruceService.TryPropose(target, player, offer);
                    return (r.Reason.ToString(), r.Fee);
                },
            });

            DialogService.RegisterTag("needs_truce", ctx =>
            {
                var (target, player) = ctx.Pair();
                if (target == null || player == null) return "0";
                var preview = TruceService.Preview(target, player, int.MaxValue);
                // Показываем пункт диалога, если корабль в принципе может согласиться (не «нет нужды» и не «не подкупен»).
                return preview.Reason != TruceRefusalReason.NoNeedForTruce
                    && preview.Reason != TruceRefusalReason.ShipTypeRefuses ? "1" : "0";
            });
        }
    }
}
