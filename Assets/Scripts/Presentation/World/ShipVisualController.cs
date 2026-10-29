using UnityEngine;
using System.Collections.Generic;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Presentation.Common;
using SRG.Presentation.Effects;
using SRG.Ships;
using SRG.Ships.Movement;
using SRG.Utils;

namespace SRG.Presentation.World
{
    /// <summary>
    /// Визуал корабля в системе — общая часть для любого способа отрисовки корпуса.
    ///
    /// Здесь «что показать»: позиция по кадрам хода, курс и плавный поворот, масштаб по корпусу,
    /// прозрачность посадки/взлёта/гиперперехода, обнаружение посадки и взлёта, выхлоп двигателей
    /// и взрыв. «Как нарисовать корпус» — в наследниках: <see cref="ShipVisualController2D"/>
    /// (спрайт-лист с клипами) и <see cref="ShipVisualController3D"/> (модель). Выбор — по
    /// <c>GameSettingsConfig.ShipRenderMode</c> через <see cref="ShipVisualFactory"/>.
    /// </summary>
    public abstract class ShipVisualController : MonoBehaviour
    {
        protected ShipData Data { get; private set; }
        /// <summary>Порядок отрисовки корпуса (SortingLayerRegistry): предмет / NPC / игрок.</summary>
        protected int BodySortingOrder { get; private set; }

        private float _targetAngle;
        private float _currentAngle;
        private const float RotationSpeed = 180f;  // градусов/сек

        private readonly List<ThrusterInstance> _thrusters = new List<ThrusterInstance>();
        private float _alpha = 1f;

        private Sprite _minimapIcon;

        // Посадка / взлёт
        public bool IsTakingOff { get; private set; }
        private bool _isLandingThisTurn;
        private PlanetData _landingPlanet;
        private string _prevLandedPlanetUid;
        private string _prevLandedShipUid;   // стыковка с носителем (LandedOnShipUid)

        private class ThrusterInstance
        {
            public SpriteRenderer Renderer;
            public Sprite[] Frames;
            public float SecPerFrame;
            public float Timer;
            public int CurrentFrame;
        }

        // ── Отрисовка корпуса (наследники) ───────────────────────────────────────

        /// <summary>Построить корпус по <see cref="Data"/> (графика из GetBodyGraphicPath).</summary>
        protected abstract void BuildBody();
        /// <summary>Пересобрать корпус после смены графики (новый корпус, маскировка).</summary>
        protected abstract void RebuildBody();
        /// <summary>Прозрачность корпуса 0..1.</summary>
        protected abstract void ApplyBodyAlpha(float alpha);
        /// <summary>Покадровое обновление корпуса (анимация клипов и т.п.).</summary>
        protected virtual void TickBody(float dt) { }
        /// <summary>Конец хода: вернуть корпус в состояние покоя.</summary>
        protected virtual void ResetBodyAnimation() { }
        /// <summary>Перед взрывом на этом GameObject: убрать то, что не должно рисоваться поверх.</summary>
        protected virtual void PrepareBodyForExplosion() { }
        /// <summary>Снять с GameObject всё, что создал этот визуал (замена 2D ↔ 3D на лету).</summary>
        public virtual void Teardown()
        {
            foreach (var t in _thrusters)
                if (t.Renderer != null) Destroy(t.Renderer.gameObject);
            _thrusters.Clear();
        }

        /// <summary>Состояние движения корпуса: «Move» / «Idle». Спрайтовый визуал переключает клип.</summary>
        public virtual void SetAnimation(string clipName) { }

        // ── Настройка ────────────────────────────────────────────────────────────

