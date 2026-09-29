using System.Text;
using SRG.Core;
using SRG.Galaxy;
using SRG.Ships.Services;

namespace SRG.Dialog
{
    /// <summary>
    /// Actions/tags для ветки совместной атаки (Attack).
    ///
    /// Actions:
    ///   JointAttackListTargets — заполняет ctx.Data["jatk_target_uids"] списком UID (через запятую)
    ///                            всех кандидатов на атаку (враждебные игроку живые корабли в звезде).
    ///                            Также ctx.Data["jatk_target_names"] — их имена через "\n".
    ///   JointAttackSetTarget &lt;index&gt; — записывает выбранный UID/имя в ctx.Data.
    ///   JointAttackPropose             — TryEnlist по выбранной цели; ставит reason.
    ///
    /// Теги:
    ///   {jatk_target_uid}   — UID выбранной цели.
    ///   {jatk_target_name}  — имя выбранной цели.
    ///   {jatk_targets_count}— число кандидатов.
    ///   {jatk_reason}       — Accepted / Fear / InFear / Suspect / WeFriends / WeAlreadyHavePact / HaveBusiness / NoTarget.
    /// </summary>
    public static class DialogJointAttackActions
    {
        private const string UIDS_KEY    = "jatk_target_uids";
        private const string NAMES_KEY   = "jatk_target_names";
        private const string SEL_UID_KEY = "jatk_target_uid";
        private const string SEL_NAME_KEY= "jatk_target_name";
        private const string REASON_KEY  = "jatk_reason";
        private const string COUNT_KEY   = "jatk_targets_count";

        public static void RegisterDefaults()
        {
            DialogService.RegisterAction("JointAttackListTargets", (ctx, _) =>
            {
                var (ally, player) = ctx.Pair();
                if (player == null) return;
                // ally исключается из списка — предлагать напасть на самого собеседника нельзя.
                var list = JointAttackService.ListPossibleTargets(player, ally);
                var uids  = new StringBuilder();
                var names = new StringBuilder();
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) { uids.Append('|'); names.Append('|'); }
                    uids.Append(list[i].Uid ?? "");
                    names.Append(list[i].Name ?? list[i].ShipTypeId ?? "?");
                }
                ctx.Data[UIDS_KEY]  = uids.ToString();
                ctx.Data[NAMES_KEY] = names.ToString();
                ctx.Data[COUNT_KEY] = list.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (list.Count == 0) return;

                // Список пересобирается на каждый вход (цели гибнут и улетают), но выбор игрока
                // сохраняем: узел «выбрать цель» возвращает сам в себя, и сброс на первую строку
                // означал бы, что «следующая цель» ничего не меняет.
                string current = ctx.ReadStr(SEL_UID_KEY);
                if (!string.IsNullOrEmpty(current))
                    for (int i = 0; i < list.Count; i++)
                        if (list[i].Uid == current) return;

                ctx.Data[SEL_UID_KEY]  = list[0].Uid ?? "";
                ctx.Data[SEL_NAME_KEY] = list[0].Name ?? list[0].ShipTypeId ?? "?";
            });

            // Переключение на следующую цель по кругу — «Следующая цель» в узле выбора.
            // Абсолютный SetTarget для этого не годился: он всегда выбирал одну и ту же строку.
            DialogService.RegisterAction("JointAttackNextTarget", (ctx, _) =>
            {
                if (ctx?.Data == null) return;
                if (!ctx.Data.TryGetValue(UIDS_KEY, out var uidsStr) || string.IsNullOrEmpty(uidsStr)) return;
                var uids  = uidsStr.Split('|');
                var names = ctx.ReadStr(NAMES_KEY).Split('|');
                if (uids.Length == 0) return;

                string current = ctx.ReadStr(SEL_UID_KEY);
                int idx = System.Array.IndexOf(uids, current);
                idx = (idx + 1) % uids.Length;          // -1 (не нашли) → 0, дальше по кругу
                ctx.Data[SEL_UID_KEY]  = uids[idx];
                ctx.Data[SEL_NAME_KEY] = idx < names.Length ? names[idx] : uids[idx];
            });

            DialogService.RegisterAction("JointAttackSetTarget", (ctx, args) =>
            {
                if (ctx == null || args == null || args.Count == 0) return;
                if (!int.TryParse(args[0], out int idx)) return;
                if (!ctx.Data.TryGetValue(UIDS_KEY,  out var uidsStr))  return;
                if (!ctx.Data.TryGetValue(NAMES_KEY, out var namesStr)) return;
                var uids  = uidsStr.Split('|');
                var names = namesStr.Split('|');
                if (idx < 0 || idx >= uids.Length) return;
                ctx.Data[SEL_UID_KEY]  = uids[idx];
                ctx.Data[SEL_NAME_KEY] = idx < names.Length ? names[idx] : uids[idx];
            });

            DialogService.RegisterAction("JointAttackPropose", (ctx, _) =>
            {
                var ally   = ctx?.TargetShip;
                var player = ctx?.PlayerShip;
                if (ally == null || player == null) return;
                var target = FindShip(ally.CurrentStar, ctx.ReadStr(SEL_UID_KEY));
                var res = JointAttackService.TryEnlist(ally, player, target);
                ctx.Data[REASON_KEY] = res.Reason.ToString();
            });

            // Вердикт по ВЫБРАННОЙ сейчас цели, без предложения: пересчитывается на каждый рендер,
            // поэтому союзник возражает («он мой друг», «у нас пакт») сразу при переключении цели,
            // а не после нажатия «Атаковать».
            DialogService.RegisterTag("jatk_verdict", ctx =>
            {
                var (ally, player) = ctx.Pair();
                if (ally == null || player == null) return "";
                var target = FindShip(ally.CurrentStar, ctx.ReadStr(SEL_UID_KEY));
                if (target == null) return nameof(JointAttackRefusalReason.NoTarget);
                return JointAttackService.Preview(ally, player, target).Reason.ToString();
            });

            DialogService.RegisterDataTag("jatk_target_uid",    SEL_UID_KEY);
            DialogService.RegisterDataTag("jatk_target_name",   SEL_NAME_KEY);
            DialogService.RegisterDataTag("jatk_targets_count", COUNT_KEY, "0");
            DialogService.RegisterDataTag("jatk_reason",        REASON_KEY);
        }

        private static ShipData FindShip(StarData star, string uid)
        {
            if (star == null || string.IsNullOrEmpty(uid)) return null;
            for (int i = 0; i < star.Ships.Count; i++)
                if (star.Ships[i].Uid == uid) return star.Ships[i];
            return null;
        }
    }
}
