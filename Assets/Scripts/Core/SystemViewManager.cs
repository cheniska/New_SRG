using UnityEngine;
using Random = UnityEngine.Random;
using System.Collections.Generic;
using SRG.Combat;
using SRG.Config;
using SRG.Dialog;
using SRG.Galaxy;
using SRG.Galaxy.Simulation;
using SRG.NpcAI;
using SRG.Presentation.Common;
using SRG.Presentation.Effects;
using SRG.Presentation.Map;
using SRG.Presentation.World;
using SRG.Ships;
using SRG.Ships.Player;
using SRG.Utils;
using SRG.UI.Common;
using SRG.UI.HUD;
using SRG.UI.Screens;
using SRG.Utils;

namespace SRG.Core
{
    public class SystemViewManager : MonoBehaviour
    {
        public static SystemViewManager Instance { get; private set; }

        [Header("Components")]
        public Transform SystemContainer;
        public SystemMinimapController MinimapController;

        [Header("Prefabs")]
        public GameObject StarPrefab;
        public GameObject PlanetPrefab;
        public GameObject SatellitePrefab;
        public GameObject ShipPrefab;
        public GameObject AsteroidPrefab;

        public GameObject DroppedItemPrefab;

        [Header("Visual Settings")]
        public Color BaseSpaceColor = Color.black;

        [Header("Config")]
        [SerializeField] private GameSettingsConfig _settings;

        private readonly List<GameObject> _spawnedObjects = new();
        private readonly List<(PlanetData planet, Transform t)> _planetList = new();
        private readonly List<(ShipData ship, Transform t, ShipVisualController visual)> _shipList = new();
        private readonly Dictionary<string, AsteroidVisualController> _asteroidControllers = new();
        private readonly Dictionary<string, (Transform t, SpriteRenderer sr, ActiveMissile missile)> _missileVisuals = new();
        private readonly HashSet<string> _firedDestroyEvents = new();
        private Sprite _missileDot;
        private TurnAnimationData _lastAnimRef;
        private WeaponVisualSystem _weaponVfx;

        private BackgroundVisualController _bgController;
        private TowBeamVisualController _towBeams;
        private HyperjumpPortalManager _hyperPortals;
        private WormholeVisualManager _wormholeVisuals;
        private float _currentWorldSize;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            // Инициализируем сервисные компоненты (если не добавлены в инспекторе)
            if (GetComponent<ObjectInfoPopup>() == null)
                gameObject.AddComponent<ObjectInfoPopup>();
            if (GetComponent<WeaponRangeIndicator>() == null)
                gameObject.AddComponent<WeaponRangeIndicator>();
            if (GetComponent<ShipScanUIController>() == null)
                gameObject.AddComponent<ShipScanUIController>();

            _weaponVfx = gameObject.AddComponent<WeaponVisualSystem>();
            _weaponVfx.Init(SystemContainer);
            _missileDot = SpriteUtility.CreateCircleSprite(12);
            if (FindAnyObjectByType<PlanetUIController>() == null)
            {
                var pgo = new GameObject("PlanetUIController");
                pgo.AddComponent<PlanetUIController>();
            }
            if (FindAnyObjectByType<DialogUIController>() == null)
            {
                var dgo = new GameObject("DialogUIController");
                dgo.AddComponent<DialogUIController>();
            }
        }

        public void SetSystemVisible(bool visible)
        {
            if (SystemContainer != null) SystemContainer.gameObject.SetActive(visible);
            if (MinimapController != null) MinimapController.gameObject.SetActive(visible);
            // BackgroundVisualController создаётся как root-объект (не child SystemContainer),
            // поэтому его нужно гасить отдельно — иначе туманности/звёзды просвечивают
            // сквозь UI планеты.
            if (_bgController != null && _bgController.gameObject != null)
                _bgController.gameObject.SetActive(visible);
        }

        public void RenderSystem(StarData star)
        {
            ClearScene();
            if (star == null) return;

            _currentWorldSize = SRUnits.ToWorld(star.SystemSize);

            SpawnStar(star);
            SpawnPlanets(star);
            SpawnShips(star);
            SpawnAsteroids(star);
            SpawnMissiles(star);

            MinimapController?.InitializeMinimap(star, _currentWorldSize);

            if (_bgController == null || _bgController.gameObject == null)
            {
                _bgController = new GameObject("BackgroundVisualController").AddComponent<BackgroundVisualController>();
            }
            _bgController.Setup(Camera.main, star.Name.GetHashCode(), (float)star.SystemSize);

            EnsureHyperPortals();
            _hyperPortals?.ClearAll();
            EnsureWormholeVisuals();
            _wormholeVisuals?.ClearAll();
            _wormholeVisuals?.Tick();

            UpdatePlanetPositions();
        }

