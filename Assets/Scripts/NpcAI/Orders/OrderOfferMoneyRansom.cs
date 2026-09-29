using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Ships;
using SRG.UI.Screens;
using SRG.Utils;

namespace SRG.NpcAI.Orders
{
    /// <summary>
    /// Денежный выкуп: payer (обычно тот, кому грозит уничтожение) предлагает receiver
    /// сумму денег за прекращение атаки. Acceptance зависит от класса receiver:
    ///   - Pirate/Mercenary: принимают по сумме * жадности / ChanceToWin
    ///   - Military: принимают только при большом offer (порог растёт с дисциплиной)
    ///   - Civilian: всегда принимают (если есть смысл — то есть payer был агрессором)
    /// При accept: деньги переводятся, ApplyAllyBonus + солидарность земляков.
    /// </summary>
    public class OrderOfferMoneyRansom : NpcOrder
    {
        private readonly string _receiverUid;
        private readonly int _offerAmount;
        private ShipData _receiver;
        private bool _done;
        private bool _accepted;
        // NegotiateRange вынесен в NpcBalance.NegotiateRange (общий с OrderRequestCeasefire).

        public OrderOfferMoneyRansom(string receiverUid, int offerAmount)
        {
            _receiverUid = receiverUid;
            _offerAmount = Mathf.Max(0, offerAmount);
        }

        public bool Accepted => _accepted;

        public override bool Execute(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            _receiver ??= FindShip(star, _receiverUid);
            if (_receiver == null || _receiver.CurrentHull <= 0) { _done = true; return true; }

            if ((ship.Position - _receiver.Position).magnitude > NpcBalance.NegotiateRange)
            {
                SetMoveTarget(ship, star, _receiver.Position);
                return false;
            }

            int actualOffer = Mathf.Min(_offerAmount, ship.Money);
            _accepted = EvaluateMoneyRansomAcceptance(ship, _receiver, actualOffer);

            if (_accepted)
            {
                ship.Money -= actualOffer;
                _receiver.Money += actualOffer;
                _receiver.Personality?.ApplyAllyBonus(ship.Uid);
                ship.Personality?.ReduceFrustration(30f);
                // Очищаем LastAttacker — стороны перестают друг друга преследовать.
                if (ship.LastAttackerUid == _receiver.Uid) ship.LastAttackerUid = null;
                if (_receiver.LastAttackerUid == ship.Uid) _receiver.LastAttackerUid = null;
                CeasefireSolidarity.ApplyHomePlanetBonus(ship, _receiver, star);
                // Для игрока-получателя оставляем консольную запись; чистые NPC↔NPC переговоры —
                // в NpcConversationLog (одна запись в панели вместо двух console-строк).
                if (ship.IsPlayer || _receiver.IsPlayer)
                {
                    GameConsoleController.AddEntry(
                        $"[Выкуп] {_receiver.Name} принял {actualOffer} от {ship.Name} — перемирие.");
                }
                else
                {
                    string rType = SRG.NpcAI.NpcConversationLog.PoolTypeOf(_receiver);
                    SRG.NpcAI.NpcConversationLog.Post(ship, _receiver,
                        "Money.PlayerSend",    $"Возьми {actualOffer} кредитов, только отвяжись.",
                        $"Money.{rType}Ok",    "По рукам. Разбегаемся.",
                        ("<Money>", actualOffer.ToString()));
                }
            }
            else
            {
                if (ship.IsPlayer || _receiver.IsPlayer)
                {
                    GameConsoleController.AddEntry(
                        $"[Выкуп] {_receiver.Name} отверг предложение {actualOffer} от {ship.Name}.");
                }
                else
                {
                    string rType = SRG.NpcAI.NpcConversationLog.PoolTypeOf(_receiver);
                    SRG.NpcAI.NpcConversationLog.Post(ship, _receiver,
                        "Money.PlayerSend",    $"Возьми {actualOffer} кредитов, только отвяжись.",
                        $"Money.{rType}No",    "Мало. Иди отсюда.",
                        ("<Money>", actualOffer.ToString()));
                }
            }

            _done = true;
            return true;
        }

        private static bool EvaluateMoneyRansomAcceptance(ShipData payer, ShipData receiver, int offer)
        {
            if (offer <= 0) return false;
            var p = receiver.Personality;
            if (p == null) return offer >= 500;

            // Базовый порог зависит от класса.
            var combatClass = NpcBrain.ResolveCombatClass(receiver.ShipTypeId);
            int threshold = combatClass switch
            {
                CombatClass.Civilian  => 100,
                CombatClass.Pirate    => Mathf.RoundToInt(500f - p.Greed * 4f),       // greedy: 100, low: 500
                CombatClass.Mercenary => Mathf.RoundToInt(800f - p.Greed * 6f),       // greedy: 200, low: 800
                CombatClass.Military  => Mathf.RoundToInt(2000f + p.Discipline * 30f),// disciplined: 5000, low: 2000
                _ => 1000
            };
            if (offer < threshold) return false;

            // У военных дополнительная защита — не примут, если payer недавно атаковал кого-то ещё (вендетта).
            if (combatClass == CombatClass.Military && receiver.CrimeRating > 30f) return false;

            // Шанс: чем больше offer относительно threshold, тем выше.
            float ratio = (float)offer / threshold;
            float chance = Mathf.Clamp01(0.3f + ratio * 0.4f);
            return UnityEngine.Random.value < chance;
        }

        public override bool IsCompleted(ShipData ship, StarData star) => _done;
        public override string DebugName => $"OfferMoney({_offerAmount}→{SpriteUtility.ShortId(_receiverUid)})";
    }

    /// <summary>Применяет +15 модификатор отношения всем военным с той же HomePlanetUid, что у beneficiary.
    /// Эмулирует механику Space Rangers HD: «взятка одному воину — благодарность всей планете».</summary>
    public static class CeasefireSolidarity
    {
        public static void ApplyHomePlanetBonus(ShipData payer, ShipData beneficiary, StarData star)
        {
            if (star == null || payer == null || beneficiary == null) return;
            if (string.IsNullOrEmpty(beneficiary.HomePlanetUid)) return;
            // Применяется только если beneficiary — военный (другие классы не «представляют» планету).
            if (NpcBrain.ResolveCombatClass(beneficiary.ShipTypeId) != CombatClass.Military) return;

            var ships = star.Ships;
            for (int i = 0; i < ships.Count; i++)
            {
                var s = ships[i];
                if (s == beneficiary || s.CurrentHull <= 0) continue;
                if (s.HomePlanetUid != beneficiary.HomePlanetUid) continue;
                if (NpcBrain.ResolveCombatClass(s.ShipTypeId) != CombatClass.Military) continue;
                s.Personality?.ApplyAllyBonus(payer.Uid);
            }
        }
    }
}
