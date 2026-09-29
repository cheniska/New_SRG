using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using SRG.Config;
using SRG.Core;
using SRG.Galaxy;
using SRG.Presentation.World;
using SRG.Ships.Movement;

namespace SRG.Ships.Player
{
    public partial class PlayerShip : MonoBehaviour, ISkillsCarrier
    {
        public static PlayerShip Instance { get; private set; }
        public ShipData ShipData { get; private set; }

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
        public static event System.Action OnRouteAssigned;
        public bool IsMovingThisTurn { get; private set; }
        public bool IsWeaponModeActive => _weaponModeActive;
        public bool IsDialogModeActive => _dialogModeActive;

        [SerializeField] private GameSettingsConfig _settings;
        private Camera _cam;
        private ShipVisualController _visual;
        private PathRenderer _pathRenderer;
        private bool _isTurnInProgress;
        private readonly Queue<Vector2> _targets = new Queue<Vector2>();
        // true при старте: маршрута ещё не было — иначе первый же ход без курса
        // ложно завершится «RouteCompleted» и прервёт симуляцию.
        private bool _routeCompleted = true;

        // Отслеживание движущейся цели
        private Func<Vector2> _trackedTargetFunc;
        private Vector2 _lastTrackedPos;
        private const float TrackUpdateThreshold = 0.01f;

        // Режим стрельбы
        private bool _weaponModeActive;

        // Режимы следования за кораблём — циклически переключаются повторным кликом по той же цели.
        // Режимы Board/Tow доступны только при наличии соответствующего оборудования; цикл их пропускает.
        public enum FollowMode
        {
            PursueAtMaxRange    = 0, // лететь и держаться на дистанции самой дальней пушки, не стреляя
            AttackWithLongRange = 1, // та же дистанция + стрельба только из самой дальней пушки
            AttackWithAllGuns   = 2, // подойти на дистанцию самой ближней пушки и стрелять всеми
            FollowClose         = 3, // следовать вплотную (без стрельбы)
            Board               = 4, // подойти на радиус крюка и взять на абордаж
            Tow                 = 5, // подойти на радиус буксира и взять на буксир
            LandOnShip          = 6, // сесть на корабль-носитель (ShipTypeConfig.CanBeLandedOn)
        }
        private const int FollowModeCount = 7;
        private string _followShipUid;
        private FollowMode _followMode;
        private const float FollowRangeFraction = 0.9f; // держимся чуть ближе требуемой дальности

        /// <summary>Активен ли режим следования за кораблём (любой из 4-х). Используется
        /// PlayerManager.CheckPauseConditions, чтобы менять правила выхода в planning:
        /// в автобое пауза запрашивается только при тяжёлых событиях.</summary>
        public bool IsFollowingShip => !string.IsNullOrEmpty(_followShipUid);
        public string FollowShipUid => _followShipUid;
        public FollowMode CurrentFollowMode => _followMode;
        /// <summary>Снимок CurrentHull игрока на момент начала текущего хода (до боевой симуляции).
        /// Используется для подсчёта урона за ход.</summary>
        public int HullBeforeLastSimulation { get; private set; } = -1;

        // Режим связи (диалога с кораблём)
        private bool _dialogModeActive;

        // Посадка / взлёт
        private PlanetData _landingPlanet; // только для логики маршрута; визуальное состояние — в ShipVisualController

        public void Init(ShipData shipData, Camera cam, GameSettingsConfig settings = null)
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[PlayerShip] Дубликат уничтожен.");
                Destroy(this);
                return;
            }
            Instance = this;

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

            HullBeforeLastSimulation = ShipData.CurrentHull;
            Debug.Log($"[PlayerShip] Init: {ShipData.Name} speed={ShipData.EngineSpeed}");
        }

        private void OnEnable()
        {
            GalaxyManager.OnTurnCalculate += OnTurnCalculate;
            GalaxyManager.OnTurnAnimate += OnTurnAnimate;
            GalaxyManager.OnTurnComplete += OnTurnComplete;
            PlayerManager.OnLeft += OnLeavePlanet;
        }

        private void OnDisable()
        {
            GalaxyManager.OnTurnCalculate -= OnTurnCalculate;
            GalaxyManager.OnTurnAnimate -= OnTurnAnimate;
            GalaxyManager.OnTurnComplete -= OnTurnComplete;
            PlayerManager.OnLeft -= OnLeavePlanet;
        }

        private void Update()
        {
            if (ShipData == null || ShipData.CurrentHull <= 0) return;
            if (!_isTurnInProgress)
            {
                HandleInput();
                UpdateTrackedTarget();
                UpdateFollowAutoFire();
            }
        }

        /// <summary>Пересчитать визуализацию запланированного маршрута. Вызывается извне
        /// (например, после активации форсажа через UI-слот), когда меняется параметр,
        /// влияющий на скорость или радиус разворота.</summary>
        public void RefreshPlannedRoute()
        {
            if (_pathRenderer == null) return;
            if (_targets.Count == 0 && _landingPlanet == null) return;
            RedrawPath();
        }
    }
}