        private void SpawnStar(StarData star)
        {
            if (StarPrefab == null) return;
            var (frames, speed) = GraphicsManager.Instance.GetStarVisuals(star.Color, star.GraphVar);
            float scale = GraphicsManager.Instance.GetStarScale(star.Type);
            var obj = Instantiate(StarPrefab, SystemContainer);
            obj.name = $"Star_{star.Name}";
            obj.transform.localPosition = Vector3.zero;
            obj.GetComponent<StarVisualController>()?.Setup(frames, speed, scale);
            var label = obj.GetComponent<StarLabelRenderer>() ?? obj.AddComponent<StarLabelRenderer>();
            label.Setup(star);

            var starCol = obj.AddComponent<CircleCollider2D>();
            starCol.isTrigger = true;
            starCol.radius = 0.5f;

            var starInfo = obj.AddComponent<ClickableInfo>();
            starInfo.Star = star;

            _spawnedObjects.Add(obj);
        }

        private void SpawnPlanets(StarData star)
        {
            if (PlanetPrefab == null) return;
            foreach (var planet in star.Planets)
            {
                var obj = Instantiate(PlanetPrefab, SystemContainer);
                obj.name = $"Planet_{planet.Name}";
                obj.GetComponent<PlanetVisualController>()?.Setup(planet, SatellitePrefab);

                var planetCol = obj.AddComponent<CircleCollider2D>();
                planetCol.isTrigger = true;
                planetCol.radius = 0.5f;

                var planetInfo = obj.AddComponent<ClickableInfo>();
                planetInfo.Planet = planet;
                _planetList.Add((planet, obj.transform));
                _spawnedObjects.Add(obj);
            }
        }

        private void SpawnShips(StarData star)
        {
            if (ShipPrefab == null) return;
            foreach (var ship in star.Ships)
            {
                if (ship.IsPlayer)
                {
                    if (PlayerShip.Instance != null && PlayerShip.Instance.gameObject != null)
                    {
                        PlayerShip.Instance.transform.position = new Vector3(ship.Position.x, ship.Position.y, 0f);
                        continue;
                    }
                }

                var obj = SpawnShip(ship);
                if (ship.IsPlayer)
                {
                    obj.name = "PlayerShip";
                    obj.AddComponent<PlayerShip>().Init(ship, Camera.main, _settings);
                    Object.DontDestroyOnLoad(obj);
                }
                else
                {
                    obj.name = $"Ship_{ship.Uid}";
                    NpcSpawner.Attach(obj, ship, star);
                    if (obj.TryGetComponent<ClickableInfo>(out var ci))
                        ci.NpcController = obj.GetComponent<NpcController>();
                }
            }
        }

        private GameObject SpawnShip(ShipData ship)
        {
            var obj = Instantiate(ShipPrefab, SystemContainer);
            obj.transform.localPosition = new Vector3(ship.Position.x, ship.Position.y, 0f);
            var visual = obj.GetComponent<ShipVisualController>();
            visual?.Setup(ship, ship.IsPlayer);
            _shipList.Add((ship, obj.transform, visual));
            _spawnedObjects.Add(obj);

            var col = obj.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.35f;

            var shipInfo = obj.AddComponent<ClickableInfo>();
            shipInfo.Ship = ship;

            return obj;
        }

        private void SpawnAsteroids(StarData star)
        {
            if (AsteroidPrefab == null || star.Asteroids == null) return;

            foreach (var asteroid in star.Asteroids)
            {
                if (!asteroid.IsDestroyed)
                    SpawnAsteroidVisual(asteroid);
            }
        }

        private void SpawnAsteroidVisual(AsteroidData asteroid)
        {
            if (AsteroidPrefab == null) return;

            var obj = Instantiate(AsteroidPrefab, SystemContainer);
            obj.name = $"Asteroid_{asteroid.Uid}";
            obj.transform.localPosition = new Vector3(asteroid.Position.x, asteroid.Position.y, 0f);

            var ctrl = obj.GetComponent<AsteroidVisualController>();
            if (ctrl != null)
            {
                ctrl.Setup(asteroid);
                _asteroidControllers[asteroid.Uid] = ctrl;
                MinimapController?.OnAsteroidSpawned(asteroid);

                var asteroidInfo = obj.AddComponent<ClickableInfo>();
                asteroidInfo.Asteroid = asteroid;
            }
            else
            {
                Debug.LogError("[SpawnAsteroidVisual] AsteroidPrefab missing AsteroidVisualController component!");
            }

            _spawnedObjects.Add(obj);
        }

