using UnityEngine;
using UnityEngine.UI;
using SRG.Core;
using SRG.Galaxy;
using SRG.Controllers;
using SRG.Dialog;
using SRG.Simulation;

namespace SRG.UI.Screens
{
    // Визуальный контроллер диалогов. Singleton.
    // Два режима компоновки:
    //   Space  — маленькая панель в правом верхнем углу экрана.
    //   Planet — широкая панель слева от формы планеты («много места»).
    //
    // Окно текста реплики персонажа и окно вариантов ответа разделены и оба
    // поддерживают вертикальный скролл, если содержимое не помещается.
    //
    // API:
    //   DialogUIController.Instance.OpenSpaceDialog(dialogId, targetShip = null)
    //   DialogUIController.Instance.OpenPlanetDialog(dialogId, planet)
    public class DialogUIController : MonoBehaviour, IDialogPresenter
    {
        public static DialogUIController Instance { get; private set; }
        public bool IsOpen => _panel != null && _panel.activeSelf;

        private Canvas      _canvas;
        private GameObject  _panel;
        private RectTransform _panelRt;
        private RectTransform _titleRt;
        private RectTransform _bodyRt;
        private RectTransform _repliesRt;

        private Text       _titleText;
        private Text       _bodyText;
        private ScrollRect _bodyScroll;
        private ScrollRect _repliesScroll;
        private RectTransform _repliesContent;

        private Font _font;
        private DialogScope _scope;

        // Дефолтные цвета — совместимы с историческим хардкодом; используются, если в
        // DialogUIConfig.Color* строка не задана или не распарсилась.
        private static readonly Color DefBg         = new(0.04f, 0.07f, 0.12f, 0.96f);
        private static readonly Color DefTitleBg    = new(0.06f, 0.10f, 0.16f, 1f);
        private static readonly Color DefPanel      = new(0.05f, 0.09f, 0.14f, 1f);
        private static readonly Color DefAccent     = new(0.55f, 0.82f, 1.00f, 1f);
        private static readonly Color DefText       = new(0.88f, 0.92f, 0.98f, 1f);
        private static readonly Color DefReplyBtn   = new(0.13f, 0.22f, 0.35f, 1f);
        private static readonly Color DefReplyHover = new(0.18f, 0.34f, 0.55f, 1f);
        private static readonly Color DefCloseBg    = new(0.45f, 0.10f, 0.10f, 0.92f);
        private static readonly Color DefScrollbar  = new(0.08f, 0.10f, 0.16f, 0.95f);
        private static readonly Color DefScrollHand = new(0.30f, 0.55f, 0.80f, 0.95f);

        private Color ColBg, ColTitleBg, ColPanel, ColAccent, ColText,
                      ColReplyBtn, ColReplyHover, ColCloseBg, ColScrollbar, ColScrollHand;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            GameWorld.Attach(this);
            LoadPaletteAndFont();
            // Список модулей живёт в DialogModules — там же, где флаг «уже поставлено»
            // и переустановка после DialogService.Reset().
            DialogModules.Install();
        }

        private void LoadPaletteAndFont()
        {
            var cfg = GetUICfg();
            _font = Resources.GetBuiltinResource<Font>(
                string.IsNullOrEmpty(cfg.FontResource) ? "LegacyRuntime.ttf" : cfg.FontResource);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            ColBg         = ParseColor(cfg.ColorBackground,   DefBg);
            ColTitleBg    = ParseColor(cfg.ColorTitleBg,      DefTitleBg);
            ColPanel      = ParseColor(cfg.ColorPanel,        DefPanel);
            ColAccent     = ParseColor(cfg.ColorAccent,       DefAccent);
            ColText       = ParseColor(cfg.ColorText,         DefText);
            ColReplyBtn   = ParseColor(cfg.ColorReplyBtn,     DefReplyBtn);
            ColReplyHover = ParseColor(cfg.ColorReplyHover,   DefReplyHover);
            ColCloseBg    = ParseColor(cfg.ColorCloseBg,      DefCloseBg);
            ColScrollbar  = ParseColor(cfg.ColorScrollbar,    DefScrollbar);
            ColScrollHand = ParseColor(cfg.ColorScrollHandle, DefScrollHand);
        }

        // Формат "#RRGGBB" или "#RRGGBB/AA" (alpha = 2 hex, отдельно после слэша).
        private static Color ParseColor(string s, Color fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            string rgb = s; string alpha = null;
            int slash = s.IndexOf('/');
            if (slash > 0) { rgb = s.Substring(0, slash); alpha = s.Substring(slash + 1); }
            if (!ColorUtility.TryParseHtmlString(rgb, out var c)) return fallback;
            if (!string.IsNullOrEmpty(alpha) &&
                int.TryParse(alpha, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out int a))
                c.a = Mathf.Clamp01(a / 255f);
            return c;
        }

