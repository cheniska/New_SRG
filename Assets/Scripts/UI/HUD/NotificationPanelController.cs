using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.Controllers;
using SRG.UI.Screens;

namespace SRG.UI.HUD
{
    /// <summary>
    /// Нижняя панель уведомлений/задач.
    ///
    /// Layout:
    ///   [Квесты]  [Галактика]  [Игрок]      …          Кред: X | Свободно: Y | Дата | HP: … | Оборуд: …
    ///     ─── левая часть ─── иконки с секциями        ─── правая часть — характеристики корабля
    ///
    /// Иконки — квадратики 28×28 с буквой-меткой и цветной рамкой по типу:
    ///   квест = золото, галактика = синий, игрок = красный.
    /// Наведение мышью → всплывает окошко с заголовком (дата+категория) и телом новости.
    /// ПКМ → удаляет уведомление из ленты через <see cref="GalaxyNewsService.RemoveById"/>.
    /// ЛКМ → открыть полный <see cref="InfoCenterOverlayController"/>.
    ///
    /// Панель — self-contained singleton, DontDestroyOnLoad, создаётся при первом обращении.
    /// Заменяет старую DataPanel в HUD-е (см. HUDController.BuildDataPanel — теперь пустышка).
    /// </summary>
    public class NotificationPanelController : MonoBehaviour
    {
        public static NotificationPanelController Instance { get; private set; }

        private const float PANEL_H  = 44f;
        private const float ICON_SIZE = 28f;
        private const float ICON_GAP  = 4f;
        private const float SIDE_PAD  = 8f;
        private const float STATS_GAP = 14f;

        private static readonly Color COL_BG        = new Color(0.04f, 0.06f, 0.10f, 0.90f);
        private static readonly Color COL_ICON_BG   = new Color(0.10f, 0.13f, 0.18f, 0.95f);
        private static readonly Color COL_QUEST     = new Color(1.00f, 0.82f, 0.28f, 1f);
        private static readonly Color COL_GALAXY    = new Color(0.36f, 0.68f, 1.00f, 1f);
        private static readonly Color COL_PLAYER    = new Color(1.00f, 0.42f, 0.32f, 1f);
        private static readonly Color COL_TXT_LABEL = new Color(0.70f, 0.88f, 1.00f, 1f);

        private GameObject _root;
        private Transform  _iconsContent;
        private Font       _font;

        // Правые метки — обновляются в Update()
        private Text _moneyLabel;
        private Text _freeLabel;
        private Text _dateLabel;
        private Text _hpLabel;
        private Text _equipLabel;

        // ── Tooltip ────────────────────────────────────────────────
        private GameObject _tooltipGo;
        private Text       _tooltipText;
        private RectTransform _tooltipRt;

        private float _refreshTimer;
        private const float REFRESH_INTERVAL = 0.25f;
        // Максимум иконок на панели — старые сдвигаются влево, лишние удаляются из ленты не хотим.
        // Просто ограничиваем показ, пользователь видит новые справа-налево (свежие ближе к статам).
        private const int MAX_VISIBLE_ICONS = 20;

        // ── Публичный API ─────────────────────────────────────────

        public static void EnsureCreated()
        {
            if (Instance != null) return;
            var go = new GameObject("NotificationPanel");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<NotificationPanelController>();
        }

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private void Start()
        {
            BuildUI();
            GalaxyNewsService.OnNewsAdded   += OnNewsAdded;
            GalaxyNewsService.OnNewsRemoved += OnNewsRemoved;
            RebuildIcons();
        }

        private void OnDestroy()
        {
            GalaxyNewsService.OnNewsAdded   -= OnNewsAdded;
            GalaxyNewsService.OnNewsRemoved -= OnNewsRemoved;
        }

        private void Update()
        {
            _refreshTimer += Time.deltaTime;
            if (_refreshTimer < REFRESH_INTERVAL) return;
            _refreshTimer = 0f;
            RefreshStats();
        }

        // ── Построение UI ─────────────────────────────────────────

