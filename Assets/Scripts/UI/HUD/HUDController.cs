using UnityEngine;
using UnityEngine.UI;
using SRG.Core;
using SRG.Dialog;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Presentation;
using SRG.Controllers;
using SRG.Ships.Services;
using SRG.UI.Screens;
using SRG.Simulation;

namespace SRG.UI.HUD
{
    // HUD: кнопки быстрого доступа + отображение состояния корабля.
    // Разместите компонент на любом GameObject сцены (например, на Canvas).
    // Все элементы создаются программно.
    public class HUDController : MonoBehaviour
    {
        // Размер одной кнопки (квадрат)
        private const int BTN = 48;
        private const int GAP = 4;
        private const int COLS = 4;

        // Ссылки на динамические метки. Корабельные характеристики (money/free/hp/equip/date)
        // теперь строит NotificationPanelController — эти поля больше не используются.
        private Text _followLabel;

        // Кнопка оружия — меняет цвет при активном режиме
        private Image _weaponBtnBg;

        private Text _fpsLabel;
        private int _fpsFrameCount;
        private float _fpsAccum;

        private InventoryUIController _inventoryUI;
        private CameraController _camCtrl;

        private float _refreshTimer;
        private const float REFRESH_INTERVAL = 0.25f;
        private bool _lastWeaponModeActive = false;

        private static readonly Color COL_BG_PANEL  = new Color(0.04f, 0.06f, 0.10f, 0.85f);
        private static readonly Color COL_BTN_ACTIVE = new Color(0.10f, 0.16f, 0.28f, 0.92f);
        private static readonly Color COL_BTN_DIMMED = new Color(0.06f, 0.08f, 0.12f, 0.70f);
        private static readonly Color COL_TXT_ACTIVE = new Color(0.80f, 0.92f, 1.00f);
        private static readonly Color COL_TXT_DIMMED = new Color(0.30f, 0.36f, 0.46f);

        // ── Панель компаньонов (партнёры + дроны) ────────────────────────────────
        // Отрисовывается через OnGUI под миникартой (примерно 260px от верха).
        // Иконки 22×22 с цветной рамкой (HP + расположение), hover→тултип, click→диалог.
        private const int COMP_ICON_SIZE     = 22;
        private const int COMP_ICON_SPACING  = 4;
        private const int COMP_BAR_MARGIN_R  = 12;
        private const int COMP_BAR_TOP       = 260;
        private GUIStyle _compTooltipStyle;

        private void Start()
        {
            _inventoryUI = FindFirstObjectByType<InventoryUIController>();
            _camCtrl     = FindFirstObjectByType<CameraController>();
            BuildHUD();
        }

        private void Update()
        {

            // Ход считается в фоне — мир меняется, не читаем и не трогаем его (см. GameWorld.IsCalculating).

            if (GameWorld.IsCalculating) return;
            _fpsFrameCount++;
            _fpsAccum += Time.unscaledDeltaTime;

            _refreshTimer += Time.deltaTime;
            if (_refreshTimer >= REFRESH_INTERVAL)
            {
                _refreshTimer = 0f;
                RefreshData();
            }

            UpdateWeaponButton();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Построение UI

        private void BuildHUD()
        {
            var canvasGo = new GameObject("HUDCanvas");
            canvasGo.transform.SetParent(null, false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            // Выше PlanetUICanvas (50), чтобы HUD-кнопки (Карта, Корабль, Инфо)
            // оставались доступны поверх полноэкранного экрана планеты.
            canvas.sortingOrder = 60;

            canvasGo.AddComponent<CanvasScaler>().uiScaleMode =
                CanvasScaler.ScaleMode.ConstantPixelSize;
            canvasGo.AddComponent<GraphicRaycaster>();

            // Корабельные характеристики теперь живут в NotificationPanelController
            // (внизу экрана), поэтому старая DataPanel в HUD не строится.
            NotificationPanelController.EnsureCreated();
            BuildButtonPanel(canvasGo);
            BuildFpsLabel(canvasGo);
            BuildFollowLabel(canvasGo);
        }

        // Метка активного режима следования — верх по центру под FPS.
        private void BuildFollowLabel(GameObject root)
        {
            var go = new GameObject("FollowModeLabel");
            go.transform.SetParent(root.transform, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0.5f, 1f);
            rt.anchorMax        = new Vector2(0.5f, 1f);
            rt.pivot            = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -28f);
            rt.sizeDelta        = new Vector2(420f, 22f);

            var txt = go.AddComponent<Text>();
            txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize  = 14;
            txt.color     = new Color(1.00f, 0.78f, 0.25f, 0.95f);
            txt.alignment = TextAnchor.UpperCenter;
            txt.text      = "";

            _followLabel = txt;
        }

        // FPS — верх по центру
        private void BuildFpsLabel(GameObject root)
        {
            var go = new GameObject("FpsLabel");
            go.transform.SetParent(root.transform, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0.5f, 1f);
            rt.anchorMax        = new Vector2(0.5f, 1f);
            rt.pivot            = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -4f);
            rt.sizeDelta        = new Vector2(80f, 22f);

            var txt = go.AddComponent<Text>();
            txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize  = 14;
            txt.color     = new Color(0.85f, 1f, 0.85f, 0.9f);
            txt.alignment = TextAnchor.UpperCenter;
            txt.text      = "FPS: —";

            _fpsLabel = txt;
        }