        private void OnEnable()
        {
            DialogService.OnDialogStarted     += HandleStart;
            DialogService.OnDialogNodeChanged += HandleNodeChanged;
            DialogService.OnDialogEnded       += HandleEnd;
        }

        private void OnDisable()
        {
            DialogService.OnDialogStarted     -= HandleStart;
            DialogService.OnDialogNodeChanged -= HandleNodeChanged;
            DialogService.OnDialogEnded       -= HandleEnd;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            GameWorld.Detach(this);
            Instance = null;
        }

        // ───── Public API ────────────────────────────────────────────────

        /// <summary>Окно улучшения оборудования (научная база) — вызывается диалоговым действием.</summary>
        public void ShowImprovementDialog(ShipData player) => SRG.UI.Common.ImprovementDialog.Show(player);

        public bool OpenSpaceDialog(string dialogId, ShipData targetShip = null)
        {
            var player = PlayerShip.Instance?.ShipData ?? PlayerManager.Instance?.GetOrFindPlayerShip();
            return DialogService.Start(dialogId, DialogScope.Space, player, planet: null, targetShip: targetShip);
        }

        /// <summary>Входящий вызов игроку: агрессор (initiator) принудительно инициирует разговор.
        /// Механически идентичен <see cref="OpenSpaceDialog"/> — тот же контекст (PlayerShip=игрок,
        /// TargetShip=инициатор), тот же UI. Отличие только в способе вызова: не игрок нажимает
        /// «Связь», а NPC-агрессор из своего AI-тика.
        ///
        /// Возвращает false, если открыть входящий сейчас нельзя (уже активен диалог, игрок
        /// сидит на планете, находится в гиперпрыжке). Вызывающий (AI) при false должен решить,
        /// что делать дальше — обычно сразу переходить к атаке.</summary>
        public bool OpenIncomingSpaceDialog(string dialogId, ShipData initiator)
        {
            if (!CanShowIncoming()) return false;
            var player = PlayerShip.Instance?.ShipData ?? PlayerManager.Instance?.GetOrFindPlayerShip();
            if (player == null) return false;
            // Игрок в гиперпрыжке или не в звезде — переговоров не бывает.
            if (player.HyperjumpPhase != SRG.Galaxy.HyperjumpPhase.None) return false;
            if (!string.IsNullOrEmpty(player.LandedPlanetUid)) return false;
            return DialogService.Start(dialogId, DialogScope.Space, player, planet: null, targetShip: initiator);
        }

        /// <summary>Может ли сейчас открыться входящий вызов. Отказ: уже открытый диалог
        /// (не перебиваем), активная планетарная форма (игрок в наземном UI).</summary>
        public bool CanShowIncoming()
        {
            if (IsOpen) return false;
            // Планетарный UI перебивать нельзя — игрок в наземном интерфейсе не должен резко
            // получать «пират требует груз». Проверка через сам сервис, без прямого reference
            // на UI-контроллер планеты.
            var planetForm = SRG.UI.Screens.PlanetUIController.Instance;
            if (planetForm != null && planetForm.IsOpen) return false;
            return true;
        }

        public bool OpenPlanetDialog(string dialogId, PlanetData planet)
        {
            if (planet == null) return false;
            var player = PlayerShip.Instance?.ShipData ?? PlayerManager.Instance?.GetOrFindPlayerShip();
            return DialogService.Start(dialogId, DialogScope.Gov, player, planet: planet, targetShip: null);
        }

        /// <summary>Открывает диалог «с командиром» — на мостике линкора-носителя или в
        /// командовании станции. Отличается от <see cref="OpenSpaceDialog"/> только скоупом
        /// (<see cref="DialogScope.Gov"/>): по нему <see cref="DialogService"/> подбирает
        /// gov-вариант dialogId, а условия реплик могут фильтроваться <c>scope == "Gov"</c>.
        /// Единый scope для «правительства планеты», «мостика линкора» и «командования станции»
        /// — их объединяет одна семантика «власть на месте посадки».</summary>
        public bool OpenBridgeDialog(string dialogId, ShipData carrier)
        {
            if (carrier == null) return false;
            var player = PlayerShip.Instance?.ShipData ?? PlayerManager.Instance?.GetOrFindPlayerShip();
            return DialogService.Start(dialogId, DialogScope.Gov, player, planet: null, targetShip: carrier);
        }