        public void Setup(ShipData data, bool isPlayer = false)
        {
            Data = data;

            // Контейнеры/предметы в космосе (IsItem=true) — отдельный слой ниже кораблей.
            if (data != null && data.IsItem)
                BodySortingOrder = SortingLayerRegistry.Get(SortLayer.DroppedItem);
            else
                BodySortingOrder = isPlayer
                    ? SortingLayerRegistry.Get(SortLayer.ShipPlayer)
                    : SortingLayerRegistry.Get(SortLayer.ShipNpc);

            BuildBody();
            LoadMinimapIcon();
            SpawnThrusters();
            ApplyHullSizeScale();
            SetAnimation("Idle");

            // Если корабль появляется уже в фазе прибытия — снапаем поворот к нужному heading,
            // иначе MoveTowardsAngle разворачивает его на 180° за ~1 сек (визуально некрасиво).
            // Также сразу делаем его невидимым: HyperArrive держит корабль скрытым весь ход,
            // а HyperExit начинает fade-in с 0.
            if (Data != null && (Data.HyperjumpPhase == HyperjumpPhase.HyperArrive
                                 || Data.HyperjumpPhase == HyperjumpPhase.HyperExit))
            {
                SnapRotationToHyperjumpHeading();
                SetAlpha(0f);
            }

            // Корабль грузится уже посаженным (load save или появление в системе на планете) —
            // визуал не должен крутиться в космосе. У игрока эту роль выполняет SetSystemVisible.
            // Зеркалим _prevLandedPlanetUid из текущего состояния: если HandleLanded успеет
            // очистить LandedPlanetUid между Setup и первым PrepareForTurn (трейдер взлетает
            // в тот же ход, что игрок прилетел в систему), PrepareForTurn увидит prev=planet,
            // current=null и корректно вызовет BeginTakeoff. Без этого корабль остался бы
            // невидимым после взлёта.
            if (Data != null && !Data.IsPlayer)
            {
                _prevLandedPlanetUid = Data.LandedPlanetUid;
                _prevLandedShipUid   = Data.LandedOnShipUid;
                if (!string.IsNullOrEmpty(Data.LandedPlanetUid) || !string.IsNullOrEmpty(Data.LandedOnShipUid))
                    gameObject.SetActive(false);
            }

            // Игрок грузится пристыкованным к носителю — скрываем корпус (система при этом видима).
            if (Data != null && Data.IsPlayer && !string.IsNullOrEmpty(Data.LandedOnShipUid))
            {
                _prevLandedShipUid = Data.LandedOnShipUid;
                SetAlpha(0f);
            }
        }

        /// <summary>Перезагрузить графику корпуса (после смены установленного корпуса или применения override).</summary>
        public void ReloadBodyGraphic()
        {
            RebuildBody();
            ApplyHullSizeScale();
            ApplyBodyAlpha(_alpha);
            SetAnimation("Idle");
        }

        // Масштаб корпуса в космосе растёт с HP: на базовом HP корпус равен «игровому»
        // (localScale = 1), при удвоении MaxHull — ×1.25. Множитель общий с геймплейным
        // SpriteWorldSize (см. ShipFactory.HullSizeMultiplier), чтобы визуал и логические
        // дистанции (абордаж/стыковка/стоп) не расходились.
        private void ApplyHullSizeScale()
        {
            if (Data == null) return;
            transform.localScale = Vector3.one * ShipFactory.HullSizeMultiplier(Data);
        }

        private void LoadMinimapIcon()
        {
            if (string.IsNullOrEmpty(Data?.MinimapIconPath)) return;
            _minimapIcon = GraphicsManager.Instance?.TryGetSprite(Data.MinimapIconPath);
            if (_minimapIcon == null)
                Debug.LogWarning($"[ShipVisualController] Minimap icon not found: {Data.MinimapIconPath}");
        }

        public Sprite GetMinimapIcon() => _minimapIcon;

        // ── Поворот ──────────────────────────────────────────────────────────────

        public void SetMovementDirection(Vector2 direction)
        {
            // Станции (FixedRotation-корпус) не поворачивают графику по курсу — висят фиксированно.
            if (Data != null && Data.SpriteFixedRotation) return;
            if (direction.sqrMagnitude < 0.001f) return;
            _targetAngle = Angles.Of(direction) * Mathf.Rad2Deg - 90f;
        }

