using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.NpcAI.Orders;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.NpcAI.Actions
{
    public class ActionRequestCeasefire : NpcAction
    {
        private readonly string _aggressorUid;
        private OrderRequestCeasefire _order;
        private bool _incomingRequested;

        public string AggressorShipUid => _aggressorUid;

        public ActionRequestCeasefire(string aggressorUid) => _aggressorUid = aggressorUid;

        public override bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            // Агрессор — игрок: открываем входящий диалог «предлагаю мир, часть груза за прекращение
            // атаки». Игрок сам решает; эффекты применяются action-handlers'ами диалога.
            var aggressor = star?.FindShip(_aggressorUid);
            if (aggressor != null && aggressor.IsPlayer)
            {
                if (!_incomingRequested)
                {
                    _incomingRequested = true;
                    var did = SRG.Dialog.DialogService.ResolveIncomingDialogId(ship, "ceasefire");
                    var ui  = GameWorld.Dialogs;
                    if (!string.IsNullOrEmpty(did) && ui != null && ui.OpenIncomingSpaceDialog(did, ship))
                        return false;
                    // Fallback: не удалось открыть диалог — жертва просто не получит мир, action завершается.
                    IsCompleted = true;
                    return true;
                }
                if (SRG.Dialog.DialogService.IsActive) return false;
                IsCompleted = true;
                return true;
            }

            if (_order == null) _order = new OrderRequestCeasefire(_aggressorUid);
            bool done = _order.Execute(ship, star, ctx);
            if (done)
            {
                IsCompleted = true;
                return true;
            }
            return false;
        }

        public override string DebugName => $"RequestCeasefire({SpriteUtility.ShortId(_aggressorUid)})";
    }
}
