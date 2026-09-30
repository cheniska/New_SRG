using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using SRG.Config;
using SRG.Core;
using SRG.Dialog;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Presentation.Common;
using SRG.Controllers;
using SRG.Ships.Services;
using SRG.UI.Common;
using SRG.UI.HUD;
using SRG.Utils;

namespace SRG.UI.Screens
{
    /// <summary>
    /// Полноэкранный интерфейс планеты / станции.
    /// Открывается через GalaxyManager.OnPlanetLanded, закрывается кнопкой «Взлететь».
    /// Все размеры и тексты берутся из GalaxyConfig.PlanetUI.
    /// </summary>
    public class PlanetUIController : MonoBehaviour
    {
        public static PlanetUIController Instance { get; private set; }

        public bool IsOpen => _root != null && _root.activeSelf;

        // ── Экраны ────────────────────────────────────────────────
        private const string SCR_OVERVIEW   = "Overview";
        private const string SCR_HANGAR     = "Hangar";
        private const string SCR_GOODS      = "GoodsShop";
        private const string SCR_EQUIP      = "EquipShop";
        private const string SCR_INFO       = "InfoCenter";

        // ── Состояние ──────────────────────────────────────────────
        // Посадочная цель — планета, станция или корабль-носитель. Разница между ними
        // передаётся через ILandingSite.Kind + type-check (для доступа к специфичным полям
        // PlanetData: поверхность/орбита; или ShipData: корпус для SB-детекта).
        private ILandingSite _site;
        // Обратная совместимость для кода экранов, который лезет напрямую в поля планеты
        // (поверхность/орбита/спутники). null для станций/носителей — соответствующие блоки
        // должны это проверять до обращения.
        private PlanetData _planet;
        private string     _currentScreen;

        // ── UI объекты ─────────────────────────────────────────────
        private Canvas     _canvas;
        private GameObject _root;
        private Text       _titleText;
        private Text       _screenTitleText;

        // Контентные панели (по screen-id)
        private readonly Dictionary<string, GameObject>  _panels     = new();
        private readonly Dictionary<string, Text>        _bodyTexts  = new();
        private readonly Dictionary<string, ScrollRect>  _scrollRects= new();
        private readonly Dictionary<string, Button>      _navBtns    = new();

        // Ссылки для динамических списков
        private Transform _goodsContent;
        private Transform _equipContent;

        // Кнопки-действия ангара (метки обновляются актуальными ценами в RefreshHangar)
        private Text _hangarRefuelLabel;
        private Text _hangarRepairLabel;
        private Text _hangarReloadLabel;
        // Секция «Корабли на объекте» в ангаре — заголовок + скролл со строками кораблей.
        private Text _hangarShipsHeader;
        private Transform _hangarShipsContent;
        // Лента новостей — общий вид с космическим оверлеем инфоцентра.
        private NewsFeedView _newsFeed;

        // Отображение денег
        private Text _moneyText;

        // Модальный диалог количества (покупка/продажа стакабельных товаров)
        private AmountSliderDialog _amountDialog;

        // Nav-бар пересобирается на каждый Open — набор кнопок зависит от типа объекта
        // (планета/станция/корабль-носитель), см. RebuildNavButtons.
        private Transform _navBarTransform;

        // ── Шрифт ─────────────────────────────────────────────────
        private Font _font;
        // Палитра вынесена в UIColorPalette (этап B5 рефакторинга). Локальные геттеры оставлены
        // как тонкие делегаты — все исходные callsite'ы (ColBg/ColPanel/…) продолжают работать.
        private static Color ColBg     => UIColorPalette.Bg;
        private static Color ColPanel  => UIColorPalette.Panel;
        private static Color ColNav    => UIColorPalette.Nav;
        private static Color ColBtn    => UIColorPalette.Button;
        private static Color ColBtnSel => UIColorPalette.ButtonSel;
        private static Color ColBtnAlt => UIColorPalette.ButtonAlt;
        private static Color ColAccent => UIColorPalette.Accent;
        private static Color ColText   => UIColorPalette.Text;
        private static Color ColGreen  => UIColorPalette.Green;
        private static Color ColRed    => UIColorPalette.Red;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private void OnEnable()
        {
            PlayerManager.OnLanded += OnLanded;
            PlayerManager.OnLeft   += OnLeft;
        }

        private void OnDisable()
        {
            PlayerManager.OnLanded -= OnLanded;
            PlayerManager.OnLeft   -= OnLeft;
        }

        private void OnLanded(ILandingSite site) => Open(site);
        private void OnLeft()                    => Close();

        // ─────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────

        public void Open(ILandingSite site)
        {
            _site = site;
            _planet = site as PlanetData;

            if (_root == null)
                BuildUI();

            RebuildNavButtons();
            RefreshAll();
            _root.SetActive(true);

            // Начальный экран: сначала пробуем LandingUIConfig.Profile.InitialScreen (может быть
            // явно задан в LandingUIConfig.json), затем — жёсткий Kind-based дефолт: планета/станция
            // → Overview (панорама/командование), корабль-носитель → Hangar. Такой fallback
            // гарантирует правильный экран даже если LandingUIConfig.json не подключён к GalaxyManager.
            var kind = site?.Kind ?? LandingSiteKind.Planet;
            var profile = GalaxyManager.Instance?.Context?.Config?.LandingUI?.ResolveProfile(kind);
            string initial = profile?.InitialScreen;
            if (string.IsNullOrEmpty(initial))
                initial = kind == LandingSiteKind.Carrier ? SCR_HANGAR : SCR_OVERVIEW;
            ShowScreen(initial);
        }

        /// <summary>Обратная совместимость: старые вызовы Open(PlanetData).</summary>
        public void Open(PlanetData planet) => Open((ILandingSite)planet);

        public void Close()
        {
            _amountDialog?.CloseSilent();
            if (_root != null) _root.SetActive(false);
        }

        // ─────────────────────────────────────────────────────────
        // Построение UI
        // ─────────────────────────────────────────────────────────

