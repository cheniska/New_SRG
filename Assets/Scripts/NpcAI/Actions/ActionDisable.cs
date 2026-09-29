using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.NpcAI.Orders;
using SRG.Utils;

namespace SRG.NpcAI.Actions
{
    public class ActionDisable : NpcAction
    {
        private readonly string _targetUid;
        private readonly float _targetHullPct;  // остановиться при этом % HP цели
        private OrderAttack _attackOrder;

        public string TargetShipUid => _targetUid;

        public ActionDisable(string targetUid, float targetHullPct = 0.2f)
        {
            _targetUid = targetUid;
            _targetHullPct = targetHullPct;
        }

        public override string CombatTargetUid => _targetUid;

        public override bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            ShipData target = FindShip(star, _targetUid);
            if (target == null || target.CurrentHull <= 0) { IsCompleted = true; return true; }
            if (target.MaxHull > 0 && (float)target.CurrentHull / target.MaxHull <= _targetHullPct)
            {
                IsCompleted = true;
                return true;
            }

            if (_attackOrder == null) _attackOrder = new OrderAttack(_targetUid);
            _attackOrder.Execute(ship, star, ctx);
            return false;
        }

        public override string DebugName => $"Disable({SpriteUtility.ShortId(_targetUid)},hp<{_targetHullPct:P0})";
    }
}