        private void BuildUI()
        {
            var canvasGo = new GameObject("NotificationCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 55; // ниже PlanetUI (50 → а нет, PlanetUI=50, HUD=60) → между ними
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = new GameObject("NotificationRoot");
            _root.transform.SetParent(canvasGo.transform, false);
            var rt = _root.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot     = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, PANEL_H);
            rt.anchoredPosition = Vector2.zero;
            _root.AddComponent<Image>().color = COL_BG;

            // ── Левая часть: контейнер иконок с HorizontalLayoutGroup ──
            var iconsGo = new GameObject("Icons");
            iconsGo.transform.SetParent(_root.transform, false);
            var iRt = iconsGo.AddComponent<RectTransform>();
            iRt.anchorMin = new Vector2(0f, 0f); iRt.anchorMax = new Vector2(0.55f, 1f);
            iRt.offsetMin = new Vector2(SIDE_PAD, 4f); iRt.offsetMax = new Vector2(0f, -4f);
            var hlg = iconsGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = ICON_GAP;
            hlg.childControlWidth = false; hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
            hlg.padding = new RectOffset(0, 0, 0, 0);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            _iconsContent = iRt;

            // ── Правая часть: горизонтальный ряд корабельных характеристик ──
            var statsGo = new GameObject("Stats");
            statsGo.transform.SetParent(_root.transform, false);
            var sRt = statsGo.AddComponent<RectTransform>();
            sRt.anchorMin = new Vector2(0.35f, 0f); sRt.anchorMax = new Vector2(1f, 1f);
            sRt.offsetMin = new Vector2(0f, 4f); sRt.offsetMax = new Vector2(-SIDE_PAD, -4f);
            var statsLayout = statsGo.AddComponent<HorizontalLayoutGroup>();
            statsLayout.spacing = STATS_GAP;
            statsLayout.childControlWidth = true; statsLayout.childControlHeight = true;
            statsLayout.childForceExpandWidth = false; statsLayout.childForceExpandHeight = true;
            statsLayout.childAlignment = TextAnchor.MiddleRight;
            statsLayout.padding = new RectOffset(0, 4, 0, 0);

            _moneyLabel = AddStatLabel(statsGo.transform, "Money",  "Кредиты: —");
            _freeLabel  = AddStatLabel(statsGo.transform, "Free",   "Свободно: —");
            _dateLabel  = AddStatLabel(statsGo.transform, "Date",   "Дата: —");
            _hpLabel    = AddStatLabel(statsGo.transform, "Hp",     "HP: —");
            _equipLabel = AddStatLabel(statsGo.transform, "Equip",  "Оборуд.: —");

            BuildTooltip(canvasGo);
        }

        private Text AddStatLabel(Transform parent, string name, string init)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var t = go.AddComponent<Text>();
            t.font = _font; t.fontSize = 13; t.color = COL_TXT_LABEL;
            t.alignment = TextAnchor.MiddleLeft; t.text = init;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = PANEL_H - 8f; le.preferredWidth = 150f;
            return t;
        }

        private void BuildTooltip(GameObject canvasRoot)
        {
            _tooltipGo = new GameObject("NewsTooltip");
            _tooltipGo.transform.SetParent(canvasRoot.transform, false);
            _tooltipRt = _tooltipGo.AddComponent<RectTransform>();
            _tooltipRt.anchorMin = _tooltipRt.anchorMax = new Vector2(0f, 0f);
            _tooltipRt.pivot     = new Vector2(0f, 0f);
            _tooltipRt.sizeDelta = new Vector2(420f, 100f);
            var img = _tooltipGo.AddComponent<Image>();
            img.color = new Color(0.05f, 0.07f, 0.12f, 0.96f);
            var border = _tooltipGo.AddComponent<Outline>();
            border.effectColor = new Color(0.36f, 0.68f, 1.00f, 0.35f);
            border.effectDistance = new Vector2(1f, 1f);

            var txtGo = new GameObject("Text");
            txtGo.transform.SetParent(_tooltipGo.transform, false);
            var tRt = txtGo.AddComponent<RectTransform>();
            tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
            tRt.offsetMin = new Vector2(8f, 6f); tRt.offsetMax = new Vector2(-8f, -6f);
            _tooltipText = txtGo.AddComponent<Text>();
            _tooltipText.font = _font; _tooltipText.fontSize = 12;
            _tooltipText.color = new Color(0.88f, 0.94f, 1f, 1f);
            _tooltipText.alignment = TextAnchor.UpperLeft;
            _tooltipText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _tooltipText.verticalOverflow = VerticalWrapMode.Overflow;
            _tooltipText.supportRichText = true;

            _tooltipGo.SetActive(false);
        }

        // ── Обработка ленты ────────────────────────────────────────