        private void BuildUI()
        {
            var cfg = GetUICfg();

            // Canvas
            var canvasGo = new GameObject("PlanetUICanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 50;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            // Root (fullscreen overlay) — полностью непрозрачный, чтобы не было видно
            // системы/туманностей под формой планеты.
            _root = new GameObject("PlanetUI_Root");
            _root.transform.SetParent(canvasGo.transform, false);
            var rootRt = _root.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;
            _root.AddComponent<Image>().color = ColBg;

            // Форма растягивается на весь экран. Полосы (Title/Nav/Bottom) вычисляются
            // в пикселях через RectTransform.rect, поэтому работают на любом разрешении.
            float nb = cfg.NavBarWidth, tb = cfg.TitleBarHeight, bb = cfg.BottomBarHeight;

            var form = MakePanel(_root.transform, "Form", new Color(0.05f, 0.07f, 0.12f, cfg.BackgroundAlpha));
            var formRt = form.GetComponent<RectTransform>();
            formRt.anchorMin = Vector2.zero;
            formRt.anchorMax = Vector2.one;
            formRt.offsetMin = formRt.offsetMax = Vector2.zero;

            // ── Title Bar ──────────────────────────────────────────
            var titleBar = MakePanel(form.transform, "TitleBar", new Color(0.04f, 0.06f, 0.10f, 1f));
            var titleRt = titleBar.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot     = new Vector2(0.5f, 1f);
            titleRt.sizeDelta = new Vector2(0f, tb);
            titleRt.anchoredPosition = Vector2.zero;

            _titleText = MakeText(titleBar.transform, "TitleText", "Планета", 18, FontStyle.Bold, ColAccent);
            SetAnchors(_titleText.gameObject, 0.01f, 0f, 0.70f, 1f);

            _screenTitleText = MakeText(titleBar.transform, "ScreenTitle", "", 14, FontStyle.Normal, ColText);
            SetAnchors(_screenTitleText.gameObject, 0.71f, 0f, 0.93f, 1f);

            BuildTitleCloseButton(titleBar.transform, tb);

            // ── Nav Bar (левая колонка от bottomBar до titleBar) ───
            var navBar = MakePanel(form.transform, "NavBar", ColNav);
            var navRt = navBar.GetComponent<RectTransform>();
            navRt.anchorMin = new Vector2(0f, 0f);
            navRt.anchorMax = new Vector2(0f, 1f);
            navRt.pivot     = new Vector2(0f, 0.5f);
            navRt.sizeDelta = new Vector2(nb, -(tb + bb));
            navRt.anchoredPosition = new Vector2(0f, (bb - tb) * 0.5f);

            _navBarTransform = navBar.transform;

            // ── Panels (контент: справа от nav, между title и bottom) ──
            var contentArea = MakePanel(form.transform, "ContentArea", ColPanel);
            var contentRt = contentArea.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 0f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.offsetMin = new Vector2(nb, bb);
            contentRt.offsetMax = new Vector2(0f, -tb);

            BuildScreenPanel(contentArea.transform, SCR_OVERVIEW);
            BuildHangarPanel(contentArea.transform);
            BuildGoodsPanel(contentArea.transform);
            BuildEquipPanel(contentArea.transform);
            BuildInfoCenterPanel(contentArea.transform);

            // ── Bottom Bar ─────────────────────────────────────────
            var bottomBar = MakePanel(form.transform, "BottomBar", new Color(0.04f, 0.06f, 0.10f, 1f));
            var bottomRt = bottomBar.GetComponent<RectTransform>();
            bottomRt.anchorMin = new Vector2(0f, 0f);
            bottomRt.anchorMax = new Vector2(1f, 0f);
            bottomRt.pivot     = new Vector2(0.5f, 0f);
            bottomRt.sizeDelta = new Vector2(0f, bb);
            bottomRt.anchoredPosition = Vector2.zero;
            BuildBottomButtons(bottomBar.transform, cfg);

            // Диалог количества — последним, поверх остального контента формы.
            _amountDialog = AmountSliderDialog.Create(rootRt);
        }

        // Крестик в правом углу шапки — закрывает форму планеты и выполняет взлёт.
        private void BuildTitleCloseButton(Transform titleBar, float titleHeight)
        {
            float btnSize = Mathf.Min(titleHeight - 6f, 32f);
            var go = new GameObject("CloseBtn");
            go.transform.SetParent(titleBar, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot     = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(btnSize, btnSize);
            rt.anchoredPosition = new Vector2(-4f, 0f);

            var img = go.AddComponent<Image>();
            img.color = new Color(0.55f, 0.10f, 0.10f, 0.92f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var cb = btn.colors;
            cb.highlightedColor = new Color(0.85f, 0.20f, 0.20f, 1f);
            cb.pressedColor     = new Color(0.35f, 0.06f, 0.06f, 1f);
            btn.colors = cb;
            btn.onClick.AddListener(LeavePlanetFromCloseButton);

            var xGo = new GameObject("X");
            xGo.transform.SetParent(go.transform, false);
            var xrt = xGo.AddComponent<RectTransform>();
            xrt.anchorMin = Vector2.zero; xrt.anchorMax = Vector2.one; xrt.sizeDelta = Vector2.zero;
            var xtxt = xGo.AddComponent<Text>();
            xtxt.font = _font; xtxt.text = "×"; xtxt.fontSize = 20;
            xtxt.color = Color.white; xtxt.alignment = TextAnchor.MiddleCenter;
        }

        private void LeavePlanetFromCloseButton()
        {
            if (PlayerManager.Instance == null) return;
            var ship = PlayerShip.Instance?.ShipData;
            ship?.RecordLastVisitAndLaunch();
            PlayerManager.Instance.LeavePlanet();
        }

        /// <summary>Пересобирает набор nav-кнопок под текущий объект (<see cref="_site"/>).
        /// Данные берутся из <c>GalaxyConfig.LandingUI</c> — по <see cref="ILandingSite.Kind"/>
        /// + HullOverride для корпуса ShipData (например, "SB" даёт кнопку «Улучшение»).
        /// Fallback (если LandingUI не загружен): полный набор для планет/станций из старого
        /// <c>PlanetUI.NavButtons</c>, минимальный (только Ангар) для носителей.
        /// Позиции пересчитываются заново, пропусков в колонке нет.</summary>
        private void RebuildNavButtons()
        {
            if (_navBarTransform == null) return;
            ClearChildren(_navBarTransform);
            _navBtns.Clear();

            var cfg = GetUICfg();
            float bh = cfg.ButtonHeight, bs = cfg.ButtonSpacing;

            ResolveNavProfile(cfg, out var buttons, out string dialogLabel);

            for (int i = 0; i < buttons.Count; i++)
            {
                var nb = buttons[i];
                var btn = MakeButton(_navBarTransform, $"Nav_{nb.Id}", nb.Label, 13, ColBtn);
                var rt = btn.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(cfg.NavBarWidth - 12f, bh);
                rt.anchoredPosition = new Vector2(0f, -bs - i * (bh + bs));

                string id = nb.Id;
                btn.onClick.AddListener(() => OnNavButtonClick(id));
                // Screens/модалы разные вещи — регистрируем в _navBtns только реальные экраны,
                // иначе ShowScreen'овая подсветка попытается покрасить кнопку без соответствующей панели.
                if (_panels.ContainsKey(id)) _navBtns[id] = btn;
            }

            // Кнопка связи (диалог с администрацией/капитаном) — отдельная, всегда снизу.
            int gi = buttons.Count;
            var govBtn = MakeButton(_navBarTransform, "DialogBtn", dialogLabel, 13, ColBtnAlt);
            var grt = govBtn.GetComponent<RectTransform>();
            grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 1f);
            grt.pivot = new Vector2(0.5f, 1f);
            grt.sizeDelta = new Vector2(cfg.NavBarWidth - 12f, bh);
            grt.anchoredPosition = new Vector2(0f, -bs - gi * (bh + bs));
            govBtn.onClick.AddListener(OpenPlanetDialog);
        }

        /// <summary>Диспетчер клика по nav-кнопке: обычный Id → ShowScreen; спец-Id (например
        /// "SbImprovement") — открывают модальный диалог, не экран.</summary>
        private void OnNavButtonClick(string id)
        {
            switch (id)
            {
                case "SbImprovement": OpenSbImprovementDialog(); return;
                default: ShowScreen(id); return;
            }
        }

        /// <summary>Возвращает активный список навкнопок и подпись диалога. Единая точка выбора
        /// профиля: LandingUIConfig (Kind + HullOverride) → GalaxyConfig.PlanetUI.NavButtons fallback.</summary>
        private void ResolveNavProfile(PlanetUIConfig cfg, out List<PlanetNavButtonConfig> buttons, out string dialogLabel)
        {
            var lui = GalaxyManager.Instance?.Context?.Config?.LandingUI;
            var kind = _site?.Kind ?? LandingSiteKind.Planet;
            var profile = lui?.ResolveProfile(kind);

            if (profile != null)
            {
                // Копируем список — HullOverride добавит свои кнопки, шаренный лист лить нельзя.
                buttons = profile.NavButtons != null
                    ? new List<PlanetNavButtonConfig>(profile.NavButtons)
                    : new List<PlanetNavButtonConfig>();
                dialogLabel = string.IsNullOrEmpty(profile.DialogButtonLabel)
                    ? cfg.DialogButtonLabel
                    : profile.DialogButtonLabel;
            }
            else
            {
                // Legacy fallback (LandingUIConfig.json не подгружен): планета/станция берут
                // старый PlanetUIConfig.NavButtons; носитель — минимум (только Ангар).
                buttons = kind == LandingSiteKind.Carrier
                    ? new List<PlanetNavButtonConfig> { new PlanetNavButtonConfig { Id = SCR_HANGAR, Label = "Ангар" } }
                    : (cfg.NavButtons?.Count > 0 ? new List<PlanetNavButtonConfig>(cfg.NavButtons) : DefaultNavButtons());
                dialogLabel = kind == LandingSiteKind.Carrier ? "Капитанский мостик" : cfg.DialogButtonLabel;
            }

            // HullOverride: доп. кнопки для корпуса ShipData (например, HullType="SB" → «Улучшение»).
            string hullType = (_site as ShipData)?.GetHullItem()?.GetParamString("HullType");
            var hullOverride = lui?.ResolveHullOverride(hullType);
            if (hullOverride?.ExtraNavButtons != null)
                foreach (var b in hullOverride.ExtraNavButtons) buttons.Add(b);
        }

        private void OpenSbImprovementDialog()
        {
            var ship = PlayerShip.Instance?.ShipData;
            if (ship == null) return;
            ImprovementDialog.Show(ship);
        }

        private void BuildBottomButtons(Transform parent, PlanetUIConfig cfg)
        {
            // Кнопка "Взлететь" перенесена в ангар — здесь её больше нет.
            // Кнопка "Пропустить ход"
            var skipBtn = MakeButton(parent, "SkipBtn", cfg.SkipTurnButtonLabel, 13, ColBtn);
            SetAnchors(skipBtn.gameObject, 0.01f, 0.1f, 0.20f, 0.9f);
            skipBtn.onClick.AddListener(() => GalaxyManager.Instance?.ExecuteInstantTurn());

            // Кнопка "Правительство" перенесена на левый борт (BuildNavButtons) — тут её больше нет.

            // Дисплей кредитов (правый угол bottom bar)
            _moneyText = MakeText(parent, "MoneyLabel", "Кредиты: 0", 14, FontStyle.Bold, ColAccent);
            SetAnchors(_moneyText.gameObject, 0.45f, 0.1f, 0.99f, 0.9f);
            _moneyText.alignment = TextAnchor.MiddleRight;
            UpdateMoneyDisplay();
        }

        // Подбирает dialogId в зависимости от типа посадочной цели:
        //   • Планета — ResolvePlanetDialogId (UID → Government → Economy → Race → default).
        //   • Станция (ShipData.IsStation) — ResolveStationDialogId со scope=Gov
        //     (командование станции, отличается от радио-связи в космосе).
        //   • Линкор-носитель — ResolveShipDialogId со scope=Gov (капитанский мостик).
        // Gov-скоуп общий для «правительства планеты»/«мостика линкора»/«командования станции»,
        // даёт свои условия реплик (например, вымогательство/абордаж скрыты «на мостике»
        // через Condition: is_space == "1"), см. DialogService.ScopeSuffix.
        private void OpenPlanetDialog()
        {
            if (_site == null || DialogUIController.Instance == null) return;

            var dialogs = GalaxyManager.Instance?.Context?.Config?.Dialogs?.Dialogs;
            if (dialogs == null || dialogs.Count == 0)
            {
                GameLog.Add("[Связь] Диалоги не настроены.");
                return;
            }

            if (_site is ShipData carrier)
            {
                string govDlg = carrier.IsStation
                    ? SRG.Dialog.DialogService.ResolveStationDialogId(carrier, SRG.Dialog.DialogScope.Gov)
                    : SRG.Dialog.DialogService.ResolveShipDialogId(carrier,    SRG.Dialog.DialogScope.Gov);
                if (!string.IsNullOrEmpty(govDlg) &&
                    DialogUIController.Instance.OpenBridgeDialog(govDlg, carrier)) return;
                GameLog.Add($"[Связь] {carrier.Name} не отвечает.");
                return;
            }

            if (_planet == null) return;
            string id = SRG.Dialog.DialogService.ResolvePlanetDialogId(_planet);
            if (!string.IsNullOrEmpty(id) &&
                DialogUIController.Instance.OpenPlanetDialog(id, _planet)) return;

            GameLog.Add($"[Связь] На {_planet.Name} нет ответа.");
        }

        private void UpdateMoneyDisplay()
        {
            if (_moneyText == null) return;
            int money = PlayerShip.Instance?.ShipData?.Money ?? 0;
            _moneyText.text = $"Кредиты: {money:N0}";
        }

        // ── Простые текстовые экраны ───────────────────────────────

        private void BuildScreenPanel(Transform parent, string screenId)
        {
            var panel = MakePanel(parent, $"Screen_{screenId}", Color.clear);
            SetAnchors(panel, 0f, 0f, 1f, 1f);
            panel.SetActive(false);
            _panels[screenId] = panel;

            var scroll = BuildScrollRect(panel.transform, out var contentText);
            _scrollRects[screenId] = scroll;
            _bodyTexts[screenId] = contentText;
        }

        // ── Экран магазина товаров ─────────────────────────────────
        //
        // Колонки шапки и строк строятся одной сеткой (одинаковые ширины/spacing/padding
        // через MakeShopRow/MakeRowLabel), поэтому заголовки стоят ровно над значениями.

        // Ширины колонок магазина товаров: иконка/название/покупка/продажа/сток/кнопка.
        private const float GOODS_COL_ICON  = 24f;
        private const float GOODS_COL_NAME  = 118f;
        private const float GOODS_COL_PRICE = 72f;
        private const float GOODS_COL_STOCK = 50f;
        private const float SHOP_COL_BTN    = 72f;
        // Ширины колонок магазина оборудования: название/ТУ/цена.
        private const float EQUIP_COL_NAME  = 260f;
        private const float EQUIP_COL_TU    = 45f;
        private const float EQUIP_COL_PRICE = 80f;

        private void BuildGoodsPanel(Transform parent)
        {
            var panel = MakePanel(parent, $"Screen_{SCR_GOODS}", Color.clear);
            SetAnchors(panel, 0f, 0f, 1f, 1f);
            panel.SetActive(false);
            _panels[SCR_GOODS] = panel;

            // Шапка — та же сетка колонок, что и у строк товаров
            // (горизонтальный padding = padding контента скролла + padding строки).
            var header = MakeShopHeaderRow(panel.transform, 8f, new RectOffset(8, 8, 2, 2));
            MakeRowLabel(header, "", GOODS_COL_ICON);
            MakeRowLabel(header, "Товар", GOODS_COL_NAME, flexible: true, bold: true, color: ColAccent);
            MakeRowLabel(header, "Покупка", GOODS_COL_PRICE, bold: true, color: ColAccent);
            MakeRowLabel(header, "Продажа", GOODS_COL_PRICE, bold: true, color: ColAccent);
            MakeRowLabel(header, "Сток", GOODS_COL_STOCK, bold: true, color: ColAccent);
            MakeRowLabel(header, "", SHOP_COL_BTN);
            MakeRowLabel(header, "", SHOP_COL_BTN);

            _goodsContent = BuildShopScroll(panel.transform, "GoodsScroll", 4f, out var sr);
            _scrollRects[SCR_GOODS] = sr;
        }

        // ── Экран магазина оборудования ────────────────────────────

        private void BuildEquipPanel(Transform parent)
        {
            var panel = MakePanel(parent, $"Screen_{SCR_EQUIP}", Color.clear);
            SetAnchors(panel, 0f, 0f, 1f, 1f);
            panel.SetActive(false);
            _panels[SCR_EQUIP] = panel;

            var header = MakeShopHeaderRow(panel.transform, 4f, new RectOffset(6, 6, 1, 1));
            MakeRowLabel(header, "Предмет", EQUIP_COL_NAME, flexible: true, bold: true, color: ColAccent);
            MakeRowLabel(header, "ТУ", EQUIP_COL_TU, bold: true, color: ColAccent);
            MakeRowLabel(header, "Цена", EQUIP_COL_PRICE, bold: true, color: ColAccent);
            MakeRowLabel(header, "", SHOP_COL_BTN);

            _equipContent = BuildShopScroll(panel.transform, "EquipScroll", 2f, out var sr);
            _scrollRects[SCR_EQUIP] = sr;
        }

        /// <summary>Скролл-область списка магазина (под шапкой): вертикальный layout,
        /// высота строк задаётся их LayoutElement.preferredHeight (childControlHeight=true).</summary>
        private Transform BuildShopScroll(Transform parent, string name, float rowSpacing, out ScrollRect sr)
        {
            var scrollGo = new GameObject(name);
            scrollGo.transform.SetParent(parent, false);
            SetAnchors(scrollGo, 0f, 0f, 1f, 0.93f);
            scrollGo.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(scrollGo.transform, false);
            var contentRt = contentGo.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f); contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0f, 1f); contentRt.sizeDelta = Vector2.zero;
            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = rowSpacing; vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr = scrollGo.AddComponent<ScrollRect>();
            sr.content = contentRt; sr.horizontal = false; sr.vertical = true;
            sr.scrollSensitivity = 30f; sr.movementType = ScrollRect.MovementType.Clamped;
            return contentRt;
        }

