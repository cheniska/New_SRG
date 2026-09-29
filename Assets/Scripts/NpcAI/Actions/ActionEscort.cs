using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Galaxy.Politics;
using SRG.NpcAI.Orders;
using SRG.Utils;

namespace SRG.NpcAI.Actions
{
    public class ActionEscort : NpcAction
    {
        // ESCORT_THREAT_RADIUS_SQ вынесен в NpcBalance.EscortThreatRadiusSq.

        private readonly string _escortTargetUid;
        private string _currentThreatUid;
        private NpcAction _subAction;
        private OrderFollow _followOrder;

        public string EscortedShipUid => _escortTargetUid;
        public override string CombatTargetUid => _subAction?.CombatTargetUid;

        public ActionEscort(string escortTargetUid) => _escortTargetUid = escortTargetUid;

        public override bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            ShipData escorted = FindShip(star, _escortTargetUid);
            if (escorted == null || escorted.CurrentHull <= 0) { IsCompleted = true; return true; }

            string threat = FindThreatTo(star, escorted, ship);

            if (threat != null && threat != _currentThreatUid)
            {
                _currentThreatUid = threat;
                _subAction = new ActionPursueAndAttack(threat);
            }

            if (_subAction != null && !_subAction.IsCompleted)
            {
                bool done = _subAction.Tick(ship, star, ctx);
                if (done) { _subAction = null; _currentThreatUid = null; }
                return false;
            }

            _followOrder ??= new OrderFollow(_escortTargetUid);
            _followOrder.Execute(ship, star, ctx);
            return false;
        }

        private static string FindThreatTo(StarData star, ShipData target, ShipData self)
        {
            // ShipQuery.HostileTo(target) итерирует только владельцев, враждебных к target
            // (через материализованные списки в GalaxyShipCounters). При 40+ кораблей в системе
            // экономит один foreach star.Ships на каждый Tick эскорта.
            float radius = Mathf.Sqrt(NpcBalance.EscortThreatRadiusSq);
            return ShipQuery.In(star)
                .Excluding(self)
                .HostileTo(target)
                .Within(target.Position, radius)
                .FirstOrDefault()?.Uid;
        }

        public override string DebugName => $"Escort({SpriteUtility.ShortId(_escortTargetUid)})";
    }
}