        // Панель кнопок — правый нижний угол, над панелью уведомлений
        private void BuildButtonPanel(GameObject root)
        {
            // Описание кнопок: (название, действие, нерабочая)
            var defs = new (string label, System.Action action, bool dim)[]
            {
                ("Карта",      () => GalaxyMapController.Instance?.ToggleMap(), false),
                ("Корабль",    OpenInventory,   false),
                ("Оружие",     ToggleWeapon,    false),
                ("Центр",      CenterCamera,    false),
                ("Фильм",      null,            true),
                ("Инфо",       () => InfoCenterOverlayController.Toggle(), false),
                ("Рейтинг",    () => RangerRatingOverlayController.Toggle(), false),
                ("Разговор",   null,            true),
            };

            int rows     = Mathf.CeilToInt((float)defs.Length / COLS);
            int panelW   = COLS * BTN + (COLS - 1) * GAP + GAP * 2;
            int panelH   = rows * BTN + (rows - 1) * GAP + GAP * 2;

            var go = new GameObject("ButtonPanel");
            go.transform.SetParent(root.transform, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin        = new Vector2(1f, 0f);
            rt.anchorMax        = new Vector2(1f, 0f);
            rt.pivot            = new Vector2(1f, 0f);
            // 44 px — высота NotificationPanel (см. NotificationPanelController.PANEL_H):
            // ставим панель кнопок прямо над лентой уведомлений.
            rt.anchoredPosition = new Vector2(0f, 44f);
            rt.sizeDelta        = new Vector2(panelW, panelH);

            go.AddComponent<Image>().color = COL_BG_PANEL;

            for (int i = 0; i < defs.Length; i++)
            {
                int col = i % COLS;
                int row = i / COLS;
                float x = GAP + col * (BTN + GAP);
                float y = -(GAP + row * (BTN + GAP));

                CreateButton(go, defs[i].label, x, y, defs[i].action, defs[i].dim,
                             isWeaponBtn: defs[i].label == "Оружие");
            }
        }

        private void CreateButton(GameObject parent, string label, float x, float y,
                                   System.Action action, bool dim, bool isWeaponBtn = false)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent.transform, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0f, 1f);
            rt.anchorMax        = new Vector2(0f, 1f);
            rt.pivot            = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta        = new Vector2(BTN, BTN);

            var bg = go.AddComponent<Image>();
            bg.color = dim ? COL_BTN_DIMMED : COL_BTN_ACTIVE;

            if (!dim && action != null)
            {
                var btn = go.AddComponent<Button>();
                var captured = action;
                btn.onClick.AddListener(() => captured());
                btn.targetGraphic = bg;

                var cb = btn.colors;
                cb.normalColor      = Color.white;
                cb.highlightedColor = new Color(1.3f, 1.3f, 1.3f);
                cb.pressedColor     = new Color(0.75f, 0.75f, 0.75f);
                btn.colors          = cb;
            }

