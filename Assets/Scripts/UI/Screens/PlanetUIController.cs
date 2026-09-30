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
    public partial class PlanetUIController : MonoBehaviour
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

    }
}
