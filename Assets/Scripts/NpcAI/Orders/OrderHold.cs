using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;

namespace SRG.NpcAI.Orders
{
    public class OrderHold : NpcOrder
    {
        private readonly Vector2 _position;
        private readonly int _durationTurns;
        private int _turnsHeld;

        public OrderHold(Vector2 position, int durationTurns = int.MaxValue)
        {
            _position = position;
            _durationTurns = durationTurns;
        }

        public override bool Execute(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            SetMoveTarget(ship, star, _position);
            _turnsHeld++;
            return IsCompleted(ship, star);
        }

        public override bool IsCompleted(ShipData ship, StarData star)
            => _turnsHeld >= _durationTurns;

        public override string DebugName => $"Hold({_turnsHeld}/{_durationTurns})";
    }
}
