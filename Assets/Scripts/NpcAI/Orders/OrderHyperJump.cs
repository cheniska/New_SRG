using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Ships.Movement;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.NpcAI.Orders
{
    /// <summary>
    /// Запрашивает многоходовый гиперпереход через HyperjumpController. Сам приказ "выполнен" как только
    /// фаза установлена и корабль начал лететь к краю — дальнейшая логика (топливо, миграция, фазы)
    /// крутится в StarNextDay автоматически.
    /// </summary>
    public class OrderHyperJump : NpcOrder
    {
        private readonly string _targetStarUid;
        private bool _requested;

        public OrderHyperJump(string targetStarUid) => _targetStarUid = targetStarUid;

        public override bool Execute(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            if (ship.CurrentStarUid == _targetStarUid) return true;

            if (!_requested && ship.HyperjumpPhase == HyperjumpPhase.None)
            {
                var galaxy = GameWorld.GeneratedGalaxy;
                if (galaxy != null && galaxy.StarsMap.TryGetValue(_targetStarUid, out var target))
                    _requested = HyperjumpController.RequestJump(ship, target, galaxy);
            }
            return _requested && ship.HyperjumpPhase != HyperjumpPhase.None;
        }

        public override bool IsCompleted(ShipData ship, StarData star)
            => ship.CurrentStarUid == _targetStarUid || _requested;

        public override string DebugName => $"HyperJump({SpriteUtility.ShortId(_targetStarUid)})";
    }
}
