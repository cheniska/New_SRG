using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Core;
using SRG.Galaxy;
using SRG.Presentation.World;
using SRG.Ships;
using SRG.Ships.Player;
using SRG.Simulation;
using SRG.Presentation.Common;
using SRG.UI.HUD;

namespace SRG.Controllers
{
    /// <summary>
    /// Корабль игрока в сцене: ввод (клики, режимы наведения и связи), анимация хода и показ
    /// маршрута. Сами команды — маршрут, следование, посадка, выстрел, захват — исполняет
    /// <see cref="PlayerPilot"/> (без MonoBehaviour, тестируется headless); этот компонент
    /// переводит в них клики и рисует то, что пилот сообщает через <see cref="IPlayerPilotView"/>.
    /// </summary>
    public partial class PlayerShip : MonoBehaviour, ISkillsCarrier, IPlayerAvatar, IPlayerPilotView, IShipVisualOwner
    {
        public static PlayerShip Instance { get; private set; }
        public ShipData ShipData { get; private set; }
        /// <summary>Управление кораблём: маршрут, следование, посадка, выстрел, захват.</summary>
        public PlayerPilot Pilot { get; private set; }

        // ── ISkillsCarrier ────────────────────────────────────────────────────────
        // Делегирует в ShipData.Skills. SkillsConfig читается из GalaxyManager.
        private static SkillsConfig SkillsCfg =>
            GalaxyManager.Instance?.Context?.Config?.Skills;
        public int GetBaseSkill(SkillType skill)
            => ShipData?.Skills?.GetBase(skill) ?? 0;
        public int GetEffectiveSkill(SkillType skill)
            => ShipData?.Skills?.GetEffective(skill, SkillsCfg) ?? 0;
        public void IncPoints(int amount, ExpCategory category)
            => ShipData?.Skills?.IncPoints(amount, category, SkillsCfg);
        public bool IsMovingThisTurn { get; private set; }
        public bool IsWeaponModeActive => _weaponModeActive;
        Transform IPlayerAvatar.Transform => transform;
        public bool IsDialogModeActive => _dialogModeActive;

        [SerializeField] private GameSettingsConfig _settings;
        private Camera _cam;
        private ShipVisualController _visual;
        /// <summary>Визуал корпуса (2D или 3D, см. VisualFactory).</summary>
        public ShipVisualController Visual => _visual;
        private PathRenderer _pathRenderer;
        private bool _isTurnInProgress;

        // Режим стрельбы
        private bool _weaponModeActive;

        // Режим связи (диалога с кораблём)
        private bool _dialogModeActive;

        public void Init(ShipData shipData, Camera cam, GameSettingsConfig settings = null)
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[PlayerShip] Дубликат уничтожен.");
                Destroy(this);
                return;
            }
            Instance = this;
            PresentationContext.Player = this;

            ShipData = shipData;
            _cam = cam;
            if (settings != null) _settings = settings;

            _visual = GetComponent<ShipVisualController>();

            _pathRenderer = GetComponentInChildren<PathRenderer>();
            if (_pathRenderer == null)
            {
                var go = new GameObject("PathRenderer");
                go.transform.SetParent(transform, worldPositionStays: false);
                _pathRenderer = go.AddComponent<PathRenderer>();
            }

            Pilot = new PlayerPilot(shipData, _settings, this);
            Debug.Log($"[PlayerShip] Init: {ShipData.Name} speed={ShipData.EngineSpeed}");
        }

        private void OnEnable()
        {
            GameWorld.OnTurnCalculate += OnTurnCalculate;
            GameWorld.OnTurnAnimate += OnTurnAnimate;
            GameWorld.OnTurnComplete += OnTurnComplete;
            PlayerManager.OnLeft += OnLeavePlanet;
        }

        private void OnDisable()
        {
            GameWorld.OnTurnCalculate -= OnTurnCalculate;
            GameWorld.OnTurnAnimate -= OnTurnAnimate;
            GameWorld.OnTurnComplete -= OnTurnComplete;
            PlayerManager.OnLeft -= OnLeavePlanet;
        }

        private void Update()
        {
            // Ход считается в фоне — мир меняется, не читаем и не трогаем его (см. GameWorld.IsCalculating).
            if (GameWorld.IsCalculating) return;
            if (ShipData == null || ShipData.CurrentHull <= 0) return;
            if (!_isTurnInProgress)
            {
                HandleInput();
                Pilot.UpdateBetweenTurns();
            }
        }

        /// <summary>Взлёт из посадочного UI (PlayerManager.OnLeft).</summary>
        private void OnLeavePlanet() => Pilot?.Takeoff();

        void IShipVisualOwner.OnShipVisualReplaced(ShipVisualController visual) => _visual = visual;

        // ── IPlayerPilotView ──────────────────────────────────────────────────────

        void IPlayerPilotView.DrawRoute(List<Vector2> chain, float dayStep, Vector2? endMarker)
            => _pathRenderer.DrawPath(chain, dayStep, preSampled: true, endMarkerOverride: endMarker);
        void IPlayerPilotView.ClearRoute() => _pathRenderer?.ClearPath();
        void IPlayerPilotView.OnLanded(PlanetData planet) => _visual?.NotifyLanded(planet);
        void IPlayerPilotView.OnDockedOnShip() => _visual?.NotifyDockedOnShip();
        void IPlayerPilotView.OnTakeoff() => _visual?.BeginTakeoff();
        void IPlayerPilotView.OnLandingCancelled() => _visual?.SetAlpha(1f);
        void IPlayerPilotView.ShowMessage(string text) => HudMessageController.Show(text);
    }
}
