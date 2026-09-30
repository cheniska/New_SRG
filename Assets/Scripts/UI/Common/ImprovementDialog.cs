using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Core;
using SRG.Controllers;
using SRG.Utils;

namespace SRG.UI.Common
{
    /// <summary>
    /// Модальный диалог улучшения оборудования на научной базе (SB).
    /// Живёт под fullscreen-корнем, строится кодом. Открывается через
    /// <see cref="Show(ShipData)"/> (или из диалогового action-а "OpenImprovement").
    /// Показывает три секции: установленное на игроке, трюм, оборудование партнёров.
    /// Клик по строке предмета переключает панель на выбор тира/атрибута; кнопка «Улучшить»
    /// вызывает <see cref="ImprovementService.Apply"/>. См. docs/modules/equipment_improvement.md.
    /// </summary>
    public class ImprovementDialog : MonoBehaviour
    {
        private static ImprovementDialog _instance;

        private RectTransform _panel;
        private Text _titleText;
        private Text _statusText;
        private ScrollRect _scroll;
        private RectTransform _listContent;
        private Font _font;

        private ShipData _player;
        private ImprovementService.EligibleItem _selected;

        public bool IsOpen => gameObject != null && gameObject.activeSelf;

        /// <summary>Точка входа: показать диалог для игрока. Ленивое создание корня.</summary>
        public static void Show(ShipData player)
        {
            if (player == null) return;
            EnsureInstance();
            _instance._player = player;
            _instance.gameObject.SetActive(true);
            _instance.transform.SetAsLastSibling();
            _instance.RefreshMainList();
        }

        public static void Close()
        {
            if (_instance != null && _instance.IsOpen)
                _instance.gameObject.SetActive(false);
        }

        private static void EnsureInstance()
        {
            if (_instance != null) return;
            // Собственный Canvas + GraphicRaycaster: пристёгиваться к чужому канвасу опасно
            // (у HudMessage/DeathScreen канваса raycaster'а нет — клики не доходили до кнопок).
            // sortingOrder=180 ставит диалог поверх PlanetUI (50) / HUD (60) / PauseMenu (150),
            // но ниже DeathScreen/HudMessage (200).
            var go = new GameObject("ImprovementDialogCanvas");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 180;
            go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            go.AddComponent<GraphicRaycaster>();

            var dlg = new GameObject("ImprovementDialog");
            dlg.transform.SetParent(go.transform, false);
            _instance = dlg.AddComponent<ImprovementDialog>();
            _instance.BuildUI();
            dlg.SetActive(false);
        }

        // ── Панели ─────────────────────────────────────────────────────────────

        private void RefreshMainList()
        {
            _titleText.text = "Научная база — улучшение оборудования";
            _statusText.text = _player != null
                ? $"Кредиты: {_player.Money:N0}   Нейроядра: {NodesAvailable(_player)}"
                : "";
            ClearList();

            var eligible = ImprovementService.CollectEligible(_player);
            if (eligible.Count == 0)
            {
                AddLabelRow("Нет оборудования, доступного к улучшению.");
                AddCloseRow();
                return;
            }

            string lastSection = null;
            for (int i = 0; i < eligible.Count; i++)
            {
                var el = eligible[i];
                string section = SectionLabel(el);
                if (section != lastSection)
                {
                    AddSectionRow(section);
                    lastSection = section;
                }
                var captured = el;
                AddItemRow(el, () => OpenItemPanel(captured));
            }
            AddCloseRow();
        }

        private void OpenItemPanel(ImprovementService.EligibleItem el)
        {
            _selected = el;
            var cfg = GetCfg();
            _titleText.text = $"Улучшить: {el.Item.Name}";
            _statusText.text = ItemStatsSummary(el.Item);
            ClearList();

            AddLabelRow("Стандартный апгрейд (Min / Avg / Max):");
            foreach (var kv in cfg.CostShare)
            {
                string tierKey = kv.Key;
                var q = ImprovementService.GetQuote(el.Item, tierKey, ImprovementService.Mode.Standard, cfg);
                string btnLbl = q.Ok
                    ? $"{tierKey}: {q.Money} кред." + (q.NeedsNodes ? $", {q.Nodes} нейроядер" : "")
                    : $"{tierKey}: {q.Reason}";
                bool enabled = q.Ok && _player.Money >= q.Money &&
                               (!q.NeedsNodes || NodesAvailable(_player) >= q.Nodes);
                AddButtonRow(btnLbl, enabled, () => OpenAttributePanel(el, tierKey, ImprovementService.Mode.Standard));
            }

            AddLabelRow("Продвинутый апгрейд (одна характеристика, включая массу/прочность):");
            var qa = ImprovementService.GetQuote(el.Item, cfg.AdvancedBaseTier, ImprovementService.Mode.Advanced, cfg);
            string alabel = qa.Ok
                ? $"Продвинутое: {qa.Money} кред." + (qa.NeedsNodes ? $", {qa.Nodes} нейроядер" : "")
                : $"Продвинутое: {qa.Reason}";
            bool aenabled = qa.Ok && _player.Money >= qa.Money &&
                            (!qa.NeedsNodes || NodesAvailable(_player) >= qa.Nodes);
            AddButtonRow(alabel, aenabled, () => OpenAttributePanel(el, cfg.AdvancedBaseTier, ImprovementService.Mode.Advanced));

            AddButtonRow("← Назад к списку", true, RefreshMainList);
            AddCloseRow();
        }