        // ─────────────────────────────────────────────────────────
        // Наполнение экранов данными
        // ─────────────────────────────────────────────────────────

        private void RefreshAll()
        {
            if (_site == null) return;
            _titleText.text = _site.Name;

            RefreshOverview();
            RefreshHangar();
            RefreshInfoCenter();
            UpdateMoneyDisplay();
            // магазины обновляются при переключении на них
        }

        private void ShowScreen(string screenId)
        {
            foreach (var kv in _panels)
                kv.Value.SetActive(kv.Key == screenId);

            _currentScreen = screenId;

            // Подсветка кнопки
            foreach (var kv in _navBtns)
            {
                var img = kv.Value.GetComponent<Image>();
                if (img != null) img.color = (kv.Key == screenId) ? ColBtnSel : ColBtn;
            }

            // Заголовок экрана: сначала LandingUIConfig.ScreenTitles профиля (даёт разные подписи
            // для Planet/Station/Carrier без клонирования Screens-словаря), затем общий
            // PlanetUIConfig.Screens[id].Title, затем screenId как последний fallback.
            _screenTitleText.text = ResolveScreenTitle(screenId);

            // Ленивое обновление динамических экранов
            if (screenId == SCR_GOODS) RefreshGoodsShop();
            else if (screenId == SCR_EQUIP) RefreshEquipShop();
            else if (screenId == SCR_HANGAR) RefreshHangar();
            else if (screenId == SCR_INFO) RefreshInfoCenter();
        }