        // Вызывается перед OnTurnCalculate — подготавливает визуальные контроллеры НПС к ходу.
        public void BeginTurnAnimation(TurnAnimationData anim)
        {
            var star = GalaxyManager.Instance?.CurrentStar;
            foreach (var (ship, _, visual) in _shipList)
            {
                if (ship == null || ship.IsPlayer || visual == null) continue;
                visual.PrepareForTurn(anim, star);
            }
        }

        public void UpdatePlanetPositions()
        {
            var anim = GalaxyManager.Instance?.LastTurnData;
            AnimateSystem(1f, GalaxyData.SubTurnsPerTurn, anim);
            _weaponVfx?.EndSimulation();

            foreach (var (ship, _, visual) in _shipList)
            {
                if (ship == null || ship.IsPlayer || visual == null) continue;
                visual.EndTurn();
            }
        }

        public void AnimateSystem(float progress, int currentSubTurn, TurnAnimationData anim)
        {
            EnsureHyperPortals();
            EnsureWormholeVisuals();
            // Если в текущую звезду прилетели новые корабли (мигрировали из других систем) —
            // спавним для них визуал, иначе они будут невидимы при выходе из портала.
            EnsureShipVisualsForCurrentStar();
            _hyperPortals?.Tick(progress);
            _wormholeVisuals?.Tick();

            UpdatePlanetVisuals(progress, anim);
            UpdateShipVisuals(progress, currentSubTurn, anim);
            UpdateTowBeams();

            if (anim != null)
            {
                // Раз-в-anim ресет общих кэшей (firedDestroyEvents — дедуп взрывов; weapon-vfx).
                if (anim != _lastAnimRef)
                {
                    _firedDestroyEvents.Clear();
                    _lastAnimRef = anim;
                    _weaponVfx?.BeginSimulation(anim);
                }

                ProcessAsteroidEvents(progress, anim);
            }

            AnimateAsteroidPositions(progress, currentSubTurn, anim);

            _weaponVfx?.Tick(progress, anim);

            // ── Missile + ship-death visuals ────────────────────────────────────────
            bool animating = progress < 0.999f;
            if (anim != null)
            {
                ProcessMissileSpawns(anim);
                ProcessMissileDeaths(progress, animating, anim);
                if (!animating) ProcessShipDeaths(anim);
                AnimateMissilePositions(progress, currentSubTurn, animating, anim);
            }
            else
            {
                AnimateMissilePositionsNoAnim();
            }
        }

        // ── AnimateSystem: missile/asteroid/ship-death helpers (этап B-followup) ──
        // Вынесены из AnimateSystem на этапе финальной чистки missile-блока. Логика
        // переплетена с _firedDestroyEvents (дедуп взрывов) и `animating` (живая ли
        // фаза симуляции). Каждый helper сохраняет прежний порядок операций.

        /// <summary>Обработка AsteroidSpawns/AsteroidDestroys (взрывы с дропом + удаление визуала).
        /// Использует _firedDestroyEvents для дедупликации (повторные вызовы AnimateSystem
        /// одним и тем же anim не пересоздают взрыв).</summary>
        private void ProcessAsteroidEvents(float progress, TurnAnimationData anim)
        {
            foreach (var spawnEvt in anim.AsteroidSpawns)
            {
                if (_asteroidControllers.ContainsKey(spawnEvt.AsteroidUid)) continue;
                var star = GalaxyManager.Instance?.CurrentStar;
                if (star == null) continue;
                var asteroid = star.Asteroids.Find(a => a.Uid == spawnEvt.AsteroidUid);
                if (asteroid != null) SpawnAsteroidVisual(asteroid);
            }

            foreach (var destroyEvt in anim.AsteroidDestroys)
            {
                if (_firedDestroyEvents.Contains(destroyEvt.AsteroidUid)) continue;

                float collisionProgress = (destroyEvt.CollisionSubTurn - 1 + destroyEvt.CollisionT)
                    / GalaxyData.SubTurnsPerTurn;
                if (progress < collisionProgress) continue;

                _firedDestroyEvents.Add(destroyEvt.AsteroidUid);

                if (!_asteroidControllers.TryGetValue(destroyEvt.AsteroidUid, out var ctrl)) continue;
                SpawnDroppedItems(destroyEvt.Position, destroyEvt.DroppedStacks, destroyEvt.DroppedItems);
                _asteroidControllers.Remove(destroyEvt.AsteroidUid);

                if (ctrl == null || ctrl.gameObject == null)
                {
                    // Уже разрушен — fallback: создать отдельный GO для взрыва на позиции события.
                    if (destroyEvt.CollisionType != AsteroidCollisionType.None)
                        ExplosionPlayback.SpawnAt(SystemContainer, destroyEvt.Position, destroyEvt.ExplosionPath);
                }
                else if (destroyEvt.CollisionType != AsteroidCollisionType.None)
                {
                    // In-place: ExplosionPlayback забирает SpriteRenderer астероида,
                    // GO самоуничтожится по окончании. _spawnedObjects НЕ трогаем —
                    // ClearScene всё равно подберёт его, если игрок сменит систему.
                    ctrl.transform.localPosition = new Vector3(destroyEvt.Position.x, destroyEvt.Position.y, 0f);
                    ctrl.PlayExplosion(destroyEvt.ExplosionPath);
                }
                else
                {
                    // Тихое исчезновение — старое поведение.
                    _spawnedObjects.Remove(ctrl.gameObject);
                    Destroy(ctrl.gameObject);
                }
            }
        }