        /// <summary>Мгновенно ставит визуал в направление текущего ship.HyperjumpHeading — без плавного разворота.</summary>
        public void SnapRotationToHyperjumpHeading()
        {
            if (Data == null || float.IsNaN(Data.HyperjumpHeading)) return;
            _targetAngle = Data.HyperjumpHeading * Mathf.Rad2Deg - 90f;
            _currentAngle = _targetAngle;
            transform.rotation = Quaternion.Euler(0f, 0f, _currentAngle);
        }

        /// <summary>Мгновенно повернуть визуал по текущему курсу корабля (после замены визуала).</summary>
        public void SnapRotationToHeading()
        {
            if (Data == null || Data.SpriteFixedRotation || float.IsNaN(Data.CurrentHeading)) return;
            _targetAngle = Data.CurrentHeading * Mathf.Rad2Deg - 90f;
            _currentAngle = _targetAngle;
            transform.rotation = Quaternion.Euler(0f, 0f, _currentAngle);
        }

        // ── Выхлоп двигателей (спрайты при любом корпусе) ────────────────────────

        public void SetThrustersActive(bool active)
        {
            // Пристыкованный к носителю корабль скрыт — выхлоп тяг не должен «светиться» отдельно.
            if (active && Data != null && !string.IsNullOrEmpty(Data.LandedOnShipUid)) return;
            if (active) RefreshExhaustColor();
            foreach (var t in _thrusters)
                if (t.Renderer != null)
                    t.Renderer.gameObject.SetActive(active);
        }

        /// <summary>
        /// Подкрашивает выхлоп всех тяг текущим цветом из ItemInstance.ParamStrings["ExhaustColor"]
        /// установленного двигателя. Если двигателя нет или цвет не задан — белый (без тинта).
        /// Вызывается при включении тяг и сразу после смены двигателя.
        /// </summary>
        public void RefreshExhaustColor()
        {
            if (_thrusters.Count == 0 || Data == null) return;
            Color tint = ResolveExhaustColor(Data);
            foreach (var t in _thrusters)
            {
                if (t.Renderer == null) continue;
                var c = t.Renderer.color;
                t.Renderer.color = new Color(tint.r, tint.g, tint.b, c.a);
            }
        }

        private static Color ResolveExhaustColor(ShipData ship)
        {
            var engine = EquipmentSystem.GetEquipped(ship, SlotKeys.Engine);
            string hex = engine?.GetParamString("ExhaustColor");
            if (!string.IsNullOrEmpty(hex)
                && ColorUtility.TryParseHtmlString(hex, out var parsed))
                return parsed;
            return Color.white;
        }

        private void SpawnThrusters()
        {
            // Станции неподвижны — выхлоп двигателей не нужен.
            if (Data != null && Data.SpriteFixedRotation) return;
            if (Data?.Thrusters == null || Data.Thrusters.Count == 0) return;

            foreach (var thrusterData in Data.Thrusters)
            {
                var go = new GameObject("Thruster");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(thrusterData.LocalOffset.x, thrusterData.LocalOffset.y, 0f);
                go.SetActive(false); // выключены по умолчанию

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = SortingLayerRegistry.Get(SortLayer.ShipThruster);

                Sprite[] frames = null;
                if (!string.IsNullOrEmpty(thrusterData.SpritesheetPath) && GraphicsManager.Instance != null)
                {
                    frames = GraphicsManager.Instance.GetSpriteSheet(thrusterData.SpritesheetPath);
                    if (frames?.Length > 0)
                        sr.sprite = frames[0];
                }

                float secPerFrame = thrusterData.FPS > 0f ? 1f / thrusterData.FPS : 1f / 12f;
                _thrusters.Add(new ThrusterInstance
                {
                    Renderer = sr,
                    Frames = frames,
                    SecPerFrame = secPerFrame,
                    Timer = 0f,
                    CurrentFrame = 0
                });
            }
        }

        // ── Ход: посадка, взлёт, анимация ────────────────────────────────────────