        private void OnNewsAdded(GalaxyNewsEntry e)   => RebuildIcons();
        private void OnNewsRemoved(GalaxyNewsEntry e) => RebuildIcons();

        private void RebuildIcons()
        {
            if (_iconsContent == null) return;
            for (int i = _iconsContent.childCount - 1; i >= 0; i--)
                Destroy(_iconsContent.GetChild(i).gameObject);

            var entries = GalaxyNewsService.Entries;
            if (entries == null || entries.Count == 0) return;

            // На панель попадают только Player/Galaxy/Quest. Планетарные события (Planet.*, смена
            // строя, локальные экономические события) не постятся сюда — они шумные и остаются
            // только в полном инфоцентре.
            var relevant = new List<GalaxyNewsEntry>();
            for (int i = 0; i < entries.Count; i++)
            {
                var kind = GalaxyNewsService.ClassifyKind(entries[i].Category);
                if (kind == NewsKind.Planet) continue;
                relevant.Add(entries[i]);
            }
            // Свежие справа (ближе к статам). Берём последние MAX_VISIBLE_ICONS.
            int start = Mathf.Max(0, relevant.Count - MAX_VISIBLE_ICONS);
            for (int i = start; i < relevant.Count; i++)
                BuildIcon(relevant[i]);
        }

        private void BuildIcon(GalaxyNewsEntry entry)
        {
            var kind = GalaxyNewsService.ClassifyKind(entry.Category);
            Color frame = kind switch
            {
                NewsKind.Quest  => COL_QUEST,
                NewsKind.Player => COL_PLAYER,
                _               => COL_GALAXY,
            };
            string letter = kind switch
            {
                NewsKind.Quest  => "!",
                NewsKind.Player => "★",
                _               => "◆",
            };

            var go = new GameObject($"Icon_{entry.Id}");
            go.transform.SetParent(_iconsContent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(ICON_SIZE, ICON_SIZE);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = ICON_SIZE; le.preferredHeight = ICON_SIZE;

            // Внешний frame — цветной, внутри тёмная плашка с буквой.
            var frameImg = go.AddComponent<Image>();
            frameImg.color = frame;
            frameImg.raycastTarget = true;

            var innerGo = new GameObject("Inner");
            innerGo.transform.SetParent(go.transform, false);
            var iRt = innerGo.AddComponent<RectTransform>();
            iRt.anchorMin = Vector2.zero; iRt.anchorMax = Vector2.one;
            iRt.offsetMin = new Vector2(2f, 2f); iRt.offsetMax = new Vector2(-2f, -2f);
            innerGo.AddComponent<Image>().color = COL_ICON_BG;

            var lblGo = new GameObject("Label");
            lblGo.transform.SetParent(innerGo.transform, false);
            var lRt = lblGo.AddComponent<RectTransform>();
            lRt.anchorMin = Vector2.zero; lRt.anchorMax = Vector2.one; lRt.offsetMin = lRt.offsetMax = Vector2.zero;
            var lbl = lblGo.AddComponent<Text>();
            lbl.font = _font; lbl.fontSize = 16; lbl.fontStyle = FontStyle.Bold;
            lbl.color = frame; lbl.alignment = TextAnchor.MiddleCenter;
            lbl.text = letter;

            // Обработчик hover/click — на root иконки.
            var handler = go.AddComponent<NotificationIconHandler>();
            handler.Bind(this, entry.Id);
        }

        // ── Tooltip / click ────────────────────────────────────────

        internal void ShowTooltipFor(int newsId, Vector3 anchorScreenPos)
        {
            var entries = GalaxyNewsService.Entries;
            if (entries == null) return;
            GalaxyNewsEntry found = null;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Id == newsId) { found = entries[i]; break; }
            if (found == null) { HideTooltip(); return; }

            string catDisplay = SRG.Galaxy.Politics.NewsTexts.Category(found.Category);
            _tooltipText.text = $"<b>{found.Date}</b>   <color=#8fbfe6>[{catDisplay}]</color>\n{found.Text}";

            // Положение: чуть выше иконки. anchorScreenPos — центр иконки в screen space.
            // Панель прибита к низу; тултип показываем над панелью с небольшим отступом.
            float tw = _tooltipRt.sizeDelta.x;
            float th = _tooltipRt.sizeDelta.y;
            float x = Mathf.Clamp(anchorScreenPos.x - tw * 0.5f, 4f, Screen.width - tw - 4f);
            float y = PANEL_H + 6f;
            _tooltipRt.anchoredPosition = new Vector2(x, y);
            _tooltipGo.SetActive(true);
        }

