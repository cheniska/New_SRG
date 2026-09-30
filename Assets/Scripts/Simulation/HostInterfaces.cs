using UnityEngine;
using SRG.Config;
using SRG.Galaxy;
using SRG.Galaxy.Generation;

namespace SRG.Simulation
{
    /// <summary>
    /// Состояние мира и цикл хода. Реализуется <c>SRG.Core.GalaxyManager</c>;
    /// в тестах — любой заглушкой. Симуляция обращается к нему только через <see cref="GameWorld"/>.
    /// </summary>
    public interface IWorldHost
    {
        /// <summary>Активная галактика (та, где игрок).</summary>
        GalaxyData GeneratedGalaxy { get; }
        /// <summary>Галактика, чей GalaxyNextDay сейчас исполняется (вне тика — null).</summary>
        GalaxyData CurrentTickingGalaxy { get; }
        GalaxyGenerationContext Context { get; }
        StarData CurrentStar { get; }
        GameSettingsConfig Settings { get; }
        TurnPhase Phase { get; }
        TurnAnimationData LastTurnData { get; }

        void RequestPlanning(PlanningReason reason);
        void ExecuteInstantTurn();
    }

    /// <summary>
    /// Игрок: корабль, посадка, смерть. Реализуется <c>SRG.Core.PlayerManager</c>.
    /// </summary>
    public interface IPlayerHost
    {
        ShipData PlayerShipData { get; }
        ShipData GetOrFindPlayerShip();
        /// <summary>UID корабля, за которым игрок сейчас летит (режим «следовать»), либо null.</summary>
        string FollowShipUid { get; }

        void LandOn(ILandingSite site);
        void DockOnShip(ShipData carrier);
        void LeavePlanet();
        bool KillPlayer(PlayerDeathCause cause, string killerName = null, string killerOwner = null);
    }

    /// <summary>
    /// Визуальное представление системы. Реализуется <c>SRG.Core.SystemViewManager</c>.
    /// Симуляция просит создать визуал для корабля, появившегося посреди хода (дроны и т.п.).
    /// </summary>
    public interface IViewHost
    {
        void EnsureShipVisual(ShipData ship);
    }

    /// <summary>
    /// Запросы к графике, которые влияют на данные (размер спрайта → радиус и т.п.).
    /// Реализуется <c>SRG.Presentation.Common.GraphicsManager</c>.
    /// </summary>
    public interface IGraphicsQuery
    {
        Sprite[] GetSpriteSheet(string path);
        string[] EnumerateSheetPathsInFolder(string folderPath);
    }

    /// <summary>
    /// Диалоговые окна, которые может инициировать симуляция (NPC вызывает игрока на связь).
    /// Реализуется <c>SRG.UI.Screens.DialogUIController</c>.
    /// </summary>
    public interface IDialogPresenter
    {
        /// <summary>Открыть входящий космический диалог. false — открыть не удалось (занято, нет конфига).</summary>
        bool OpenIncomingSpaceDialog(string dialogId, ShipData initiator);
        void ShowImprovementDialog(ShipData player);
    }
}