        // ── Overview ───────────────────────────────────────────────

        private void RefreshOverview()
        {
            if (!_bodyTexts.TryGetValue(SCR_OVERVIEW, out var txt)) return;
            var sb = new StringBuilder(512);

            // Станция — не поселение: показываем только имя/командира, без строя/населения/
            // экономики/поверхности. Вкладки магазина/оборудования/ангара работают как у планет.
            if (_site is ShipData station && station.IsStation)
            {
                sb.AppendLine($"<b>{station.Name}</b>");
                sb.AppendLine("Тип: Космическая станция");
                sb.AppendLine($"Раса: {station.Race ?? "—"}   Командир: {station.Owner ?? "—"}");
                sb.AppendLine();
                sb.AppendLine("Доступно: торговля, магазин оборудования, ангар, связь с командиром.");
                txt.text = sb.ToString();
                return;
            }

            if (_planet == null) { txt.text = ""; return; }
            sb.AppendLine($"<b>{_planet.Name}</b>");
            sb.AppendLine($"Тип: {_planet.Type}   Размер: {_planet.Size}");
            if (_planet.IsTerraformed)
                sb.AppendLine("Терраформирована");
            string tempArrow = "";
            if (_planet.IsTerraformed && _planet.TerraformOriginals.TryGetValue("SurfaceTemp", out float origTemp))
                tempArrow = _planet.SurfaceTemp > origTemp + 0.001f ? " ↑" : _planet.SurfaceTemp < origTemp - 0.001f ? " ↓" : "";
            sb.AppendLine($"Температура поверхности: {_planet.SurfaceTemp:F1} К{tempArrow}");
            sb.AppendLine($"Раса: {_planet.Race ?? "—"}   Owner: {_planet.Owner ?? "—"}");
            sb.AppendLine($"Строй: {_planet.Settlement.Government ?? "Неизвестно"}");
            sb.AppendLine();
            if (_planet.Settlement.Population > 0)
                sb.AppendLine($"Население:  {_planet.Settlement.Population:N0}");
            if (!string.IsNullOrEmpty(_planet.Settlement.EconomyType))
                sb.AppendLine($"Экономика:  {_planet.Settlement.EconomyType}");
            if (_planet.Settlement.TechLevel > 0)
            {
                var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
                var ptuCfg = GalaxyManager.Instance?.Context?.Config?.Planets?.PTU;
                float threshold = ptuCfg?.Research?.ProgressPerLevel ?? 100f;
                sb.AppendLine($"ПТУ: {_planet.Settlement.TechLevel}  (прогресс: {_planet.Settlement.PtuProgress:F1}/{threshold:F0})");
                if (galaxy != null)
                    sb.AppendLine($"ГТУ: {galaxy.GtuLevel}");
            }
            if (_planet.Settlement.ActiveEvents != null && _planet.Settlement.ActiveEvents.Count > 0)
            {
                var cfg = GalaxyManager.Instance?.Context?.Config;
                sb.AppendLine();
                sb.AppendLine("── События ──");
                foreach (var evt in _planet.Settlement.ActiveEvents)
                {
                    string evtName = cfg?.Events != null && cfg.Events.TryGetValue(evt.EventId, out var ec)
                        ? ec.DisplayName : evt.EventId;
                    string dur = evt.RemainingMonths >= 0 ? $"ещё {evt.RemainingMonths} мес." : "бессрочно";
                    sb.AppendLine($"  {evtName} ({dur})");
                }
            }
            sb.AppendLine();
            sb.AppendLine("── Поверхность ──");
            sb.AppendLine($"Жидкость:  {_planet.AreaLiquid:F0} ({(int)_planet.SurfaceLiquid}%)");
            sb.AppendLine($"Равнины:   {_planet.AreaPlains:F0} ({(int)_planet.SurfacePlains}%)");
            sb.AppendLine($"Горы:      {_planet.AreaMountains:F0} ({(int)_planet.SurfaceMountains}%)");
            sb.AppendLine($"Итого:     {_planet.TotalSurfaceArea:F0} млн км²");
            sb.AppendLine();
            sb.AppendLine($"Орбита #{_planet.OrbitIndex}   Спутников: {_planet.Satellites.Count}");
            txt.text = sb.ToString();
        }

        // ── Hangar ─────────────────────────────────────────────────
        //
        // Ряд кнопок-действий сверху (взлёт/дозаправка/ремонт корпуса/дозарядка ракет —
        // с актуальными ценами) + текстовая сводка по кораблю под ними.

