using System;
using SRG.Config;
using SRG.Galaxy;
using SRG.Galaxy.Generation;

namespace SRG.Simulation
{
    /// <summary>
    /// Точка доступа симуляции к окружению (composition root).
    ///
    /// Симуляция (Galaxy, Ships, NpcAI, Combat, Equipment, Economy, Science, Dialog) не знает
    /// ни о MonoBehaviour-менеджерах, ни об UI: всё, что ей нужно снаружи, описано интерфейсами
    /// в <see cref="IWorldHost"/>/<see cref="IPlayerHost"/>/… и подключается сюда на старте
    /// слоем приложения (<c>SRG.Core</c>), презентацией и UI.
    ///
    /// Любой хост может отсутствовать (тесты, headless-прогон) — тогда свойства возвращают
    /// значения по умолчанию, а запросы к UI/визуалу игнорируются.
    /// </summary>
    public static class GameWorld
    {
        public static IWorldHost Host { get; private set; }
        public static IPlayerHost Player { get; private set; }
        public static IViewHost View { get; private set; }
        public static IGraphicsQuery Graphics { get; private set; }
        public static IDialogPresenter Dialogs { get; private set; }

        /// <summary>Резолвер Owner/Race-вариантов графики корпуса: (basePath, owner, race) → path.
        /// Без резолвера используется базовый путь.</summary>
        public static Func<string, string, string, string> ShipSheetResolver { get; set; }

        // ── Регистрация хостов ─────────────────────────────────────────────────
        // Detach снимает хост только если он всё ещё текущий: иначе уничтожение старого
        // объекта при перезагрузке сцены затёрло бы уже зарегистрированный новый.

        public static void Attach(IWorldHost host) => Host = host;
        public static void Detach(IWorldHost host) { if (ReferenceEquals(Host, host)) Host = null; }

        public static void Attach(IPlayerHost host) => Player = host;
        public static void Detach(IPlayerHost host) { if (ReferenceEquals(Player, host)) Player = null; }

        public static void Attach(IViewHost host) => View = host;
        public static void Detach(IViewHost host) { if (ReferenceEquals(View, host)) View = null; }

        public static void Attach(IGraphicsQuery host) => Graphics = host;
        public static void Detach(IGraphicsQuery host) { if (ReferenceEquals(Graphics, host)) Graphics = null; }

        public static void Attach(IDialogPresenter host) => Dialogs = host;
        public static void Detach(IDialogPresenter host) { if (ReferenceEquals(Dialogs, host)) Dialogs = null; }

        // ── Состояние мира (делегирует в Host) ─────────────────────────────────

        public static bool IsAttached => Host != null;
        public static GalaxyData GeneratedGalaxy => Host?.GeneratedGalaxy;
        public static GalaxyData CurrentTickingGalaxy => Host?.CurrentTickingGalaxy;
        /// <summary>Галактика, которой адресуются события: тикающая, а вне тика — активная.</summary>
        public static GalaxyData TargetGalaxy => Host?.CurrentTickingGalaxy ?? Host?.GeneratedGalaxy;
        public static GalaxyGenerationContext Context => Host?.Context;
        public static StarData CurrentStar => Host?.CurrentStar;
        public static GameSettingsConfig Settings => Host?.Settings;
        public static TurnPhase Phase => Host?.Phase ?? TurnPhase.Planning;
        public static TurnAnimationData LastTurnData => Host?.LastTurnData;

        public static void RequestPlanning(PlanningReason reason) => Host?.RequestPlanning(reason);
        public static void ExecuteInstantTurn() => Host?.ExecuteInstantTurn();

        // ── События хода ───────────────────────────────────────────────────────
        // Поднимаются хостом (GalaxyManager) через Raise*; подписчики — и симуляция, и UI.

        /// <summary>После расчёта симуляции, до анимации.</summary>
        public static event Action<TurnAnimationData> OnTurnCalculate;
        /// <summary>Каждый кадр фазы Simulation: (прогресс 0..1, текущий сабтёрн).</summary>
        public static event Action<float, int> OnTurnAnimate;
        /// <summary>После окончания анимации хода.</summary>
        public static event Action<TurnAnimationData> OnTurnComplete;

        public static void RaiseTurnCalculate(TurnAnimationData data) => OnTurnCalculate?.Invoke(data);
        public static void RaiseTurnAnimate(float progress, int subTurn) => OnTurnAnimate?.Invoke(progress, subTurn);
        public static void RaiseTurnComplete(TurnAnimationData data) => OnTurnComplete?.Invoke(data);

        // ── Игрок ──────────────────────────────────────────────────────────────

        public static ShipData PlayerShip => Player?.GetOrFindPlayerShip();

        /// <summary>Сбросить все хосты и подписки. Для тестов.</summary>
        public static void ResetForTests()
        {
            Host = null; Player = null; View = null; Graphics = null; Dialogs = null;
            ShipSheetResolver = null;
            OnTurnCalculate = null; OnTurnAnimate = null; OnTurnComplete = null;
        }
    }
}
