using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.NpcAI.Orders;

namespace SRG.NpcAI.Actions
{
    public class ActionIdle : NpcAction
    {
        private readonly OrderIdle _order;

        public ActionIdle(int turns = 5) => _order = new OrderIdle(turns);

        public override bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            bool done = _order.Execute(ship, star, ctx);
            if (done) { IsCompleted = true; return true; }
            return false;
        }

        public override string DebugName => "Idle";
    }
}
