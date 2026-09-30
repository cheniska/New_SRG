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
    public partial class PlanetUIController
    {
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
    }
}
