using SRG.Galaxy.Politics;
using SRG.Ships;

namespace SRG.NpcAI
{
    public static class DecisionTable
    {
        // Категории отношений (см. OwnerRaceRelationsManager):
        //   Hostile   (1..20)   — атака всех, кроме невоюющих случаев
        //   Bad       (21..40)  — атака только не-Civilian
        //   Neutral   (41..60)  — игнор
        //   Good      (61..80)  — игнор
        //   Excellent (81..100) — защита (особенно Civilian)
        public static TargetDecision Evaluate(int relation, CombatClass targetClass)
        {
            var level = OwnerRaceRelationsManager.ToLevel(relation);

            switch (level)
            {
                case RelationLevel.Hostile:
                    return TargetDecision.Attack;

                case RelationLevel.Bad:
                    return targetClass == CombatClass.Civilian ? TargetDecision.Ignore : TargetDecision.Attack;

                case RelationLevel.Normal:
                case RelationLevel.Good:
                    return TargetDecision.Ignore;

                case RelationLevel.Best:
                    return targetClass == CombatClass.Civilian ? TargetDecision.Protect : TargetDecision.Ignore;
            }

            return TargetDecision.Ignore;
        }
    }
}
