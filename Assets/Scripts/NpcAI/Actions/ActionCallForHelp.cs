using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Galaxy.Politics;
using SRG.Ships;

namespace SRG.NpcAI.Actions
{
    /// <summary>
    /// «SOS»: атакованный NPC зовёт на помощь союзников в текущей звезде.
    /// Один тик — рассылает personal-модификатор Vendetta-стиля всем подходящим NPC:
    /// они становятся враждебны к атакующему и через обычный EvaluateSituation выберут атаку.
    ///
    /// Кто зовёт кого (по CombatClass):
    ///   Civilian  → Civilian + Mercenary + Military (все, кроме пиратов).
    ///   Pirate    → только Pirate.
    ///   Mercenary → Mercenary + Military (вольные пилоты зовут «власть»).
    ///   Military  → не зовёт (у них своя доктрина, атакуют сами).
    ///
    /// Дополнительно: не зовёт своего атакующего (само собой), не зовёт мёртвых/пристыкованных,
    /// не зовёт тех, кто уже атакует того же — иначе шум. Помощники должны быть не-hostile
    /// к жертве по фракции.
    ///
    /// Лимит: не более <see cref="MaxHelpers"/> помощников за один вызов — больше нужен редко
    /// и превращает панель уведомлений в спам. Обход прекращается, как только достигнут лимит.
    ///
    /// Кулдаун — <see cref="NpcBalance.LastAttackerMemoryTurns"/>/2 ходов на корабль,
    /// хранится в <see cref="ShipData.LastHelpCallTurn"/> (сериализуется).
    /// </summary>
    public class ActionCallForHelp : NpcAction
    {
        /// <summary>Максимум помощников, которых жертва обзвонит за один вызов.
        /// Даёт до 3 индивидуальных диалогов в панели и достаточно поддержки в бою.</summary>
        public const int MaxHelpers = 3;

        private readonly string _attackerUid;

        public ActionCallForHelp(string attackerUid) => _attackerUid = attackerUid;

        public override string DebugName => "CallForHelp";

        public override bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            if (ship == null || star == null || string.IsNullOrEmpty(_attackerUid))
            { IsCompleted = true; return true; }
            var attacker = FindShip(star, _attackerUid);
            if (attacker == null || attacker.CurrentHull <= 0) { IsCompleted = true; return true; }

            int turn = SRG.Core.GalaxyManager.Instance?.GeneratedGalaxy?.CurrentTurn ?? 0;
            var victimClass = NpcBrain.ResolveCombatClass(ship.ShipTypeId);
            int helpers = 0;
            var rel = OwnerRaceRelationsManager.Instance;
            string vType = NpcConversationLog.PoolTypeOf(ship);

            foreach (var ally in ShipQuery.In(star).Excluding(ship).Enumerate())
            {
                if (helpers >= MaxHelpers) break;
                if (ally == attacker) continue;
                if (rel != null && rel.AreHostile(ship, ally)) continue;
                var allyClass = NpcBrain.ResolveCombatClass(ally.ShipTypeId);
                if (!IsEligibleHelper(victimClass, allyClass)) continue;

                // Персональная вражда к атакующему: помощник через свой EvaluateSituation
                // выберет PursueAndAttack. Не форсируем ForceActivity — уважаем его текущие
                // директивы (ГШ, партнёрство).
                ally.Personality?.ApplyHostilityPenalty(attacker.Uid);

                // Индивидуальное уведомление на каждого помощника — заголовок «жертва → помощник»,
                // разные реплики из пула. Это читается как реальный радио-обмен, а не сводка.
                NpcConversationLog.Post(ship, ally,
                    $"Help.{vType}Send", "SOS! На меня напали! Помогите!",
                    "Help.Answer",       "Приняли. Идём.");

                helpers++;
            }

            ship.LastHelpCallTurn = turn;
            IsCompleted = true;
            return true;
        }

        private static bool IsEligibleHelper(CombatClass victim, CombatClass ally)
        {
            switch (victim)
            {
                case CombatClass.Civilian:
                    return ally == CombatClass.Civilian
                        || ally == CombatClass.Mercenary
                        || ally == CombatClass.Military;
                case CombatClass.Pirate:
                    return ally == CombatClass.Pirate;
                case CombatClass.Mercenary:
                    return ally == CombatClass.Mercenary
                        || ally == CombatClass.Military;
                default:
                    return false; // Military сам разбирается
            }
        }
    }
}