        private void BuildHangarPanel(Transform parent)
        {
            var panel = MakePanel(parent, $"Screen_{SCR_HANGAR}", Color.clear);
            SetAnchors(panel, 0f, 0f, 1f, 1f);
            panel.SetActive(false);
            _panels[SCR_HANGAR] = panel;

            // Ряд кнопок-действий (верхняя полоса)
            var launchBtn = MakeButton(panel.transform, "HangarLaunch", "Взлететь", 12, ColBtnAlt);
            SetAnchors(launchBtn.gameObject, 0.005f, 0.9f, 0.245f, 0.99f);
            launchBtn.onClick.AddListener(LaunchFromHangar);

            var refuelBtn = MakeButton(panel.transform, "HangarRefuel", "Дозаправиться", 12, ColBtn);
            SetAnchors(refuelBtn.gameObject, 0.255f, 0.9f, 0.495f, 0.99f);
            refuelBtn.onClick.AddListener(RefuelShip);
            _hangarRefuelLabel = refuelBtn.GetComponentInChildren<Text>();

            var repairBtn = MakeButton(panel.transform, "HangarRepair", "Починить корпус", 12, ColBtn);
            SetAnchors(repairBtn.gameObject, 0.505f, 0.9f, 0.745f, 0.99f);
            repairBtn.onClick.AddListener(RepairHull);
            _hangarRepairLabel = repairBtn.GetComponentInChildren<Text>();

            var reloadBtn = MakeButton(panel.transform, "HangarReload", "Зарядить оружие", 12, ColBtn);
            SetAnchors(reloadBtn.gameObject, 0.755f, 0.9f, 0.995f, 0.99f);
            reloadBtn.onClick.AddListener(ReloadWeapons);
            _hangarReloadLabel = reloadBtn.GetComponentInChildren<Text>();

            // Секция «Корабли на объекте»: перечень пришвартованных сюда чужих кораблей
            // (для планеты — LandedPlanetUid, для станции/носителя — LandedOnShipUid), у каждого
            // строка с кнопкой «Сканировать» (открывает <see cref="ShipScanUIController"/>).
            _hangarShipsHeader = MakeText(panel.transform, "HangarShipsHeader",
                "Корабли на объекте:", 12, FontStyle.Bold, ColAccent);
            SetAnchors(_hangarShipsHeader.gameObject, 0.005f, 0.865f, 0.995f, 0.895f);

            var shipsScrollPanel = MakePanel(panel.transform, "HangarShipsScrollPanel", Color.clear);
            SetAnchors(shipsScrollPanel, 0f, 0.68f, 1f, 0.86f);
            _hangarShipsContent = BuildShopScroll(shipsScrollPanel.transform,
                "HangarShipsScroll", 2f, out _);

            // Текстовая сводка по своему кораблю — ниже списка.
            var textArea = MakePanel(panel.transform, "HangarText", Color.clear);
            SetAnchors(textArea, 0f, 0f, 1f, 0.67f);
            var scroll = BuildScrollRect(textArea.transform, out var contentText);
            _scrollRects[SCR_HANGAR] = scroll;
            _bodyTexts[SCR_HANGAR] = contentText;
        }

        private void LaunchFromHangar()
        {
            if (PlayerManager.Instance == null) return;
            var ship = PlayerShip.Instance?.ShipData;
            ship?.RecordLastVisitAndLaunch();
            PlayerManager.Instance.LeavePlanet();
        }

        private void RefuelShip()
        {
            var ship = PlayerShip.Instance?.ShipData;
            if (ship == null || _site == null) return;

            int missing = EquipmentSystem.GetFuelCapacity(ship) - EquipmentSystem.GetCurrentFuel(ship);
            if (missing <= 0) { GameLog.Add("[Ангар] Бак уже полон."); return; }

            int filled = FuelService.RefillToFull(ship, _site);
            if (filled <= 0) { GameLog.Add("[Ангар] Недостаточно кредитов для дозаправки."); return; }

            GameLog.Add(
                $"[Ангар] Дозаправка: +{filled} ед. за {filled * FuelService.FuelCostPerUnit} кр.");
            UpdateMoneyDisplay();
            RefreshHangar();
        }

        private void RepairHull()
        {
            var ship = PlayerShip.Instance?.ShipData;
            if (ship == null || _site == null) return;

            if (ship.CurrentHull >= ship.MaxHull) { GameLog.Add("[Ангар] Корпус цел."); return; }

            int restored = RepairService.RepairHullOnly(ship, _site);
            if (restored <= 0) { GameLog.Add("[Ангар] Недостаточно кредитов для ремонта."); return; }

            GameLog.Add($"[Ангар] Ремонт корпуса: +{restored} HP.");
            UpdateMoneyDisplay();
            RefreshHangar();
        }

        private void ReloadWeapons()
        {
            var ship = PlayerShip.Instance?.ShipData;
            if (ship == null || _site == null) return;

            if (!AmmoService.HasReloadableWeapons(ship))
            { GameLog.Add("[Ангар] Ракетного оружия нет."); return; }
            if (AmmoService.EstimateReloadCost(ship) <= 0)
            { GameLog.Add("[Ангар] Боезапас полон."); return; }

            int loaded = AmmoService.ReloadAllAmmo(ship, _site);
            if (loaded <= 0) { GameLog.Add("[Ангар] Недостаточно кредитов для зарядки."); return; }

            GameLog.Add($"[Ангар] Заряжено снарядов: {loaded}.");
            UpdateMoneyDisplay();
            RefreshHangar();
        }

        // Обновляет метки кнопок ангара актуальными ценами (вызывается из RefreshHangar).
        private void RefreshHangarButtons(ShipData ship)
        {
            if (_hangarRefuelLabel != null)
            {
                int missing = ship == null ? 0
                    : EquipmentSystem.GetFuelCapacity(ship) - EquipmentSystem.GetCurrentFuel(ship);
                _hangarRefuelLabel.text = missing > 0
                    ? $"Дозаправиться\n{missing * FuelService.FuelCostPerUnit} кр."
                    : "Дозаправиться\n(полный)";
            }
            if (_hangarRepairLabel != null)
            {
                int cost = RepairService.EstimateHullRepairCost(ship);
                _hangarRepairLabel.text = cost > 0
                    ? $"Починить корпус\n{cost} кр."
                    : "Починить корпус\n(цел)";
            }
            if (_hangarReloadLabel != null)
            {
                if (ship == null || !AmmoService.HasReloadableWeapons(ship))
                    _hangarReloadLabel.text = "Зарядить оружие\n(нет ракет)";
                else
                {
                    int cost = AmmoService.EstimateReloadCost(ship);
                    _hangarReloadLabel.text = cost > 0
                        ? $"Зарядить оружие\n{cost} кр."
                        : "Зарядить оружие\n(полный)";
                }
            }
        }

        private void RefreshHangar()
        {
            if (!_bodyTexts.TryGetValue(SCR_HANGAR, out var txt)) return;
            var ship = PlayerShip.Instance?.ShipData;
            RefreshHangarButtons(ship);
            RefreshHangarShips();
            if (ship == null) { txt.text = "Нет данных о корабле."; return; }

            var equipCfg = GalaxyManager.Instance?.Context?.ItemsConfig;
            var sb = new StringBuilder(1024);

            sb.AppendLine($"<b>Корабль:</b> {ship.Name}");
            sb.AppendLine($"HP      : {ship.CurrentHull} / {ship.MaxHull}");
            sb.AppendLine($"Скорость: {ship.ActualSpeed:F0}");
            sb.AppendLine($"Кредиты : {ship.Money:N0}");
            sb.AppendLine();

            float freeW = EquipmentSystem.GetFreeSpace(ship);
            sb.AppendLine($"Грузовой трюм: свободно {freeW} ед.");
            sb.AppendLine();
            sb.AppendLine("─── Снаряжение ───");

            if (ship.Equipment?.Slots != null)
            {
                foreach (var slotKey in ship.Equipment.Slots.Keys)
                {
                    string uid = ship.Equipment.GetItemUid(slotKey);
                    if (uid == null || !ship.AllItems.TryGetValue(uid, out var item))
                        continue;
                    string dur = item.NoWear ? "без износа" : $"{item.Durability}/{item.MaxDurability}";
                    sb.AppendLine($"  [{slotKey.Split('_')[0]}] {item.Name}  ({dur})  ТУ{item.TechLevel}");
                }
            }

            if (ship.Inventory?.Stacks?.Count > 0)
            {
                sb.AppendLine(); sb.AppendLine("─── Грузы ───");
                foreach (var kv in ship.Inventory.Stacks)
                    sb.AppendLine($"  {kv.Value.Name}: {kv.Value.TotalWeight} ед.  x{kv.Value.BasePrice} = {kv.Value.TotalPrice}");
            }

            txt.text = sb.ToString();
        }

