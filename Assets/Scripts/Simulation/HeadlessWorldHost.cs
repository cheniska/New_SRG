using System.Collections.Generic;
using SRG.Config;
using SRG.Galaxy;
using SRG.Galaxy.Generation;

namespace SRG.Simulation
{
    /// <summary>
    /// <see cref="IWorldHost"/> без сцены: прогон симуляции в тестах, бенчмарках и утилитах.
    /// Ход = <see cref="SimulationSession.SimulateDay"/> + финализация гиперпрыжков (то, что
    /// GalaxyManager делает по окончании анимации), события хода поднимаются так же.
    /// </summary>
    public sealed class HeadlessWorldHost : IWorldHost
    {
        public SimulationSession Session { get; }
        public GameSettingsConfig Settings { get; }
        public StarData CurrentStar { get; set; }
        public TurnAnimationData LastTurnData { get; private set; }
        public TurnPhase Phase => TurnPhase.Planning;

        /// <summary>Причины паузы, запрошенные симуляцией за время жизни хоста.</summary>
        public HashSet<PlanningReason> PlanningRequests { get; } = new();

        public GalaxyData GeneratedGalaxy => Session.ActiveGalaxy;
        public GalaxyData CurrentTickingGalaxy => Session.CurrentTickingGalaxy;
        public GalaxyGenerationContext Context => Session.Context;

        public HeadlessWorldHost(SimulationSession session, GameSettingsConfig settings = null)
        {
            Session = session;
            Settings = settings;
        }

        public void RequestPlanning(PlanningReason reason) => PlanningRequests.Add(reason);

        public void ExecuteInstantTurn() => Step();

        /// <summary>Один полный ход.</summary>
        public TurnAnimationData Step()
        {
            LastTurnData = Session.SimulateDay();
            if (LastTurnData == null) return null;
            GameWorld.RaiseTurnCalculate(LastTurnData);
            GameWorld.RaiseTurnComplete(LastTurnData);
            Session.FinalizeHyperjumpPhases();
            return LastTurnData;
        }
    }
}
