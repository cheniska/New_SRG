using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;

namespace SRG.NpcAI.Orders
{
    public class OrderPatrol : NpcOrder
    {
        private readonly Vector2 _pointA;
        private readonly Vector2 _pointB;
        private bool _goingToB;
        // ArrivalThresholdSq вынесен в NpcBalance (общий с OrderMoveTo).

        public OrderPatrol(Vector2 a, Vector2 b) { _pointA = a; _pointB = b; }

        public override bool Execute(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            Vector2 dest = _goingToB ? _pointB : _pointA;
            SetMoveTarget(ship, star, dest);

            if ((ship.Position - dest).sqrMagnitude < NpcBalance.ArrivalThresholdSq)
                _goingToB = !_goingToB;

            return false;
        }

        public override bool IsCompleted(ShipData ship, StarData star) => false;
        public override string DebugName => "Patrol(A↔B)";
    }
}