        public void Close() => DialogService.End();

        // ───── События DialogService ─────────────────────────────────────

        private void HandleStart(DialogContext ctx)
        {
            _scope = ctx.Scope;
            if (_panel == null) BuildUI();
            ApplyLayout(_scope);
            _panel.SetActive(true);
            // UpdateTitle делает HandleNodeChanged; на Start достаточно раскрыть панель.
        }

        private void HandleNodeChanged(DialogContext ctx)
        {
            UpdateTitle(ctx);
            RenderCurrentNode();
        }

        private void HandleEnd(DialogContext ctx)
        {
            if (_panel != null) _panel.SetActive(false);
        }

        // ───── Рендер ────────────────────────────────────────────────────

        private void UpdateTitle(DialogContext ctx)
        {
            if (_titleText == null || ctx == null) return;
            var node = DialogService.CurrentNode();
            string speaker = node?.Speaker
                             ?? ctx.Tree?.Speaker
                             ?? ctx.TargetPlanet?.Name
                             ?? ctx.TargetShip?.Name
                             ?? DialogTexts.Phrase("ui_untitled");
            string title = ctx.Tree?.Title;
            // Склейка «говорящий — заголовок»: разделитель тоже фраза (тире, дефис, двоеточие —
            // вопрос типографики языка), поэтому формат живёт в текст-конфиге.
            string raw = speaker;
            if (!string.IsNullOrEmpty(title))
            {
                string fmt = DialogTexts.Phrase("ui_title_format");
                raw = string.IsNullOrEmpty(fmt) ? $"{speaker} {title}" : string.Format(fmt, speaker, title);
            }
            _titleText.text = DialogService.Substitute(raw, ctx);
        }

        private void RenderCurrentNode()
        {
            var node = DialogService.CurrentNode();
            if (node == null) return;

            _bodyText.text = DialogService.Substitute(
                DialogService.ResolveNodeText(node) ?? string.Empty);
            Canvas.ForceUpdateCanvases();
            if (_bodyScroll != null) _bodyScroll.verticalNormalizedPosition = 1f;

            ClearChildren(_repliesContent);
            var replies = DialogService.GetVisibleReplies();
            var cfg = GetUICfg();

            if (replies.Count == 0)
            {
                BuildReplyButton(DialogTexts.Phrase("ui_close"), () => DialogService.End());
            }
            else
            {
                for (int n = 0; n < replies.Count; n++)
                {
                    int captured = replies[n].Index;
                    string label = $"{n + 1}. " +
                        DialogService.Substitute(replies[n].Reply.Text, null, DialogVoice.Player);
                    BuildReplyButton(label, () => DialogService.ChooseReply(captured));
                }
            }
            Canvas.ForceUpdateCanvases();
            if (_repliesScroll != null) _repliesScroll.verticalNormalizedPosition = 1f;
        }

        private void BuildReplyButton(string label, System.Action onClick)
        {
            var go = new GameObject("Reply");
            go.transform.SetParent(_repliesContent, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = ColReplyBtn;
            var btn = go.AddComponent<Button>();
            var cb = btn.colors;
            cb.normalColor = ColReplyBtn;
            cb.highlightedColor = ColReplyHover;
            cb.pressedColor = ColReplyHover;
            cb.selectedColor = ColReplyHover;
            btn.colors = cb;
            btn.targetGraphic = img;

            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 4, 4);
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var txtGo = new GameObject("Label");
            txtGo.transform.SetParent(go.transform, false);
            txtGo.AddComponent<RectTransform>();
            var txt = txtGo.AddComponent<Text>();
            txt.font = _font;
            txt.text = label;
            txt.fontSize = GetUICfg().ReplyFontSize;
            txt.color = ColText;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.supportRichText = true;

            btn.onClick.AddListener(() => onClick?.Invoke());
        }

        // ───── Построение базового каркаса ───────────────────────────────