            if (isWeaponBtn)
                _weaponBtnBg = bg;

            // Текст кнопки
            var tgo = new GameObject("Text");
            tgo.transform.SetParent(go.transform, false);

            var trt = tgo.AddComponent<RectTransform>();
            trt.anchorMin        = Vector2.zero;
            trt.anchorMax        = Vector2.one;
            trt.sizeDelta        = Vector2.zero;
            trt.anchoredPosition = Vector2.zero;

            var txt = tgo.AddComponent<Text>();
            txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize  = 10;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color     = dim ? COL_TXT_DIMMED : COL_TXT_ACTIVE;
            txt.text      = label;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Действия кнопок

        private void OpenInventory()  => _inventoryUI?.Toggle();
        private void CenterCamera()   => _camCtrl?.StartCenterOnPlayer();
        private void ToggleWeapon()   => PlayerShip.Instance?.ToggleWeaponMode();

        // ─────────────────────────────────────────────────────────────────────────
        // Обновление данных

        private void RefreshData()
        {
            if (_fpsLabel != null && _fpsAccum > 0f)
            {
                _fpsLabel.text = $"FPS: {Mathf.RoundToInt(_fpsFrameCount / _fpsAccum)}";
                _fpsFrameCount = 0;
                _fpsAccum = 0f;
            }
            UpdateFollowLabel();
        }

        private void UpdateFollowLabel()
        {
            if (_followLabel == null) return;
            var ps = PlayerShip.Instance;

            // Стыковка с носителем/станцией: корабль игрока скрыт — показываем статус вместо
            // режима следования. Разделяем текст: у станций «На станции», у линкоров «На борту».
            // Взлёт идёт из посадочного UI (кнопка «Взлететь» в ангаре) — HUD-подсказку не показываем.
            if (ps != null && !string.IsNullOrEmpty(ps.ShipData?.LandedOnShipUid))
            {
                var star = GalaxyManager.Instance?.CurrentStar;
                var carrier = star?.FindShip(ps.ShipData.LandedOnShipUid);
                string prefix = carrier != null && carrier.IsStation ? "На станции" : "На борту";
                string name = carrier?.Name ?? (carrier != null && carrier.IsStation ? "станция" : "носитель");
                _followLabel.text = $"{prefix}: {name}";
                return;
            }

            if (ps == null || !ps.IsFollowingShip)
            {
                if (_followLabel.text.Length != 0) _followLabel.text = "";
                return;
            }
            string targetName = ps.GetFollowTargetName() ?? "цель";
            _followLabel.text = $"Следование: {targetName} — {PlayerShip.GetFollowModeName(ps.CurrentFollowMode)}";
        }

        private void UpdateWeaponButton()
        {
            if (_weaponBtnBg == null || PlayerShip.Instance == null) return;
            bool active = PlayerShip.Instance.IsWeaponModeActive;
            if (active == _lastWeaponModeActive) return;
            _lastWeaponModeActive = active;
            _weaponBtnBg.color = active
                ? new Color(0.55f, 0.12f, 0.08f, 0.95f)
                : COL_BTN_ACTIVE;
        }

        // ── Панель компаньонов ───────────────────────────────────────────────────