        // Вызывается в начале хода (после симуляции, до анимации).
        // Вычисляет isLandingThisTurn и автоматически обнаруживает взлёт для НПС-кораблей.
        public void PrepareForTurn(TurnAnimationData anim, StarData star)
        {
            // Обнаружение взлёта: был посажен, сейчас нет, BeginTakeoff ещё не вызывался явно
            if (_prevLandedPlanetUid != null && Data.LandedPlanetUid == null && !IsTakingOff)
            {
                var p = star?.FindPlanet(_prevLandedPlanetUid);
                if (p != null) _landingPlanet = p;
                BeginTakeoff();
                // Кадры этого хода в StarSimulator были посчитаны, когда корабль ещё был посажен
                // (LandedPlanetUid set → сабтёрны не двигались, все SubTurns[i] = позиция корабля
                // на момент начала симуляции = позиция планеты на пред. ход). BeginTakeoff обновил
                // Data.Position/transform на текущую планету, но AnimateTurn перезапишет transform
                // старыми SubTurns. Синхронизируем и кадры хода, и PreviousPosition — fade-in
                // сыграет на планете, а следующий ход стартует без визуального скачка.
                if (p != null && anim != null && anim.ShipFrames.TryGetValue(Data.Uid, out var takeoffFrames))
                {
                    Vector2 planetPos = OrbitMath.GetPlanetWorldPosition(p);
                    for (int i = 0; i < takeoffFrames.SubTurns.Length; i++)
                        takeoffFrames.SubTurns[i] = planetPos;
                    Data.PreviousPosition = planetPos;
                }
            }

            // Обнаружение расстыковки с носителем (в т.ч. авто-освобождение при гибели носителя):
            // позиция уже актуальна (ShipDockingService.Undock), нужен только fade-in.
            if (_prevLandedShipUid != null && string.IsNullOrEmpty(Data.LandedOnShipUid) && !IsTakingOff)
                BeginTakeoff();
            _prevLandedShipUid = Data.LandedOnShipUid;

            // Обнаружение посадки: был в полёте, теперь сел
            if (Data.LandedPlanetUid != null && _prevLandedPlanetUid == null)
            {
                var p = star?.FindPlanet(Data.LandedPlanetUid);
                if (p != null) _landingPlanet = p;
            }

            _prevLandedPlanetUid = Data.LandedPlanetUid;

            // Двухфазная посадка (единая для планеты и корабля-носителя через
            // ILandingSite): ставим LandingPhase=Fading если хвост хода попадает внутрь site.LandingRadius
            // от site.CenterPosition (для носителя — конечная позиция его SubTurns; для планеты
            // CenterPosition уже пересчитан по OrbitMath после симуляции). Финализация — в
            // PlayerPilot.TryFinalizeLanding по тому же LandingPhase.Fading.
            _isLandingThisTurn = false;
            var landingTarget = LandingSiteRegistry.ResolveActiveTarget(Data, star);
            bool hasTarget = landingTarget != null;
            if (hasTarget && anim.ShipFrames.TryGetValue(Data.Uid, out var frames))
            {
                var finalPos = frames.SubTurns[GalaxyData.SubTurnsPerTurn];
                Vector2 targetEnd = landingTarget.CenterPosition;
                // Для носителя-корабля CenterPosition = его текущая Position, а нужна конечная
                // позиция после этого хода — берём из его SubTurns, если посчитаны.
                if (landingTarget is ShipData carrier
                    && anim.ShipFrames.TryGetValue(carrier.Uid, out var cf))
                    targetEnd = cf.SubTurns[GalaxyData.SubTurnsPerTurn];

                float r = landingTarget.LandingRadius;
                _isLandingThisTurn = (finalPos - targetEnd).sqrMagnitude < r * r;
            }
            Data.LandingPhase = _isLandingThisTurn
                ? LandingPhase.Fading
                : (hasTarget ? LandingPhase.Approach : LandingPhase.None);
        }

        // Вызывается игровой логикой при подтверждении посадки.
        public void NotifyLanded(PlanetData planet)
        {
            _landingPlanet      = planet;
            _prevLandedPlanetUid = planet.Uid; // синхронизируем, чтобы PrepareForTurn не детектировал повторно
        }