        private void BuildUI()
        {
            var canvasGo = new GameObject("DialogCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = GetUICfg().SortingOrder;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            _panel = new GameObject("DialogPanel");
            _panel.transform.SetParent(canvasGo.transform, false);
            _panelRt = _panel.AddComponent<RectTransform>();
            _panel.AddComponent<Image>().color = ColBg;

            BuildTitleBar();
            BuildBodyScroll();
            BuildRepliesScroll();

            _panel.SetActive(false);
        }

        private void BuildTitleBar()
        {
            var title = new GameObject("TitleBar");
            title.transform.SetParent(_panel.transform, false);
            _titleRt = title.AddComponent<RectTransform>();
            title.AddComponent<Image>().color = ColTitleBg;

            _titleRt.anchorMin = new Vector2(0f, 1f);
            _titleRt.anchorMax = new Vector2(1f, 1f);
            _titleRt.pivot = new Vector2(0.5f, 1f);
            _titleRt.sizeDelta = new Vector2(0f, GetUICfg().TitleHeight);
            _titleRt.anchoredPosition = Vector2.zero;

            var txtGo = new GameObject("TitleText");
            txtGo.transform.SetParent(title.transform, false);
            var trt = txtGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(8f, 0f); trt.offsetMax = new Vector2(-36f, 0f);
            _titleText = txtGo.AddComponent<Text>();
            _titleText.font = _font;
            _titleText.fontSize = GetUICfg().TitleFontSize;
            _titleText.fontStyle = FontStyle.Bold;
            _titleText.color = ColAccent;
            _titleText.alignment = TextAnchor.MiddleLeft;
            _titleText.horizontalOverflow = HorizontalWrapMode.Overflow;

            var closeGo = new GameObject("CloseBtn");
            closeGo.transform.SetParent(title.transform, false);
            var crt = closeGo.AddComponent<RectTransform>();
            crt.anchorMin = new Vector2(1f, 0f); crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 0.5f);
            crt.sizeDelta = new Vector2(28f, 0f);
            crt.anchoredPosition = new Vector2(-2f, 0f);
            closeGo.AddComponent<Image>().color = ColCloseBg;
            var cbtn = closeGo.AddComponent<Button>();
            cbtn.onClick.AddListener(() => DialogService.End());

            var xGo = new GameObject("X");
            xGo.transform.SetParent(closeGo.transform, false);
            var xrt = xGo.AddComponent<RectTransform>();
            xrt.anchorMin = Vector2.zero; xrt.anchorMax = Vector2.one; xrt.sizeDelta = Vector2.zero;
            var xtxt = xGo.AddComponent<Text>();
            xtxt.font = _font; xtxt.text = DialogTexts.Phrase("ui_close_icon");
            xtxt.fontSize = GetUICfg().CloseIconFontSize; xtxt.color = Color.white;
            xtxt.alignment = TextAnchor.MiddleCenter;
        }

        private void BuildBodyScroll()
        {
            var body = new GameObject("BodyArea");
            body.transform.SetParent(_panel.transform, false);
            _bodyRt = body.AddComponent<RectTransform>();
            body.AddComponent<Image>().color = ColPanel;
            _bodyRt.anchorMin = Vector2.zero;
            _bodyRt.anchorMax = Vector2.one;
            _bodyRt.offsetMin = Vector2.zero;
            _bodyRt.offsetMax = Vector2.zero;

            var (scroll, content, bar) = BuildScrollHost(body.transform, "Body");
            _bodyScroll = scroll;

            var txtGo = new GameObject("BodyText");
            txtGo.transform.SetParent(content, false);
            var rt = txtGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 0f);
            _bodyText = txtGo.AddComponent<Text>();
            _bodyText.font = _font;
            _bodyText.fontSize = GetUICfg().BodyFontSize;
            _bodyText.color = ColText;
            _bodyText.alignment = TextAnchor.UpperLeft;
            _bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _bodyText.verticalOverflow = VerticalWrapMode.Overflow;
            _bodyText.supportRichText = true;
            _bodyText.lineSpacing = 1.15f;

            var fitter = txtGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void BuildRepliesScroll()
        {
            var rep = new GameObject("RepliesArea");
            rep.transform.SetParent(_panel.transform, false);
            _repliesRt = rep.AddComponent<RectTransform>();
            rep.AddComponent<Image>().color = ColPanel;
            _repliesRt.anchorMin = new Vector2(0f, 0f);
            _repliesRt.anchorMax = new Vector2(1f, 0f);
            _repliesRt.pivot = new Vector2(0.5f, 0f);
            _repliesRt.sizeDelta = new Vector2(0f, 180f);
            _repliesRt.anchoredPosition = Vector2.zero;

            var (scroll, content, bar) = BuildScrollHost(rep.transform, "Replies");
            _repliesScroll = scroll;
            _repliesContent = content;

            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.spacing = 4f;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        }