        private void OnGUI()
        {

            // Ход считается в фоне — мир меняется, не читаем и не трогаем его (см. GameWorld.IsCalculating).

            if (GameWorld.IsCalculating) return;
            var player = PlayerShip.Instance?.ShipData;
            if (player == null) return;
            if (player.PartnerFollowerUids == null || player.PartnerFollowerUids.Count == 0) return;
            if (GalaxyManager.Instance?.Phase == TurnPhase.Simulation) return;

            EnsureCompStyle();

            var followers = new System.Collections.Generic.List<ShipData>();
            foreach (var uid in player.PartnerFollowerUids)
            {
                var s = PartnerService.FindShipInGalaxy(uid);
                if (s == null || s.CurrentHull <= 0) continue;
                followers.Add(s);
            }
            if (followers.Count == 0) return;

            Vector2 mouse = new(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            ShipData hovered = null;

            int totalWidth = followers.Count * COMP_ICON_SIZE + (followers.Count - 1) * COMP_ICON_SPACING;
            int x = Screen.width - COMP_BAR_MARGIN_R - totalWidth;
            int y = COMP_BAR_TOP;

            for (int i = 0; i < followers.Count; i++)
            {
                var f = followers[i];
                var rect = new Rect(x + i * (COMP_ICON_SIZE + COMP_ICON_SPACING), y, COMP_ICON_SIZE, COMP_ICON_SIZE);
                DrawCompanionIcon(rect, f, player);

                if (rect.Contains(mouse))
                {
                    hovered = f;
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                    {
                        OpenCompanionDialog(f);
                        Event.current.Use();
                    }
                }
            }

            if (hovered != null) DrawCompanionTooltip(hovered, mouse);
        }

        private void EnsureCompStyle()
        {
            if (_compTooltipStyle != null) return;
            _compTooltipStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 11,
                richText = true,
                wordWrap = false,
                padding = new RectOffset(6, 6, 4, 4),
            };
            _compTooltipStyle.normal.textColor = new Color(0.88f, 0.92f, 0.98f, 1f);
        }

        private static void DrawCompanionIcon(Rect rect, ShipData follower, ShipData player)
        {
            float hp = follower.MaxHull > 0 ? (float)follower.CurrentHull / follower.MaxHull : 1f;
            bool sameStar = follower.CurrentStarUid == player.CurrentStarUid;
            Color frame = hp < 0.33f ? new Color(0.85f, 0.20f, 0.20f, 1f) :
                          (hp < 0.66f || !sameStar) ? new Color(0.90f, 0.75f, 0.20f, 1f) :
                          new Color(0.35f, 0.80f, 0.40f, 1f);

            var prev = GUI.color;
            GUI.color = frame;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(0.05f, 0.09f, 0.14f, 1f);
            GUI.DrawTexture(new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4), Texture2D.whiteTexture);
            GUI.color = prev;

            string letter = string.IsNullOrEmpty(follower.Name) ? "?" : follower.Name.Substring(0, 1);
            GUI.Label(new Rect(rect.x, rect.y + 3, rect.width, rect.height), letter, new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter, fontSize = 12,
                normal = { textColor = new Color(0.85f, 0.9f, 0.95f, 1f) }
            });
        }

        private void DrawCompanionTooltip(ShipData f, Vector2 mouse)
        {
            var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
            string starName = "?";
            if (galaxy?.StarsMap != null && galaxy.StarsMap.TryGetValue(f.CurrentStarUid ?? "", out var s))
                starName = s.Name ?? "?";
            float hpPct = f.MaxHull > 0 ? (float)f.CurrentHull / f.MaxHull * 100f : 0f;
            var fuelTank = EquipmentSystem.GetEquipped(f, SlotKeys.FuelTank);
            int fuel = fuelTank?.CurrentFuel ?? 0;

            string text = $"<b>{f.Name ?? f.ShipTypeId}</b>\n" +
                          $"Тип: {f.ShipTypeId}\n" +
                          $"Звезда: {starName}\n" +
                          $"Корпус: {f.CurrentHull}/{f.MaxHull} ({hpPct:F0}%)\n" +
                          $"Топливо: {fuel}\n" +
                          $"Приказ: {f.PartnerOrder}";

            var size = _compTooltipStyle.CalcSize(new GUIContent(text));
            var r = new Rect(mouse.x + 14, mouse.y + 14, Mathf.Min(size.x + 12, 260), size.y + 4);
            if (r.xMax > Screen.width)  r.x = Screen.width - r.width - 4;
            if (r.yMax > Screen.height) r.y = Screen.height - r.height - 4;
            GUI.Box(r, text, _compTooltipStyle);
        }

        private static void OpenCompanionDialog(ShipData follower)
        {
            var ui = DialogUIController.Instance;
            if (ui == null || follower == null || follower.CurrentHull <= 0) return;
            string id = SRG.Dialog.DialogService.ResolveShipDialogId(follower);
            if (!string.IsNullOrEmpty(id)) ui.OpenSpaceDialog(id, follower);
        }
    }
}