        /// <summary>Спавнит визуал для ракет, появившихся в anim.MissileFrames, но ещё не имеющих
        /// _missileVisuals-записи. Идемпотентен — повторные вызовы не дублируют.</summary>
        private void ProcessMissileSpawns(TurnAnimationData anim)
        {
            foreach (var uid in anim.MissileFrames.Keys)
            {
                if (_missileVisuals.ContainsKey(uid)) continue;
                var star = GalaxyManager.Instance?.CurrentStar;
                var m = star?.ActiveMissiles?.Find(x => x.Uid == uid);
                if (m != null) SpawnMissileVisual(m);
            }
        }

        /// <summary>
        /// Обрабатывает смерть ракет (взрыв in-place либо silent destroy). Запускается в момент,
        /// когда progress достиг impact-сабтёрна (живая фаза) ИЛИ когда фаза анимации завершена
        /// (для пропущенных). Дедупликация через _firedDestroyEvents.
        /// </summary>
        private void ProcessMissileDeaths(float progress, bool animating, TurnAnimationData anim)
        {
            foreach (var kvp in anim.MissileDeathUids)
            {
                var uid = kvp.Key;
                int deathSub = kvp.Value;
                if (_firedDestroyEvents.Contains(uid)) continue;

                float impactProgress = (float)deathSub / GalaxyData.SubTurnsPerTurn;
                if (animating && progress < impactProgress) continue;

                _firedDestroyEvents.Add(uid);

                if (!_missileVisuals.TryGetValue(uid, out var dead)) continue;
                _missileVisuals.Remove(uid);

                if (dead.t == null || dead.t.gameObject == null) continue;

                if (anim.MissileSilentDeathUids.Contains(uid))
                {
                    _spawnedObjects.Remove(dead.t.gameObject);
                    Destroy(dead.t.gameObject);
                    continue;
                }

                // In-place взрыв: гасим покадровку ракеты и отдаём GO ExplosionPlayback,
                // он подменит спрайт на кадры взрыва и сам уничтожит GO.
                if (dead.t.TryGetComponent<AnimatedSpriteRenderer>(out var anr))
                    anr.enabled = false;
                ExplosionPlayback.PlayOn(dead.t.gameObject);
            }
        }

        /// <summary>
        /// Обрабатывает смерть кораблей (включая игрока) после окончания анимационной фазы хода.
        /// Вызывается только когда !animating (progress &gt;= 0.999), чтобы взрыв синхронизировался
        /// с финальной позицией. Дедупликация через _firedDestroyEvents (метод может быть вызван
        /// дважды — в TickSimulation на финальном кадре и в CompleteCurrentTurn).
        /// </summary>
        private void ProcessShipDeaths(TurnAnimationData anim)
        {
            foreach (var uid in anim.DeathUids)
            {
                var playerInstance = PlayerShip.Instance;
                bool isPlayer = playerInstance != null && playerInstance.ShipData != null
                                && playerInstance.ShipData.Uid == uid;

                if (isPlayer)
                {
                    if (_firedDestroyEvents.Contains(uid)) continue;
                    _firedDestroyEvents.Add(uid);

                    // PlayerShip — DontDestroyOnLoad, не уничтожаем — спавним отдельный GO взрыва.
                    Vector2 pos = playerInstance.transform.position;
                    ExplosionPlayback.SpawnAt(SystemContainer, pos);
                    playerInstance.gameObject.SetActive(false);

                    int playerIdx = _shipList.FindIndex(s => s.ship != null && s.ship.Uid == uid);
                    if (playerIdx >= 0) _shipList.RemoveAt(playerIdx);

                    PlayerManager.Instance?.NotifyDeathAnimationFinished();
                    continue;
                }

                int idx = _shipList.FindIndex(s => s.ship != null && s.ship.Uid == uid);
                if (idx < 0) continue;
                var (deadShip, t, visual) = _shipList[idx];
                _shipList.RemoveAt(idx);
                if (t == null || t.gameObject == null) continue;

                // Контейнеры (IsItem) исчезают тихо — нет взрыва при подборе.
                bool silent = deadShip != null && deadShip.IsItem;
                if (silent || visual == null)
                {
                    _spawnedObjects.Remove(t.gameObject);
                    Destroy(t.gameObject);
                }
                else
                {
                    // In-place взрыв: ShipVisualController отдаёт SpriteRenderer ExplosionPlayback,
                    // GO самоуничтожится по окончании. _spawnedObjects не трогаем — ClearScene
                    // подберёт, если игрок улетит во время взрыва.
                    visual.PlayExplosion();
                }
            }
        }