        // Создаёт viewport + content + вертикальный скроллбар для scroll-host.
        private (ScrollRect scroll, RectTransform content, Scrollbar bar) BuildScrollHost(
            Transform host, string namePrefix)
        {
            Scrollbar scrollbar;
            {
                var sbGo = new GameObject($"{namePrefix}Scrollbar");
                sbGo.transform.SetParent(host, false);
                var sbRt = sbGo.AddComponent<RectTransform>();
                sbRt.anchorMin = new Vector2(1f, 0f);
                sbRt.anchorMax = new Vector2(1f, 1f);
                sbRt.pivot = new Vector2(1f, 0.5f);
                sbRt.offsetMin = new Vector2(-12f, 0f);
                sbRt.offsetMax = new Vector2(0f, 0f);
                sbGo.AddComponent<Image>().color = ColScrollbar;
                scrollbar = sbGo.AddComponent<Scrollbar>();
                scrollbar.direction = Scrollbar.Direction.BottomToTop;

                var areaGo = new GameObject("SlidingArea");
                areaGo.transform.SetParent(sbGo.transform, false);
                var arrt = areaGo.AddComponent<RectTransform>();
                arrt.anchorMin = Vector2.zero; arrt.anchorMax = Vector2.one; arrt.sizeDelta = Vector2.zero;

                var handleGo = new GameObject("Handle");
                handleGo.transform.SetParent(areaGo.transform, false);
                var hrt = handleGo.AddComponent<RectTransform>();
                hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one; hrt.sizeDelta = Vector2.zero;
                var himg = handleGo.AddComponent<Image>();
                himg.color = ColScrollHand;
                scrollbar.handleRect = hrt;
                scrollbar.targetGraphic = himg;
            }

            var vpGo = new GameObject($"{namePrefix}Viewport");
            vpGo.transform.SetParent(host, false);
            var vprt = vpGo.AddComponent<RectTransform>();
            vprt.anchorMin = Vector2.zero;
            vprt.anchorMax = Vector2.one;
            vprt.offsetMin = new Vector2(4f, 4f);
            vprt.offsetMax = new Vector2(-14f, -4f);
            vpGo.AddComponent<RectMask2D>();

            var contentGo = new GameObject($"{namePrefix}Content");
            contentGo.transform.SetParent(vpGo.transform, false);
            var crt = contentGo.AddComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0f, 1f);
            crt.sizeDelta = Vector2.zero;
            crt.anchoredPosition = Vector2.zero;
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = host.gameObject.AddComponent<ScrollRect>();
            sr.content = crt;
            sr.viewport = vprt;
            sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 30f;
            sr.verticalScrollbar = scrollbar;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            sr.verticalNormalizedPosition = 1f;

            return (sr, crt, scrollbar);
        }

        // ───── Раскладка под режим ───────────────────────────────────────

        private void ApplyLayout(DialogScope scope)
        {
            var cfg = GetUICfg();
            float titleH = cfg.TitleHeight;

            if (scope == DialogScope.Space)
            {
                _panelRt.anchorMin = new Vector2(1f, 1f);
                _panelRt.anchorMax = new Vector2(1f, 1f);
                _panelRt.pivot = new Vector2(1f, 1f);
                _panelRt.sizeDelta = new Vector2(cfg.SpaceWidth, cfg.SpaceHeight);
                _panelRt.anchoredPosition = new Vector2(-cfg.SpaceMargin, -cfg.SpaceMargin);

                _repliesRt.sizeDelta = new Vector2(0f, cfg.SpaceRepliesHeight);
                _bodyRt.offsetMin = new Vector2(0f, cfg.SpaceRepliesHeight);
                _bodyRt.offsetMax = new Vector2(0f, -titleH);
            }
            else
            {
                // Gov-контекст (правительство планеты / мостик линкора / командование станции):
                // якорь к левой стороне экрана, по вертикали — по центру. Форма посадочного
                // UI (PlanetUIController) центрована, поэтому диалог встаёт слева от неё.
                _panelRt.anchorMin = new Vector2(0f, 0.5f);
                _panelRt.anchorMax = new Vector2(0f, 0.5f);
                _panelRt.pivot = new Vector2(0f, 0.5f);
                _panelRt.sizeDelta = new Vector2(cfg.PlanetWidth, cfg.PlanetHeight);
                _panelRt.anchoredPosition = new Vector2(cfg.PlanetMargin, 0f);

                _repliesRt.sizeDelta = new Vector2(0f, cfg.PlanetRepliesHeight);
                _bodyRt.offsetMin = new Vector2(0f, cfg.PlanetRepliesHeight);
                _bodyRt.offsetMax = new Vector2(0f, -titleH);
            }
        }

        private DialogUIConfig GetUICfg() =>
            GalaxyManager.Instance?.Context?.Config?.Dialogs?.DialogUI ?? new DialogUIConfig();

        private static void ClearChildren(Transform t)
        {
            if (t == null) return;
            for (int i = t.childCount - 1; i >= 0; i--)
                Destroy(t.GetChild(i).gameObject);
        }
    }
}
