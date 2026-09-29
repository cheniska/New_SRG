using SRG.Ships.Services;

namespace SRG.Dialog
{
    /// <summary>
    /// Actions/tags для ветки грабежа груза (Goods).
    ///
    /// Actions:
    ///   CargoRobPreview — заполняет ctx.Data["cargo_rob_reason"] Preview'ом (без побочек).
    ///   CargoRob        — TryRob: сбрасывает контейнеры, обновляет reason/weight/containers.
    ///
    /// Теги:
    ///   {cargo_rob_reason}     — Accepted / LongDistance / NoCargo / ShipRefuses / WeAlreadyHavePact.
    ///   {cargo_rob_weight}     — сколько ед. груза сброшено (Accepted).
    ///   {cargo_rob_containers} — сколько контейнеров создано (Accepted).
    ///   {can_rob_cargo}        — 1 если цель в принципе поддаётся грабежу (не Warrior/Ranger/Pirate/Dominator и есть груз).
    /// </summary>
    public static class DialogCargoRobActions
    {
        private const string REASON_KEY = "cargo_rob_reason";
        private const string WEIGHT_KEY = "cargo_rob_weight";
        private const string COUNT_KEY  = "cargo_rob_containers";

        public static void RegisterDefaults()
        {
            DialogService.RegisterAction("CargoRobPreview", (ctx, _) =>
            {
                var (target, player) = ctx.Pair();
                if (target == null || player == null) return;
                var pv = CargoRobberyService.Preview(target, player);
                ctx.Data[REASON_KEY] = pv.Reason.ToString();
            });

            DialogService.RegisterAction("CargoRob", (ctx, _) =>
            {
                var (target, player) = ctx.Pair();
                if (target == null || player == null) return;
                var res = CargoRobberyService.TryRob(target, player);
                ctx.Data[REASON_KEY] = res.Reason.ToString();
                ctx.WriteInt(WEIGHT_KEY, res.Weight);
                ctx.WriteInt(COUNT_KEY,  res.Containers);
            });

            DialogService.RegisterDataTag("cargo_rob_reason",     REASON_KEY);
            DialogService.RegisterDataTag("cargo_rob_weight",     WEIGHT_KEY, "0");
            DialogService.RegisterDataTag("cargo_rob_containers", COUNT_KEY,  "0");

            DialogService.RegisterTag("can_rob_cargo", ctx =>
            {
                var (target, player) = ctx.Pair();
                if (target == null || player == null) return "0";
                var pv = CargoRobberyService.Preview(target, player);
                return pv.Reason != CargoRobRefusalReason.ShipRefuses
                    && pv.Reason != CargoRobRefusalReason.WeAlreadyHavePact ? "1" : "0";
            });
        }
    }
}