        /// <summary>
        /// Анимация положения и поворота ракет: интерполяция между сабтёрнами с учётом фаз
        /// видимости (до launchSub — невидима; после deathSub — невидима в этом же сабтёрне,
        /// чтобы не висела до конца хода в impact-позиции).
        /// </summary>
        private void AnimateMissilePositions(float progress, int currentSubTurn, bool animating, TurnAnimationData anim)
        {
            int subA = Mathf.Clamp(currentSubTurn - 1, 0, GalaxyData.SubTurnsPerTurn);
            int subB = Mathf.Clamp(currentSubTurn,     0, GalaxyData.SubTurnsPerTurn);
            int sub0 = Mathf.Max(0, subA - 1);
            int subC = Mathf.Min(GalaxyData.SubTurnsPerTurn, subB + 1);
            float tickProgress = Mathf.Clamp01((progress * GalaxyData.SubTurnsPerTurn) - (currentSubTurn - 1));

            foreach (var kvp in _missileVisuals)
            {
                var t = kvp.Value.t;
                if (t == null || t.gameObject == null) continue;
                if (!anim.MissileFrames.TryGetValue(kvp.Key, out var mf))
                {
                    t.gameObject.SetActive(false);
                    continue;
                }

                // Ракета не видна, пока анимация не дошла до сабтёрна запуска — иначе
                // спрайт «висит» под кораблём с начала дня до момента залпа. После
                // конца дня LaunchSubTurn сбрасывается, значит со 2-го дня ракета всегда видима.
                int launchSub = kvp.Value.missile?.LaunchSubTurn ?? 0;
                if (animating && currentSubTurn < launchSub)
                {
                    t.gameObject.SetActive(false);
                    continue;
                }

                // Сабтёрн смерти: спрайт виден ВО ВРЕМЯ анимации сабтёрна импакта (подлёт к цели),
                // но как только анимация ушла в следующий сабтёрн — ракета должна исчезнуть сразу,
                // не висеть до конца хода в impact-позиции.
                if (animating && anim.MissileDeathUids.TryGetValue(kvp.Key, out int deathSub)
                    && currentSubTurn > deathSub)
                {
                    t.gameObject.SetActive(false);
                    continue;
                }
                t.gameObject.SetActive(true);

                // Гладкая интерполяция (Catmull-Rom) по 4 кадрам — без неё траектория
                // быстро поворачивающихся торпед выглядит ломаной из 10 прямых сегментов.
                Vector2 p0 = mf.SubTurns[sub0];
                Vector2 pa = mf.SubTurns[subA];
                Vector2 pb = mf.SubTurns[subB];
                Vector2 pc = mf.SubTurns[subC];

                Vector2 pos = animating
                    ? CatmullRom(p0, pa, pb, pc, tickProgress)
                    : mf.SubTurns[GalaxyData.SubTurnsPerTurn];
                t.localPosition = new Vector3(pos.x, pos.y, 0f);

                // Угол — тангенс той же сплайн-кривой; фолбэк на (pb-pa), если кривая
                // вырождена в точку (стоячая ракета до запуска / после фриза).
                Vector2 tangent = animating ? CatmullRomTangent(p0, pa, pb, pc, tickProgress) : pb - pa;
                if (tangent.sqrMagnitude < 1e-8f) tangent = pb - pa;
                if (tangent.sqrMagnitude > 1e-8f)
                {
                    // Спрайт нарисован "носом вверх" (как корабли) — компенсируем -90°.
                    float angle = Angles.Of(tangent) * Mathf.Rad2Deg - 90f;
                    t.localRotation = Quaternion.Euler(0f, 0f, angle);
                }
            }
        }

        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        private static Vector2 CatmullRomTangent(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            return 0.5f * (
                (-p0 + p2) +
                2f * (2f * p0 - 5f * p1 + 4f * p2 - p3) * t +
                3f * (-p0 + 3f * p1 - 3f * p2 + p3) * t2
            );
        }

