using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SRG.Config;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Presentation.Common;
using SRG.Presentation.Map;
using SRG.Ships.Movement;

namespace SRG.UI.Screens
{
    /// <summary>
    /// Галактическая карта. Открывается клавишей M.
    /// Масштаб: MapPixelsPerParsec из GalaxyConfig.json → MilkyWay.MapPixelsPerParsec
    /// Зум: колесо мыши над областью карты (центр зума — курсор).
    /// </summary>
    public class GalaxyMapController : MonoBehaviour
    {
        public static GalaxyMapController Instance { get; private set; }

        private const float SB       = 4f;    // толщина скроллбаров, px
        private const float ZOOM_MAX = 6f;
        private const float ZOOM_SPD = 0.12f; // шаг зума на одно деление колеса

        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Спрайты (опционально)")]
        [SerializeField] private Sprite panelBgSprite;
        [SerializeField] private Sprite mapBgSprite;

        [Header("Размер панели")]
        [SerializeField] private Vector2 panelSize  = new Vector2(940f, 680f);
        [SerializeField] private float   titleHeight = 36f;

        // ── Ссылки ────────────────────────────────────────────────────────────────
        private Canvas            _canvas;
        private GameObject        _rootGo;
        private RectTransform     _rootRT;
        private RectTransform     _vpRT;
        private RectTransform     _mapContent;
        private ScrollRect        _scrollRect;
        private GameObject        _hSbGO;
        private GameObject        _vSbGO;
        private RectTransform     _starContainer;
        private GalaxyMapGraphics _voronoi;

        // ── Название галактики + тултип ───────────────────────────────────────────
        private Text       _galaxyNameText;
        private GameObject _galaxyTooltipGO;
        private Text       _galaxyTooltipText;

        // ── Тултип звезды (на hover по иконке звезды) ─────────────────────────────
        private GameObject _starTooltipGO;
        private Image      _starTooltipIcon;
        private Text       _starTooltipNameText;
        private Text       _starTooltipPlanetsText;

        // ── Режим окраски секторов ────────────────────────────────────────────────
        private enum ColorMode { Owner, Race }
        private ColorMode _colorMode = ColorMode.Owner;
        private Image     _btnModeOwner;
        private Image     _btnModeRace;
        private Image     _btnExpansion;
        private bool      _showExpansionLines = false;

        // ── Данные ───────────────────────────────────────────────────────────────
        private GalaxyData       _galaxy;
        private GalaxyConfigData _galCfg;
        /// <summary>Ключ галактики, которую ПРОСМАТРИВАЕТ пользователь. Может отличаться от активной
        /// (игрок в галактике A, смотрит галактику B). Для полёта используем активную (через прыжки/телепорт).</summary>
        private string           _viewingGalaxyKey;
        private GameObject       _btnSwapGalaxyGO;
        private Text             _btnSwapGalaxyLabel;
        private Vector2          _baseMapSize;  // размер при zoom = 1
        private float            _zoom = 1f;
        private Vector2          MapSize => _baseMapSize * _zoom;

        // Список иконок звёзд: RT + исходная позиция в сетке
        private readonly List<(RectTransform rt, Vector2 grid)> _starRTs = new();

        // Подписи секторов: RT + центр сектора в сетке
        private RectTransform _sectorLabelContainer;
        private readonly List<(RectTransform rt, Vector2 grid)> _sectorLabelRTs = new();

        // Debug: координатные подписи (Вороной-вершины)
        private RectTransform _debugLabelContainer;
        private readonly List<(RectTransform rt, Vector2 grid)> _debugLabelRTs = new();

        // Подписи линий экспансии (расстояние + год)
        private RectTransform _expansionLabelContainer;
        private readonly List<(RectTransform rt, Vector2 gridMid)> _expansionLabelRTs = new();

        // ── Состояние ─────────────────────────────────────────────────────────────
        private bool    _isOpen;
        private bool    _dragging;
        private Vector2 _dragOffset;

        // Гиперпереход: выбранная цель и UI-элементы кнопки.
        private StarData _selectedTarget;
        private GameObject _jumpBtnGO;
        private Image     _jumpBtnImg;
        private Text      _jumpBtnLabel;
        private readonly List<(RectTransform rt, StarData star, Image img)> _starButtons = new();

        // ══════════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void Update()
        {
            if (GameConsoleController.IsOpen) return;
            if (Input.GetKeyDown(KeyCode.M)) ToggleMap();
            if (!_isOpen) return;
            HandleDrag();
            HandleZoom();
        }

        // ══════════════════════════════════════════════════════════════════════════
        // API

        public void ToggleMap() { if (_isOpen) CloseMap(); else OpenMap(); }

        public void OpenMap()
        {
            var gm = GalaxyManager.Instance;
            if (gm == null) return;
            // При открытии показываем активную галактику (там, где игрок).
            _viewingGalaxyKey = gm.ActiveGalaxyKey;
            _galaxy = gm.GeneratedGalaxy;
            if (_galaxy == null) return;

            var ctx = gm.Context;
            if (ctx?.Config?.Galaxies == null) return;
            string key = !string.IsNullOrEmpty(_viewingGalaxyKey)
                ? _viewingGalaxyKey : GalaxyConstants.DEFAULT_GALAXY_KEY;
            if (!ctx.Config.Galaxies.TryGetValue(key, out _galCfg)) return;

            float ppp  = _galCfg.MapPixelsPerParsec > 0 ? _galCfg.MapPixelsPerParsec : 4f;
            var   grid = _galCfg.GalaxyGridSize;
            float szW  = (grid != null && grid.Length >= 2) ? grid[0] : _galaxy.Width;
            float szH  = (grid != null && grid.Length >= 2) ? grid[1] : _galaxy.Height;
            _baseMapSize = new Vector2(szW * ppp, szH * ppp);

            // Соотношение сторон панели: подбираем высоту так, чтобы VIEWPORT (панель минус UI-оверхед)
            // имел то же соотношение сторон, что и галактика.
            // Горизонтальный оверхед: 4 (left) + 4 (right) = 8 px
            // Вертикальный оверхед: 4 (top gap) + titleHeight + 4 (title bottom) + 4 (bottom) = titleHeight+12
            if (grid != null && grid.Length >= 2 && grid[0] > 0)
            {
                float vpW = panelSize.x - 8f;
                panelSize = new Vector2(panelSize.x, vpW * grid[1] / grid[0] + titleHeight + 12f);
            }

            if (_rootGo == null) BuildUI();
            else if (_rootRT != null) _rootRT.sizeDelta = panelSize;
            _zoom = Mathf.Clamp(_zoom, ZoomMin(), ZOOM_MAX);
            RefreshContent();

            _rootGo.SetActive(true);
            _isOpen = true;
        }

        public void CloseMap()
        {
            _isOpen = _dragging = false;
            if (_rootGo != null) _rootGo.SetActive(false);
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Drag окна за заголовок

        private void HandleDrag()
        {
            if (_rootRT == null) return;
            if (Input.GetMouseButtonDown(0) && IsMouseOverTitle())
            {
                _dragging   = true;
                _dragOffset = _rootRT.anchoredPosition - ScreenToCanvas(Input.mousePosition);
            }
            if (Input.GetMouseButtonUp(0)) _dragging = false;
            if (_dragging && Input.GetMouseButton(0))
                _rootRT.anchoredPosition = ScreenToCanvas(Input.mousePosition) + _dragOffset;
        }

        private bool IsMouseOverTitle()
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rootRT, Input.mousePosition, null, out var lp);
            var r = _rootRT.rect;
            return lp.x >= r.xMin && lp.x <= r.xMax
                && lp.y >= r.yMax - titleHeight - 4f && lp.y <= r.yMax;
        }

        private Vector2 ScreenToCanvas(Vector2 sp) =>
            sp - new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        // ══════════════════════════════════════════════════════════════════════════
        // Зум колесом мыши (центр зума — позиция курсора)

        // Минимальный зум: карта не должна быть меньше области viewport
        private float ZoomMin()
        {
            if (_vpRT == null || _baseMapSize.x <= 0 || _baseMapSize.y <= 0) return 0.1f;
            var r = _vpRT.rect;
            if (r.width <= 0 || r.height <= 0) return 0.1f;
            return Mathf.Min(ZOOM_MAX, Mathf.Max(r.width / _baseMapSize.x, r.height / _baseMapSize.y));
        }

