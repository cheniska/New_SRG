using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.NpcAI.Orders;
using SRG.Utils;

namespace SRG.NpcAI.Actions
{
    public class ActionFlee : NpcAction
    {
        private readonly string _threatUid;
        private OrderFlee _fleeOrder;

        public string ThreatShipUid => _threatUid;

        public ActionFlee(string threatUid) => _threatUid = threatUid;

        public override bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            if (_fleeOrder == null) _fleeOrder = new OrderFlee(_threatUid);
            bool done = _fleeOrder.Execute(ship, star, ctx);
            if (done) { IsCompleted = true; return true; }
            return false;
        }

        public override string DebugName => $"Flee({SpriteUtility.ShortId(_threatUid)})";
    }
}
