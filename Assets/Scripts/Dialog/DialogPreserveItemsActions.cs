using SRG.Galaxy;

namespace SRG.Dialog
{
    /// <summary>
    /// Actions/tags для ветки «не стреляй по ошмёткам» (§8 диалогов с кораблями в космосе).
    /// Работает по паре ShipData: если TargetShip сейчас атакует дроп-контейнер
    /// (<see cref="ShipData.IsItem"/>), игрок может уговорить его прекратить.
    /// Механика простая: сбрасываем текущую NPC-активность в Idle — CombatSubTurn перестанет
    /// стрелять по item-цели. По результату проставляется <c>preserve_reason</c>: Accepted / NoTarget.
    ///
    /// Теги:
    ///   {can_preserve}     — 1, если TargetShip атакует item.
    ///   {preserve_reason}  — Accepted / NoTarget (после PreserveItems).
    ///   {preserve_target_name} — имя цели-контейнера (или пусто).
    /// </summary>
    public static class DialogPreserveItemsActions
    {
        private const string REASON_KEY = "preserve_reason";
        private const string TARGET_KEY = "preserve_target_name";

        public static void RegisterDefaults()
        {
            DialogService.RegisterAction("PreserveItems", (ctx, _) =>
            {
                var target = ctx?.TargetShip;
                var itemVictim = FindItemVictim(target);
                if (itemVictim == null)
                {
                    ctx.Data[REASON_KEY] = "NoTarget";
                    ctx.Data[TARGET_KEY] = "";
                    return;
                }
                // Сбрасываем текущую активность (ActionPursueAndAttack по item-цели) — следующий
                // Tick подберёт нормальный дефолт (Patrol/Trade/…) и стрельба по контейнеру прекратится.
                target.Brain?.ResetActivity();
                ctx.Data[REASON_KEY] = "Accepted";
                ctx.Data[TARGET_KEY] = itemVictim.Name ?? itemVictim.Uid ?? "";
            });

            DialogService.RegisterTag("can_preserve", ctx =>
                FindItemVictim(ctx?.TargetShip) != null ? "1" : "0");

            DialogService.RegisterDataTag("preserve_reason", REASON_KEY);

            DialogService.RegisterTag("preserve_target_name", ctx =>
            {
                var stored = ctx.ReadStr(TARGET_KEY);
                if (!string.IsNullOrEmpty(stored)) return stored;
                return FindItemVictim(ctx?.TargetShip)?.Name ?? "";
            });
        }

        // Если у NPC текущая активность помечает боевой цели UID, и по этому UID в системе
        // находится корабль-контейнер (IsItem=true), возвращаем его.
        private static ShipData FindItemVictim(ShipData target)
        {
            if (target == null || target.Brain == null) return null;
            string uid = target.Brain.GetCombatTargetUid();
            if (string.IsNullOrEmpty(uid)) return null;
            var star = target.CurrentStar;
            if (star?.Ships == null) return null;
            for (int i = 0; i < star.Ships.Count; i++)
            {
                var s = star.Ships[i];
                if (s != null && s.Uid == uid) return s.IsItem ? s : null;
            }
            return null;
        }
    }
}