        /// <summary>Фолбэк отображения ракет когда нет anim (между ходами): ставим каждый
        /// спрайт в текущую сим-позицию.</summary>
        private void AnimateMissilePositionsNoAnim()
        {
            foreach (var kvp in _missileVisuals)
            {
                var (t, _, missile) = kvp.Value;
                if (t == null || t.gameObject == null) continue;
                t.gameObject.SetActive(true);
                t.localPosition = new Vector3(missile.Position.x, missile.Position.y, 0f);
            }
        }

        // ── AnimateSystem: вспомогательные private методы ─────────────────────────
        // Вынесены из AnimateSystem (см. историю изменений: B2 рефакторинг). Поведение
        // идентично прежнему — это чистый extract-method, главный метод стал короче и
        // легче читается, но порядок и логика тиков сохранены до строки.

        /// <summary>Анимирует позиции планет по PlanetAngles из anim (или текущим, если anim==null).
        /// Также обновляет MinimapController.</summary>
        private void UpdatePlanetVisuals(float progress, TurnAnimationData anim)
        {
            foreach (var (planet, t) in _planetList)
            {
                if (planet == null || t == null || t.gameObject == null) continue;

                float fromAngle = planet.PreviousAngle;
                float toAngle = planet.CurrentAngle;

                if (anim != null && anim.PlanetAngles.TryGetValue(planet.Uid, out var angles))
                {
                    fromAngle = angles.From;
                    toAngle = angles.To;
                }

                float orbitRadius = planet.OrbitRadius > 0f
                    ? planet.OrbitRadius
                    : (2f + planet.OrbitIndex * 1.5f) * 100f; // конвертируем обратно в единицы орбиты

                var pos2d = OrbitMath.GetEllipticPositionLerp(
                    orbitRadius, fromAngle, toAngle, progress,
                    planet.OrbitEccentricity, planet.OrbitTiltDeg);
                var worldPos = new Vector3(pos2d.x, pos2d.y, 0f);

                t.localPosition = worldPos;
                MinimapController?.UpdatePlanetPosition(planet, worldPos);
            }
        }

        /// <summary>Анимирует NPC-корабли (игрок управляется PlayerShip.Update сам).</summary>
        private void UpdateShipVisuals(float progress, int currentSubTurn, TurnAnimationData anim)
        {
            foreach (var (ship, t, visual) in _shipList)
            {
                if (ship == null || t == null || t.gameObject == null || ship.IsPlayer) continue;

                if (anim != null && visual != null)
                {
                    visual.AnimateTurn(progress, currentSubTurn, anim);
                }
                else
                {
                    var pos = Vector2.Lerp(ship.PreviousPosition, ship.Position, progress);
                    t.localPosition = new Vector3(pos.x, pos.y, 0f);
                }

                MinimapController?.UpdateNpcShipPosition(ship, t.position);
            }
        }

        /// <summary>Обновляет лучи буксира — собирает пары (ShipData,Transform) для всех NPC и
        /// игрока, передаёт в TowBeams.Refresh.</summary>
        private void UpdateTowBeams()
        {
            // Положение игрока в _shipList не храним — берём из PlayerShip.Instance.
            // Лучи рисуются для всех буксирующих в текущей системе.
            EnsureTowBeams();
            if (_towBeams == null) return;

            var pairs = new List<(ShipData ship, Transform t)>(_shipList.Count + 1);
            foreach (var (ship, t, _) in _shipList) pairs.Add((ship, t));
            if (PlayerShip.Instance != null && PlayerShip.Instance.ShipData != null)
                pairs.Add((PlayerShip.Instance.ShipData, PlayerShip.Instance.transform));
            _towBeams.Refresh(pairs);
        }

        /// <summary>Двигает спрайты астероидов по AsteroidFrames (если есть anim) либо по PreviousPosition/Position.</summary>
        private void AnimateAsteroidPositions(float progress, int currentSubTurn, TurnAnimationData anim)
        {
            if (anim != null)
            {
                foreach (var kvp in _asteroidControllers)
                {
                    var ctrl = kvp.Value;
                    if (ctrl == null || ctrl.gameObject == null) continue;
                    if (!anim.AsteroidFrames.TryGetValue(kvp.Key, out var frames)) continue;

                    float tickProgress = Mathf.Clamp01((progress * GalaxyData.SubTurnsPerTurn) - (currentSubTurn - 1));
                    Vector2 pos = Vector2.Lerp(frames.SubTurns[currentSubTurn - 1], frames.SubTurns[currentSubTurn], tickProgress);
                    ctrl.UpdateVisualPosition(pos);
                }
            }
            else
            {
                var currentStar = GalaxyManager.Instance?.CurrentStar;
                if (currentStar?.Asteroids != null)
                {
                    foreach (var asteroid in currentStar.Asteroids)
                    {
                        if (asteroid.IsDestroyed) continue;
                        if (!_asteroidControllers.TryGetValue(asteroid.Uid, out var ctrl) || ctrl == null) continue;
                        ctrl.UpdateVisualPosition(Vector2.Lerp(asteroid.PreviousPosition, asteroid.Position, progress));
                    }
                }
            }
        }

