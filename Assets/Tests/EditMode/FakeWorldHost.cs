using SRG.Config;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Simulation;

namespace SRG.Tests
{
    /// <summary>Минимальный <see cref="IWorldHost"/> для юнит-тестов: только то, что задано явно.</summary>
    internal sealed class FakeWorldHost : IWorldHost
    {
        public GalaxyData GeneratedGalaxy { get; set; }
        public GalaxyData CurrentTickingGalaxy { get; set; }
        public GalaxyGenerationContext Context { get; set; }
        public StarData CurrentStar { get; set; }
        public GameSettingsConfig Settings { get; set; }
        public TurnPhase Phase { get; set; }
        public TurnAnimationData LastTurnData { get; set; }
        public int PlanningRequests { get; private set; }
        public int InstantTurns { get; private set; }

        public void RequestPlanning(PlanningReason reason) => PlanningRequests++;
        public void ExecuteInstantTurn() => InstantTurns++;
    }
}
