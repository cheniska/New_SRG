using SRG.NpcAI;
using SRG.Ships.Player;

namespace SRG.Ships
{
    /// <summary>
    /// Контракт носителя пилот-скилов (PlayerShip, в будущем — NpcController).
    /// База скила хранится в [0..ProgressionCap] и прокачивается за FreePoints.
    /// Эффективный = база + бонусы оборудования + дельты болезней/стимуляторов,
    /// зажат в [EffectiveMin..EffectiveMax].
    /// </summary>
    public interface ISkillsCarrier
    {
        int GetBaseSkill(SkillType skill);
        int GetEffectiveSkill(SkillType skill);
        void IncPoints(int amount, ExpCategory category);
    }
}
