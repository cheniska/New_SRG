using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.NpcAI.Orders;
using SRG.Utils;

namespace SRG.NpcAI.Actions
{
    /// <summary>
    /// Корабль с низким HP предлагает деньги преследователю в обмен на перемирие.
    /// Сумма масштабируется от потерянного HP — чем хуже, тем больше.
    /// </summary>
    public class ActionOfferMoneyRansom : NpcAction
    {
        private readonly string _receiverUid;
        private OrderOfferMoneyRansom _order;
        private int _offer;
        private bool _incomingRequested;

        public string ReceiverShipUid => _receiverUid;

        public ActionOfferMoneyRansom(string receiverUid) => _receiverUid = receiverUid;

        public override bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            // Получатель — игрок: открываем входящий диалог с предложением взятки.
            // Игрок сам решает; эффекты через action-handlers диалога.
            var receiver = star?.FindShip(_receiverUid);
            if (receiver != null && receiver.IsPlayer)
            {
                if (!_incomingRequested)
                {
                    _incomingRequested = true;
                    var did = SRG.Dialog.DialogService.ResolveIncomingDialogId(ship, "offer_money");
                    var ui  = SRG.Dialog.DialogUIController.Instance;
                    if (!string.IsNullOrEmpty(did) && ui != null && ui.OpenIncomingSpaceDialog(did, ship))
                        return false;
                    IsCompleted = true;
                    return true;
                }
                if (SRG.Dialog.DialogService.IsActive) return false;
                IsCompleted = true;
                return true;
            }

            if (_order == null)
            {
                _offer = CalculateOffer(ship);
                _order = new OrderOfferMoneyRansom(_receiverUid, _offer);
            }
            bool done = _order.Execute(ship, star, ctx);
            if (done)
            {
                IsCompleted = true;
                return true;
            }
            return false;
        }

        private static int CalculateOffer(ShipData ship)
        {
            if (ship.Money <= 0 || ship.MaxHull <= 0) return 0;
            float hpLost = 1f - (float)ship.CurrentHull / ship.MaxHull;
            // Чем больше потерь — тем щедрее: от 20% Money при HP=100% до 80% Money при HP=0%.
            float fraction = Mathf.Clamp01(0.2f + hpLost * 0.6f);
            return Mathf.Max(50, Mathf.RoundToInt(ship.Money * fraction));
        }

        public override string DebugName => $"OfferMoney({SpriteUtility.ShortId(_receiverUid)},{_offer})";
    }
}