        /// <summary>Вызывается в момент стыковки с кораблём-носителем (LandedOnShipUid уже
        /// проставлен): мгновенно скрывает корпус, без планетного fade-пайплайна.</summary>
        public void NotifyDockedOnShip()
        {
            _prevLandedShipUid = Data?.LandedOnShipUid;
            SetAlpha(0f);
            SetThrustersActive(false);
        }

        // Запускает взлёт: обновляет позицию до текущего положения планеты, включает fade-in.
        public void BeginTakeoff()
        {
            // На посадке EndTurn гасит GameObject — обратно поднимаем тут, иначе SetAlpha/transform
            // отработают на неактивном объекте и анимация взлёта не запустится.
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            if (_landingPlanet != null)
            {
                Vector2 pos = OrbitMath.GetPlanetWorldPosition(_landingPlanet);
                Data.Position      = pos;
                transform.position = new Vector3(pos.x, pos.y, 0f);
            }
            _landingPlanet = null;
            IsTakingOff    = true;
            SetAlpha(0f);
        }

        // Вызывается в конце анимации хода — сбрасывает временные визуальные состояния.
        public void EndTurn()
        {
            IsTakingOff = false;
            SetThrustersActive(false);
            ResetBodyAnimation();
            // В фазах гиперперехода прозрачность ведёт AnimateTurn — не затираем её.
            var phase = Data?.HyperjumpPhase ?? HyperjumpPhase.None;
            if (phase == HyperjumpPhase.HyperEnter
                || phase == HyperjumpPhase.HyperArrive
                || phase == HyperjumpPhase.HyperExit) return;
            // Хвост Travel (последний ход с открытым порталом) тоже ведёт fade-out в AnimateTurn —
            // иначе сброс alpha→1 «затрёт» наполовину сделанную прозрачность перед HyperEnter.
            if (phase == HyperjumpPhase.Travel && HyperjumpController.IsApproachingHyperEdge(Data)) return;
            if (Data == null) { SetAlpha(1f); return; }
            // Корабль считается «вне космоса» если уже посажен ИЛИ закончил fade-out посадки этим ходом
            // (для NPC LandedPlanetUid проставится только на следующем Execute OrderLand),
            // ЛИБО пристыкован к кораблю-носителю.
            bool isLanded = Data.LandedPlanetUid != null || Data.LandingPhase == LandingPhase.Fading
                || !string.IsNullOrEmpty(Data.LandedOnShipUid);
            SetAlpha(isLanded ? 0f : 1f);
            // Гасим только NPC-визуал: у игрока на этом же GameObject висит PlayerShip с подпиской
            // на OnPlanetLeft через OnEnable, и SetActive(false) сорвал бы обратный взлёт.
            // Сцена с игроком гасится в PlayerManager.LandOnPlanet через SetSystemVisible(false).
            if (isLanded && !Data.IsPlayer) gameObject.SetActive(false);
        }

        // Посадка/взлёт: первые LandingFadeHold хода корабль полностью виден (заходит на глиссаду),
        // затем плавно (smoothstep) растворяется; взлёт — зеркально.
        private const float LandingFadeHold = 0.25f;