        internal void HideTooltip()
        {
            if (_tooltipGo != null) _tooltipGo.SetActive(false);
        }

        internal void DismissNews(int newsId)
        {
            HideTooltip();
            GalaxyNewsService.RemoveById(newsId);
        }

        internal void OpenFullFeed()
        {
            HideTooltip();
            InfoCenterOverlayController.Toggle();
        }

        // ── Обновление статов ─────────────────────────────────────

        private void RefreshStats()
        {
            var ship = PlayerShip.Instance?.ShipData;
            var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;

            if (ship == null)
            {
                if (_moneyLabel) _moneyLabel.text = "Кредиты: —";
                if (_freeLabel)  _freeLabel.text  = "Свободно: —";
                if (_hpLabel)    _hpLabel.text    = "HP: —";
                if (_equipLabel) _equipLabel.text = "Оборуд.: —";
            }
            else
            {
                if (_moneyLabel) _moneyLabel.text = $"Кредиты: {ship.Money:N0}";
                if (_freeLabel)  _freeLabel.text  = $"Свободно: {EquipmentSystem.GetFreeSpace(ship)} ед.";
                if (_hpLabel)
                {
                    _hpLabel.text = $"HP: {ship.CurrentHull}/{ship.MaxHull}";
                    float r = ship.MaxHull > 0 ? (float)ship.CurrentHull / ship.MaxHull : 1f;
                    _hpLabel.color = r > 0.50f ? new Color(0.35f, 0.90f, 0.35f)
                                  : r > 0.25f ? new Color(1.00f, 0.80f, 0.20f)
                                  :             new Color(1.00f, 0.28f, 0.18f);
                }
                if (_equipLabel)
                {
                    float cond = CalcEquipCondition(ship);
                    _equipLabel.text  = $"Оборуд.: {cond:F0}%";
                    _equipLabel.color = cond > 70f ? new Color(0.35f, 0.90f, 0.35f)
                                      : cond > 40f ? new Color(1.00f, 0.80f, 0.20f)
                                      :              new Color(1.00f, 0.28f, 0.18f);
                }
            }

            if (_dateLabel)
            {
                if (galaxy != null) _dateLabel.text = $"Дата: {galaxy.GetCurrentDate()} (ход {galaxy.CurrentTurn:N0})";
                else _dateLabel.text = "Дата: —";
            }
        }

        private static float CalcEquipCondition(ShipData ship)
        {
            int dur = 0, maxDur = 0;
            foreach (var kv in ship.Equipment.Slots)
            {
                if (EquipmentSystem.SlotCategory(kv.Key) == EquipmentCategory.Hull) continue;
                if (kv.Value == null) continue;
                if (!ship.AllItems.TryGetValue(kv.Value, out var item)) continue;
                if (item.MaxDurability <= 0) continue;
                dur    += item.Durability;
                maxDur += item.MaxDurability;
            }
            return maxDur > 0 ? (float)dur / maxDur * 100f : 100f;
        }
    }

    /// <summary>Обработчик hover/pointer-событий на одной иконке уведомления. Отдельный класс,
    /// потому что <see cref="IPointerEnterHandler"/> и т.п. требуются на GameObject с EventTrigger-ом.</summary>
    internal class NotificationIconHandler : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private NotificationPanelController _panel;
        private int _newsId;

        public void Bind(NotificationPanelController panel, int newsId)
        {
            _panel = panel; _newsId = newsId;
        }

        public void OnPointerEnter(PointerEventData ev)
        {
            if (_panel == null) return;
            var rt = transform as RectTransform;
            Vector3[] corners = new Vector3[4];
            rt.GetWorldCorners(corners); // BL, TL, TR, BR
            Vector3 center = (corners[0] + corners[2]) * 0.5f;
            _panel.ShowTooltipFor(_newsId, center);
        }

        public void OnPointerExit(PointerEventData ev)
        {
            _panel?.HideTooltip();
        }

        public void OnPointerClick(PointerEventData ev)
        {
            if (_panel == null) return;
            if (ev.button == PointerEventData.InputButton.Right)
                _panel.DismissNews(_newsId);
            else if (ev.button == PointerEventData.InputButton.Left)
                _panel.OpenFullFeed();
        }
    }
}