        private void OpenAttributePanel(ImprovementService.EligibleItem el, string tierKey, ImprovementService.Mode mode)
        {
            var cfg = GetCfg();
            var attrs = mode == ImprovementService.Mode.Advanced
                ? ImprovementService.ListAdvancedAttributes(el.Item, cfg)
                : ImprovementService.ListStandardAttributes(el.Item, cfg);

            _titleText.text = mode == ImprovementService.Mode.Advanced
                ? $"Продвинутый: {el.Item.Name}"
                : $"{tierKey}: {el.Item.Name}";
            _statusText.text = ItemStatsSummary(el.Item);
            ClearList();

            if (attrs == null || attrs.Count == 0)
            {
                AddLabelRow("Нет доступных характеристик для улучшения.");
                AddButtonRow("← Назад", true, () => OpenItemPanel(el));
                AddCloseRow();
                return;
            }

            // Обычный апгрейд: «Все характеристики (обе растут одинаково)» + список отдельных.
            if (mode == ImprovementService.Mode.Standard)
            {
                AddButtonRow("Все характеристики (равномерно)", true,
                    () => Confirm(el, tierKey, mode, null));
                AddLabelRow("Или сфокусироваться (выбранная растёт полностью, остальные — частично):");
            }
            else
            {
                AddLabelRow("Выберите одну характеристику:");
            }

            foreach (var kv in attrs)
            {
                var name = string.IsNullOrEmpty(kv.Value.DisplayName) ? kv.Key : kv.Value.DisplayName;
                string capturedKey = kv.Key;
                AddButtonRow(name, true, () => Confirm(el, tierKey, mode, capturedKey));
            }
            AddButtonRow("← Назад", true, () => OpenItemPanel(el));
            AddCloseRow();
        }

        private void Confirm(ImprovementService.EligibleItem el, string tierKey, ImprovementService.Mode mode, string attrKey)
        {
            var cfg = GetCfg();
            var result = ImprovementService.Apply(el, _player, tierKey, mode, attrKey, cfg);
            if (!result.Ok)
            {
                _statusText.text = "Ошибка: " + result.Message;
                return;
            }
            GameLog.Add("[Наукобаза] " + result.Message);
            RefreshMainList();
        }

        // ── UI-строительство ───────────────────────────────────────────────────

        private ImprovementConfig GetCfg()
        {
            return GalaxyManager.Instance?.Context?.ItemsConfig?.Improvement ?? new ImprovementConfig();
        }

        private static string SectionLabel(ImprovementService.EligibleItem el) => el.Location switch
        {
            ImprovementService.ItemLocation.EquippedOnPlayer => "── Установленное ──",
            ImprovementService.ItemLocation.InPlayerInventory => "── Трюм игрока ──",
            ImprovementService.ItemLocation.PartnerEquipped => $"── {el.OwnerLabel} (установленное) ──",
            ImprovementService.ItemLocation.PartnerInventory => $"── {el.OwnerLabel} (трюм) ──",
            _ => "──"
        };

        private static string ItemStatsSummary(ItemInstance item)
        {
            if (item == null) return "";
            return $"Категория: {item.Category}   Тир: T{item.TechLevel}   Масса: {item.Weight}   Прочность: {item.Durability}/{item.MaxDurability}";
        }

        private static int NodesAvailable(ShipData ship)
        {
            int fromStack = 0;
            if (ship?.Inventory?.Stacks != null && ship.Inventory.Stacks.TryGetValue("Nod", out var s))
                fromStack = s.TotalWeight;
            return fromStack + Mathf.Max(0, ship?.NodeAccount ?? 0);
        }

        private void ClearList()
        {
            if (_listContent == null) return;
            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);
        }

        private void AddSectionRow(string label)
        {
            var t = MakeRowText(label, FontStyle.Bold, 14);
            t.color = UIColorPalette.Accent;
        }

        private void AddLabelRow(string label)
        {
            MakeRowText(label, FontStyle.Italic, 13);
        }

        private void AddItemRow(ImprovementService.EligibleItem el, System.Action onClick)
        {
            var go = new GameObject("Item");
            go.transform.SetParent(_listContent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, 32f);
            var img = go.AddComponent<Image>();
            img.color = UIColorPalette.Button;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var lblGo = new GameObject("Lbl");
            lblGo.transform.SetParent(go.transform, false);
            var lrt = lblGo.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(10f, 2f); lrt.offsetMax = new Vector2(-10f, -2f);
            var txt = lblGo.AddComponent<Text>();
            txt.font = _font; txt.fontSize = 13;
            txt.color = UIColorPalette.Text; txt.alignment = TextAnchor.MiddleLeft;
            txt.text = $"{el.Item.Name}  (T{el.Item.TechLevel}, {el.Item.Weight} масса)";
            txt.raycastTarget = false;
        }

