using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Galaxy.Politics;
using SRG.Ships.Services;
using SRG.Utils;

namespace SRG.NpcAI.Actions
{
    /// <summary>
    /// AI-вымогательство денег: пират выводит цель из строя (ActionDisable) и требует
    /// перевод через <see cref="ExtortionService.TryDemand"/>. Перевод бесконтактный —
    /// money списывается с цели и зачисляется грабителю за один тик.
    ///
    /// Отдельный Action от <see cref="ActionRob"/>: у игрока и NPC оба способа существуют
    /// независимо (см. требование A). Выбор между ними — на уровне <see cref="NpcBrain.PirateLogic"/>.
    ///
    /// При отказе (нет денег / отказалась заплатить / далеко) — LowerToLevel(Hostile) +
    /// ForceActivity в ActionPursueAndAttack (пункт D требований).
    /// </summary>
    public class ActionExtort : NpcAction
    {
        private readonly string _targetUid;
        private enum Phase { Disable, Demand }
        private Phase _phase = Phase.Disable;

        private ActionDisable _disableAction;
        private bool _incomingRequested;

        public override string CombatTargetUid => _phase == Phase.Disable ? _targetUid : null;

        public ActionExtort(string targetUid) => _targetUid = targetUid;

        public override bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            ShipData target = FindShip(star, _targetUid);
            if (target == null || target.CurrentHull <= 0) { IsCompleted = true; return true; }

            switch (_phase)
            {
                case Phase.Disable:
                    if (_disableAction == null) _disableAction = new ActionDisable(_targetUid, 0.25f);
                    bool disableDone = _disableAction.Tick(ship, star, ctx);
                    if (disableDone || target.CurrentHull <= 0)
                        _phase = Phase.Demand;
                    break;

                case Phase.Demand:
                    return TickDemand(ship, target);
            }
            return false;
        }

        private bool TickDemand(ShipData robber, ShipData target)
        {
            // Игрок-жертва — открываем входящий диалог. Пока диалог открыт — ждём.
            // Эффекты (списание денег / атака) выполняет DialogIncomingRobActions; после
            // закрытия диалога action завершается без вызова offline-TryDemand.
            if (target.IsPlayer)
            {
                if (!_incomingRequested)
                {
                    _incomingRequested = true;
                    var did = SRG.Dialog.DialogService.ResolveIncomingDialogId(robber, "extort");
                    var ui  = SRG.Dialog.DialogUIController.Instance;
                    if (!string.IsNullOrEmpty(did) && ui != null && ui.OpenIncomingSpaceDialog(did, robber))
                        return false;
                    // Fallback: диалог открыть нельзя — сразу атака.
                    Relations.LowerToLevel(robber, target, RelationLevel.Hostile);
                    robber.Brain?.ForceActivity(new ActionPursueAndAttack(target.Uid, limitEngageRange: false));
                    IsCompleted = true;
                    return true;
                }
                if (SRG.Dialog.DialogService.IsActive) return false;
                IsCompleted = true;
                return true;
            }

            int demand = ExtortionService.SeedDemand(target);
            var result = ExtortionService.TryDemand(target, robber, demand);
            if (result.Accepted)
            {
                Debug.Log($"[ActionExtort] {robber.Name} → {target.Name}: получено {result.Fee} кр. CrimeRating={robber.CrimeRating:F0}");
                string tOk = NpcConversationLog.PoolTypeOf(target);
                NpcConversationLog.Post(robber, target,
                    "Money.Send",       $"Кошелёк или жизнь. {demand} кредитов — и разбежались.",
                    $"Money.{tOk}Ok",   $"Держи {result.Fee}. Оставь меня в покое.",
                    ("<Money>", demand.ToString()));
                IsCompleted = true;
                return true;
            }

            // Пункт D: жертва не отдала деньги — падаем в атаку.
            Relations.LowerToLevel(robber, target, RelationLevel.Hostile);
            Debug.Log($"[ActionExtort] {robber.Name} → {target.Name}: отказ ({result.Reason}), переходим в атаку.");
            string tType = NpcConversationLog.PoolTypeOf(target);
            string refuseKey = result.Reason == ExtortRefusalReason.LongDistance
                ? $"Money.{tType}LongDistance"
                : result.Reason == ExtortRefusalReason.WeAlreadyHavePact
                    ? "Money.WeAlreadyHavePact"
                    : result.Reason == ExtortRefusalReason.NotEnoughMoney
                        ? "Money.AnswerNotMoney"
                        : result.Reason == ExtortRefusalReason.SumIsVeryBig
                            ? $"Money.{tType}SumIsVeryBig"
                            : $"Money.{tType}No";
            NpcConversationLog.Post(robber, target,
                "Money.Send", $"Кошелёк или жизнь. {demand} кредитов — и разбежались.",
                refuseKey,    "Не будет тебе ни кредита. Отвали.",
                ("<Money>", demand.ToString()));
            robber.Brain?.ForceActivity(new ActionPursueAndAttack(target.Uid, limitEngageRange: false));
            IsCompleted = true;
            return true;
        }

        public override string DebugName => $"Extort({SpriteUtility.ShortId(_targetUid)},phase={_phase})";
    }
}