        public bool TryGetAsteroidVisualPosition(string uid, out Vector2 position)
        {
            if (_asteroidControllers.TryGetValue(uid, out var ctrl) && ctrl != null)
            {
                position = ctrl.transform.localPosition;
                return true;
            }
            position = Vector2.zero;
            return false;
        }

        /// <summary>Создаёт отдельный GO взрыва в указанной позиции. Использовать только когда
        /// нет существующего объекта-носителя (см. ExplosionPlayback.PlayOn для in-place).</summary>
        public void SpawnExplosionEffect(Vector2 position, AsteroidCollisionType collisionType, string explosionPath = null)
        {
            ExplosionPlayback.SpawnAt(SystemContainer, position, explosionPath);
        }

        public void SpawnDroppedItems(
            Vector2 position,
            System.Collections.Generic.List<ItemStack> stacks,
            System.Collections.Generic.List<ItemInstance> items)
        {
            var ctx = GalaxyManager.Instance?.Context;

            int totalCount = (stacks?.Count ?? 0) + (items?.Count ?? 0);
            if (totalCount == 0) return;

            float angleStep = totalCount > 1 ? 360f / totalCount : 0f;
            float baseAngle = Random.Range(0f, 360f);
            int idx = 0;

            if (stacks != null)
            {
                foreach (var stack in stacks)
                {
                    if (stack == null || stack.TotalWeight <= 0) { idx++; continue; }

                    var obj = SpawnDroppedItemObject($"Drop_{stack.ItemId}");
                    obj.transform.localPosition = new Vector3(position.x, position.y, 0f);

                    float angle = (baseAngle + angleStep * idx) * Mathf.Deg2Rad;
                    float speed = Random.Range(0.5f, 1.4f);
                    var drift = new Vector2(Mathf.Cos(angle) * speed, Mathf.Sin(angle) * speed);

                    var ctrl = obj.GetComponent<DroppedItemVisualController>()
                               ?? obj.AddComponent<DroppedItemVisualController>();
                    ctrl.SetupStack(stack, drift, ctx);

                    _spawnedObjects.Add(obj);
                    idx++;
                }
            }

            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item == null) { idx++; continue; }

                    var obj = SpawnDroppedItemObject($"Drop_{item.Category}");
                    obj.transform.localPosition = new Vector3(position.x, position.y, 0f);

                    float angle = (baseAngle + angleStep * idx) * Mathf.Deg2Rad;
                    float speed = Random.Range(0.5f, 1.4f);
                    var drift = new Vector2(Mathf.Cos(angle) * speed, Mathf.Sin(angle) * speed);

                    var ctrl = obj.GetComponent<DroppedItemVisualController>()
                               ?? obj.AddComponent<DroppedItemVisualController>();
                    ctrl.SetupItem(item, drift);

