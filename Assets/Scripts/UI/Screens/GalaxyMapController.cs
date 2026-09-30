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
using SRG.Simulation;

namespace SRG.UI.Screens
{
    /// <summary>
    /// Галактическая карта. Открывается клавишей M.
    /// Масштаб: MapPixelsPerParsec из GalaxyConfig.json → MilkyWay.MapPixelsPerParsec
    /// Зум: колесо мыши над областью карты (центр зума — курсор).
    /// </summary>
    public partial class GalaxyMapController : MonoBehaviour
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

            // Ход считается в фоне — мир меняется, не читаем и не трогаем его (см. GameWorld.IsCalculating).

            if (GameWorld.IsCalculating) return;
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

    }
}
