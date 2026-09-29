using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.NpcAI.Orders;

namespace SRG.NpcAI.Actions
{
    public class ActionHyperJumpTo : NpcAction
    {
        // После стольких неудачных запросов прыжка (нет топлива/двигателя/дистанция) действие
        // завершается — иначе корабль вечно стоит, долбя RequestJump, вместо обычной жизни.
        private const int MaxFailedAttempts = 3;

        private readonly string _targetStarUid;
        private OrderHyperJump _jumpOrder;
        private int _failedAttempts;

        public string TargetStarUid => _targetStarUid;

        public ActionHyperJumpTo(string targetStarUid) => _targetStarUid = targetStarUid;

        public override bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            if (ship.CurrentStarUid == _targetStarUid) { IsCompleted = true; return true; }
            if (_jumpOrder == null) _jumpOrder = new OrderHyperJump(_targetStarUid);
            bool done = _jumpOrder.Execute(ship, star, ctx);
            if (done) { IsCompleted = true; return true; }

            // Фаза так и не началась — запрос отклонён (не «ждём в пути»). Считаем отказ.
            if (ship.HyperjumpPhase == HyperjumpPhase.None
                && ++_failedAttempts >= MaxFailedAttempts)
            {
                IsCompleted = true;
                return true;
            }
            return false;
        }

        public override string DebugName => $"HyperJumpTo({_targetStarUid[..6]})";
    }
}
