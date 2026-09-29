using SRG.Galaxy;
using SRG.Galaxy.Generation;

namespace SRG.NpcAI.Orders
{
    public class OrderIdle : NpcOrder
    {
        private readonly int _durationTurns;
        private int _waited;

        public OrderIdle(int durationTurns = 3) => _durationTurns = durationTurns;

        public override bool Execute(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            SetMoveTarget(ship, star, ship.Position);
            _waited++;
            return IsCompleted(ship, star);
        }

        public override bool IsCompleted(ShipData ship, StarData star) => _waited >= _durationTurns;
        public override string DebugName => $"Idle({_waited}/{_durationTurns})";
    }
}