        /// <summary>Заполняет секцию «Корабли на объекте»: все чужие ShipData, у которых
        /// LandedPlanetUid равен uid этой планеты (для планет) либо LandedOnShipUid равен
        /// uid этого корабля-носителя/станции. У каждой строки — кнопка «Сканировать»,
        /// открывающая <see cref="ShipScanUIController"/> для выбранного корабля.</summary>
        private void RefreshHangarShips()
        {
            if (_hangarShipsContent == null) return;
            ClearChildren(_hangarShipsContent);

            var star = GalaxyManager.Instance?.CurrentStar;
            if (star?.Ships == null || _site == null) { UpdateHangarShipsHeader(0); return; }

            string uid = _site.Uid;
            bool siteIsShip = _site is ShipData;

            int count = 0;
            for (int i = 0; i < star.Ships.Count; i++)
            {
                var s = star.Ships[i];
                if (s == null || s.IsPlayer) continue;
                if (s.CurrentHull <= 0) continue;
                bool onSite = siteIsShip
                    ? s.LandedOnShipUid == uid
                    : s.LandedPlanetUid == uid;
                if (!onSite) continue;

                var row = MakeShopRow(_hangarShipsContent,
                    $"HangarShipRow_{s.Uid}", 28f, 6f, new RectOffset(6, 6, 2, 2));

                string type = string.IsNullOrEmpty(s.ShipTypeId) ? "—" : s.ShipTypeId;
                string race = string.IsNullOrEmpty(s.Race) ? "—" : s.Race;
                MakeRowLabel(row, $"[{type}] {s.Name}", 220f, flexible: true);
                MakeRowLabel(row, race, 80f);
                var scanShip = s;
                MakeSmallButton(row, "Сканировать", ColBtn, () => OpenShipScan(scanShip));
                count++;
            }
            UpdateHangarShipsHeader(count);
        }

        private void UpdateHangarShipsHeader(int count)
        {
            if (_hangarShipsHeader == null) return;
            _hangarShipsHeader.text = count == 0
                ? "Корабли на объекте: нет"
                : $"Корабли на объекте: {count}";
        }

        private static void OpenShipScan(ShipData ship)
        {
            if (ship == null) return;
            ShipScanUIController.Instance?.OpenFor(ship);
        }

        // ── Info Center ────────────────────────────────────────────
        //
        // Заголовок сверху + общий NewsFeedView (чипы-фильтры и карточки новостей) —
        // тот же вид, что и в космическом оверлее инфоцентра.

        private void BuildInfoCenterPanel(Transform parent)
        {
            var panel = MakePanel(parent, $"Screen_{SCR_INFO}", Color.clear);
            SetAnchors(panel, 0f, 0f, 1f, 1f);
            panel.SetActive(false);
            _panels[SCR_INFO] = panel;

            var header = MakeText(panel.transform, "InfoHeader",
                "Инфоцентр — сводки Галактического Совета", 12, FontStyle.Bold, ColAccent);
            SetAnchors(header.gameObject, 0.01f, 0.94f, 0.99f, 1f);

            var feedGo = new GameObject("Feed");
            feedGo.transform.SetParent(panel.transform, false);
            var feedRt = feedGo.AddComponent<RectTransform>();
            SetAnchors(feedGo, 0f, 0f, 1f, 0.94f);
            _newsFeed = NewsFeedView.Create(feedRt, _font);
        }

        private void RefreshInfoCenter() => _newsFeed?.Refresh();

        // ── Goods Shop ─────────────────────────────────────────────

        private void RefreshGoodsShop()
        {
            var shop = _site?.Settlement?.Shop;
            if (_goodsContent == null || shop == null) return;

            var galaxyCfg = GalaxyManager.Instance?.Context?.Config;

            // Если магазин пустой (старый сейв), инициализируем заново
            if (shop.Goods.Count == 0 && galaxyCfg?.Goods != null)
                TradeSystem.InitPlanetShop(_site, galaxyCfg);

            // Пересчитываем цены по текущему стоку перед отображением
            if (galaxyCfg != null)
                TradeSystem.RecalculatePrices(_site, galaxyCfg);

            ClearChildren(_goodsContent);

            var ship = PlayerShip.Instance?.ShipData;

            foreach (var kv in shop.Goods)
            {
                string goodId = kv.Key;
                var entry = kv.Value;
                string goodName = TradeSystem.GetDisplayName(goodId, galaxyCfg);
                bool illegal = galaxyCfg != null && !TradeSystem.IsLegal(_site, goodId, galaxyCfg);

                var row = MakeShopRow(_goodsContent, $"GoodRow_{goodId}", 30f, 8f, new RectOffset(4, 4, 2, 2));

                MakeRowIcon(row, goodId, entry.Stock);

                string label = illegal ? $"<color=#E05050>{goodName}*</color>" : goodName;
                MakeRowLabel(row, label, GOODS_COL_NAME, flexible: true);
                MakeRowLabel(row, $"{entry.BuyPrice} кр.", GOODS_COL_PRICE);
                MakeRowLabel(row, $"{entry.SellPrice} кр.", GOODS_COL_PRICE);
                MakeRowLabel(row, $"{entry.Stock}", GOODS_COL_STOCK);

                if (ship != null)
                {
                    MakeSmallButton(row, "Купить",  ColGreen, () => OpenBuyDialog(goodId));
                    MakeSmallButton(row, "Продать", ColRed,   () => OpenSellDialog(goodId));
                }
            }
        }

        /// <summary>Диалог количества для покупки: ползунок 1..min(сток, на сколько хватает денег),
        /// счётчик показывает суммарную стоимость по текущей цене.</summary>
        private void OpenBuyDialog(string goodId)
        {
            var ship = PlayerShip.Instance?.ShipData;
            var galaxyCfg = GalaxyManager.Instance?.Context?.Config;
            var entry = ShopService.GetEntry(_site, goodId);
            if (ship == null || entry == null) return;

            if (entry.Stock <= 0)
            {
                GameLog.Add("[Магазин] Товара нет в наличии.");
                return;
            }
            int affordable = entry.BuyPrice > 0 ? ship.Money / entry.BuyPrice : entry.Stock;
            int max = Mathf.Min(entry.Stock, affordable);
            if (max <= 0)
            {
                GameLog.Add("[Магазин] Недостаточно кредитов.");
                return;
            }

            string name = TradeSystem.GetDisplayName(goodId, galaxyCfg);
            int price = entry.BuyPrice;
            _amountDialog.Show($"Купить: {name}", max,
                               amount => BuyGood(goodId, amount),
                               confirmLabel: "Купить",
                               amountFormatter: a => $"{a} / {max}   ({a * price} кр.)");
        }

