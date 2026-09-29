using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;

namespace SRG.NpcAI.Orders
{
    public class OrderMoveTo : NpcOrder
    {
        private readonly Vector2 _target;
        // ArrivalThresholdSq вынесен в NpcBalance (общий с OrderPatrol).

        public OrderMoveTo(Vector2 target) => _target = target;

        public override bool Execute(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            SetMoveTarget(ship, star, _target);
            return IsCompleted(ship, star);
        }

        public override bool IsCompleted(ShipData ship, StarData star)
            => (ship.Position - _target).sqrMagnitude < NpcBalance.ArrivalThresholdSq;

        public override string DebugName => $"MoveTo({_target.x:F1},{_target.y:F1})";
    }
}