                    _spawnedObjects.Add(obj);
                    idx++;
                }
            }
        }

        private GameObject SpawnDroppedItemObject(string name)
        {
            if (DroppedItemPrefab != null)
                return Instantiate(DroppedItemPrefab, SystemContainer);

            var obj = new GameObject(name);
            obj.transform.SetParent(SystemContainer, false);
            return obj;
        }

        private void OnDestroy()
        {
            if (_missileDot != null) { Destroy(_missileDot.texture); Destroy(_missileDot); }
        }

        private void SpawnMissiles(StarData star)
        {
            if (star.ActiveMissiles == null) return;
            foreach (var missile in star.ActiveMissiles)
                SpawnMissileVisual(missile);
        }

        private void SpawnMissileVisual(ActiveMissile missile)
        {
            if (_missileVisuals.ContainsKey(missile.Uid)) return;

            var gm = GraphicsManager.Instance;
            var sheet = !string.IsNullOrEmpty(missile.GraphicPath)
                ? (gm?.GetSheetHandle(missile.GraphicPath, 12f) ?? SheetHandle.Empty)
                : SheetHandle.Empty;
            Sprite sprite = sheet.IsValid ? sheet.Frames[0] : gm?.TryGetSprite(missile.GraphicPath);
            bool useDot = sprite == null;
            if (useDot && _missileDot == null) return;
            if (useDot) sprite = _missileDot;

            var go = new GameObject($"Missile_{missile.Uid}");
            go.transform.SetParent(SystemContainer, false);
            go.transform.localPosition = new Vector3(missile.Position.x, missile.Position.y, 0f);
            float scale = missile.Scale > 0f ? missile.Scale : 0.07f;
            go.transform.localScale = Vector3.one * scale;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = useDot ? MissileDotColor(missile.DamageType) : Color.white;
            // Ракеты/торпеды летят на слое предметов (под кораблями) — как просил дизайн.
            sr.sortingOrder = SortingLayerRegistry.Get(SortLayer.DroppedItem);

            if (sheet.FrameCount > 1)
                go.AddComponent<AnimatedSpriteRenderer>().Init(sheet, sr);

            _missileVisuals[missile.Uid] = (go.transform, sr, missile);
            _spawnedObjects.Add(go);
        }

        static Color MissileDotColor(DamageType dt) => dt switch
        {
            DamageType.Explosive => new Color(1f, 0.40f, 0.05f, 0.85f),
            DamageType.Energy    => new Color(0.15f, 0.85f, 1f,  0.85f),
            _                    => new Color(1f, 0.75f, 0.10f, 0.85f),
        };

        private void ClearScene()
        {
            ObjectInfoPopup.Instance?.Close();

            foreach (var obj in _spawnedObjects)
                if (obj != null && obj.GetComponent<PlayerShip>() == null)
                    Destroy(obj);

            _spawnedObjects.Clear();
            _planetList.Clear();
            _shipList.Clear();
            _asteroidControllers.Clear();
            _missileVisuals.Clear();
            _towBeams?.Clear();
            MinimapController?.ClearMinimap();
        }

        private void EnsureTowBeams()
        {
            if (_towBeams != null) return;
            var go = new GameObject("TowBeamVisualController");
            go.transform.SetParent(SystemContainer, false);
            _towBeams = go.AddComponent<TowBeamVisualController>();
        }

        private void EnsureHyperPortals()
        {
            if (_hyperPortals != null) return;
            var go = new GameObject("HyperjumpPortalManager");
            go.transform.SetParent(SystemContainer, false);
            _hyperPortals = go.AddComponent<HyperjumpPortalManager>();
            _hyperPortals.Init(SystemContainer);
        }

        private void EnsureWormholeVisuals()
        {
            if (_wormholeVisuals != null) return;
            var go = new GameObject("WormholeVisualManager");
            go.transform.SetParent(SystemContainer, false);
            _wormholeVisuals = go.AddComponent<WormholeVisualManager>();
            _wormholeVisuals.Init(SystemContainer, _settings);
        }

        /// <summary>Гарантирует, что у всех текущих NPC-кораблей в CurrentStar есть визуал в _shipList.
        /// Если корабль прилетел из гипера (или иным способом мигрировал) — спавним для него обычный
        /// SpawnShip и добавляем в список.</summary>
        private void EnsureShipVisualsForCurrentStar()
        {
            var star = GalaxyManager.Instance?.CurrentStar;
            if (star == null || ShipPrefab == null) return;
            var ships = star.Ships;
            for (int i = 0; i < ships.Count; i++)
                EnsureShipVisual(ships[i], star);
        }

        /// <summary>Публичный вход: гарантирует визуал для конкретного ShipData в текущей системе.
        /// Используется, например, ShipFormView после ContainerFactory.SpawnContainerWithItem, чтобы
        /// контейнер появился в космосе мгновенно, а не на следующем кадре симуляции.</summary>
        public void EnsureShipVisual(ShipData ship)
        {
            var star = GalaxyManager.Instance?.CurrentStar;
            EnsureShipVisual(ship, star);
        }

        private void EnsureShipVisual(ShipData ship, StarData star)
        {
            if (ship == null || ShipPrefab == null) return;
            if (ship.IsPlayer) return;
            if (ship.CurrentHull <= 0) return;
            if (star == null) return;
            for (int j = 0; j < _shipList.Count; j++)
                if (_shipList[j].ship == ship) return;

            var obj = SpawnShip(ship);
            obj.name = $"Ship_{ship.Uid}";
            NpcSpawner.Attach(obj, ship, star);
            if (obj.TryGetComponent<ClickableInfo>(out var ci))
                ci.NpcController = obj.GetComponent<NpcController>();
        }

        /// <summary>Возвращает менеджер порталов (создаётся лениво при первом обращении).</summary>
        public HyperjumpPortalManager GetHyperPortals()
        {
            EnsureHyperPortals();
            return _hyperPortals;
        }
    }
}