        /// <summary>Диалог количества для продажи: ползунок 1..вес стека в трюме,
        /// счётчик показывает суммарную выручку по текущей цене.</summary>
        private void OpenSellDialog(string goodId)
        {
            var ship = PlayerShip.Instance?.ShipData;
            var galaxyCfg = GalaxyManager.Instance?.Context?.Config;
            if (ship == null) return;

            if (!ship.Inventory.Stacks.TryGetValue(goodId, out var stack) || stack.TotalWeight <= 0)
            {
                GameLog.Add("[Магазин] Нет такого товара в трюме.");
                return;
            }

            string name = TradeSystem.GetDisplayName(goodId, galaxyCfg);
            int price = ShopService.GetEntry(_site, goodId)?.SellPrice ?? 0;
            int max = stack.TotalWeight;
            _amountDialog.Show($"Продать: {name}", max,
                               amount => SellGood(goodId, amount),
                               confirmLabel: "Продать",
                               amountFormatter: a => $"{a} / {max}   ({a * price} кр.)");
        }

        private void BuyGood(string goodId, int amount)
        {
            var ship = PlayerShip.Instance?.ShipData;
            var galaxyCfg = GalaxyManager.Instance?.Context?.Config;
            var entry = ShopService.GetEntry(_site, goodId);
            if (ship == null || entry == null) return;

            // Контрабанда — штраф к отношениям и отказ если отношения плохие
            if (galaxyCfg != null && !TradeSystem.IsLegal(_site, goodId, galaxyCfg))
            {
                int penalty = galaxyCfg.Trade?.ContrabandRelationPenalty ?? -20;
                GameLog.Add(
                    $"[Магазин] {TradeSystem.GetDisplayName(goodId, galaxyCfg)} — контрабанда. Отношения: {penalty}.");
            }

            // ShopService.TryBuy сам делает: stock-, money-, RecalculatePrices, AddStack.
            if (!ShopService.TryBuy(ship, _site, goodId, amount, galaxyCfg, out int actualAmount, out int totalCost))
            {
                // Различаем «нет товара» и «нет денег» по сравнению с предусчитанной стоимостью.
                int desired = Mathf.Min(amount, entry.Stock);
                if (desired <= 0) return;
                if (ship.Money < desired * entry.BuyPrice)
                    GameLog.Add("[Магазин] Недостаточно кредитов.");
                return;
            }

            string displayName = TradeSystem.GetDisplayName(goodId, galaxyCfg);
            GameLog.Add($"[Магазин] Куплено: {displayName} x{actualAmount} за {totalCost} кр.");
            UpdateMoneyDisplay();
            RefreshGoodsShop();
            RefreshHangar();
        }

        private void SellGood(string goodId, int amount)
        {
            var ship = PlayerShip.Instance?.ShipData;
            var galaxyCfg = GalaxyManager.Instance?.Context?.Config;
            if (ship == null) return;

            var taken = ShopService.TrySell(ship, _site, goodId, amount, galaxyCfg, out int income);
            if (taken == null || taken.TotalWeight == 0)
            {
                GameLog.Add("[Магазин] Нет такого товара в трюме.");
                return;
            }

            GameLog.Add($"[Магазин] Продано: {taken.Name} x{taken.TotalWeight} за {income} кр.");
            UpdateMoneyDisplay();
            RefreshGoodsShop();
            RefreshHangar();
        }

        // ── Equipment Shop ─────────────────────────────────────────

        private void RefreshEquipShop()
        {
            if (_equipContent == null || _site?.Settlement?.EquipmentShop == null) return;
            ClearChildren(_equipContent);

            var ship = PlayerShip.Instance?.ShipData;

            foreach (var kv in _site.Settlement.EquipmentShop.Items)
            {
                var item = kv.Value;
                var row = MakeShopRow(_equipContent, $"EquipRow_{item.Uid}", 28f, 4f, new RectOffset(2, 2, 1, 1));

                MakeRowLabel(row, $"[{item.Category}] {item.Name}", EQUIP_COL_NAME, flexible: true);
                MakeRowLabel(row, $"ТУ{item.TechLevel}", EQUIP_COL_TU);
                MakeRowLabel(row, $"{item.Price} кр.", EQUIP_COL_PRICE);

                if (ship != null)
                {
                    string uid = item.Uid;
                    MakeSmallButton(row, "Купить", ColGreen, () => BuyEquipment(uid));
                }
            }

            // Продажа из инвентаря
            if (ship?.AllItems != null)
            {
                var sep = new GameObject("Sep");
                sep.transform.SetParent(_equipContent, false);
                sep.AddComponent<LayoutElement>().preferredHeight = 20f;
                var sepTxt = MakeText(sep.transform, "SepText", "── Продать из трюма ──", 11, FontStyle.Italic, ColAccent);
                SetAnchors(sepTxt.gameObject, 0f, 0f, 1f, 1f);

                foreach (var kv in ship.AllItems)
                {
                    var item = kv.Value;
                    bool equipped = false;
                    foreach (var s in ship.Equipment.Slots.Values)
                        if (s == item.Uid) { equipped = true; break; }
                    if (equipped) continue;

                    var row2 = MakeShopRow(_equipContent, $"SellRow_{item.Uid}", 26f, 4f, new RectOffset(2, 2, 1, 1));

                    int sellPrice = Mathf.RoundToInt(item.Price * 0.5f);
                    MakeRowLabel(row2, $"[{item.Category}] {item.Name}", EQUIP_COL_NAME, flexible: true);
                    MakeRowLabel(row2, "", EQUIP_COL_TU); // пустая колонка ТУ — для выравнивания с секцией покупки
                    MakeRowLabel(row2, $"{sellPrice} кр.", EQUIP_COL_PRICE);
                    string uid = item.Uid;
                    MakeSmallButton(row2, "Продать", ColRed, () => SellEquipment(uid));
                }
            }
        }

        private void BuyEquipment(string itemUid)
        {
            var ship = PlayerShip.Instance?.ShipData;
            if (ship == null || !_site.Settlement.EquipmentShop.Items.TryGetValue(itemUid, out var item)) return;

            if (ship.Money < item.Price)
            {
                GameLog.Add("[Магазин] Недостаточно кредитов.");
                return;
            }

            ship.Money -= item.Price;
            InventoryService.PutItem(ship, item);
            _site.Settlement.EquipmentShop.Items.Remove(itemUid);

            GameLog.Add($"[Магазин] Куплено оборудование: {item.Name} за {item.Price} кр.");
            UpdateMoneyDisplay();
            RefreshEquipShop();
            RefreshHangar();
        }

        private void SellEquipment(string itemUid)
        {
            var ship = PlayerShip.Instance?.ShipData;
            if (ship == null || !ship.AllItems.TryGetValue(itemUid, out var item)) return;

            int price = Mathf.RoundToInt(item.Price * 0.5f);
            ship.AllItems.Remove(itemUid);
            ship.Inventory.Remove(itemUid);
            ship.Money += price;

            _site.Settlement.EquipmentShop.Items[item.Uid] = item;

            GameLog.Add($"[Магазин] Продано: {item.Name} за {price} кр.");
            UpdateMoneyDisplay();
            RefreshEquipShop();
            RefreshHangar();
        }

        // ─────────────────────────────────────────────────────────
        // Вспомогательные методы построения UI
        // ─────────────────────────────────────────────────────────

        private ScrollRect BuildScrollRect(Transform parent, out Text contentText)
        {
            var vpGo = new GameObject("VP"); vpGo.transform.SetParent(parent, false);
            SetAnchors(vpGo, 0f, 0f, 1f, 1f);
            vpGo.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content"); contentGo.transform.SetParent(vpGo.transform, false);
            var rt = contentGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f); rt.sizeDelta = Vector2.zero;