        private void HandleZoom()
        {
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) < 0.001f) return;
            if (!IsMouseOverViewport()) return;

            // Позиция курсора в системе координат viewport (нормализованная [0..1])
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _vpRT, Input.mousePosition, null, out var localInVp);
            var vpRect = _vpRT.rect;
            float normX = Mathf.Clamp01((localInVp.x - vpRect.xMin) / Mathf.Max(1f, vpRect.width));
            float normY = Mathf.Clamp01((localInVp.y - vpRect.yMin) / Mathf.Max(1f, vpRect.height));

            // Точка на контенте под курсором до зума
            float oldW    = MapSize.x;
            float oldH    = MapSize.y;
            float vpW     = vpRect.width;
            float vpH     = vpRect.height;
            float oldHScr = _scrollRect != null ? _scrollRect.horizontalNormalizedPosition : 0.5f;
            float oldVScr = _scrollRect != null ? _scrollRect.verticalNormalizedPosition   : 0.5f;
            float contentX = oldHScr * Mathf.Max(0f, oldW - vpW) + normX * vpW;
            float contentY = oldVScr * Mathf.Max(0f, oldH - vpH) + normY * vpH;

            // Новый зум (не меньше размера viewport)
            float delta = wheel > 0 ? ZOOM_SPD : -ZOOM_SPD;
            _zoom = Mathf.Clamp(_zoom + delta, ZoomMin(), ZOOM_MAX);

            // Обновляем размер контента и позиции иконок
            _mapContent.sizeDelta = MapSize;
            UpdateStarPositions();
            AdjustScrollbars();

            // Восстанавливаем точку под курсором
            if (_scrollRect != null)
            {
                float newW = MapSize.x;
                float newH = MapSize.y;
                // Масштабируем точку пропорционально новому размеру
                float scaledX = contentX * (newW / Mathf.Max(1f, oldW));
                float scaledY = contentY * (newH / Mathf.Max(1f, oldH));
                float newHScr = (scaledX - normX * vpW) / Mathf.Max(1f, newW - vpW);
                float newVScr = (scaledY - normY * vpH) / Mathf.Max(1f, newH - vpH);
                _scrollRect.horizontalNormalizedPosition = Mathf.Clamp01(newHScr);
                _scrollRect.verticalNormalizedPosition   = Mathf.Clamp01(newVScr);
            }
        }

        private bool IsMouseOverViewport()
        {
            if (_vpRT == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(_vpRT, Input.mousePosition, null);
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Построение UI

        private void BuildUI()
        {
            var cgo = new GameObject("GalaxyMapCanvas");
            DontDestroyOnLoad(cgo);
            _canvas = cgo.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 200;
            cgo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            cgo.AddComponent<GraphicRaycaster>();

            _rootGo = new GameObject("GalaxyMapPanel", typeof(RectTransform));
            _rootGo.transform.SetParent(_canvas.transform, false);
            _rootRT = _rootGo.GetComponent<RectTransform>();
            _rootRT.anchorMin = _rootRT.anchorMax = new Vector2(0.5f, 0.5f);
            _rootRT.pivot            = new Vector2(0.5f, 0.5f);
            _rootRT.sizeDelta        = panelSize;
            _rootRT.anchoredPosition = Vector2.zero;

            var bg = _rootGo.AddComponent<Image>();
            if (panelBgSprite != null) { bg.sprite = panelBgSprite; bg.type = Image.Type.Sliced; }
            else bg.color = new Color(0.07f, 0.08f, 0.12f, 0.97f);

            BuildTitleBar();
            BuildScrollArea();
            BuildStarTooltip();
        }

        private void BuildTitleBar()
        {
            var go = new GameObject("TitleBar", typeof(RectTransform));
            go.transform.SetParent(_rootGo.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(4f, -titleHeight);
            rt.offsetMax = new Vector2(-4f, -4f);
            go.AddComponent<Image>().color = new Color(0.04f, 0.05f, 0.10f, 1f);

            // Статичная подпись слева
            Label(go.transform, "Title", "Галакт. карта", 13,
                new Vector2(6f, 0f), new Vector2(-panelSize.x + 120f, 0f),
                new Color(0.60f, 0.63f, 0.75f, 0.8f), TextAnchor.MiddleLeft);

            // Кнопки режима окраски
            BuildModeButtons(go.transform);

            // Кнопка переключения просматриваемой галактики (если галактик > 1)
            BuildSwapGalaxyButton(go.transform);

            // Кнопка запуска/отмены гиперперехода
            BuildJumpButton(go.transform);

            // Название галактики — интерактивный элемент в центре
            var nameGO = new GameObject("GalaxyName", typeof(RectTransform));
            nameGO.transform.SetParent(go.transform, false);
            var nameRT = nameGO.GetComponent<RectTransform>();
            // anchor.min.x=0.52 — чтобы область названия (raycastTarget=true для тултипа)
            // не перекрывала кнопку «▷ Галактика» (x=320..480 в TitleBar ~932 px = ~0.34..0.52).
            nameRT.anchorMin = new Vector2(0.52f, 0f);
            nameRT.anchorMax = new Vector2(0.7f, 1f);
            nameRT.offsetMin = Vector2.zero;
            nameRT.offsetMax = Vector2.zero;
            _galaxyNameText = nameGO.AddComponent<Text>();
            _galaxyNameText.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _galaxyNameText.fontSize      = 16;
            _galaxyNameText.fontStyle     = FontStyle.Bold;
            _galaxyNameText.alignment     = TextAnchor.MiddleCenter;
            _galaxyNameText.color         = new Color(0.88f, 0.92f, 1f, 1f);
            _galaxyNameText.raycastTarget = true;

            // Привязать hover-события через EventTrigger
            var et = nameGO.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            AddEventTrigger(et, UnityEngine.EventSystems.EventTriggerType.PointerEnter,
                _ => { if (_galaxyTooltipGO != null) _galaxyTooltipGO.SetActive(true); });
            AddEventTrigger(et, UnityEngine.EventSystems.EventTriggerType.PointerExit,
                _ => { if (_galaxyTooltipGO != null) _galaxyTooltipGO.SetActive(false); });

            // Тултип (изначально скрыт)
            BuildGalaxyTooltip();

            // Кнопка закрытия справа
            float bs = titleHeight - 6f;
            var btn = new GameObject("CloseBtn", typeof(RectTransform));
            btn.transform.SetParent(go.transform, false);
            var brt = btn.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = new Vector2(1f, 0.5f);
            brt.pivot = new Vector2(1f, 0.5f);
            brt.anchoredPosition = new Vector2(-3f, 0f);
            brt.sizeDelta = new Vector2(bs, bs);
            var bi = btn.AddComponent<Image>();
            bi.color = new Color(0.5f, 0.12f, 0.12f, 1f);
            var b = btn.AddComponent<Button>();
            b.targetGraphic = bi;
            var bc = b.colors;
            bc.highlightedColor = new Color(0.8f, 0.2f, 0.2f, 1f);
            bc.pressedColor     = new Color(0.3f, 0.06f, 0.06f, 1f);
            b.colors = bc;
            b.onClick.AddListener(CloseMap);
            Label(btn.transform, "X", "×", 18, Vector2.zero, Vector2.zero, Color.white, TextAnchor.MiddleCenter);
        }

        private void BuildGalaxyTooltip()
        {
            // Тултип размещается в корне панели, под шапкой
            _galaxyTooltipGO = new GameObject("GalaxyTooltip", typeof(RectTransform));
            _galaxyTooltipGO.transform.SetParent(_rootGo.transform, false);
            var tRT = _galaxyTooltipGO.GetComponent<RectTransform>();
            tRT.anchorMin        = new Vector2(0.5f, 1f);
            tRT.anchorMax        = new Vector2(0.5f, 1f);
            tRT.pivot            = new Vector2(0.5f, 1f);
            tRT.sizeDelta        = new Vector2(200f, 48f);
            tRT.anchoredPosition = new Vector2(0f, -(4f + titleHeight + 4f));
            var tBg = _galaxyTooltipGO.AddComponent<Image>();
            tBg.color = new Color(0.06f, 0.07f, 0.12f, 0.97f);

            var tTextGO = new GameObject("Text", typeof(RectTransform));
            tTextGO.transform.SetParent(_galaxyTooltipGO.transform, false);
            var tTRT = tTextGO.GetComponent<RectTransform>();
            tTRT.anchorMin = Vector2.zero; tTRT.anchorMax = Vector2.one;
            tTRT.offsetMin = new Vector2(8f, 4f); tTRT.offsetMax = new Vector2(-8f, -4f);
            _galaxyTooltipText = tTextGO.AddComponent<Text>();
            _galaxyTooltipText.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _galaxyTooltipText.fontSize      = 12;
            _galaxyTooltipText.alignment     = TextAnchor.UpperLeft;
            _galaxyTooltipText.color         = new Color(0.80f, 0.84f, 0.95f, 1f);
            _galaxyTooltipText.raycastTarget = false;

            _galaxyTooltipGO.SetActive(false);
        }

        private void BuildStarTooltip()
        {
            _starTooltipGO = new GameObject("StarTooltip", typeof(RectTransform));
            _starTooltipGO.transform.SetParent(_rootGo.transform, false);
            var rt = _starTooltipGO.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(220f, 100f);

            var bg = _starTooltipGO.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.07f, 0.12f, 0.97f);
            bg.raycastTarget = false;

            var iconGO = new GameObject("Icon", typeof(RectTransform));
            iconGO.transform.SetParent(_starTooltipGO.transform, false);
            var irt = iconGO.GetComponent<RectTransform>();
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 1f);
            irt.pivot     = new Vector2(0f, 1f);
            irt.anchoredPosition = new Vector2(6f, -6f);
            irt.sizeDelta        = new Vector2(24f, 24f);
            _starTooltipIcon = iconGO.AddComponent<Image>();
            _starTooltipIcon.raycastTarget = false;

            var nameGO = new GameObject("Name", typeof(RectTransform));
            nameGO.transform.SetParent(_starTooltipGO.transform, false);
            var nrt = nameGO.GetComponent<RectTransform>();
            nrt.anchorMin = new Vector2(0f, 1f); nrt.anchorMax = new Vector2(1f, 1f);
            nrt.pivot     = new Vector2(0f, 1f);
            nrt.offsetMin = new Vector2(34f, -30f); nrt.offsetMax = new Vector2(-6f, -6f);
            _starTooltipNameText = nameGO.AddComponent<Text>();
            _starTooltipNameText.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _starTooltipNameText.fontSize      = 14;
            _starTooltipNameText.fontStyle     = FontStyle.Bold;
            _starTooltipNameText.alignment     = TextAnchor.MiddleLeft;
            _starTooltipNameText.color         = new Color(0.88f, 0.92f, 1f);
            _starTooltipNameText.raycastTarget = false;
            _starTooltipNameText.supportRichText= true;

            var planetsGO = new GameObject("Planets", typeof(RectTransform));
            planetsGO.transform.SetParent(_starTooltipGO.transform, false);
            var prt = planetsGO.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(8f, 6f); prt.offsetMax = new Vector2(-6f, -36f);
            _starTooltipPlanetsText = planetsGO.AddComponent<Text>();
            _starTooltipPlanetsText.font            = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _starTooltipPlanetsText.fontSize        = 12;
            _starTooltipPlanetsText.alignment       = TextAnchor.UpperLeft;
            _starTooltipPlanetsText.color           = new Color(0.85f, 0.85f, 0.9f);
            _starTooltipPlanetsText.supportRichText = true;
            _starTooltipPlanetsText.raycastTarget   = false;

            _starTooltipGO.SetActive(false);
        }

        private void ShowStarTooltip(StarData star, RectTransform starRT)
        {
            if (_starTooltipGO == null || star == null || starRT == null) return;
            var ctx = GalaxyManager.Instance?.Context;

            var spr = GraphicsManager.Instance?.TryGetSprite(star.MapIcon);
            if (spr != null) { _starTooltipIcon.sprite = spr; _starTooltipIcon.color = Color.white; }
            else             { _starTooltipIcon.sprite = null; _starTooltipIcon.color = StarColor(star.Color); }

            _starTooltipNameText.text  = string.IsNullOrEmpty(star.Name) ? "?" : star.Name;
            _starTooltipNameText.color = StarNameColor(star, ctx);

            var sb = new System.Text.StringBuilder();
            if (star.Planets != null && star.Planets.Count > 0)
            {
                var sorted = new List<PlanetData>(star.Planets);
                sorted.Sort((a, b) => a.OrbitIndex.CompareTo(b.OrbitIndex));
                foreach (var p in sorted)
                {
                    string label   = string.IsNullOrEmpty(p.Name) ? "?" : p.Name;
                    string hex     = GetPlanetNameColorHex(p, ctx);
                    sb.AppendLine($"<color=#{hex}>{label}</color>");
                }
            }
            else
            {
                sb.AppendLine("<color=#7B7B7B>нет планет</color>");
            }

            // Дальняя антенна: если у игрока есть артефакт с Artefacts.GalaxyMapScope > 0,
            // и звезда в пределах его радиуса — показать сводку кораблей по типу и стороне.
            int shipLines = AppendProlongerShipsInfo(sb, star);

            _starTooltipPlanetsText.text = sb.ToString();

            int planetCount = star.Planets?.Count ?? 0;
            float bodyH = Mathf.Max(1, planetCount + shipLines) * 16f + 12f;
            float h     = Mathf.Min(340f, 36f + bodyH);
            var rt = _starTooltipGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(220f, h);

            // Позиция: переводим экранную позицию иконки звезды в локальные координаты панели.
            Vector3 worldPos = starRT.TransformPoint(new Vector3(starRT.rect.width * 0.5f, starRT.rect.height * 0.5f, 0));
            Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(null, worldPos);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRT, screenPos, null, out Vector2 panelLocal);

            // Сместить вправо-вверх от звезды; если уходит за правую границу — повернуть влево.
            Vector2 offset = new Vector2(14f, 14f);
            Vector2 pos = panelLocal + offset;
            var panelHalf = _rootRT.rect.size * 0.5f;
            if (pos.x + rt.sizeDelta.x > panelHalf.x) pos.x = panelLocal.x - rt.sizeDelta.x - 14f;
            if (pos.y > panelHalf.y)                  pos.y = panelHalf.y - 2f;
            if (pos.y - rt.sizeDelta.y < -panelHalf.y) pos.y = -panelHalf.y + rt.sizeDelta.y + 2f;
            rt.anchoredPosition = pos;

            _starTooltipGO.SetActive(true);
            _starTooltipGO.transform.SetAsLastSibling();
        }

        private void HideStarTooltip()
        {
            if (_starTooltipGO != null) _starTooltipGO.SetActive(false);
        }

        /// <summary>Показывает сводку кораблей в системе <paramref name="star"/>, если игрок
        /// находится в пределах Дальней антенны (<c>Artefacts.GalaxyMapScope</c>) от неё. Возвращает
        /// число дописанных строк (для расчёта высоты тултипа).</summary>
        private int AppendProlongerShipsInfo(System.Text.StringBuilder sb, StarData star)
        {
            if (star?.Ships == null || star.Ships.Count == 0) return 0;
            var player = PlayerManager.Instance?.GetOrFindPlayerShip();
            if (player == null) return 0;
            float scope = StatBus.SumShipCategory(player, EquipmentCategory.Artefacts, "GalaxyMapScope");
            if (scope <= 0f) return 0;
            var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
            if (galaxy == null || string.IsNullOrEmpty(player.CurrentStarUid)) return 0;
            if (!galaxy.StarsMap.TryGetValue(player.CurrentStarUid, out var playerStar)) return 0;
            if (HyperjumpController.CalcDistance(playerStar, star) > scope) return 0;

            // Сводим корабли по (SideKey, TypeKey), пропуская контейнеры/предметы и погибших.
            var groups = new Dictionary<string, int>();
            foreach (var s in star.Ships)
            {
                if (s == null || s.IsItem) continue;
                if (s.CurrentHull <= 0) continue;
                string side = string.IsNullOrEmpty(s.Owner) ? "?" : s.Owner;
                string type = string.IsNullOrEmpty(s.ShipTypeId) ? "?" : s.ShipTypeId;
                string key = side + " · " + type;
                groups.TryGetValue(key, out var c);
                groups[key] = c + 1;
            }
            if (groups.Count == 0) return 0;

            sb.AppendLine("<color=#8FD1FF>◈ обнаружено (Дальняя антенна):</color>");
            int lines = 1;
            foreach (var kv in groups)
            {
                sb.AppendLine($"<color=#B9C6D6>  {kv.Key}: {kv.Value}</color>");
                lines++;
                if (lines >= 8) { sb.AppendLine("<color=#B9C6D6>  …</color>"); lines++; break; }
            }
            return lines;
        }

        private static string GetPlanetNameColorHex(PlanetData planet, GalaxyGenerationContext ctx)
        {
            if (planet == null || string.IsNullOrEmpty(planet.Race) || planet.Race == GalaxyConstants.RACE_NONE_KEY)
                return "7B7B7B";
            if (ctx?.Config?.Races != null
                && ctx.Config.Races.TryGetValue(planet.Race, out var rc)
                && !string.IsNullOrEmpty(rc.Color))
                return ColorUtility.ToHtmlStringRGB(ParseColor(rc.Color));
            return "AAAAAA";
        }

        private void BuildJumpButton(Transform parent)
        {
            float btnH = titleHeight - 10f;
            float btnW = 240f;
            float closeBtnSize = titleHeight - 6f;
            var go = new GameObject("BtnHyperjump", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            // Слева от крестика закрытия (3px + closeBtnSize + 6px зазор)
            rt.anchoredPosition = new Vector2(-(3f + closeBtnSize + 6f), 0f);
            rt.sizeDelta = new Vector2(btnW, btnH);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.10f, 0.18f, 0.30f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(OnJumpClicked);
            var labelGo = new GameObject("L", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            _jumpBtnLabel = labelGo.AddComponent<Text>();
            _jumpBtnLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _jumpBtnLabel.fontSize = 12;
            _jumpBtnLabel.alignment = TextAnchor.MiddleCenter;
            _jumpBtnLabel.color = new Color(0.85f, 0.90f, 1f, 1f);
            _jumpBtnLabel.text = "Гиперпрыжок";
            _jumpBtnImg = img;
            _jumpBtnGO = go;
        }

        private void OnJumpClicked()
        {
            var gm = GalaxyManager.Instance;
            var player = PlayerManager.Instance?.GetOrFindPlayerShip();
            if (gm == null || player == null) return;

            // Если прыжок активен — кнопка работает на отмену.
            if (CanCancelHyperjump(player))
            {
                if (gm.CancelHyperjump()) CloseMap();
                return;
            }
            if (_selectedTarget == null) return;
            if (gm.JumpToStar(_selectedTarget))
                CloseMap();
        }

        private void RefreshJumpButton()
        {
            if (_jumpBtnLabel == null || _jumpBtnImg == null) return;
            var gm = GalaxyManager.Instance;
            var player = PlayerManager.Instance?.GetOrFindPlayerShip();
            var galaxy = gm?.GeneratedGalaxy;
            if (player == null || galaxy == null) { _jumpBtnImg.color = ColorDisabled(); return; }

            if (CanCancelHyperjump(player))
            {
                _jumpBtnLabel.text = $"Отменить ({player.HyperjumpPhase})";
                _jumpBtnImg.color = new Color(0.40f, 0.18f, 0.18f, 1f);
                return;
            }

            // Просмотр другой галактики: прыжок недоступен — нужны червоточины/консольный teleport.
            if (!string.IsNullOrEmpty(_viewingGalaxyKey) && _viewingGalaxyKey != gm.ActiveGalaxyKey)
            {
                _jumpBtnLabel.text = "Прыжок в другую галактику недоступен";
                _jumpBtnImg.color = ColorDisabled();
                return;
            }

            if (_selectedTarget == null)
            {
                _jumpBtnLabel.text = "Гиперпрыжок (выберите цель)";
                _jumpBtnImg.color = ColorDisabled();
                return;
            }
            if (HyperjumpController.CanRequestJump(player, _selectedTarget, galaxy, out string reason))
            {
                int cost = HyperjumpController.CalcFuelCost(gm.CurrentStar, _selectedTarget);
                _jumpBtnLabel.text = $"Гиперпрыжок → {_selectedTarget.Name} (-{cost} топлива)";
                _jumpBtnImg.color = new Color(0.14f, 0.28f, 0.46f, 1f);
            }
            else
            {
                _jumpBtnLabel.text = $"Прыжок невозможен: {reason}";
                _jumpBtnImg.color = ColorDisabled();
            }
        }

        private static Color ColorDisabled() => new Color(0.18f, 0.20f, 0.26f, 1f);

        private static bool CanCancelHyperjump(ShipData player)
            => player != null
            && (player.HyperjumpPhase == HyperjumpPhase.Travel
                || player.HyperjumpPhase == HyperjumpPhase.HyperEnter);

        private void SelectTarget(StarData star)
        {
            _selectedTarget = star;
            foreach (var (rt, s, img) in _starButtons)
            {
                if (img == null) continue;
                bool sel = s == _selectedTarget;
                // У звёзд со спрайтом-иконкой исходный tint=white (спрайт уже окрашен).
                // У fallback-кружков tint берётся из StarColor(s.Color).
                Color baseCol = img.sprite != null ? Color.white : StarColor(s.Color);
                img.color = sel ? new Color(1f, 1f, 0.3f, 1f) : baseCol;
            }
            RefreshJumpButton();
        }

        private void BuildModeButtons(Transform parent)
        {
            float btnH = titleHeight - 10f;
            _btnModeOwner = MakeModeButton(parent, "BtnOwner", "Владелец", 122f, 68f, btnH,
                () => SetColorMode(ColorMode.Owner));
            _btnModeRace = MakeModeButton(parent, "BtnRace", "Раса", 193f, 48f, btnH,
                () => SetColorMode(ColorMode.Race));
            _btnExpansion = MakeModeButton(parent, "BtnExpansion", "Экспансия", 244f, 72f, btnH,
                () => ToggleExpansionLines());
            UpdateModeBtnColors();
        }

        private void BuildSwapGalaxyButton(Transform parent)
        {
            var gm = GalaxyManager.Instance;
            if (gm?.Galaxies == null || gm.Galaxies.Count <= 1) return;
            float btnH = titleHeight - 10f;
            var go = new GameObject("BtnSwapGalaxy", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(320f, 0f);
            rt.sizeDelta = new Vector2(160f, btnH);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.10f, 0.20f, 0.35f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(CycleViewingGalaxy);
            var lblGo = new GameObject("L", typeof(RectTransform));
            lblGo.transform.SetParent(go.transform, false);
            var lrt = lblGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(4f, 0f); lrt.offsetMax = new Vector2(-4f, 0f);
            _btnSwapGalaxyLabel = lblGo.AddComponent<Text>();
            _btnSwapGalaxyLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _btnSwapGalaxyLabel.fontSize = 11;
            _btnSwapGalaxyLabel.color = new Color(0.85f, 0.90f, 1f, 1f);
            _btnSwapGalaxyLabel.text = "▷ Галактика";
            _btnSwapGalaxyLabel.alignment = TextAnchor.MiddleCenter;
            _btnSwapGalaxyLabel.raycastTarget = false;
            _btnSwapGalaxyGO = go;
            UpdateSwapGalaxyLabel();
        }

        private void CycleViewingGalaxy()
        {
            var gm = GalaxyManager.Instance;
            if (gm?.Galaxies == null || gm.Galaxies.Count <= 1) return;
            var keys = new List<string>(gm.Galaxies.Keys);
            int idx = Mathf.Max(0, keys.IndexOf(_viewingGalaxyKey));
            string nextKey = keys[(idx + 1) % keys.Count];
            // Оба ключа должны существовать одновременно (Galaxies + Config.Galaxies), иначе
            // получится рассинхрон _galaxy vs _galCfg → некорректный масштаб карты.
            if (gm.Context?.Config?.Galaxies == null
                || !gm.Context.Config.Galaxies.TryGetValue(nextKey, out var cfg))
            {
                UnityEngine.Debug.LogWarning($"[GalaxyMap] Cycle skipped: config for '{nextKey}' missing.");
                return;
            }
            _viewingGalaxyKey = nextKey;
            _galaxy = gm.Galaxies[nextKey];
            _galCfg = cfg;
            // Пересчёт размеров и перезагрузка контента под новую галактику
            float ppp  = _galCfg.MapPixelsPerParsec > 0 ? _galCfg.MapPixelsPerParsec : 4f;
            var   grid = _galCfg.GalaxyGridSize;
            float szW  = (grid != null && grid.Length >= 2) ? grid[0] : _galaxy.Width;
            float szH  = (grid != null && grid.Length >= 2) ? grid[1] : _galaxy.Height;
            _baseMapSize = new Vector2(szW * ppp, szH * ppp);
            _zoom = Mathf.Clamp(_zoom, ZoomMin(), ZOOM_MAX);
            _selectedTarget = null;
            RefreshContent();
            UpdateSwapGalaxyLabel();
        }

        private void UpdateSwapGalaxyLabel()
        {
            if (_btnSwapGalaxyLabel == null) return;
            var gm = GalaxyManager.Instance;
            var keys = gm != null ? new List<string>(gm.Galaxies.Keys) : new List<string>();
            int idx = Mathf.Max(0, keys.IndexOf(_viewingGalaxyKey));
            int next = keys.Count > 0 ? (idx + 1) % keys.Count : 0;
            string nextName = keys.Count > 0
                && gm.Context?.Config?.Galaxies != null
                && gm.Context.Config.Galaxies.TryGetValue(keys[next], out var nextCfg)
                && !string.IsNullOrEmpty(nextCfg?.Name)
                ? nextCfg.Name : (keys.Count > 0 ? keys[next] : "—");
            _btnSwapGalaxyLabel.text = $"▷ {nextName}";
        }

        private Image MakeModeButton(Transform parent, string name, string label,
            float x, float w, float h, System.Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot            = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta        = new Vector2(w, h);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.08f, 0.10f, 0.18f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            Label(go.transform, "L", label, 10,
                new Vector2(2f, 0f), new Vector2(-2f, 0f),
                new Color(0.75f, 0.80f, 0.95f, 1f), TextAnchor.MiddleCenter);
            return img;
        }

        private void SetColorMode(ColorMode mode)
        {
            if (_colorMode == mode) return;
            _colorMode = mode;
            UpdateModeBtnColors();
            if (_isOpen) { RebuildVoronoi(); SpawnSectorLabels(); }
        }

        private void ToggleExpansionLines()
        {
            _showExpansionLines = !_showExpansionLines;
            UpdateModeBtnColors();
            if (_isOpen) RebuildVoronoi();
        }

        private void UpdateModeBtnColors()
        {
            var active   = new Color(0.20f, 0.32f, 0.58f, 1f);
            var inactive = new Color(0.08f, 0.10f, 0.18f, 1f);
            if (_btnModeOwner != null)
                _btnModeOwner.color = _colorMode == ColorMode.Owner ? active : inactive;
            if (_btnModeRace != null)
                _btnModeRace.color = _colorMode == ColorMode.Race ? active : inactive;
            if (_btnExpansion != null)
                _btnExpansion.color = _showExpansionLines ? active : inactive;
        }

        private static void AddEventTrigger(
            UnityEngine.EventSystems.EventTrigger et,
            UnityEngine.EventSystems.EventTriggerType type,
            UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData> action)
        {
            var entry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(action);
            et.triggers.Add(entry);
        }

        private void BuildScrollArea()
        {
            var scrollGO = new GameObject("ScrollView", typeof(RectTransform));
            scrollGO.transform.SetParent(_rootGo.transform, false);
            var srt = scrollGO.GetComponent<RectTransform>();
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(4f, 4f);
            srt.offsetMax = new Vector2(-4f, -(4f + titleHeight + 4f));
            scrollGO.AddComponent<Image>().color = new Color(0.03f, 0.03f, 0.06f, 1f);

            var vpGO = new GameObject("Viewport", typeof(RectTransform));
            vpGO.transform.SetParent(scrollGO.transform, false);
            _vpRT = vpGO.GetComponent<RectTransform>();
            _vpRT.anchorMin = Vector2.zero; _vpRT.anchorMax = Vector2.one;
            _vpRT.offsetMin = Vector2.zero; _vpRT.offsetMax = Vector2.zero;
            vpGO.AddComponent<RectMask2D>();

            var contentGO = new GameObject("MapContent", typeof(RectTransform));
            contentGO.transform.SetParent(vpGO.transform, false);
            _mapContent = contentGO.GetComponent<RectTransform>();
            _mapContent.anchorMin = _mapContent.anchorMax = Vector2.zero;
            _mapContent.pivot = Vector2.zero;
            _mapContent.anchoredPosition = Vector2.zero;
            _mapContent.sizeDelta = new Vector2(100f, 100f);
            var cbg = contentGO.AddComponent<Image>();
            if (mapBgSprite != null) { cbg.sprite = mapBgSprite; cbg.type = Image.Type.Tiled; }
            else cbg.color = new Color(0.02f, 0.02f, 0.05f, 1f);

            // Вороной (нижний слой)
            // pivot=(0,0): локальный (0,0) совпадает с нижним левым углом rect,
            // что соответствует системе координат полигонов Вороного (0..mapSize)
            var vorGO = new GameObject("Voronoi", typeof(RectTransform));
            vorGO.transform.SetParent(contentGO.transform, false);
            var vorRT = vorGO.GetComponent<RectTransform>();
            vorRT.anchorMin = Vector2.zero;
            vorRT.anchorMax = Vector2.one;
            vorRT.offsetMin = Vector2.zero;
            vorRT.offsetMax = Vector2.zero;
            vorRT.pivot     = Vector2.zero;
            _voronoi = vorGO.AddComponent<GalaxyMapGraphics>();
            _voronoi.raycastTarget = false;

            // Подписи секторов (над Вороным, под звёздами)
            var slGO = new GameObject("SectorLabels", typeof(RectTransform));
            slGO.transform.SetParent(contentGO.transform, false);
            _sectorLabelContainer = slGO.GetComponent<RectTransform>();
            Stretch(_sectorLabelContainer);

            // Debug: координатные подписи вершин Вороного
            var dlGO = new GameObject("DebugLabels", typeof(RectTransform));
            dlGO.transform.SetParent(contentGO.transform, false);
            _debugLabelContainer = dlGO.GetComponent<RectTransform>();
            Stretch(_debugLabelContainer);

            // Подписи линий экспансии (расстояние + год)
            var elGO = new GameObject("ExpansionLabels", typeof(RectTransform));
            elGO.transform.SetParent(contentGO.transform, false);
            _expansionLabelContainer = elGO.GetComponent<RectTransform>();
            Stretch(_expansionLabelContainer);

            // Иконки звёзд (верхний слой)
            var starsGO = new GameObject("Stars", typeof(RectTransform));
            starsGO.transform.SetParent(contentGO.transform, false);
            _starContainer = starsGO.GetComponent<RectTransform>();
            Stretch(_starContainer);

            _hSbGO = MakeScrollbar(scrollGO.transform, true);
            _vSbGO = MakeScrollbar(scrollGO.transform, false);

            _scrollRect = scrollGO.AddComponent<ScrollRect>();
            _scrollRect.content           = _mapContent;
            _scrollRect.viewport          = _vpRT;
            _scrollRect.horizontal        = true;
            _scrollRect.vertical          = true;
            _scrollRect.scrollSensitivity = 0f;
            _scrollRect.movementType      = ScrollRect.MovementType.Clamped;
            _scrollRect.horizontalScrollbar = _hSbGO.GetComponent<Scrollbar>();
            _scrollRect.verticalScrollbar   = _vSbGO.GetComponent<Scrollbar>();
            _scrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            _scrollRect.verticalScrollbarVisibility   = ScrollRect.ScrollbarVisibility.Permanent;
        }

        private GameObject MakeScrollbar(Transform parent, bool horizontal)
        {
            var go = new GameObject(horizontal ? "HScrollbar" : "VScrollbar", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            if (horizontal)
            {
                rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.sizeDelta = new Vector2(-SB, SB);
                rt.anchoredPosition = Vector2.zero;
            }
            else
            {
                rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(SB, -SB);
                rt.anchoredPosition = Vector2.zero;
            }
            go.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f, 1f);

            var sa = new GameObject("SA", typeof(RectTransform));
            sa.transform.SetParent(go.transform, false);
            Stretch(sa.GetComponent<RectTransform>());

            var h = new GameObject("H", typeof(RectTransform));
            h.transform.SetParent(sa.transform, false);
            var hrt = h.GetComponent<RectTransform>();
            hrt.anchorMin = hrt.anchorMax = Vector2.zero;
            hrt.sizeDelta = new Vector2(SB, SB);
            var hi = h.AddComponent<Image>();
            hi.color = new Color(0.35f, 0.45f, 0.65f, 1f);

            var sb = go.AddComponent<Scrollbar>();
            sb.handleRect    = hrt;
            sb.targetGraphic = hi;
            sb.direction     = horizontal ? Scrollbar.Direction.LeftToRight
                                          : Scrollbar.Direction.BottomToTop;
            var sc = sb.colors;
            sc.highlightedColor = new Color(0.5f, 0.62f, 0.88f, 1f);
            sc.pressedColor     = new Color(0.22f, 0.3f, 0.5f, 1f);
            sb.colors = sc;
            return go;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Контент

        private void RefreshContent()
        {
            _mapContent.sizeDelta = MapSize;
            RefreshGalaxyTitle();
            RebuildVoronoi();
            SpawnSectorLabels();
            SpawnStarIcons();
            SpawnDebugLabels();
            LogCoordinates();
            AdjustScrollbars();
            RefreshJumpButton();
        }

        private void RefreshGalaxyTitle()
        {
            if (_galaxyNameText == null) return;

            string name = !string.IsNullOrEmpty(_galCfg?.Name) ? _galCfg.Name : "—";
            _galaxyNameText.text = name;

            if (_galaxyTooltipText == null) return;

            int sectorCount = _galaxy?.Sectors?.Count ?? 0;
            int starCount   = 0;
            if (_galaxy?.Sectors != null)
                foreach (var s in _galaxy.Sectors) starCount += s.Stars?.Count ?? 0;

            var grid = _galCfg?.GalaxyGridSize;
            string sizeStr = (grid != null && grid.Length >= 2)
                ? $"{grid[0]} × {grid[1]}"
                : "—";

            _galaxyTooltipText.text =
                $"Секторов: {sectorCount}    Звёзд: {starCount}\n" +
                $"Размер: {sizeStr}";
        }

        // ── Диаграмма Вороного ────────────────────────────────────────────────────

        /// <summary>
        /// Читает VoronoiPolygon/VoronoiEdgeNeighbors из SectorData (вычислено генератором),
        /// переводит координаты в пространство карты и заполняет _voronoi.Cells.
        /// Вся геометрия Вороного уже посчитана — здесь только рендеринг.
        /// </summary>
        private void RebuildVoronoi()
        {
            if (_voronoi == null || _galaxy?.Sectors == null) return;

            _voronoi.Cells.Clear();

            var ctx     = GalaxyManager.Instance?.Context;
            var sectors = _galaxy.Sectors;
            if (sectors.Count == 0) { _voronoi.Refresh(); return; }

            Color borderCol = ParseColor(_galCfg?.VoronoiBorderColor ?? "90,100,130");
            _voronoi.BorderColor = new Color(borderCol.r, borderCol.g, borderCol.b, 1f);
            _voronoi.BorderWidth = 1f;

            // Быстрый lookup uid → sector для определения наличия соседа
            var uidToSector = new System.Collections.Generic.Dictionary<string, SectorData>(sectors.Count);
            foreach (var s in sectors) uidToSector[s.Uid] = s;

            foreach (var sector in sectors)
            {
                var gridPoly = sector.VoronoiPolygon;
                if (gridPoly == null || gridPoly.Count < 3) continue;

                // Перевод из координат сетки в пиксели карты
                var mapPoly = new List<Vector2>(gridPoly.Count);
                foreach (var gp in gridPoly) mapPoly.Add(GridToMap(gp));

                Color fill = SectorFillColor(sector, ctx);

                // Определяем видимость рёбер: рисовать только на стыке разных секторов
                int       edgeCount  = mapPoly.Count;
                var       showBorder = new List<bool>(edgeCount);
                var       edgeNeigh  = sector.VoronoiEdgeNeighbors;
                for (int e = 0; e < edgeCount; e++)
                {
                    // Ребро граничит с другим сектором (или внешняя граница) → рисуем
                    string neighborUid = (edgeNeigh != null && e < edgeNeigh.Count) ? edgeNeigh[e] : null;
                    showBorder.Add(neighborUid == null || neighborUid != sector.Uid);
                }

                _voronoi.Cells.Add(new GalaxyMapGraphics.SectorCell
                {
                    Polygon        = mapPoly,
                    FillColor      = new Color(fill.r, fill.g, fill.b, 0.10f),
                    IsOpen         = true,
                    ShowBorderEdge = showBorder,
                });
            }

            // Линии экспансии рас
            _voronoi.ExpansionLines.Clear();
            if (_showExpansionLines && _galaxy.ExpansionEdges?.Count > 0)
            {
                var ectx = GalaxyManager.Instance?.Context;
                foreach (var edge in _galaxy.ExpansionEdges)
                {
                    Color ec = Color.white;
                    if (ectx?.Config?.Races?.TryGetValue(edge.RaceKey, out var rc) == true && !string.IsNullOrEmpty(rc.Color))
                        ec = ParseColor(rc.Color);
                    ec.a = 0.50f;
                    _voronoi.ExpansionLines.Add(new GalaxyMapGraphics.ExpansionLine
                    {
                        From  = GridToMap(edge.From),
                        To    = GridToMap(edge.To),
                        Color = ec,
                    });
                }
            }

            SpawnExpansionLabels();
            UpdateGrid();
            _voronoi.Refresh();
        }

        // ── Сетка парсеков ────────────────────────────────────────────────────────
        private void UpdateGrid()
        {
            if (_voronoi == null || _galaxy == null) return;

            _voronoi.GridLinesX.Clear();
            _voronoi.GridLinesY.Clear();

            var   ms     = MapSize;
            const float stepPc  = 5f;
            float stepPxX = stepPc * ms.x / Mathf.Max(1f, _galaxy.Width);
            float stepPxY = stepPc * ms.y / Mathf.Max(1f, _galaxy.Height);

            for (float x = 0f; x <= ms.x + 0.5f; x += stepPxX) _voronoi.GridLinesX.Add(x);
            for (float y = 0f; y <= ms.y + 0.5f; y += stepPxY) _voronoi.GridLinesY.Add(y);
        }

        // ── Debug: координатные подписи вершин Вороного ───────────────────────────

        private void SpawnDebugLabels()
        {
            foreach (var (rt, _) in _debugLabelRTs)
                if (rt != null) Destroy(rt.gameObject);
            _debugLabelRTs.Clear();

            if (_debugLabelContainer == null || _galaxy?.Sectors == null) return;

            var seen = new System.Collections.Generic.HashSet<string>();

            foreach (var sector in _galaxy.Sectors)
            {
                var poly = sector.VoronoiPolygon;
                if (poly == null || poly.Count < 2) continue;

                for (int i = 0; i < poly.Count; i++)
                {
                    Vector2 v = poly[i];
                    // Дедупликация: одна вершина появляется в 2-3 ячейках
                    string key = $"{v.x:F2}_{v.y:F2}";
                    if (!seen.Add(key)) continue;

                    var go = new GameObject($"VV_{key}", typeof(RectTransform));
                    go.transform.SetParent(_debugLabelContainer, false);
                    var rt = go.GetComponent<RectTransform>();
                    rt.anchorMin = rt.anchorMax = Vector2.zero;
                    rt.pivot     = new Vector2(0f, 0.5f);
                    rt.sizeDelta = new Vector2(72f, 9f);
                    rt.anchoredPosition = GridToMap(v);

                    var tx = go.AddComponent<Text>();
                    tx.text          = $"({Mathf.RoundToInt(v.x)},{Mathf.RoundToInt(v.y)})";
                    tx.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    tx.fontSize      = 7;
                    tx.color         = new Color(0.45f, 0.55f, 0.85f, 0.75f);
                    tx.raycastTarget = false;

                    _debugLabelRTs.Add((rt, v));
                }
            }
        }

        // ── Логирование координат ─────────────────────────────────────────────────

        private void LogCoordinates()
        {
            if (_galaxy?.Sectors == null) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Map] ══ КООРДИНАТЫ ЗВЁЗД (пк) ══");
            foreach (var sector in _galaxy.Sectors)
                foreach (var star in sector.Stars)
                    sb.AppendLine($"[Map] ★ {star.Name,-24} ({Mathf.RoundToInt(star.Position.x),5} пк, {Mathf.RoundToInt(star.Position.y),5} пк)  сектор: {sector.Name}");

            sb.AppendLine("[Map] ══ ВЕРШИНЫ ВОРОНОГО (пк) ══");
            foreach (var sector in _galaxy.Sectors)
            {
                var poly = sector.VoronoiPolygon;
                if (poly == null || poly.Count < 2) continue;
                sb.AppendLine($"[Map]   Сектор '{sector.Name}' ({poly.Count} вершин):");
                for (int i = 0; i < poly.Count; i++)
                {
                    Vector2 a = poly[i];
                    Vector2 b = poly[(i + 1) % poly.Count];
                    sb.AppendLine($"[Map]     [{i}] ({Mathf.RoundToInt(a.x)},{Mathf.RoundToInt(a.y)}) пк → ({Mathf.RoundToInt(b.x)},{Mathf.RoundToInt(b.y)}) пк");
                }
            }

            Debug.Log(sb.ToString());
        }

        // ── Подписи секторов ──────────────────────────────────────────────────────
        private void SpawnSectorLabels()
        {
            foreach (var (rt, _) in _sectorLabelRTs)
                if (rt != null) Destroy(rt.gameObject);
            _sectorLabelRTs.Clear();

            if (_sectorLabelContainer == null || _galaxy?.Sectors == null) return;

            var ctx = GalaxyManager.Instance?.Context;

            foreach (var sector in _galaxy.Sectors)
            {
                if (string.IsNullOrEmpty(sector.Name)) continue;

                var go = new GameObject($"SL_{sector.Uid}", typeof(RectTransform));
                go.transform.SetParent(_sectorLabelContainer, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin        = rt.anchorMax = Vector2.zero;
                rt.pivot            = new Vector2(0.5f, 0.5f);
                rt.sizeDelta        = new Vector2(140f, 60f);
                rt.anchoredPosition = GridToMap(sector.Center);

                // Название сектора
                var nameGO = new GameObject("Name", typeof(RectTransform));
                nameGO.transform.SetParent(go.transform, false);
                var nrt = nameGO.GetComponent<RectTransform>();
                nrt.anchorMin        = nrt.anchorMax = new Vector2(0.5f, 1f);
                nrt.pivot            = new Vector2(0.5f, 1f);
                nrt.sizeDelta        = new Vector2(140f, 20f);
                nrt.anchoredPosition = Vector2.zero;
                var nameT = nameGO.AddComponent<Text>();
                nameT.text          = sector.Name;
                nameT.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                nameT.fontSize      = 15;
                nameT.fontStyle     = FontStyle.Bold;
                nameT.alignment     = TextAnchor.MiddleCenter;
                nameT.color         = new Color(1f, 1f, 1f, 0.38f);
                nameT.raycastTarget = false;

                // Раса сектора
                string raceName = GetSectorRaceName(sector, ctx);
                if (!string.IsNullOrEmpty(raceName))
                {
                    Color _rc = SectorFillColor(sector, ctx);
                    Color raceCol = sector.Race == GalaxyConstants.RACE_MIXED_KEY
                        ? new Color(0.85f, 0.85f, 0.85f, 0.55f)
                        : new Color(_rc.r, _rc.g, _rc.b, 0.55f);

                    var raceGO = new GameObject("Race", typeof(RectTransform));
                    raceGO.transform.SetParent(go.transform, false);
                    var rrt = raceGO.GetComponent<RectTransform>();
                    rrt.anchorMin        = rrt.anchorMax = new Vector2(0.5f, 1f);
                    rrt.pivot            = new Vector2(0.5f, 1f);
                    rrt.sizeDelta        = new Vector2(140f, 16f);
                    rrt.anchoredPosition = new Vector2(0f, -20f);
                    var raceT = raceGO.AddComponent<Text>();
                    raceT.text          = raceName;
                    raceT.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    raceT.fontSize      = 11;
                    raceT.fontStyle     = FontStyle.Normal;
                    raceT.alignment     = TextAnchor.MiddleCenter;
                    raceT.color         = raceCol;
                    raceT.raycastTarget = false;
                }

                // Владелец сектора
                string ownerName = GetSectorOwnerName(sector, ctx);
                if (!string.IsNullOrEmpty(ownerName))
                {
                    Color ownerCol = SectorFillColor(sector, ctx);
                    ownerCol.a = 0.60f;

                    var ownerGO = new GameObject("Owner", typeof(RectTransform));
                    ownerGO.transform.SetParent(go.transform, false);
                    var ort = ownerGO.GetComponent<RectTransform>();
                    ort.anchorMin        = ort.anchorMax = new Vector2(0.5f, 0f);
                    ort.pivot            = new Vector2(0.5f, 0f);
                    ort.sizeDelta        = new Vector2(140f, 18f);
                    ort.anchoredPosition = Vector2.zero;
                    var ownerT = ownerGO.AddComponent<Text>();
                    ownerT.text          = ownerName;
                    ownerT.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    ownerT.fontSize      = 12;
                    ownerT.fontStyle     = FontStyle.Italic;
                    ownerT.alignment     = TextAnchor.MiddleCenter;
                    ownerT.color         = ownerCol;
                    ownerT.raycastTarget = false;
                }

                _sectorLabelRTs.Add((rt, sector.Center));
            }
        }

        private void SpawnExpansionLabels()
        {
            foreach (var (rt, _) in _expansionLabelRTs)
                if (rt != null) Destroy(rt.gameObject);
            _expansionLabelRTs.Clear();

            if (_expansionLabelContainer == null || !_showExpansionLines) return;
            if (_galaxy?.ExpansionEdges == null || _galaxy.ExpansionEdges.Count == 0) return;

            var ectx = GalaxyManager.Instance?.Context;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            foreach (var edge in _galaxy.ExpansionEdges)
            {
                float dist = (edge.To - edge.From).magnitude;
                Vector2 gridMid = (edge.From + edge.To) * 0.5f;

                Color ec = Color.white;
                if (ectx?.Config?.Races?.TryGetValue(edge.RaceKey, out var rc) == true && !string.IsNullOrEmpty(rc.Color))
                    ec = ParseColor(rc.Color);

                var go = new GameObject($"EL_{edge.RaceKey}", typeof(RectTransform));
                go.transform.SetParent(_expansionLabelContainer, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin        = rt.anchorMax = Vector2.zero;
                rt.pivot            = new Vector2(0.5f, 0f);
                rt.sizeDelta        = new Vector2(64f, 24f);
                rt.anchoredPosition = GridToMap(gridMid) + new Vector2(4f, 3f);

                var distGO = new GameObject("Dist", typeof(RectTransform));
                distGO.transform.SetParent(go.transform, false);
                var drt = distGO.GetComponent<RectTransform>();
                drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 1f);
                drt.pivot     = new Vector2(0.5f, 1f);
                drt.sizeDelta = new Vector2(64f, 12f);
                drt.anchoredPosition = Vector2.zero;
                var dt = distGO.AddComponent<Text>();
                dt.text          = $"{Mathf.RoundToInt(dist)} пк";
                dt.font          = font;
                dt.fontSize      = 10;
                dt.alignment     = TextAnchor.MiddleCenter;
                dt.color         = new Color(ec.r, ec.g, ec.b, 0.65f);
                dt.raycastTarget = false;

                if (edge.ColonizationYear > 0)
                {
                    var yearGO = new GameObject("Year", typeof(RectTransform));
                    yearGO.transform.SetParent(go.transform, false);
                    var yrt = yearGO.GetComponent<RectTransform>();
                    yrt.anchorMin        = yrt.anchorMax = new Vector2(0.5f, 1f);
                    yrt.pivot            = new Vector2(0.5f, 1f);
                    yrt.sizeDelta        = new Vector2(64f, 12f);
                    yrt.anchoredPosition = new Vector2(0f, -12f);
                    var yt = yearGO.AddComponent<Text>();
                    yt.text          = $"{edge.ColonizationYear} г.";
                    yt.font          = font;
                    yt.fontSize      = 9;
                    yt.alignment     = TextAnchor.MiddleCenter;
                    yt.color         = new Color(ec.r, ec.g, ec.b, 0.50f);
                    yt.raycastTarget = false;
                }

                _expansionLabelRTs.Add((rt, gridMid));
            }
        }

        private static string GetSectorOwnerName(SectorData sector, GalaxyGenerationContext ctx)
        {
            if (sector == null || ctx == null) return null;

            if (!string.IsNullOrEmpty(sector.ResolvedOwnerId))
                return sector.ResolvedOwnerId;

            string owner = sector.Owner;
            if (!string.IsNullOrEmpty(owner) && owner != GalaxyConstants.OWNER_NONE_KEY)
                return owner;

            return null;
        }

        private static string GetSectorRaceName(SectorData sector, GalaxyGenerationContext ctx)
        {
            if (sector == null) return null;
            string race = sector.Race;
            if (string.IsNullOrEmpty(race) || race == GalaxyConstants.RACE_NONE_KEY) return null;
            if (race == GalaxyConstants.RACE_MIXED_KEY) return "Mixed";
            if (ctx?.TextConfig?.RaceNames?.TryGetValue(race, out string displayName) == true
                && !string.IsNullOrEmpty(displayName))
                return displayName;
            return race;
        }

        /// <summary>Цвет заливки ячейки Вороного для конкретной звезды.</summary>
        private Color StarFillColor(StarData star, GalaxyGenerationContext ctx)
        {
            if (star == null) return new Color(0.2f, 0.22f, 0.28f);

            // 1. Owner с заданным Color (Пираты, Синтеты)
            if (!string.IsNullOrEmpty(star.ResolvedOwnerId)
                && ctx?.Config?.Ships?.Owners?.TryGetValue(star.ResolvedOwnerId, out var owner) == true
                && !string.IsNullOrEmpty(owner.Color))
                return ParseColor(owner.Color);

            // 2. Owner без цвета (Coalition) → цвет расы звезды
            if (!string.IsNullOrEmpty(star.Race)
                && star.Race != GalaxyConstants.RACE_NONE_KEY
                && star.Race != GalaxyConstants.RACE_MIXED_KEY
                && ctx?.Config?.Races?.TryGetValue(star.Race, out var race) == true
                && !string.IsNullOrEmpty(race.Color))
                return ParseColor(race.Color);

            // 3. Mixed — доминирующая раса планет
            if (star.Race == GalaxyConstants.RACE_MIXED_KEY && star.Planets?.Count > 0)
            {
                var raceCounts = new System.Collections.Generic.Dictionary<string, int>();
                foreach (var p in star.Planets)
                {
                    if (string.IsNullOrEmpty(p.Race) || p.Race == GalaxyConstants.RACE_NONE_KEY) continue;
                    raceCounts.TryGetValue(p.Race, out int cnt);
                    raceCounts[p.Race] = cnt + 1;
                }
                string domRace = null; int maxCnt = 0;
                foreach (var kv in raceCounts) if (kv.Value > maxCnt) { maxCnt = kv.Value; domRace = kv.Key; }
                if (!string.IsNullOrEmpty(domRace)
                    && ctx?.Config?.Races?.TryGetValue(domRace, out var dr) == true
                    && !string.IsNullOrEmpty(dr.Color))
                    return ParseColor(dr.Color);
            }

            // 4. Fallback на сектор
            return SectorFillColor(star.ParentSector, ctx);
        }

        private Color SectorFillColor(SectorData sector, GalaxyGenerationContext ctx)
        {
            if (sector == null) return new Color(0.2f, 0.22f, 0.28f);

            if (_colorMode == ColorMode.Owner)
            {
                string ownerId = sector.ResolvedOwnerId ?? sector.Owner;
                if (!string.IsNullOrEmpty(ownerId) && ownerId != GalaxyConstants.OWNER_NONE_KEY
                    && ctx?.Config?.Ships?.Owners?.TryGetValue(ownerId, out var ownerCfg) == true
                    && !string.IsNullOrEmpty(ownerCfg.Color))
                    return ParseColor(ownerCfg.Color);
                return new Color(0.2f, 0.22f, 0.28f);
            }

            string raceId = sector.Race;
            if (!string.IsNullOrEmpty(raceId) && raceId != GalaxyConstants.RACE_NONE_KEY
                && ctx?.Config?.Races?.TryGetValue(raceId, out var raceC) == true
                && !string.IsNullOrEmpty(raceC.Color))
                return ParseColor(raceC.Color);

            return new Color(0.2f, 0.22f, 0.28f);
        }

        private void SpawnStarIcons()
        {
            foreach (var (rt, _) in _starRTs)
                if (rt != null) Destroy(rt.gameObject);
            _starRTs.Clear();
            _starButtons.Clear();

            if (_galaxy?.Sectors == null) return;

            var ctx = GalaxyManager.Instance?.Context;

            foreach (var sector in _galaxy.Sectors)
            foreach (var star in sector.Stars)
            {
                var go = new GameObject($"S_{star.Uid}", typeof(RectTransform));
                go.transform.SetParent(_starContainer, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.pivot            = new Vector2(0.5f, 0.5f);
                rt.sizeDelta        = new Vector2(16f, 16f);
                rt.anchoredPosition = GridToMap(star.Position);

                // Иконка миникарты
                var img = go.AddComponent<Image>();
                var spr = GraphicsManager.Instance?.TryGetSprite(star.MapIcon);
                if (spr != null) { img.sprite = spr; img.color = Color.white; }
                else img.color = StarColor(star.Color);

                // Клик по звезде — выбор цели прыжка (не телепорт).
                var btn = go.AddComponent<Button>();
                btn.targetGraphic = img;
                var bc = btn.colors;
                bc.highlightedColor = new Color(1f, 1f, 1f, 0.75f);
                bc.pressedColor     = new Color(0.6f, 0.6f, 0.6f, 1f);
                btn.colors = bc;
                var capturedStar = star;
                btn.onClick.AddListener(() => SelectTarget(capturedStar));
                _starButtons.Add((rt, star, img));

                // Тултип при наведении: иконка, имя, список планет в цветах рас
                var et = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                var capturedRT = rt;
                AddEventTrigger(et, UnityEngine.EventSystems.EventTriggerType.PointerEnter,
                    _ => ShowStarTooltip(capturedStar, capturedRT));
                AddEventTrigger(et, UnityEngine.EventSystems.EventTriggerType.PointerExit,
                    _ => HideStarTooltip());

                // Подпись (имя звезды) — используем NameRenderData как в SystemViewManager
                var labelGO = new GameObject("Name", typeof(RectTransform));
                labelGO.transform.SetParent(go.transform, false);
                var lrt = labelGO.GetComponent<RectTransform>();
                lrt.anchorMin        = new Vector2(0.5f, 0f);
                lrt.anchorMax        = new Vector2(0.5f, 0f);
                lrt.pivot            = new Vector2(0.5f, 1f);
                lrt.anchoredPosition = new Vector2(0f, -2f);
                lrt.sizeDelta        = new Vector2(90f, 16f);
                var t = labelGO.AddComponent<Text>();
                var rd = star.NameRenderData;
                if (rd != null && rd.Segments.Count > 0)
                {
                    t.supportRichText = true;
                    t.text  = rd.ToRichText();
                    t.color = Color.white; // цвет задаётся тегами внутри текста
                }
                else
                {
                    t.text  = star.Name;
                    t.color = StarNameColor(star, ctx);
                }
                t.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                t.fontSize      = 12;
                t.alignment     = TextAnchor.UpperCenter;
                t.raycastTarget = false;

                // Координаты в парсеках
                var coordGO = new GameObject("Coords", typeof(RectTransform));
                coordGO.transform.SetParent(go.transform, false);
                var crt = coordGO.GetComponent<RectTransform>();
                crt.anchorMin        = new Vector2(0.5f, 0f);
                crt.anchorMax        = new Vector2(0.5f, 0f);
                crt.pivot            = new Vector2(0.5f, 1f);
                crt.anchoredPosition = new Vector2(0f, -15f);
                crt.sizeDelta        = new Vector2(80f, 10f);
                var ct = coordGO.AddComponent<Text>();
                ct.text          = $"({Mathf.RoundToInt(star.Position.x)}, {Mathf.RoundToInt(star.Position.y)}) пк";
                ct.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                ct.fontSize      = 10;
                ct.alignment     = TextAnchor.UpperCenter;
                ct.color         = new Color(0.55f, 0.75f, 0.55f, 0.85f);
                ct.raycastTarget = false;

                SpawnWormholeMarker(go.transform, star);

                _starRTs.Add((rt, star.Position));
            }
        }

        /// <summary>Иконка активной червоточины у звезды на галакарте. Рисуется справа-сверху
        /// от иконки звезды, если у неё есть хотя бы одна червоточина в фазе Open.</summary>
        private void SpawnWormholeMarker(Transform parent, StarData star)
        {
            if (star?.Wormholes == null || star.Wormholes.Count == 0) return;
            WormholeData iconWh = null;
            for (int i = 0; i < star.Wormholes.Count; i++)
                if (star.Wormholes[i] != null && star.Wormholes[i].Phase == WormholePhase.Open)
                { iconWh = star.Wormholes[i]; break; }
            if (iconWh == null) return;

            string iconPath = !string.IsNullOrEmpty(iconWh.Graphics?.IconPath)
                ? iconWh.Graphics.IconPath
                : GalaxyConstants.WORMHOLE_ICON_PATH;
            var sprite = GraphicsManager.Instance?.TryGetSprite(iconPath);
            if (sprite == null) return;

            var whGO = new GameObject("Wormhole", typeof(RectTransform));
            whGO.transform.SetParent(parent, false);
            var wrt = whGO.GetComponent<RectTransform>();
            wrt.anchorMin = wrt.anchorMax = new Vector2(1f, 1f);
            wrt.pivot = new Vector2(0f, 1f);
            wrt.anchoredPosition = new Vector2(2f, 2f);
            wrt.sizeDelta = new Vector2(14f, 14f);
            var img = whGO.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
        }

        private static Color StarNameColor(StarData star, GalaxyGenerationContext ctx)
        {
            if (ctx == null) return new Color(0.82f, 0.82f, 0.82f);

            // Owner с цветом (Пираты, Синтеты)
            if (!string.IsNullOrEmpty(star.Owner)
                && star.Owner != GalaxyConstants.OWNER_NONE_KEY
                && ctx.Config.Ships?.Owners?.TryGetValue(star.Owner, out var ownerCfg) == true
                && !string.IsNullOrEmpty(ownerCfg.Color))
                return ParseColor(ownerCfg.Color);

            // Race
            if (!string.IsNullOrEmpty(star.Race)
                && star.Race != GalaxyConstants.RACE_NONE_KEY
                && star.Race != GalaxyConstants.RACE_MIXED_KEY
                && ctx.Config.Races?.TryGetValue(star.Race, out var race) == true
                && !string.IsNullOrEmpty(race.Color))
                return ParseColor(race.Color);

            return new Color(0.78f, 0.78f, 0.82f);
        }

        private static Color ParseColor(string csv)
        {
            var p = csv.Split(',');
            if (p.Length >= 3
                && float.TryParse(p[0].Trim(), out float r)
                && float.TryParse(p[1].Trim(), out float g)
                && float.TryParse(p[2].Trim(), out float b))
                return new Color(r / 255f, g / 255f, b / 255f);
            return ColorUtility.TryParseHtmlString(csv, out var c) ? c : Color.white;
        }

        private void UpdateStarPositions()
        {
            foreach (var (rt, grid) in _starRTs)
                if (rt != null) rt.anchoredPosition = GridToMap(grid);
            foreach (var (rt, grid) in _sectorLabelRTs)
                if (rt != null) rt.anchoredPosition = GridToMap(grid);
            foreach (var (rt, grid) in _debugLabelRTs)
                if (rt != null) rt.anchoredPosition = GridToMap(grid);
            foreach (var (rt, grid) in _expansionLabelRTs)
                if (rt != null) rt.anchoredPosition = GridToMap(grid) + new Vector2(4f, 3f);
            RebuildVoronoi();
        }

        private void AdjustScrollbars()
        {
            if (_vpRT == null || _hSbGO == null || _vSbGO == null) return;

            float svW = panelSize.x - 8f;
            float svH = panelSize.y - titleHeight - 12f;
            var   ms  = MapSize;

            bool needH = ms.x > svW;
            bool needV = ms.y > svH;
            if (needH) needV |= (ms.y > svH - SB);
            if (needV) needH |= (ms.x > svW - SB);

            _hSbGO.SetActive(needH);
            _vSbGO.SetActive(needV);
            _vpRT.offsetMin = new Vector2(0f, needH ? SB : 0f);
            _vpRT.offsetMax = new Vector2(needV ? -SB : 0f, 0f);
            _scrollRect.horizontal = needH;
            _scrollRect.vertical   = needV;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Утилиты

        private Vector2 GridToMap(Vector2 gridPos)
        {
            var ms = MapSize;
            return new Vector2(
                gridPos.x / Mathf.Max(1, _galaxy.Width)  * ms.x,
                gridPos.y / Mathf.Max(1, _galaxy.Height) * ms.y);
        }

        private static Color StarColor(string name)
        {
            switch ((name ?? "").ToLowerInvariant())
            {
                case "red":    return new Color(1f,    0.35f, 0.25f);
                case "yellow": return new Color(1f,    0.92f, 0.45f);
                case "blue":   return new Color(0.45f, 0.70f, 1f);
                case "green":  return new Color(0.40f, 1f,    0.50f);
                case "white":  return new Color(0.90f, 0.92f, 1f);
                case "black":  return new Color(0.40f, 0.35f, 0.55f);
                case "purple": return new Color(0.75f, 0.40f, 1f);
                default:       return Color.white;
            }
        }

        private static void Label(Transform parent, string name, string text, int size,
            Vector2 oMin, Vector2 oMax, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = oMin; rt.offsetMax = oMax;
            var t = go.AddComponent<Text>();
            t.text          = text;
            t.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize      = size;
            t.color         = color;
            t.alignment     = anchor;
            t.raycastTarget = false;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }
    }
}
