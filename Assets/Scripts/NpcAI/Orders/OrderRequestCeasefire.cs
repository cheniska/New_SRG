using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Utils;

namespace SRG.NpcAI.Orders
{
    public class OrderRequestCeasefire : NpcOrder
    {
        private readonly string _aggressorUid;
        [System.NonSerialized] private ShipData _aggressor;        // прямая ссылка
        private bool _done;
        private bool _accepted;
        // NegotiateRange/OfferFraction вынесены в NpcBalance.NegotiateRange / NpcBalance.CeasefireOfferFraction.

        public OrderRequestCeasefire(string aggressorUid) => _aggressorUid = aggressorUid;
        public bool Accepted => _accepted;

        public override bool Execute(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            _aggressor ??= FindShip(star, _aggressorUid);
            if (_aggressor == null || _aggressor.CurrentHull <= 0) { _done = true; return true; }

            if ((ship.Position - _aggressor.Position).magnitude > NpcBalance.NegotiateRange)
            {
                SetMoveTarget(ship, star, _aggressor.Position);
                return false;
            }

            var aggressorPersonality = _aggressor.Personality;
            // Предложение принимает только жадный агрессор (Greed > 60) — остальные отказывают.
            bool isGreedy = aggressorPersonality != null && aggressorPersonality.Greed > 60f;

            string sType = NpcConversationLog.PoolTypeOf(ship);
            string aType = NpcConversationLog.PoolTypeOf(_aggressor);
            if (!isGreedy)
            {
                _accepted = false;
                Debug.Log($"[Ceasefire] {_aggressor.Name} rejected ceasefire offer from {ship.Name}");
                NpcConversationLog.Post(ship, _aggressor,
                    $"Truce.{sType}Send", "Хватит стрелять! Отдам часть груза — только уйди.",
                    $"Truce.{aType}No",   "Мне не нужен твой хлам. Продолжаем.");
            }
            else
            {
                TransferOffer(ship, _aggressor);
                _accepted = true;
                Debug.Log($"[Ceasefire] {_aggressor.Name} ACCEPTED ceasefire from {ship.Name}");
                aggressorPersonality?.ApplyAllyBonus(ship.Uid);
                CeasefireSolidarity.ApplyHomePlanetBonus(ship, _aggressor, star);
                NpcConversationLog.Post(ship, _aggressor,
                    $"Truce.{sType}Send", "Хватит стрелять! Отдам часть груза — только уйди.",
                    $"Truce.{aType}Ok",   "Разумное предложение. Расходимся.");
            }

            _aggressor.CrimeRating += 5f;
            _done = true;
            return true;
        }

        private void TransferOffer(ShipData from, ShipData to)
        {
            var stackKeys = new System.Collections.Generic.List<string>(from.Inventory.Stacks.Keys);
            foreach (var key in stackKeys)
            {
                if (!from.Inventory.Stacks.TryGetValue(key, out var stackEntry)) continue;
                int amount = Mathf.Max(1, Mathf.RoundToInt(stackEntry.TotalWeight * NpcBalance.CeasefireOfferFraction));
                var stack = from.Inventory.TakeStack(key, amount);
                if (stack != null) to.Inventory.AddStack(stack);
            }
        }

        public override bool IsCompleted(ShipData ship, StarData star) => _done;
        public override string DebugName => $"RequestCeasefire(→{SpriteUtility.ShortId(_aggressorUid)})";
    }
}