            contentText = contentGo.AddComponent<Text>();
            contentText.font = _font; contentText.fontSize = 13; contentText.color = ColText;
            contentText.alignment = TextAnchor.UpperLeft;
            contentText.horizontalOverflow = HorizontalWrapMode.Wrap;
            contentText.verticalOverflow = VerticalWrapMode.Overflow;
            contentText.supportRichText = true;
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // ScrollRect добавляем на Panel (parent), viewport = vpGo
            var srComp = parent.gameObject.GetComponent<ScrollRect>();
            if (srComp == null) srComp = parent.gameObject.AddComponent<ScrollRect>();
            srComp.content = rt; srComp.viewport = vpGo.GetComponent<RectTransform>();
            srComp.horizontal = false; srComp.vertical = true;
            var sr = srComp;
            sr.scrollSensitivity = 40f; sr.movementType = ScrollRect.MovementType.Clamped;
            sr.verticalNormalizedPosition = 1f;
            return sr;
        }

        // Make*/SetAnchors мигрированы на UIBuilder (этап B5 рефакторинга). Локальные методы
        // оставлены как тонкие делегаты, которые автоматически передают `_font`, чтобы все
        // callsite'ы продолжали работать с прежней сигнатурой.
        private static GameObject MakePanel(Transform parent, string name, Color color)
            => UIBuilder.MakePanel(parent, name, color);

        private Text MakeText(Transform parent, string name, string text, int size, FontStyle style, Color color)
            => UIBuilder.MakeText(parent, name, text, _font, size, style, color);

        private Button MakeButton(Transform parent, string name, string label, int fontSize, Color color)
            => UIBuilder.MakeButton(parent, name, label, _font, fontSize, color);

        /// <summary>Шапка списка магазина: строка с теми же spacing/padding, что и строки
        /// данных, — колонки задаются последующими MakeRowLabel с теми же ширинами.</summary>
        private Transform MakeShopHeaderRow(Transform parent, float spacing, RectOffset padding)
        {
            var row = new GameObject("Header");
            row.transform.SetParent(parent, false);
            row.AddComponent<RectTransform>();
            SetAnchors(row, 0f, 0.93f, 1f, 1f);
            AddShopRowLayout(row, spacing, padding);
            return row.transform;
        }

        /// <summary>Строка списка магазина фиксированной высоты с горизонтальной сеткой колонок.</summary>
        private static Transform MakeShopRow(Transform parent, string name, float height, float spacing, RectOffset padding)
        {
            var row = new GameObject(name);
            row.transform.SetParent(parent, false);
            row.AddComponent<RectTransform>();
            row.AddComponent<LayoutElement>().preferredHeight = height;
            AddShopRowLayout(row, spacing, padding);
            return row.transform;
        }

        // Настройки layout-группы общие для шапки и строк: только колонка с flexibleWidth
        // (название) растягивается, остальные держат заданную ширину — иначе колонки
        // расползаются и шапка перестаёт совпадать со строками.
        private static void AddShopRowLayout(GameObject row, float spacing, RectOffset padding)
        {
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = spacing; hlg.padding = padding;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
        }

        private Text MakeRowLabel(Transform parent, string text, float width,
                                  bool flexible = false, bool bold = false, Color? color = null)
        {
            var go = new GameObject("Lbl"); go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width;
            le.flexibleWidth = flexible ? 1f : 0f;
            var txt = go.AddComponent<Text>();
            txt.font = _font; txt.text = text; txt.fontSize = 12; txt.color = color ?? ColText;
            txt.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            txt.alignment = TextAnchor.MiddleLeft; txt.supportRichText = true;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            return txt;
        }

        /// <summary>Ячейка-иконка товара в строке магазина. Путь — из ItemsConfig
        /// (StackGraphics.ResolveIcon); если иконки нет, ячейка остаётся пустой,
        /// чтобы колонки не съезжали.</summary>
        private static void MakeRowIcon(Transform parent, string goodId, int amount)
        {
            var go = new GameObject("Icon"); go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 24f; le.minWidth = 24f; le.flexibleWidth = 0;
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            var sprite = LoadGoodIcon(goodId, amount);
            img.sprite = sprite;
            img.enabled = sprite != null;
        }

        /// <summary>Загрузка инвентарной иконки товара. Спрайты нарезаны в режиме Multiple,
        /// поэтому сначала пробуем спрайтшит (первый кадр), затем одиночный Load.</summary>
        private static Sprite LoadGoodIcon(string goodId, int amount)
        {
            string path = StackGraphics.ResolveIcon(goodId, amount);
            if (string.IsNullOrEmpty(path)) return null;
            var frames = GraphicsManager.Instance?.GetSpriteSheet(path);
            if (frames != null && frames.Length > 0) return frames[0];
            return GraphicsManager.Instance?.TryGetSprite(path);
        }

        private void MakeSmallButton(Transform parent, string label, Color color, UnityEngine.Events.UnityAction action)
        {
            var btn = MakeButton(parent, label, label, 11, color);
            var le = btn.GetComponent<LayoutElement>() ?? btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = SHOP_COL_BTN; le.minWidth = SHOP_COL_BTN; le.flexibleWidth = 0;
            btn.onClick.AddListener(action);
        }

        private static void SetAnchors(GameObject go, float xMin, float yMin, float xMax, float yMax)
            => UIBuilder.SetAnchors(go, xMin, yMin, xMax, yMax);

        private static void ClearChildren(Transform t)
            => UIBuilder.ClearChildren(t);

        private PlanetUIConfig GetUICfg()
        {
            var cfg = GalaxyManager.Instance?.Context?.Config?.PlanetUI;
            return cfg ?? new PlanetUIConfig();
        }

        /// <summary>Возвращает подпись для заголовка экрана. Приоритет:
        /// 1) LandingUIConfig.Profile.ScreenTitles (явное задание в конфиге),
        /// 2) жёсткий Kind-based override для Overview (планета → «Планета», станция →
        ///    «Командование станции», носитель → «Мостик»),
        /// 3) PlanetUIConfig.Screens[id].Title (общий заголовок вкладки), 4) сам screenId.</summary>
        private string ResolveScreenTitle(string screenId)
        {
            var kind = _site?.Kind ?? LandingSiteKind.Planet;
            var lui = GalaxyManager.Instance?.Context?.Config?.LandingUI;
            var profile = lui?.ResolveProfile(kind);
            if (profile?.ScreenTitles != null
                && profile.ScreenTitles.TryGetValue(screenId, out var overrideTitle)
                && !string.IsNullOrEmpty(overrideTitle))
                return overrideTitle;

            // Kind-based дефолты — чтобы «Командование станции» показывалось даже без
            // подключённого LandingUIConfig.json. Меняется только заголовок Overview:
            // остальные экраны (Hangar/GoodsShop/EquipShop/InfoCenter) одинаковы для всех типов.
            if (screenId == SCR_OVERVIEW)
            {
                switch (kind)
                {
                    case LandingSiteKind.Station: return "Командование станции";
                    case LandingSiteKind.Carrier: return "Мостик";
                }
            }

            var cfg = GetUICfg();
            if (cfg.Screens != null && cfg.Screens.TryGetValue(screenId, out var scrCfg)
                && !string.IsNullOrEmpty(scrCfg?.Title))
                return scrCfg.Title;
            return screenId;
        }

        private static List<PlanetNavButtonConfig> DefaultNavButtons() => new()
        {
            new PlanetNavButtonConfig { Id = SCR_OVERVIEW,   Label = "Планета"       },
            new PlanetNavButtonConfig { Id = SCR_HANGAR,     Label = "Ангар"         },
            new PlanetNavButtonConfig { Id = SCR_GOODS,      Label = "Магазин"       },
            new PlanetNavButtonConfig { Id = SCR_EQUIP,      Label = "Оборудование"  },
            new PlanetNavButtonConfig { Id = SCR_INFO,       Label = "Инфо-центр"    },
        };
    }
}
