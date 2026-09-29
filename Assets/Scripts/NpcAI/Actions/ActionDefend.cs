using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.NpcAI.Orders;

namespace SRG.NpcAI.Actions
{
    public class ActionDefend : NpcAction
    {
        private readonly Vector2 _zone;
        private readonly float _radius;
        private NpcAction _subAction;
        private readonly OrderHold _holdOrder;

        public ActionDefend(Vector2 zone, float radius = 4f)
        {
            _zone = zone;
            _radius = radius;
            _holdOrder = new OrderHold(zone);
        }

        public override string CombatTargetUid => _subAction?.CombatTargetUid;

        public override bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            if (_subAction != null && !_subAction.IsCompleted)
            {
                bool done = _subAction.Tick(ship, star, ctx);
                if (done) _subAction = null;
                return false;
            }

            // Враг ищется от центра зоны в её радиусе; погоня лимитирована MaxEngageRange —
            // убежавшего не преследуем через всю систему, возвращаемся охранять зону.
            string enemy = NpcTargeting.FindNearestHostileUid(star, ship, _zone, _radius * _radius);
            if (enemy != null) { _subAction = new ActionPursueAndAttack(enemy, limitEngageRange: true); return false; }

            _holdOrder.Execute(ship, star, ctx);
            return false;
        }

        public override string DebugName => "Defend";
    }
}
