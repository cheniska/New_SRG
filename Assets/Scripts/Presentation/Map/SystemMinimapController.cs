using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using SRG.Config;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Presentation.Common;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.Presentation.Map
{
    public class SystemMinimapController : MonoBehaviour,
        IPointerClickHandler, IDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("UI References")]
        [SerializeField] private RectTransform minimapContainer;
        [SerializeField] private RectTransform screenBoundsRect;
        [SerializeField] private Camera mainCamera;

        [Header("Settings")]
        [SerializeField] private Vector2 starIconSize = new Vector2(20, 20);
        [SerializeField] private Vector2 planetIconSize = new Vector2(6, 6);
        [SerializeField] private Vector2 shipIconSize = new Vector2(7, 7);
        [SerializeField] private Vector2 playerIconSize = new Vector2(10, 10);
        [SerializeField] private float orbitAlpha = 0.3f;

        [Header("Habitable Zone")]
        [SerializeField] private bool showHabitableZone = true;
        private Text systemSizeLabel;
        private static string PlanetIconPath => GalaxyConstants.PATH_PLANET_MINIMAP_ICON;
        // Толщина обводки и цвет «гало» вокруг диска-корабля — даёт визуальное
        // отличие от плоского круглого маркера планеты при одинаковом размере.
        private const float ShipOutlineThickness = 1.2f;
        private static readonly Color ShipOutlineColor = new Color(1f, 1f, 1f, 0.95f);
        private static readonly Color PlayerOutlineColor = new Color(1f, 0.92f, 0.30f, 1f);
        private static readonly Color PlayerInnerColor = new Color(0.95f, 0.98f, 1f, 1f);

        private static string AsteroidIconPath => GalaxyConstants.ASTEROID_MINIMAP_ICON_PATH;

        private float _worldRadius;
        private readonly Dictionary<PlanetData, RectTransform> _planetIcons = new Dictionary<PlanetData, RectTransform>();
        private readonly List<GameObject> _minimapObjects = new List<GameObject>();
        private RectTransform _playerIcon;
        private readonly Dictionary<string, MaskableGraphic> _npcShipImages = new Dictionary<string, MaskableGraphic>();
        private readonly Dictionary<string, RectTransform> _asteroidIcons = new Dictionary<string, RectTransform>();
        private readonly Dictionary<string, Image> _asteroidImages = new Dictionary<string, Image>();
        private readonly Dictionary<string, RectTransform> _wormholeIcons = new Dictionary<string, RectTransform>();
        private GameObject _asteroidTrailObj;
        private UnityEngine.UI.Image[] _asteroidTrailDots;
        private string _hoveredAsteroidUid;
        private const int TrailDotCount = 12;
        private const int TrailPredictSteps = 40;
        private Vector3 _lastCamPos;
        private float _lastCamSize;
        private EllipseOrbitGraphic _radarRingGraphic;
        private GameObject _radarRingObj;
        private RadarFogGraphic _radarFogGraphic;
        private GameObject _radarFogObj;
        private float _lastRadarWorldRadius = -1f;
        private Vector2 _lastRadarMapPos = new Vector2(float.MaxValue, float.MaxValue);
        private float _lastRadarMapRadius = -1f;
        private readonly Dictionary<string, bool> _npcShipInRange = new Dictionary<string, bool>();
        private GameObject _hzRingObj;
        private FilledRingGraphic _hzRingGraphic;
        public static bool IsMouseOverMinimap { get; private set; }
        private bool IsReady => minimapContainer != null && mainCamera != null && _worldRadius > 0;
        public void InitializeMinimap(StarData star, float worldRadius)
        {
            ClearMinimap();
            if (minimapContainer == null) return;

            _worldRadius = worldRadius;

            CreateHZRing(star);
            CreateStarIcon(star);
            foreach (var planet in star.Planets)
            {
                CreateOrbitRing(planet);
                CreatePlanetIcon(planet);
            }
            foreach (var ship in star.Ships)
                if (!ship.IsPlayer) CreateNpcShipIcon(ship);

            foreach (var asteroid in star.Asteroids)
                if (!asteroid.IsDestroyed) CreateAsteroidIcon(asteroid);

            if (star.Wormholes != null)
                foreach (var wh in star.Wormholes) CreateWormholeIcon(wh);

            CreatePlayerIcon();
            SetupScreenBounds();
            UpdateSystemSizeLabel(star);
        }

        public void OnAsteroidSpawned(AsteroidData asteroid)
        {
            if (!_asteroidIcons.ContainsKey(asteroid.Uid))
                CreateAsteroidIcon(asteroid);
        }
        public void ClearMinimap()
        {
            foreach (var obj in _minimapObjects)
                if (obj != null) Destroy(obj);
            _minimapObjects.Clear();
            _planetIcons.Clear();
            _npcShipImages.Clear();
            _npcShipInRange.Clear();
            _asteroidIcons.Clear();
            _asteroidImages.Clear();
            _wormholeIcons.Clear();
            _playerIcon = null;
            _hzRingObj = null;
            _hzRingGraphic = null;
            if (systemSizeLabel != null) systemSizeLabel.text = "";
            _hoveredAsteroidUid = null;
            _asteroidTrailDots = null;
            if (_asteroidTrailObj != null) { Destroy(_asteroidTrailObj); _asteroidTrailObj = null; }
            DestroyRadarVisuals();
        }

        public void UpdatePlanetPosition(PlanetData planet, Vector3 worldPos)
        {
            if (_planetIcons.TryGetValue(planet, out var rt))
                rt.anchoredPosition = WorldToMapPos(worldPos);
        }

        public void UpdateNpcShipPosition(ShipData ship, Vector2 worldPos)
        {
            if (_npcShipImages.TryGetValue(ship.Uid, out var img) && img != null)
                img.rectTransform.anchoredPosition = WorldToMapPos(worldPos);
        }

        public void UpdatePlayerPosition(Vector2 worldPos)
        {
            if (_playerIcon != null)
                _playerIcon.anchoredPosition = WorldToMapPos(worldPos);
        }

        private void OnEnable()
        {
            GameWorld.OnTurnComplete += OnTurnComplete;
        }

        private void OnDisable()
        {
            GameWorld.OnTurnComplete -= OnTurnComplete;
        }

        private void OnTurnComplete(TurnAnimationData anim)
        {
            if (anim == null) return;
            foreach (var evt in anim.AsteroidDestroys)
                RemoveAsteroidIcon(evt.AsteroidUid);
        }

        private void Update()
        {
            if (!IsReady || screenBoundsRect == null) return;

            Vector3 camPos = mainCamera.transform.position;
            float camSize = mainCamera.orthographicSize;

            if (camPos != _lastCamPos || !Mathf.Approximately(camSize, _lastCamSize))
            {
                RefreshScreenBounds();
                _lastCamPos = camPos;
                _lastCamSize = camSize;
            }

            Vector2 playerWorldPos = Vector2.zero;
            if (_playerIcon != null && PresentationContext.Player != null)
            {
                var pp = PresentationContext.Player.Transform.position;
                playerWorldPos = new Vector2(pp.x, pp.y);
                Vector2 newMapPos = WorldToMapPos(pp);
                if ((newMapPos - _playerIcon.anchoredPosition).sqrMagnitude > 0.0001f)
                    _playerIcon.anchoredPosition = newMapPos;
            }
            float radarWorldRadius = GetPlayerRadarWorldRadius();
            UpdateRadarVisuals(playerWorldPos, radarWorldRadius);
            foreach (var kv in _npcShipImages)
            {
                if (kv.Value == null) continue;
                Vector2 npcMapPos = kv.Value.rectTransform.anchoredPosition;
                Vector2 npcWorldPos = MapPosToWorld(npcMapPos);
                bool inRange = radarWorldRadius <= 0f ||
                               (npcWorldPos - playerWorldPos).magnitude <= radarWorldRadius;
                if (!_npcShipInRange.TryGetValue(kv.Key, out bool wasInRange) || wasInRange != inRange)
                {
                    kv.Value.gameObject.SetActive(inRange);
                    _npcShipInRange[kv.Key] = inRange;
                }
            }
            var star = GameWorld.CurrentStar;
            if (star?.Asteroids != null)
            {
                var svm = PresentationContext.SystemView;
                bool isSimulating = GameWorld.Phase == TurnPhase.Simulation;

                foreach (var asteroid in star.Asteroids)
                {
                    Vector2 astWorldPos;
                    if (_asteroidIcons.TryGetValue(asteroid.Uid, out var rt))
                    {
                        if (isSimulating && svm != null && svm.TryGetAsteroidVisualPosition(asteroid.Uid, out var visPos))
                            astWorldPos = visPos;
                        else
                            astWorldPos = asteroid.Position;
                        rt.anchoredPosition = WorldToMapPos(astWorldPos);
                        bool inRange = radarWorldRadius <= 0f ||
                                       (astWorldPos - playerWorldPos).magnitude <= radarWorldRadius;
                        if (_asteroidImages.TryGetValue(asteroid.Uid, out var img) && img != null)
                            img.gameObject.SetActive(inRange);
                    }
                    else
                        CreateAsteroidIcon(asteroid);
                }
            }

            UpdateAsteroidTrail();
            SyncWormholeIcons(star);
        }

        /// <summary>Держит набор иконок червоточин в синхроне с текущим списком в системе:
        /// добавляет новые, удаляет исчезнувшие. Позиция червоточины фиксирована на срок жизни,
        /// но иконка позиционируется каждый кадр — вдруг сменился world-to-map scale.</summary>
        private void SyncWormholeIcons(StarData star)
        {
            if (star == null) return;
            var whList = star.Wormholes;

            if (whList != null)
            {
                for (int i = 0; i < whList.Count; i++)
                {
                    var wh = whList[i];
                    if (wh == null) continue;
                    if (!_wormholeIcons.TryGetValue(wh.Uid, out var rt) || rt == null)
                        CreateWormholeIcon(wh);
                    else
                        rt.anchoredPosition = WorldToMapPos(wh.Position);
                }
            }

            List<string> toRemove = null;
            foreach (var kv in _wormholeIcons)
            {
                bool alive = false;
                if (whList != null)
                {
                    for (int i = 0; i < whList.Count; i++)
                        if (whList[i] != null && whList[i].Uid == kv.Key) { alive = true; break; }
                }
                if (!alive) (toRemove ??= new List<string>()).Add(kv.Key);
            }
            if (toRemove != null)
            {
                foreach (var id in toRemove)
                {
                    if (_wormholeIcons[id] != null && _wormholeIcons[id].gameObject != null)
                    {
                        _minimapObjects.Remove(_wormholeIcons[id].gameObject);
                        Destroy(_wormholeIcons[id].gameObject);
                    }
                    _wormholeIcons.Remove(id);
                }
            }
        }

        private void CreateWormholeIcon(WormholeData wh)
        {
            if (wh == null || _wormholeIcons.ContainsKey(wh.Uid)) return;
            string iconPath = !string.IsNullOrEmpty(wh.Graphics?.IconPath)
                ? wh.Graphics.IconPath
                : GalaxyConstants.WORMHOLE_ICON_PATH;
            var sprite = GraphicsManager.Instance?.TryGetSprite(iconPath);
            if (sprite == null) return;

            var (_, img, rt) = SpawnMinimapElement($"Mini_Wormhole_{wh.Uid[..6]}", siblingIndex: 1);
            img.sprite = sprite;
            img.color = Color.white;
            img.raycastTarget = false;
            rt.sizeDelta = planetIconSize * 1.6f;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = WorldToMapPos(wh.Position);
            _wormholeIcons[wh.Uid] = rt;
        }

        private float WorldToMapScale()
        {
            if (minimapContainer == null || _worldRadius <= 0) return 1f;
            float minDim = Mathf.Min(minimapContainer.rect.width, minimapContainer.rect.height);
            return minDim * 0.95f / (_worldRadius * 2f);
        }

        private Vector2 WorldToMapPos(Vector3 worldPos)
        {
            float scale = WorldToMapScale();
            return new Vector2(worldPos.x * scale, worldPos.y * scale);
        }

        private Vector2 MapPosToWorld(Vector2 localPos)
        {
            float scale = WorldToMapScale();
            if (scale == 0f) return Vector2.zero;
            return localPos / scale;
        }

        private void SetupScreenBounds()
        {
            if (screenBoundsRect == null) return;
            screenBoundsRect.SetAsLastSibling();
            _lastCamPos = Vector3.one * float.MaxValue;
            RefreshScreenBounds();
        }

        private void RefreshScreenBounds()
        {
            float scale = WorldToMapScale();
            float camHeight = 2f * mainCamera.orthographicSize;
            float camWidth = camHeight * mainCamera.aspect;
            screenBoundsRect.sizeDelta = new Vector2(camWidth * scale, camHeight * scale);
            screenBoundsRect.anchoredPosition = WorldToMapPos(mainCamera.transform.position);
        }

        private void CreateHZRing(StarData star)
        {
            if (star == null || (star.HabitableZoneMin <= 0f && star.HabitableZoneMax <= 0f)) return;

            float scale = WorldToMapScale();
            float innerMapR = SRUnits.ToWorld(star.HabitableZoneMin) * scale;
            float outerMapR = SRUnits.ToWorld(star.HabitableZoneMax) * scale;
            if (outerMapR <= innerMapR || outerMapR <= 0f) return;

            _hzRingObj = new GameObject("HZ_Ring");
            _hzRingObj.transform.SetParent(minimapContainer, false);
            _minimapObjects.Add(_hzRingObj);

            var rt = _hzRingObj.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(outerMapR * 2f, outerMapR * 2f);

            _hzRingGraphic = _hzRingObj.AddComponent<FilledRingGraphic>();
            _hzRingGraphic.InnerRadius = innerMapR;
            _hzRingGraphic.OuterRadius = outerMapR;
            _hzRingGraphic.Segments = 64;
            _hzRingGraphic.color = new Color(0.4f, 1f, 0.5f, 0.18f);
            _hzRingGraphic.raycastTarget = false;
            _hzRingObj.transform.SetSiblingIndex(0);
            _hzRingObj.SetActive(showHabitableZone);
        }

        public void ToggleHabitableZone()
        {
            showHabitableZone = !showHabitableZone;
            if (_hzRingObj != null) _hzRingObj.SetActive(showHabitableZone);
        }

        private void EnsureSystemSizeLabel()
        {
            if (systemSizeLabel != null) return;

            var panelRt = minimapContainer.parent as RectTransform;
            var grandparentRt = panelRt?.parent as RectTransform;
            if (grandparentRt == null) return;

            var go = new GameObject("Minimap_SizeLabel");
            var rt = go.AddComponent<RectTransform>();
            go.transform.SetParent(grandparentRt, false);

            // Повторяем якоря MinimapPanel (top-right), смещаем вниз на высоту панели
            rt.anchorMin = panelRt.anchorMin;
            rt.anchorMax = panelRt.anchorMax;
            rt.pivot = new Vector2(panelRt.pivot.x, 1f);
            rt.sizeDelta = new Vector2(panelRt.sizeDelta.x, 18f);
            rt.anchoredPosition = new Vector2(
                panelRt.anchoredPosition.x,
                panelRt.anchoredPosition.y - panelRt.sizeDelta.y - 2f
            );

            systemSizeLabel = go.AddComponent<Text>();
            systemSizeLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            systemSizeLabel.fontSize = 11;
            systemSizeLabel.alignment = TextAnchor.UpperCenter;
            systemSizeLabel.color = new Color(0.7f, 0.7f, 0.7f, 0.9f);
            systemSizeLabel.raycastTarget = false;
        }

        private void UpdateSystemSizeLabel(StarData star)
        {
            if (star == null) return;
            EnsureSystemSizeLabel();
            if (systemSizeLabel != null)
                systemSizeLabel.text = $"{star.SystemSize} ед.";
        }

        private void CreateStarIcon(StarData star)
        {
            var (_, img, rt) = SpawnMinimapElement("Mini_Star", siblingIndex: 0);

            var gm = GraphicsManager.Instance;
            Sprite sprite = gm?.TryGetSprite(star.MapIcon) ?? gm?.TryGetSprite(PlanetIconPath);

            img.sprite = sprite;
            img.preserveAspect = true;
            rt.sizeDelta = starIconSize;
        }

        private void CreateOrbitRing(PlanetData planet)
        {
            float scale = WorldToMapScale();
            float a = planet.OrbitRadius > 0f
                ? SRUnits.ToWorld(planet.OrbitRadius)
                : (2f + planet.OrbitIndex * 1.5f);

            float e = Mathf.Clamp(planet.OrbitEccentricity, 0f, 0.99f);
            float b = a * Mathf.Sqrt(1f - e * e);
            float c = a * e;

            var obj = new GameObject($"Mini_Orbit_{planet.Name}");
            var rt = obj.AddComponent<RectTransform>();
            var ellipse = obj.AddComponent<EllipseOrbitGraphic>();

            obj.transform.SetParent(minimapContainer, false);
            obj.transform.SetSiblingIndex(0);
            _minimapObjects.Add(obj);
            rt.sizeDelta = new Vector2(a * 2f * scale, b * 2f * scale);
            float tiltRad = planet.OrbitTiltDeg * Mathf.Deg2Rad;
            rt.anchoredPosition = new Vector2(
                -c * scale * Mathf.Cos(tiltRad),
                -c * scale * Mathf.Sin(tiltRad)
            );
            rt.localRotation = Quaternion.Euler(0f, 0f, planet.OrbitTiltDeg);

            ellipse.Eccentricity = e;
            ellipse.color = new Color(1f, 1f, 1f, orbitAlpha);
            ellipse.LineWidth = 1.5f;
            ellipse.Segments = CalculateOrbitSegments(a * scale);
        }

        private int CalculateOrbitSegments(float screenRadius)
        {
            int segments = Mathf.RoundToInt(screenRadius * Mathf.PI * 2f / 2f);
            return Mathf.Clamp(segments, 32, 256);
        }

        private void CreatePlanetIcon(PlanetData planet)
        {
            var ctx = GameWorld.Context;
            Color col = ctx != null
                ? OwnershipDisplayResolver.ResolvePlanetMinimapColor(planet, ctx)
                : (ColorUtility.TryParseHtmlString(planet.CurrentColor, out Color fallback) ? fallback : Color.white);

            var go = new GameObject($"Mini_P_{planet.Name}");
            var rt = go.AddComponent<RectTransform>();
            go.transform.SetParent(minimapContainer, false);
            go.transform.SetSiblingIndex(2);
            _minimapObjects.Add(go);
            rt.sizeDelta = planetIconSize;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

            // Конфигурируемый sprite побеждает; иначе — плоский диск кодом (без обводки).
            Sprite sprite = GraphicsManager.Instance?.TryGetSprite(planet.MinimapIconPath);

            if (sprite != null)
            {
                var img = go.AddComponent<Image>();
                img.sprite = sprite;
                img.color = col;
                img.raycastTarget = false;
            }
            else
            {
                var disc = go.AddComponent<FilledDiskGraphic>();
                disc.color = col;
                disc.Segments = 20;
                disc.raycastTarget = false;
            }

            _planetIcons[planet] = rt;
        }

        private void CreateNpcShipIcon(ShipData ship)
        {
            var ctx = GameWorld.Context;
            Color col = ctx != null
                ? OwnershipDisplayResolver.ResolveShipMinimapColor(ship, ctx)
                : Color.white;

            string iconPath = ResolveShipMinimapIconPath(ship, ctx);

            var go = new GameObject($"Mini_Ship_{ship.Uid}");
            var rt = go.AddComponent<RectTransform>();
            go.transform.SetParent(minimapContainer, false);
            go.transform.SetSiblingIndex(1);
            _minimapObjects.Add(go);
            rt.sizeDelta = shipIconSize;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = WorldToMapPos(ship.Position);

            MaskableGraphic marker = BuildShipMarker(go, iconPath, col, ShipOutlineColor);
            _npcShipImages[ship.Uid] = marker;
        }

        private void CreatePlayerIcon()
        {
            var ctx = GameWorld.Context;
            ShipData shipData = PresentationContext.Player?.ShipData;
            string iconPath = shipData != null ? ResolveShipMinimapIconPath(shipData, ctx) : null;

            var go = new GameObject("Mini_Player");
            var rt = go.AddComponent<RectTransform>();
            go.transform.SetParent(minimapContainer, false);
            go.transform.SetSiblingIndex(0);
            _minimapObjects.Add(go);
            rt.sizeDelta = playerIconSize;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

            BuildShipMarker(go, iconPath, PlayerInnerColor, PlayerOutlineColor);
            rt.SetAsLastSibling();
            _playerIcon = rt;
        }

        /// <summary>
        /// Рисует «корабельный» маркер: либо спрайт-иконку (если задан путь и сприайт загрузился),
        /// либо код-нарисованный диск с белой обводкой — визуально отличный от плоского кружка планет.
        /// </summary>
        private static MaskableGraphic BuildShipMarker(GameObject go, string iconPath, Color innerColor, Color outlineColor)
        {
            Sprite sprite = GraphicsManager.Instance?.TryGetSprite(iconPath);
            if (sprite != null)
            {
                var img = go.AddComponent<Image>();
                img.sprite = sprite;
                img.color = innerColor;
                img.raycastTarget = false;
                return img;
            }

            var disc = go.AddComponent<FilledDiskGraphic>();
            disc.color = innerColor;
            disc.Segments = 18;
            disc.OutlineThickness = ShipOutlineThickness;
            disc.OutlineColor = outlineColor;
            disc.raycastTarget = false;
            return disc;
        }

        /// <summary>
        /// Путь к иконке корабля для миникарты: приоритет — у установленного корпуса (HullTypeDef.MinimapIconPath),
        /// затем fallback на ShipType.MinimapIconPath (устаревшее поле). null → код-нарисованный кружок.
        /// </summary>
        private static string ResolveShipMinimapIconPath(ShipData ship, GalaxyGenerationContext ctx)
        {
            if (ship == null) return null;

            var hull = EquipmentSystem.GetEquipped(ship, SlotKeys.Hull);
            var hullTpl = ctx?.ItemsConfig?.GetTemplate(EquipmentCategory.Hull);
            if (hull != null && hullTpl?.HullTypes != null)
            {
                string hullTypeKey = hull.GetParamString("HullType");
                if (!string.IsNullOrEmpty(hullTypeKey)
                    && hullTpl.HullTypes.TryGetValue(hullTypeKey, out var def)
                    && !string.IsNullOrEmpty(def?.MinimapIconPath))
                    return def.MinimapIconPath;
            }

            return !string.IsNullOrEmpty(ship.MinimapIconPath) ? ship.MinimapIconPath : null;
        }

        private (GameObject obj, Image img, RectTransform rt) SpawnMinimapElement(string name, int siblingIndex)
        {
            var obj = new GameObject(name);
            var rt = obj.AddComponent<RectTransform>();
            var img = obj.AddComponent<Image>();
            obj.transform.SetParent(minimapContainer, false);
            obj.transform.SetSiblingIndex(siblingIndex);
            _minimapObjects.Add(obj);
            return (obj, img, rt);
        }

        private void CreateAsteroidIcon(AsteroidData asteroid)
        {
            var (_, img, rt) = SpawnMinimapElement($"Mini_Asteroid_{asteroid.Uid}", siblingIndex: 1);

            var gm = GraphicsManager.Instance;
            Sprite icon = gm?.TryGetSprite(AsteroidIconPath) ?? gm?.TryGetSprite(PlanetIconPath);

            img.sprite = icon;
            img.color = new Color(0.72f, 0.68f, 0.60f, 0.9f);

            rt.sizeDelta = planetIconSize * 0.75f;
            rt.anchoredPosition = WorldToMapPos(asteroid.Position);

            _asteroidIcons[asteroid.Uid] = rt;
            _asteroidImages[asteroid.Uid] = img;

            var trigger = (rt.gameObject.GetComponent<UnityEngine.EventSystems.EventTrigger>()
                        ?? rt.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>());

            string uid = asteroid.Uid;
            var enterEntry = new UnityEngine.EventSystems.EventTrigger.Entry
            { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
            enterEntry.callback.AddListener(_ => _hoveredAsteroidUid = uid);
            trigger.triggers.Add(enterEntry);

            var exitEntry = new UnityEngine.EventSystems.EventTrigger.Entry
            { eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit };
            exitEntry.callback.AddListener(_ => { if (_hoveredAsteroidUid == uid) _hoveredAsteroidUid = null; });
            trigger.triggers.Add(exitEntry);
        }

        private void RemoveAsteroidIcon(string uid)
        {
            if (!_asteroidIcons.TryGetValue(uid, out var rt)) return;
            if (rt != null && rt.gameObject != null)
            {
                _minimapObjects.Remove(rt.gameObject);
                Destroy(rt.gameObject);
            }
            _asteroidIcons.Remove(uid);
            _asteroidImages.Remove(uid);

            if (_hoveredAsteroidUid == uid)
            {
                _hoveredAsteroidUid = null;
                SetTrailVisible(false);
            }
        }

        private void UpdateAsteroidTrail()
        {
            if (string.IsNullOrEmpty(_hoveredAsteroidUid))
            {
                SetTrailVisible(false);
                return;
            }

            var star = GameWorld.CurrentStar;
            AsteroidData src = star?.Asteroids?.Find(a => a.Uid == _hoveredAsteroidUid);
            if (src == null || src.IsDestroyed)
            {
                SetTrailVisible(false);
                return;
            }

            EnsureTrailDots();
            SetTrailVisible(true);

            float gravityConst = GameWorld.Context?.Config?.Asteroids?.GravityConst
                                 ?? GalaxyConstants.ASTEROID_GRAVITY_CONST;

            var pos = src.Position;
            var vel = src.Velocity;
            float systemRadius = SRUnits.ToWorld(star.SystemSize) * 1.5f;

            int step = Mathf.Max(1, TrailPredictSteps / TrailDotCount);
            int dotIdx = 0;

            for (int i = 0; i < TrailPredictSteps && dotIdx < TrailDotCount; i++)
            {
                Vector2 toStar = -pos;
                float distSq = toStar.sqrMagnitude;
                if (distSq >= 0.01f)
                {
                    float acc = gravityConst / distSq;
                    vel += toStar.normalized * acc / GalaxyData.SubTurnsPerTurn;
                }
                pos += vel / GalaxyData.SubTurnsPerTurn;

                if ((i + 1) % step == 0)
                {
                    var dot = _asteroidTrailDots[dotIdx];
                    dot.rectTransform.anchoredPosition = WorldToMapPos(pos);

                    float alpha = Mathf.Lerp(0.7f, 0.1f, (float)dotIdx / TrailDotCount);
                    dot.color = new Color(1f, 0.85f, 0.3f, alpha);
                    if (pos.magnitude > systemRadius)
                    {
                        for (int j = dotIdx; j < TrailDotCount; j++)
                            _asteroidTrailDots[j].color = Color.clear;
                        break;
                    }
                    dotIdx++;
                }
            }

            for (int i = dotIdx; i < TrailDotCount; i++)
                _asteroidTrailDots[i].color = Color.clear;
        }

        private void EnsureTrailDots()
        {
            if (_asteroidTrailDots != null && _asteroidTrailDots.Length == TrailDotCount) return;

            if (_asteroidTrailObj != null) Destroy(_asteroidTrailObj);
            _asteroidTrailObj = new GameObject("AsteroidTrail");
            _asteroidTrailObj.transform.SetParent(minimapContainer, false);
            _minimapObjects.Add(_asteroidTrailObj);

            _asteroidTrailDots = new UnityEngine.UI.Image[TrailDotCount];
            float dotSize = 2.5f;
            for (int i = 0; i < TrailDotCount; i++)
            {
                var go = new GameObject($"TrailDot_{i}");
                go.transform.SetParent(_asteroidTrailObj.transform, false);
                var img = go.AddComponent<UnityEngine.UI.Image>();
                var rt = go.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(dotSize, dotSize);

                var sprite = GraphicsManager.Instance?.TryGetSprite(PlanetIconPath);
                if (sprite != null) img.sprite = sprite;
                img.color = Color.clear;

                img.raycastTarget = false;

                _asteroidTrailDots[i] = img;
            }
        }

        private void SetTrailVisible(bool visible)
        {
            if (_asteroidTrailDots == null) return;
            if (!visible)
                foreach (var dot in _asteroidTrailDots)
                    if (dot != null) dot.color = Color.clear;
        }

        private float GetPlayerRadarWorldRadius()
        {
            var ship = PresentationContext.Player?.ShipData;
            if (ship == null)
                return 0f;

            float result = EquipmentSystem.GetRadarRange(ship);
            return result;
        }

        private void UpdateRadarVisuals(Vector2 playerWorldPos, float radarWorldRadius)
        {
            if (minimapContainer == null) return;

            if (radarWorldRadius <= 0f)
            {
                if (_radarRingObj != null) _radarRingObj.SetActive(false);
                if (_radarFogObj != null) _radarFogObj.SetActive(false);
                _lastRadarWorldRadius = -1f;
                _lastRadarMapPos = new Vector2(float.MaxValue, float.MaxValue);
                _lastRadarMapRadius = -1f;
                return;
            }

            float scale = WorldToMapScale();
            float radarMapRadius = radarWorldRadius * scale;
            Vector2 playerMapPos = playerWorldPos * scale;

            bool posChanged = (playerMapPos - _lastRadarMapPos).sqrMagnitude > 0.0001f;
            bool radChanged = !Mathf.Approximately(radarMapRadius, _lastRadarMapRadius);

            EnsureRadarRing();
            _radarRingObj.SetActive(true);
            if (posChanged || radChanged)
            {
                var ringRt = _radarRingObj.GetComponent<RectTransform>();
                ringRt.anchoredPosition = playerMapPos;
                float ringDiameter = radarMapRadius * 2f;
                ringRt.sizeDelta = new Vector2(ringDiameter, ringDiameter);
                _radarRingGraphic.SetLayoutDirty();
            }

            EnsureRadarFog();
            _radarFogObj.SetActive(true);
            if (posChanged || radChanged)
            {
                _radarFogGraphic.HoleCenter = playerMapPos;
                _radarFogGraphic.HoleRadius = radarMapRadius;
                _radarFogGraphic.SetVerticesDirty();
                _lastRadarMapPos = playerMapPos;
                _lastRadarMapRadius = radarMapRadius;
            }

            _lastRadarWorldRadius = radarWorldRadius;
        }

        private void EnsureRadarRing()
        {
            if (_radarRingObj != null) return;

            _radarRingObj = new GameObject("Radar_Ring");
            _radarRingObj.transform.SetParent(minimapContainer, false);

            var rt = _radarRingObj.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

            _radarRingGraphic = _radarRingObj.AddComponent<EllipseOrbitGraphic>();
            _radarRingGraphic.Eccentricity = 0f;
            _radarRingGraphic.LineWidth = 1.5f;
            _radarRingGraphic.Segments = 64;
            _radarRingGraphic.color = new Color(1f, 0.9f, 0.1f, 0.85f);
            _radarRingGraphic.raycastTarget = false;
            _radarRingObj.transform.SetSiblingIndex(minimapContainer.childCount - 1);
        }

        private void EnsureRadarFog()
        {
            if (_radarFogObj != null) return;

            _radarFogObj = new GameObject("Radar_Fog");
            _radarFogObj.transform.SetParent(minimapContainer, false);

            var rt = _radarFogObj.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            _radarFogGraphic = _radarFogObj.AddComponent<RadarFogGraphic>();
            _radarFogGraphic.FogColor = new Color(0f, 0f, 0f, 0.6f);
            _radarFogGraphic.raycastTarget = false;
            _radarFogGraphic.Segments = 64;
            _radarFogObj.transform.SetAsLastSibling();
        }

        private void DestroyRadarVisuals()
        {
            if (_radarRingObj != null) { Destroy(_radarRingObj); _radarRingObj = null; _radarRingGraphic = null; }
            if (_radarFogObj != null) { Destroy(_radarFogObj); _radarFogObj = null; _radarFogGraphic = null; }
            _lastRadarWorldRadius = -1f;
            _lastRadarMapPos = new Vector2(float.MaxValue, float.MaxValue);
            _lastRadarMapRadius = -1f;
        }

        private void MoveCameraToPoint(PointerEventData eventData)
        {
            if (!IsReady) return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                minimapContainer, eventData.position, eventData.pressEventCamera, out Vector2 local))
            {
                Vector2 worldXY = MapPosToWorld(local);
                mainCamera.transform.position = new Vector3(
                    worldXY.x,
                    worldXY.y,
                    mainCamera.transform.position.z
                );
                RefreshScreenBounds();
            }
        }

        public void OnDrag(PointerEventData e) => MoveCameraToPoint(e);
        public void OnPointerClick(PointerEventData e) => MoveCameraToPoint(e);
        public void OnPointerEnter(PointerEventData e) => IsMouseOverMinimap = true;
        public void OnPointerExit(PointerEventData e) => IsMouseOverMinimap = false;
    }
}
