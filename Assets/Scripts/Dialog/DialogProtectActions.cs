using SRG.Galaxy;
using SRG.Ships.Services;

namespace SRG.Dialog
{
    /// <summary>
    /// Actions/tags для ветки заступничества (Protect).
    ///
    /// Actions:
    ///   ProtectPreview — заполняет reason + victim_name из ProtectService.Preview.
    ///   Protect        — TryProtect: снимает обиду, поднимает отношения, помечает жертву RescuedByPlayerTurn.
    ///   ProtectClaimReward — если TargetShip = спасённая жертва, выдаёт награду (money/goods/thanks).
    ///
    /// Теги:
    ///   {protect_reason} — Accepted / NoVictim / AggressorRefuses / AggressorTooBusy.
    ///   {victim_name}    — имя жертвы (по preview) — для реплики «отстань от &lt;victim&gt;».
    ///   {can_protect}    — 1, если у TargetShip есть жертва и он не «неубеждаем».
    ///   {wants_reward}   — 1, если TargetShip — свежеспасённая жертва (RescuedByPlayerTurn ещё не протух).
    ///   {reward_kind}    — Money / Goods / Thanks (после ProtectClaimReward).
    ///   {reward_amount}  — сумма (Money) или вес груза (Goods) награды.
    /// </summary>
    public static class DialogProtectActions
    {
        private const string REASON_KEY  = "protect_reason";
        private const string VICTIM_KEY  = "protect_victim_uid";
        private const string VICTIM_NAME = "protect_victim_name";
        private const string RKIND_KEY   = "protect_reward_kind";
        private const string RAMT_KEY    = "protect_reward_amount";

        public static void RegisterDefaults()
        {
            DialogService.RegisterAction("ProtectPreview", (ctx, _) =>
            {
                var (target, player) = ctx.Pair();
                if (target == null || player == null) return;
                var pv = ProtectService.Preview(target, player);
                WriteVictim(ctx, target, pv.Reason.ToString(), pv.VictimUid);
            });

            DialogService.RegisterAction("Protect", (ctx, _) =>
            {
                var (target, player) = ctx.Pair();
                if (target == null || player == null) return;
                var res = ProtectService.TryProtect(target, player);
                WriteVictim(ctx, target, res.Reason.ToString(), res.VictimUid);
            });

            DialogService.RegisterAction("ProtectClaimReward", (ctx, _) =>
            {
                var (target, player) = ctx.Pair();
                if (target == null || player == null) return;
                var reward = ProtectService.ClaimReward(target, player);
                ctx.Data[RKIND_KEY] = reward.Kind.ToString();
                ctx.WriteInt(RAMT_KEY, reward.Amount);
            });

            DialogService.RegisterDataTag("protect_reason", REASON_KEY);
            DialogService.RegisterDataTag("victim_name",    VICTIM_NAME);
            DialogService.RegisterDataTag("reward_kind",    RKIND_KEY);
            DialogService.RegisterDataTag("reward_amount",  RAMT_KEY, "0");

            DialogService.RegisterTag("can_protect", ctx =>
            {
                var (target, player) = ctx.Pair();
                if (target == null || player == null) return "0";
                var pv = ProtectService.Preview(target, player);
                return pv.Reason != ProtectRefusalReason.NoVictim ? "1" : "0";
            });

            DialogService.RegisterTag("wants_reward", ctx =>
                ProtectService.IsRescueRewardPending(ctx?.TargetShip) ? "1" : "0");
        }

        /// <summary>Общая запись исхода: код причины + жертва, за которую вступается игрок.
        /// Имя не найденной жертвы остаётся пустым намеренно — фразу закрывает
        /// <c>TagDefaults["victim_name"]</c> в TextsConfig, а не литерал в коде.</summary>
        private static void WriteVictim(DialogContext ctx, ShipData target, string reason, string victimUid)
        {
            ctx.Data[REASON_KEY]  = reason;
            ctx.Data[VICTIM_KEY]  = victimUid ?? "";
            ctx.Data[VICTIM_NAME] = target.CurrentStar?.FindShip(victimUid)?.Name ?? "";
        }
    }
}