        private static float LandingFade(float progress)
            => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - LandingFadeHold) / (1f - LandingFadeHold)));

        // Анимирует позицию и визуальное состояние корабля в течение хода.
        // Возвращает вектор движения — вызывающий код использует его для обновления игрового состояния.
        // Для гиперперехода позиция считается стандартно (по frames/path), а альфа накладывается
        // оверлеем: HyperEnter — fade-out, HyperExit — fade-in.
        public Vector2 AnimateTurn(float progress, int currentSubTurn, TurnAnimationData animData)
        {
            if (!animData.ShipFrames.TryGetValue(Data.Uid, out var frames)) return Vector2.zero;

            Vector2 pos;
            Vector2 direction = Vector2.zero;

            animData.ShipRenderPaths.TryGetValue(Data.Uid, out var path);
            if (path != null && path.Count >= 2)
            {
                float totalLen = 0f;
                for (int i = 1; i < path.Count; i++)
                    totalLen += Vector2.Distance(path[i - 1], path[i]);

                pos       = path[path.Count - 1];
                direction = path[path.Count - 1] - path[path.Count - 2];

                if (totalLen > 0.0001f)
                {
                    float target = progress * totalLen;
                    float acc    = 0f;
                    for (int i = 1; i < path.Count; i++)
                    {
                        float seg = Vector2.Distance(path[i - 1], path[i]);
                        if (acc + seg >= target)
                        {
                            float t = seg > 0.0001f ? (target - acc) / seg : 1f;
                            pos       = Vector2.Lerp(path[i - 1], path[i], Mathf.Clamp01(t));
                            int dirTo = Mathf.Min(path.Count - 1, i + 5);
                            direction = path[dirTo] - pos;
                            break;
                        }
                        acc += seg;
                    }
                }
            }
            else
            {
                float tickProgress = Mathf.Clamp01((progress * GalaxyData.SubTurnsPerTurn) - (currentSubTurn - 1));
                var posFrom = frames.SubTurns[currentSubTurn - 1];
                var posTo   = frames.SubTurns[currentSubTurn];
                pos       = Vector2.Lerp(posFrom, posTo, tickProgress);
                direction = posTo - posFrom;
            }

            transform.position = new Vector3(pos.x, pos.y, 0f);

            bool moving = direction.sqrMagnitude > 0.0001f;
            SetMovementDirection(direction);
            // Во время HyperExit движок ВСЕГДА горит (корабль вылетает из портала, даже если
            // путь короткий) — даёт ощущение действия после прыжка, а не "промокшая котлета".
            // Во время HyperArrive корабль ещё невидим за порталом — двигатели тоже отключены.
            var phaseForThrusters = Data != null ? Data.HyperjumpPhase : HyperjumpPhase.None;
            bool forceThrusters = phaseForThrusters == HyperjumpPhase.HyperExit;
            bool suppressThrusters = phaseForThrusters == HyperjumpPhase.HyperArrive;
            SetThrustersActive(!suppressThrusters && (moving || forceThrusters));
            SetAnimation(moving ? "Move" : "Idle");

            float? a = ComputeFadeAlpha(progress);
            if (a.HasValue) SetAlpha(a.Value);

            return direction;
        }

        // Доля хода, оставшаяся ПОСЛЕ окончания begin-клипа портала (1 - 1.88/2). Если поменяется
        // длина begin-клипа или TurnDuration — это можно подстроить вручную.
        private const float BeginEndProgress = 0.94f;
        private const float TailFadeShare = 1f - BeginEndProgress;

        /// <summary>
        /// Единая точка расчёта прозрачности корабля во время анимации хода. Возвращает alpha,
        /// который НАДО применить (через SetAlpha), либо null если в этой фазе нет fade-override
        /// и текущее значение alpha сохраняется. Приоритет:
        ///   1. Landing fade-out (фаза Fading посадки, см. PrepareForTurn) — 1→0 по LandingFade.
        ///   2. Takeoff fade-in — 0→1 по LandingFade.
        ///   3. Червоточина HyperEnter — landing-style: полный линейный fade-out 1→0 за ход,
        ///      без хвоста Travel (корабль «садится» на червоточину как на планету).
        ///   4. HyperArrive (гиперпрыжок и червоточина) — корабль стоит невидимо у точки выхода,
        ///      весь ход играется begin+mid портала (или червоточины). Alpha = 0.
        ///   5. Червоточина HyperExit — takeoff-style: fade-in 0→1 из парной червоточины.
        ///   6. Hyperjump HyperEnter — fade-out 0.943→0, склейка с хвостом Travel.
        ///   7. Hyperjump HyperExit — fade-in 0→1 при выходе из портала.
        ///   8. Hyperjump Travel «хвост» — fade-out на последнем ходу перед HyperEnter.
        /// </summary>
        private float? ComputeFadeAlpha(float progress)
        {
            if (_isLandingThisTurn)
            {
                return 1f - LandingFade(progress);
            }
            if (IsTakingOff) return 1f - LandingFade(1f - progress);

            var phase = Data != null ? Data.HyperjumpPhase : HyperjumpPhase.None;
            bool viaWormhole = Data != null && !string.IsNullOrEmpty(Data.HyperjumpViaWormholeUid);
            switch (phase)
            {
                case HyperjumpPhase.HyperEnter:
                    // Червоточина: полный линейный fade-out — как посадка на планету.
                    if (viaWormhole) return 1f - progress;
                    // Гиперпрыжок: сквозная шкала — хвост Travel (после конца begin) + весь HyperEnter.
                    // begin ≈ 0.94 хода → 0.06 хода уходит на хвост. Шкала 0.06+1.0=1.06.
                    // В начале HyperEnter alpha ≈ 0.943, в конце = 0. Стыкуется с Travel-веткой.
                    return 1f - (TailFadeShare + progress) / (TailFadeShare + 1f);

                case HyperjumpPhase.HyperArrive:
                    // Корабль ещё внутри портала — не показываем корпус весь ход.
                    return 0f;

                case HyperjumpPhase.HyperExit:
                    return progress;

                case HyperjumpPhase.Travel:
                    // Червоточина: без хвостового fade — корабль должен долететь до червоточины
                    // с полной непрозрачностью и уже там начать «садиться» (см. HyperEnter выше).
                    if (viaWormhole) break;
                    // Гиперпрыжок: портал открыт с начала симуляции; begin-клип заканчивается
                    // на ~94% хода и портал переходит в mid-loop — отсюда плавно делаем корабль прозрачным.
                    if (HyperjumpController.IsApproachingHyperEdge(Data) && progress > BeginEndProgress)
                        return 1f - (progress - BeginEndProgress) / (TailFadeShare + 1f);
                    break;
            }
            return null;
        }

        public void SetAlpha(float alpha)
        {
            _alpha = alpha;
            ApplyBodyAlpha(alpha);
            foreach (var t in _thrusters)
            {
                if (t.Renderer == null) continue;
                var c = t.Renderer.color;
                t.Renderer.color = new Color(c.r, c.g, c.b, alpha);
            }
        }

        /// <summary>
        /// Прерывает обычную анимацию корабля и проигрывает на его GameObject взрыв
        /// (через ExplosionPlayback). По окончании анимации GO самоуничтожается.
        /// Не использовать для игрока (DontDestroyOnLoad) — там SpawnAt создаёт отдельный GO.
        /// </summary>
        public void PlayExplosion(string explosionPath = null)
        {
            // Гасим тяги, чтобы они не светились поверх взрыва, и выключаем наш Update.
            SetThrustersActive(false);
            foreach (var t in _thrusters)
                if (t.Renderer != null) t.Renderer.gameObject.SetActive(false);
            PrepareBodyForExplosion();
            enabled = false;
            ExplosionPlayback.PlayOn(gameObject, explosionPath);
        }

        // ── Кадр ─────────────────────────────────────────────────────────────────

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            TickBody(dt);
            TickRotation(dt);
            TickThrusters(dt);
        }

        private void TickRotation(float dt)
        {
            _currentAngle = Mathf.MoveTowardsAngle(_currentAngle, _targetAngle, RotationSpeed * dt);
            transform.rotation = Quaternion.Euler(0f, 0f, _currentAngle);
        }

        private void TickThrusters(float dt)
        {
            foreach (var t in _thrusters)
            {
                if (t.Renderer == null || !t.Renderer.gameObject.activeSelf) continue;
                if (t.Frames == null || t.Frames.Length <= 1) continue;

                t.Timer += dt;
                if (t.Timer < t.SecPerFrame) continue;

                int advance = Mathf.FloorToInt(t.Timer / t.SecPerFrame);
                t.Timer %= t.SecPerFrame;
                t.CurrentFrame = (t.CurrentFrame + advance) % t.Frames.Length;
                t.Renderer.sprite = t.Frames[t.CurrentFrame];
            }
        }
    }
}