        private void AddButtonRow(string label, bool enabled, System.Action onClick)
        {
            var go = new GameObject("Btn");
            go.transform.SetParent(_listContent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, 28f);
            var img = go.AddComponent<Image>();
            img.color = enabled ? UIColorPalette.ButtonSel : new Color(0.20f, 0.20f, 0.24f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.interactable = enabled;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var lblGo = new GameObject("Lbl");
            lblGo.transform.SetParent(go.transform, false);
            var lrt = lblGo.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(10f, 2f); lrt.offsetMax = new Vector2(-10f, -2f);
            var txt = lblGo.AddComponent<Text>();
            txt.font = _font; txt.fontSize = 13;
            txt.color = enabled ? UIColorPalette.Text : new Color(0.55f, 0.55f, 0.58f, 1f);
            txt.alignment = TextAnchor.MiddleCenter;
            txt.text = label;
            txt.raycastTarget = false;
        }

        private void AddCloseRow()
        {
            var go = new GameObject("BtnClose");
            go.transform.SetParent(_listContent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, 30f);
            var img = go.AddComponent<Image>();
            img.color = UIColorPalette.ButtonAlt;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(Close);

            var lblGo = new GameObject("Lbl");
            lblGo.transform.SetParent(go.transform, false);
            var lrt = lblGo.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(10f, 2f); lrt.offsetMax = new Vector2(-10f, -2f);
            var txt = lblGo.AddComponent<Text>();
            txt.font = _font; txt.fontSize = 13;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.text = "Закрыть";
            txt.raycastTarget = false;
        }

        private Text MakeRowText(string label, FontStyle style, int size)
        {
            var go = new GameObject("Row");
            go.transform.SetParent(_listContent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, size + 8f);
            var txt = go.AddComponent<Text>();
            txt.font = _font; txt.fontSize = size; txt.fontStyle = style;
            txt.color = UIColorPalette.Text; txt.alignment = TextAnchor.MiddleLeft;
            txt.text = label;
            txt.raycastTarget = false;
            return txt;
        }

        // ── Каркас ─────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var rt = gameObject.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var backdrop = gameObject.AddComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.60f);
            backdrop.raycastTarget = true;

            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(transform, false);
            _panel = panelGo.AddComponent<RectTransform>();
            _panel.anchorMin = new Vector2(0.5f, 0.5f);
            _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = new Vector2(560f, 460f);
            var panelImg = panelGo.AddComponent<Image>();
            panelImg.color = new Color(UIColorPalette.Bg.r, UIColorPalette.Bg.g, UIColorPalette.Bg.b, 0.98f);
            panelImg.raycastTarget = true;
            panelGo.AddComponent<ClickBlocker>();

            _titleText = MakeText(panelGo.transform, new Vector2(0f, 210f), new Vector2(540f, 28f), 16, FontStyle.Bold, TextAnchor.MiddleCenter);
            _statusText = MakeText(panelGo.transform, new Vector2(0f, 182f), new Vector2(540f, 22f), 13, FontStyle.Normal, TextAnchor.MiddleCenter);

            // Scroll list
            var scrollGo = new GameObject("Scroll");
            scrollGo.transform.SetParent(panelGo.transform, false);
            var srt = scrollGo.AddComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.5f, 0.5f);
            srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.sizeDelta = new Vector2(540f, 340f);
            srt.anchoredPosition = new Vector2(0f, -14f);
            var scrollImg = scrollGo.AddComponent<Image>();
            scrollImg.color = new Color(0f, 0f, 0f, 0.20f);
            _scroll = scrollGo.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollGo.transform, false);
            var vrt = viewport.AddComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero; vrt.offsetMax = Vector2.zero;
            var vimg = viewport.AddComponent<Image>();
            vimg.color = new Color(0f, 0f, 0f, 0.01f);
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            _scroll.viewport = vrt;

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            _listContent = content.AddComponent<RectTransform>();
            _listContent.anchorMin = new Vector2(0f, 1f);
            _listContent.anchorMax = new Vector2(1f, 1f);
            _listContent.pivot = new Vector2(0.5f, 1f);
            _listContent.anchoredPosition = Vector2.zero;
            _listContent.sizeDelta = new Vector2(0f, 0f);

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4f;
            vlg.padding = new RectOffset(6, 6, 6, 6);
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = _listContent;
        }

        private void Update()
        {
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        private Text MakeText(Transform parent, Vector2 pos, Vector2 size, int fontSize, FontStyle style, TextAnchor anchor)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size; rt.anchoredPosition = pos;
            var t = go.AddComponent<Text>();
            t.font = _font; t.fontSize = fontSize; t.fontStyle = style;
            t.color = UIColorPalette.Text; t.alignment = anchor;
            t.raycastTarget = false;
            return t;
        }

        private class ClickBlocker : MonoBehaviour, IPointerClickHandler
        {
            public void OnPointerClick(PointerEventData _) { }
        }
    }
}
