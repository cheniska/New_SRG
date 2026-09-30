using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SRG.Combat;
using SRG.Config;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Presentation.Common;
using SRG.Ships;
using SRG.Controllers;
using SRG.UI.Common;
using SRG.UI.HUD;
using SRG.Utils;

namespace SRG.UI.Screens
{
    /// <summary>
    /// Визуальная "форма корабля": фон + слоты под оборудование с состояниями
    /// (Usual/MouseEntered/Blocked/Broken), поверх — иконки оборудования.
    /// В слотах — анимированные спрайты (_a), в трюме и "в руке" — статичные (_i).
    /// При наведении показывается всплывающее окно с информацией (включая цену).
    /// Клик ЛКМ — взять в руку / поставить из руки. ПКМ или Esc — отменить руку.
    ///
    /// Координаты слотов заданы в абсолютных пикселях для 1920x1080.
    /// Конверсия abs → local: X_local = X_abs - 960; Y_local = 540 - Y_abs.
    /// </summary>
    public class ShipFormView : MonoBehaviour
    {
        private const float RefWidth = 1920f;
        private const float RefHeight = 1080f;
        private const float FormImageWidth = 700f;
        private const float FormImageHeight = 900f;
        private const float SlotSize = 96f;
        private const float ArtSlotSize = 96f;
        private const float HullIconSize = 220f;
        private const float CargoCellSize = 80f;
        private const int CargoColumns = 4;
        private const int CargoRows = 6;

        private static string ResourceRoot => GalaxyConstants.PATH_SHIP_FORM_RES_ROOT;

        // Координаты слотов: задаются КОДОМ (значения по умолчанию) и опционально перекрываются
        // из Resources/Config/ShipFormSlots.json секции "Layout" (см. B4 рефакторинга).
        // Все позиции в абсолютных пикселях для 1920×1080; конверсия в local-coords — через AbsToLocal.
        // НЕ помечены readonly — LoadLayoutFromConfig перезаписывает при загрузке.
        private static (string slotKey, Vector2 pos)[] FixedSlotLayout =
        {
            (SlotKeys.Hull,     new Vector2(960, 540)),
            (SlotKeys.Forsage,  new Vector2(660, 450)),
            (SlotKeys.Engine,   new Vector2(760, 450)),
            (SlotKeys.FuelTank, new Vector2(760, 600)),
            (SlotKeys.Radar,    new Vector2(1160, 450)),
            (SlotKeys.Scanner,  new Vector2(1160, 600)),
            ("CargoGrabber_0",  new Vector2(960, 760)),
            (SlotKeys.Shield,   new Vector2(1100, 760)),
            ("Droid_0",         new Vector2(820, 760)),
        };

        /// <summary>Позиция слота активации справа от радара. Появляется только когда в руке
        /// предмет с Activatable=true.</summary>
        private static Vector2 ActivationSlotPos = new Vector2(1260, 450);

        private static Vector2[] WeaponPositions =
        {
            new Vector2(960, 300),
            new Vector2(1100, 300),
            new Vector2(820, 300),
            new Vector2(1030, 200),
            new Vector2(890, 200),
        };

        private static Vector2[] ArtefactPositions =
        {
            new Vector2(700, 340),
            new Vector2(700, 740),
            new Vector2(1220, 340),
            new Vector2(1220, 740),
        };

        private static Vector2 CargoFirstCell = new Vector2(1350, 225);

        /// <summary>Позиция «псевдо-слота» Throw_0 в абсолютных координатах 1920×1080:
        /// левый нижний угол экрана. Графика — обычная (SlotUsual).</summary>
        private static Vector2 ThrowSlotPos = new Vector2(80, 620);

        public bool AllowInteraction = true;

        /// <summary>
        /// Если задан — режим «грабежа во время абордажа»: левый клик по слоту или ячейке трюма
        /// сканируемого корабля сразу переносит предмет в инвентарь указанного получателя
        /// (без подъёма «в руку»). Используется ShipScanUIController, когда сканируемый корабль
        /// абордирован игроком.
        /// </summary>
        public ShipData BoardingLootRecipient;

        /// <summary>Колбэк после любой манипуляции с оборудованием — для живого обновления статистики.</summary>
        public System.Action OnEquipmentChanged;

        private RectTransform _root;
        private RectTransform _popupRoot;
        private Text _popupText;
        private RectTransform _handRoot;
        private Image _handIcon;
        private Image _handEmbedBadge;
        private ShipData _ship;
        private readonly List<SlotWidget> _slots = new();
        private PilotPanelController _pilotPanel;

        /// <summary>Абсолютная позиция (1920×1080) центра панели пилота.
        /// Слева от инвентаря на форме корабля. Перекрывается через Config/ShipFormSlots.json
        /// → Layout.PilotPanelPos.</summary>
        private static Vector2 PilotPanelPos = new Vector2(220, 540);

        // Сессионная карта ячеек трюма: индекс ячейки → предмет. Позволяет ставить
        // предмет в КОНКРЕТНУЮ ячейку, а не в "первую свободную". Перестраивается
        // при SetShip из ship.Inventory.Items последовательно.
        private readonly Dictionary<int, ItemInstance> _cargoSlotMap = new();
        /// <summary>Смещение прокрутки трюма (в строках). 0 = самый верх. Изменяется колёсиком
        /// мыши над областью трюма и клавишами PgUp/PgDown. Клампится в <see cref="ClampCargoScroll"/>
        /// после любых изменений в инвентаре, чтобы не показать пустое окно.</summary>
        private int _cargoScrollRows;

        private HandState _hand;
        private SlotWidget _activateSlot;

        // ── Диалог количества при выбрасывании стака ─────────────────────────────
        // Появляется при drop стек-предмета (uid "stack:<goodId>") на слот Throw_0:
        // ползунок 1..вес стека, «Выбросить» кидает выбранную часть, остаток
        // возвращается в исходную ячейку трюма.
        private AmountSliderDialog _throwDialog;

        private static Sprite _formSprite;
        private static SlotTextureSet _defaultRegular;
        private static SlotTextureSet _defaultArt;
        private static Dictionary<string, SlotTextureSet> _slotOverrides = new();
        private static bool _spritesLoaded;

        public RectTransform Root => _root;

        // ── Публичный API ─────────────────────────────────────────────────────────

        public void Build(RectTransform parent)
        {
            EnsureSpritesLoaded();

            var go = new GameObject("ShipFormView");
            go.transform.SetParent(parent, false);
            _root = go.AddComponent<RectTransform>();
            _root.anchorMin = new Vector2(0.5f, 0.5f);
            _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0.5f);
            _root.sizeDelta = new Vector2(RefWidth, RefHeight);
            _root.anchoredPosition = Vector2.zero;

            BuildBackground();
            // Панель пилота — до слотов: её фон (FormPilot.png, 404×1080) занимает всю левую
            // полосу экрана и иначе закрывает псевдо-слот Throw_0 (80, 620).
            BuildPilotPanel();
            BuildAllSlots();
            BuildHandLayer();
            BuildPopup();
            _throwDialog = AmountSliderDialog.Create(_root);
        }

        public void SetShip(ShipData ship)
        {
            _ship = ship;
            CancelHand();
            RebuildCargoMap();
            Refresh();
            _pilotPanel?.SetShip(ship);
        }

        private void RebuildCargoMap()
        {
            _cargoSlotMap.Clear();
            if (_ship?.Inventory?.Items == null) return;
            ClampCargoScroll();
            int cap = CargoColumns * CargoRows;
            var items = _ship.Inventory.Items;
            int start = _cargoScrollRows * CargoColumns;
            for (int i = 0; i < cap && start + i < items.Count; i++)
                if (items[start + i] != null)
                    _cargoSlotMap[i] = items[start + i];
        }

        /// <summary>Максимальное значение <see cref="_cargoScrollRows"/>: число строк, которые
        /// нужны, чтобы показать все предметы, минус видимая область. Клампится в [0..max].</summary>
        private void ClampCargoScroll()
        {
            int total = _ship?.Inventory?.Items?.Count ?? 0;
            int totalRows = Mathf.CeilToInt(total / (float)CargoColumns);
            int maxScroll = Mathf.Max(0, totalRows - CargoRows);
            _cargoScrollRows = Mathf.Clamp(_cargoScrollRows, 0, maxScroll);
        }

        /// <summary>true, если экранная точка находится над областью трюма (для колёсика мыши).
        /// Границы — от <see cref="CargoFirstCell"/> до конца сетки CargoColumns × CargoRows.</summary>
        private bool IsMouseOverCargo(Vector2 screenPos)
        {
            if (_root == null) return false;
            Vector2 local = ScreenToRootLocal(screenPos);
            Vector2 min = AbsToLocal(CargoFirstCell);
            float w = CargoColumns * CargoCellSize;
            float h = CargoRows * CargoCellSize;
            // AbsToLocal кладёт верх-лево ячейки в min; сетка растёт вправо и вниз (Y уменьшается).
            return local.x >= min.x - CargoCellSize * 0.5f
                && local.x <= min.x - CargoCellSize * 0.5f + w
                && local.y <= min.y + CargoCellSize * 0.5f
                && local.y >= min.y + CargoCellSize * 0.5f - h;
        }

        /// <summary>Находит ячейку трюма, в которой лежит данный предмет, либо -1.</summary>
        private int FindCargoIndex(ItemInstance item)
        {
            foreach (var kv in _cargoSlotMap)
                if (kv.Value == item) return kv.Key;
            return -1;
        }

        /// <summary>Возвращает предмет из "руки" в исходный слот/трюм. Без перерисовки.</summary>
        public void DropHandToSource()
        {
            CancelHand();
        }

        public void Refresh()
        {
            if (_root == null) return;
            // Если после изменения инвентаря скролл ушёл за конец списка (удалили строки) —
            // подтянуть к последней доступной странице. RebuildCargoMap НЕ вызываем: пользователь
            // мог вручную разложить предметы по ячейкам (drag&drop), это состояние в _cargoSlotMap
            // сохраняется до явного скролла или SetShip.
            ClampCargoScroll();
            foreach (var slot in _slots)
                UpdateSlot(slot);
            RefreshCargo();
            HidePopup();
            _pilotPanel?.Refresh();
        }

        private void BuildPilotPanel()
        {
            var go = new GameObject("PilotPanelHost");
            go.transform.SetParent(_root, false);
            _pilotPanel = go.AddComponent<PilotPanelController>();
            _pilotPanel.Build(_root, PilotPanelPos);
        }

        // ── Постройка ─────────────────────────────────────────────────────────────

        private void BuildBackground()
        {
            var go = new GameObject("FormBackground");
            go.transform.SetParent(_root, false);
            var rt = go.AddComponent<RectTransform>();
            // Форма всегда отрисовывается в натуральном размере 700x900, без скейла,
            // строго по центру экрана (нативная привязка к 1920x1080 хост-канвасу).
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(FormImageWidth, FormImageHeight);
            rt.anchoredPosition = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.sprite = _formSprite;
            img.preserveAspect = false;
            img.raycastTarget = false;
        }

        private void BuildAllSlots()
        {
            foreach (var (slotKey, pos) in FixedSlotLayout)
            {
                float size = slotKey == SlotKeys.Hull ? HullIconSize : SlotSize;
                bool hideBg = slotKey == SlotKeys.Hull;
                _slots.Add(BuildSlotWidget(slotKey, pos, size, hideBg, isArtefactStyle: false, isCargo: false));
            }

            for (int i = 0; i < WeaponPositions.Length; i++)
                _slots.Add(BuildSlotWidget($"Weapons_{i}", WeaponPositions[i], SlotSize,
                                           hideBg: false, isArtefactStyle: false, isCargo: false));

            for (int i = 0; i < ArtefactPositions.Length; i++)
                _slots.Add(BuildSlotWidget($"Artefacts_{i}", ArtefactPositions[i], ArtSlotSize,
                                           hideBg: false, isArtefactStyle: true, isCargo: false));

            for (int i = 0; i < CargoColumns * CargoRows; i++)
            {
                int col = i % CargoColumns;
                int row = i / CargoColumns;
                Vector2 abs = new Vector2(
                    CargoFirstCell.x + col * CargoCellSize,
                    CargoFirstCell.y + row * CargoCellSize);
                var w = BuildSlotWidget($"Cargo_{i}", abs, CargoCellSize,
                                        hideBg: false, isArtefactStyle: true, isCargo: true);
                w.CargoIndex = i;
                _slots.Add(w);
            }

            // Псевдо-слот «выбросить»: drop любого предмета спавнит контейнер в космосе.
            _slots.Add(BuildSlotWidget(SlotKeys.Throw, ThrowSlotPos, SlotSize,
                                       hideBg: false, isArtefactStyle: false, isCargo: false));

            // Псевдо-слот «активировать»: появляется только когда в руке активируемый предмет.
            _activateSlot = BuildSlotWidget(SlotKeys.Activate, ActivationSlotPos, SlotSize,
                                            hideBg: false, isArtefactStyle: false, isCargo: false);
            _slots.Add(_activateSlot);
            _activateSlot.Root.gameObject.SetActive(false);
        }

        private SlotWidget BuildSlotWidget(string slotKey, Vector2 absPos, float size,
                                          bool hideBg, bool isArtefactStyle, bool isCargo)
        {
            // Размер слота (если не корпус) берётся из конфига текстур по slotKey.
            var texSet = GetSlotTextureSet(slotKey, isArtefactStyle);
            float w = size, h = size;
            if (!hideBg && texSet != null && texSet.Width > 0 && texSet.Height > 0)
            {
                w = texSet.Width;
                h = texSet.Height;
            }

            var go = new GameObject($"Slot_{slotKey}");
            go.transform.SetParent(_root, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = AbsToLocal(absPos);

            Image bg = null;
            Image rcFallback = null;
            if (!hideBg)
            {
                var bgGo = new GameObject("Bg");
                bgGo.transform.SetParent(go.transform, false);
                var bgRt = bgGo.AddComponent<RectTransform>();
                bgRt.anchorMin = Vector2.zero;
                bgRt.anchorMax = Vector2.one;
                bgRt.offsetMin = Vector2.zero;
                bgRt.offsetMax = Vector2.zero;
                bg = bgGo.AddComponent<Image>();
                bg.preserveAspect = true;
                bg.raycastTarget = true;
            }
            else
            {
                var rcGo = new GameObject("RaycastTarget");
                rcGo.transform.SetParent(go.transform, false);
                var rcRt = rcGo.AddComponent<RectTransform>();
                rcRt.anchorMin = Vector2.zero; rcRt.anchorMax = Vector2.one;
                rcRt.offsetMin = Vector2.zero; rcRt.offsetMax = Vector2.zero;
                rcFallback = rcGo.AddComponent<Image>();
                rcFallback.color = new Color(1f, 1f, 1f, 0f);
                rcFallback.raycastTarget = true;
            }

            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(go.transform, false);
            var iconRt = iconGo.AddComponent<RectTransform>();
            float iconScale = hideBg ? 1f : 0.82f;
            iconRt.anchorMin = new Vector2(0.5f, 0.5f);
            iconRt.anchorMax = new Vector2(0.5f, 0.5f);
            iconRt.pivot = new Vector2(0.5f, 0.5f);
            iconRt.sizeDelta = new Vector2(size * iconScale, size * iconScale);
            iconRt.anchoredPosition = Vector2.zero;
            var icon = iconGo.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = false;
            var animator = iconGo.AddComponent<SlotIconAnimator>();
            animator.Target = icon;

            var badge = BuildEmbedBadge(go.transform, size);

            var widget = new SlotWidget
            {
                SlotKey = slotKey,
                IsArtefactStyle = isArtefactStyle,
                IsCargo = isCargo,
                Bg = bg,
                RaycastFallback = rcFallback,
                Icon = icon,
                EmbedBadge = badge,
                IconAnimator = animator,
                Root = rt,
                TextureSet = texSet
            };

            var hover = go.AddComponent<SlotInputHandler>();
            hover.Owner = this;
            hover.Widget = widget;

            return widget;
        }

        /// <summary>Мини-иконка в правом нижнем углу слота — визуальный маркер вставленного
        /// в предмет-носитель встраиваемого (микромодуль/встраиваемый артефакт). Виден во всех
        /// состояниях слота (обычное/hover/broken), поверх основной иконки.</summary>
        private static Image BuildEmbedBadge(Transform parent, float slotSize)
        {
            var badgeGo = new GameObject("EmbedBadge");
            badgeGo.transform.SetParent(parent, false);
            var rt = badgeGo.AddComponent<RectTransform>();
            float badgeSize = Mathf.Max(16f, slotSize * 0.36f);
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(badgeSize, badgeSize);
            rt.anchoredPosition = new Vector2(-2f, 2f);
            var img = badgeGo.AddComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.enabled = false;
            return img;
        }

        /// <summary>Обновляет мини-иконку встроенного модуля: показывает первый встроенный
        /// (item.Embeds[0]) если есть, иначе прячет. Значок отображается во всех состояниях
        /// слота — не гасится, когда основная иконка скрыта.</summary>
        private static void RefreshEmbedBadge(Image badge, ItemInstance item)
        {
            if (badge == null) return;
            if (item == null || item.Embeds == null || item.Embeds.Count == 0
                || item.EmbedItems == null)
            {
                badge.enabled = false;
                badge.sprite = null;
                return;
            }
            ItemInstance embed = null;
            foreach (var uid in item.Embeds)
            {
                if (item.EmbedItems.TryGetValue(uid, out embed) && embed != null) break;
            }
            var sprite = embed != null ? LoadItemStaticSprite(embed) : null;
            if (sprite == null)
            {
                badge.enabled = false;
                badge.sprite = null;
                return;
            }
            badge.sprite = sprite;
            badge.color = Color.white;
            badge.enabled = true;
            badge.transform.SetAsLastSibling();
        }

        private void BuildHandLayer()
        {
            var go = new GameObject("HandLayer");
            go.transform.SetParent(_root, false);
            _handRoot = go.AddComponent<RectTransform>();
            _handRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _handRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _handRoot.pivot = new Vector2(0.5f, 0.5f);
            _handRoot.sizeDelta = new Vector2(SlotSize, SlotSize);

            _handIcon = go.AddComponent<Image>();
            _handIcon.preserveAspect = true;
            _handIcon.raycastTarget = false;
            _handIcon.color = new Color(1f, 1f, 1f, 0.9f);
            _handEmbedBadge = BuildEmbedBadge(go.transform, SlotSize);
            go.SetActive(false);
        }

        private void BuildPopup()
        {
            var go = new GameObject("ItemInfoPopup");
            go.transform.SetParent(_root, false);
            _popupRoot = go.AddComponent<RectTransform>();
            // Anchor по центру _root, чтобы anchoredPosition напрямую
            // совпадал с локальной системой координат (origin = pivot центра _root).
            _popupRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _popupRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _popupRoot.pivot = new Vector2(0f, 1f);
            _popupRoot.sizeDelta = new Vector2(340f, 200f);

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.06f, 0.10f, 0.96f);
            bg.raycastTarget = false;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10f, 10f);
            trt.offsetMax = new Vector2(-10f, -10f);

            _popupText = textGo.AddComponent<Text>();
            _popupText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _popupText.fontSize = 14;
            _popupText.color = new Color(0.92f, 0.92f, 0.95f);
            _popupText.alignment = TextAnchor.UpperLeft;
            _popupText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _popupText.verticalOverflow = VerticalWrapMode.Overflow;
            _popupText.raycastTarget = false;

            go.SetActive(false);
        }

        // ── Состояние слотов ──────────────────────────────────────────────────────

        private void UpdateSlot(SlotWidget slot)
        {
            slot.Item = null;
            slot.IconAnimator?.Configure(null);
            slot.IsBlocked = false;
            RefreshEmbedBadge(slot.EmbedBadge, null);

            if (slot.IsCargo)
            {
                SetSlotRaycastable(slot, true);
                return;
            }

            // Псевдо-слот «выбросить»: всегда активен, ничего не «хранит».
            if (slot.SlotKey == SlotKeys.Throw)
            {
                ApplyBg(slot, SlotVisualState.Usual);
                slot.Icon.enabled = false;
                SetSlotRaycastable(slot, true);
                return;
            }

            // Псевдо-слот «активировать»: видимостью управляет рука (см. UpdateActivateSlotVisibility).
            // Здесь только сбрасываем визуал — раскастуем всегда, чтобы можно было кликнуть.
            if (slot.SlotKey == SlotKeys.Activate)
            {
                ApplyBg(slot, SlotVisualState.Green);
                slot.Icon.enabled = false;
                SetSlotRaycastable(slot, true);
                return;
            }

            if (_ship?.Equipment == null)
            {
                slot.IsBlocked = true;
                ApplyBg(slot, SlotVisualState.Blocked);
                slot.Icon.enabled = false;
                SetSlotRaycastable(slot, false);
                return;
            }

            bool slotExists = _ship.Equipment.Slots.ContainsKey(slot.SlotKey);
            if (!slotExists)
            {
                slot.IsBlocked = true;
                ApplyBg(slot, SlotVisualState.Blocked);
                slot.Icon.enabled = false;
                SetSlotRaycastable(slot, false);
                return;
            }

            SetSlotRaycastable(slot, true);

            var item = EquipmentSystem.GetEquipped(_ship, slot.SlotKey);
            slot.Item = item;

            if (item == null)
            {
                ApplyBg(slot, SlotVisualState.Usual);
                slot.Icon.enabled = false;
                return;
            }

            ApplyBg(slot, item.IsWorking ? SlotVisualState.Usual : SlotVisualState.Broken);
            AssignAnimatedIcon(slot, item);
            RefreshEmbedBadge(slot.EmbedBadge, item);
        }

        private static void SetSlotRaycastable(SlotWidget slot, bool on)
        {
            if (slot.Bg != null) slot.Bg.raycastTarget = on;
            if (slot.RaycastFallback != null) slot.RaycastFallback.raycastTarget = on;
        }

        private void RefreshCargo()
        {
            if (_ship?.Inventory == null) return;
            foreach (var slot in _slots)
            {
                if (!slot.IsCargo) continue;
                if (_cargoSlotMap.TryGetValue(slot.CargoIndex, out var item) && item != null)
                {
                    slot.Item = item;
                    ApplyBg(slot, item.IsWorking ? SlotVisualState.Usual : SlotVisualState.Broken);
                    AssignStaticIcon(slot, item);
                    RefreshEmbedBadge(slot.EmbedBadge, item);
                }
                else
                {
                    slot.Item = null;
                    ApplyBg(slot, SlotVisualState.Usual);
                    slot.Icon.enabled = false;
                    slot.IconAnimator?.Configure(null);
                    RefreshEmbedBadge(slot.EmbedBadge, null);
                }
            }
        }

        private void AssignAnimatedIcon(SlotWidget slot, ItemInstance item)
        {
            var frames = LoadItemSpriteSheet(item);
            if (frames != null && frames.Length > 0)
            {
                slot.Icon.sprite = frames[0];
                slot.Icon.color = Color.white;
                slot.Icon.enabled = true;
                slot.IconAnimator?.Configure(frames);
            }
            else
            {
                slot.Icon.enabled = false;
                slot.IconAnimator?.Configure(null);
            }
        }

        private void AssignStaticIcon(SlotWidget slot, ItemInstance item)
        {
            var sprite = LoadItemStaticSprite(item);
            if (sprite != null)
            {
                slot.Icon.sprite = sprite;
                slot.Icon.color = Color.white;
                slot.Icon.enabled = true;
                slot.IconAnimator?.Configure(null);
            }
            else
            {
                slot.Icon.enabled = false;
                slot.IconAnimator?.Configure(null);
            }
        }

        private void ApplyBg(SlotWidget slot, SlotVisualState state)
        {
            if (slot.Bg == null) return;
            var sprite = PickBgSprite(slot, state);
            slot.Bg.sprite = sprite;
            // Если спрайт не загружен — рисуем полупрозрачный плейсхолдер;
            // у артефактов/трюма оттенок другой, чтобы отличаться от обычных слотов.
            slot.Bg.color = sprite != null
                ? Color.white
                : PlaceholderColorFor(state, slot.IsArtefactStyle);
        }

        private static Color PlaceholderColorFor(SlotVisualState s, bool isArt)
        {
            if (isArt)
            {
                return s switch
                {
                    SlotVisualState.Blocked      => new Color(0.35f, 0.10f, 0.30f, 0.45f),
                    SlotVisualState.Broken       => new Color(0.55f, 0.20f, 0.45f, 0.55f),
                    SlotVisualState.MouseEntered => new Color(0.85f, 0.55f, 1.0f, 0.55f),
                    SlotVisualState.Green        => new Color(0.30f, 0.65f, 0.45f, 0.55f),
                    _                            => new Color(0.55f, 0.35f, 0.70f, 0.30f),
                };
            }
            return s switch
            {
                SlotVisualState.Blocked      => new Color(0.40f, 0.10f, 0.10f, 0.45f),
                SlotVisualState.Broken       => new Color(0.50f, 0.20f, 0.20f, 0.55f),
                SlotVisualState.MouseEntered => new Color(0.60f, 0.80f, 1.00f, 0.55f),
                SlotVisualState.Green        => new Color(0.20f, 0.60f, 0.20f, 0.55f),
                _                            => new Color(0.50f, 0.55f, 0.65f, 0.30f),
            };
        }

        private static Sprite PickBgSprite(SlotWidget slot, SlotVisualState s)
        {
            var set = slot.TextureSet ?? (slot.IsArtefactStyle ? _defaultArt : _defaultRegular);
            if (set == null) return null;
            return s switch
            {
                SlotVisualState.Usual        => set.Usual,
                SlotVisualState.MouseEntered => set.MouseEntered,
                SlotVisualState.Blocked      => set.Blocked,
                SlotVisualState.Broken       => set.Broken,
                SlotVisualState.Green        => set.Green,
                _                            => set.Usual,
            };
        }

        // ── Hover / click ─────────────────────────────────────────────────────────

        internal void OnSlotEnter(SlotWidget slot)
        {
            if (slot.IsBlocked) return;
            // Cargo-слоты: визуальной реакции/анимации нет, но popup инфо предмета
            // показываем как и для обычных слотов.
            if (slot.IsCargo)
            {
                if (slot.Item != null && _hand.Item == null)
                    ShowPopup(slot.Item);
                return;
            }
            slot.IsHovered = true;
            ApplyBg(slot, SlotVisualState.MouseEntered);
            slot.IconAnimator?.SetPlaying(true);
            if (slot.Item != null && _hand.Item == null)
                ShowPopup(slot.Item);
        }

        internal void OnSlotExit(SlotWidget slot)
        {
            if (slot.IsBlocked) return;
            if (slot.IsCargo) { HidePopup(); return; }
            slot.IsHovered = false;
            slot.IconAnimator?.SetPlaying(false);
            RestoreSlotBg(slot);
            HidePopup();
        }

        internal void OnSlotClick(SlotWidget slot, PointerEventData e)
        {
            if (!AllowInteraction) return;
            if (slot.IsBlocked) return;
            if (_ship == null) return;

            // Абордажный «грабёж»: ЛКМ переносит предмет напрямую в инвентарь получателя.
            if (BoardingLootRecipient != null && e.button == PointerEventData.InputButton.Left)
            {
                if (TryLootToRecipient(slot))
                {
                    Refresh();
                    OnEquipmentChanged?.Invoke();
                }
                return;
            }

            if (e.button == PointerEventData.InputButton.Right)
            {
                CancelHand();
                OnEquipmentChanged?.Invoke();
                return;
            }
            if (e.button != PointerEventData.InputButton.Left) return;

            if (_hand.Item == null)
                TryPickFromSlot(slot);
            else
                TryPlaceToSlot(slot);

            Refresh();
            OnEquipmentChanged?.Invoke();
        }

        private bool TryLootToRecipient(SlotWidget slot)
        {
            var recipient = BoardingLootRecipient;
            if (recipient == null || _ship == null) return false;
            var item = slot.Item;
            if (item == null) return false;
            // Корпус снимать нельзя (логика установки корпуса инвалидирует слоты).
            if (item.Category == EquipmentCategory.Hull) return false;

            if (slot.IsCargo)
            {
                InventoryService.TakeFromInventory(_ship, item.Uid);
                _cargoSlotMap.Remove(slot.CargoIndex);
            }
            else
            {
                if (InventoryService.UnequipFromSlot(_ship, slot.SlotKey) == null) return false;
            }

            InventoryService.AddToInventory(recipient, item);
            GameLog.Add($"[Абордаж] {item.Name} → инвентарь {recipient.Name}.");
            return true;
        }

        private void TryPickFromSlot(SlotWidget slot)
        {
            if (slot.Item == null) return;
            var item = slot.Item;
            if (slot.IsCargo)
            {
                InventoryService.TakeFromInventory(_ship, item.Uid);
                _cargoSlotMap.Remove(slot.CargoIndex);
            }
            else
            {
                if (InventoryService.UnequipFromSlot(_ship, slot.SlotKey) == null) return;
                // Снятие из слота меняет источники SlotCode/ShipScope — пересчитываем корабль.
                ShipBonusService.RecomputeShipEquipment(_ship);
            }
            SetHand(item, slot);
            HidePopup();
        }

        private void TryPlaceToSlot(SlotWidget targetSlot)
        {
            var handItem = _hand.Item;
            if (handItem == null) return;

            if (targetSlot.IsCargo)
            {
                PlaceToCargo(handItem, targetSlot);
                return;
            }

            if (targetSlot.SlotKey == SlotKeys.Throw)
            {
                ThrowHandIntoSpace(handItem);
                return;
            }

            if (targetSlot.SlotKey == SlotKeys.Activate)
            {
                ActivateHandItem(handItem);
                return;
            }

            if (!_ship.Equipment.Slots.ContainsKey(targetSlot.SlotKey)) return;

            // Встраиваемый предмет (микромодуль/встраиваемый артефакт) в руке + предмет в слоте,
            // разрешающий встраивание → попытка Install. Иначе идём по обычной ветке экипировки.
            if (handItem.IsEmbeddable && targetSlot.Item != null && targetSlot.Item.AllowEmbeds)
            {
                var check = EmbedService.CanInstall(targetSlot.Item, handItem);
                if (!check.Ok) return;
                if (EmbedService.Install(targetSlot.Item, handItem))
                {
                    _ship.Inventory.Remove(handItem.Uid);
                    _ship.AllItems.Remove(handItem.Uid);
                    ShipBonusService.RecomputeShipEquipment(_ship);
                    ClearHand();
                }
                return;
            }

            string targetCategory = EquipmentSystem.SlotCategory(targetSlot.SlotKey);
            if (!handItem.CanFitInSlotCategory(targetCategory)) return;

            ItemInstance displaced;
            if (handItem.Category == EquipmentCategory.Hull)
            {
                displaced = EquipmentSystem.GetEquipped(_ship, targetSlot.SlotKey);
                PlaceHull(handItem);
            }
            else
            {
                displaced = InventoryService.EquipToSlot(_ship, targetSlot.SlotKey, handItem);
            }

            // Установка в слот меняет источники SlotCode/ShipScope — пересчитываем корабль
            // (иначе Bonuses артефакта не применяются к другим слотам).
            ShipBonusService.RecomputeShipEquipment(_ship);

            if (displaced != null && displaced.Uid != handItem.Uid)
                SetHand(displaced, targetSlot);
            else
                ClearHand();
        }

        private void PlaceHull(ItemInstance hull)
        {
            var equipCfg = GalaxyManager.Instance?.Context?.ItemsConfig;
            if (equipCfg == null) return;

            string oldHullUid = _ship.Equipment.Clear(SlotKeys.Hull);
            if (oldHullUid != null && _ship.AllItems.TryGetValue(oldHullUid, out var oldHull))
            {
                _ship.AllItems.Remove(oldHullUid);
                PutToInventoryAtFreeCell(oldHull);
            }

            _ship.Equipment.Set(SlotKeys.Hull, hull.Uid);
            _ship.AllItems[hull.Uid] = hull;
            EquipmentSystem.ApplySeriesToHull(hull, equipCfg);
            var slotPlan = EquipmentSystem.GetHullSlotPlan(hull, equipCfg);
            var evicted = _ship.Equipment.RebuildForHullSlots(slotPlan, equipCfg);
            foreach (var uid in evicted)
                if (_ship.AllItems.TryGetValue(uid, out var ev))
                {
                    _ship.AllItems.Remove(uid);
                    PutToInventoryAtFreeCell(ev);
                }

            int hullHp = Mathf.RoundToInt(hull.GetParam("HP", hull.MaxDurability > 0 ? hull.MaxDurability : 100));
            _ship.MaxHull = hullHp;
            _ship.CurrentHull = Mathf.Min(_ship.CurrentHull, _ship.MaxHull);
            _ship.InvalidateWeaponSlotsCache();
            ShipFactory.RecalculateSpriteWorldSize(_ship);
        }

        /// <summary>
        /// Выкидывает предмет «из руки» в космос контейнером (CosmicItem с IsItem=true),
        /// который ставится в star.Ships рядом с кораблём. Item полностью удаляется из инвентаря/AllItems корабля.
        /// </summary>
        private void ThrowHandIntoSpace(ItemInstance item)
        {
            if (_ship == null || item == null) return;

            var star = ResolveCurrentStar();
            if (star == null)
            {
                GameLog.Add("[Выбросить] Нет звёздной системы для спавна контейнера.");
                return;
            }

            // «Теневой» предмет, представляющий стек товара: количество выбирается ползунком
            // в мини-форме; стек весом 1 кидается сразу без диалога.
            if (IsStackItem(item))
            {
                if (item.Weight <= 0)
                {
                    GameLog.Add("[Выбросить] Стек груза пуст.");
                    ClearHand();
                    return;
                }
                if (item.Weight == 1)
                {
                    ThrowStackAmount(item, 1);
                    return;
                }
                HidePopup();
                _throwDialog.Show($"Выбросить: {item.Name}", item.Weight,
                                  amount => ThrowStackAmount(item, amount),
                                  onCancel: CancelHand,
                                  confirmLabel: "Выбросить");
                return;
            }

            InventoryService.RemoveCompletely(_ship, item);
            var container = ContainerFactory.SpawnContainerWithItem(_ship, item, star);
            if (container != null)
            {
                // Контейнер должен появиться визуально немедленно — без ожидания следующего хода симуляции.
                SystemViewManager.Instance?.EnsureShipVisual(container);
                GameLog.Add($"[Выбросить] {item.Name} → контейнер в космосе.");
            }

            ClearHand();
        }

        private static bool IsStackItem(ItemInstance item) =>
            !string.IsNullOrEmpty(item?.Uid)
            && item.Uid.StartsWith(ShipInventory.StackItemUidPrefix, System.StringComparison.Ordinal);

        private StarData ResolveCurrentStar()
        {
            var star = _ship.CurrentStar;
            if (star == null && !string.IsNullOrEmpty(_ship.CurrentStarUid))
            {
                var map = GalaxyManager.Instance?.GeneratedGalaxy?.StarsMap;
                if (map != null) map.TryGetValue(_ship.CurrentStarUid, out star);
            }
            return star;
        }

        /// <summary>
        /// Выбрасывает <paramref name="amount"/> единиц из стека, представленного «теневым»
        /// предметом из руки. Стек нужно изымать через TakeStack — иначе вес из Stacks останется
        /// и груз задублируется. Остаток (если есть) TakeStack пересоздаёт новым теневым
        /// ItemInstance — возвращаем его в исходную ячейку трюма.
        /// </summary>
        private void ThrowStackAmount(ItemInstance stackItem, int amount)
        {
            var star = ResolveCurrentStar();
            if (star == null)
            {
                GameLog.Add("[Выбросить] Нет звёздной системы для спавна контейнера.");
                CancelHand();
                return;
            }

            string goodId = stackItem.ItemId;
            var taken = _ship.Inventory?.TakeStack(goodId, amount);
            if (taken == null || taken.TotalWeight <= 0)
            {
                GameLog.Add("[Выбросить] Стек груза пуст.");
                ClearHand();
                Refresh();
                return;
            }

            var goodsContainer = ContainerFactory.SpawnContainerWithStack(_ship, taken, star);
            if (goodsContainer != null)
            {
                SystemViewManager.Instance?.EnsureShipVisual(goodsContainer);
                GameLog.Add($"[Выбросить] {taken.Name} x{taken.TotalWeight} → контейнер в космосе.");
            }

            var rest = _ship.Inventory?.GetByUid(ShipInventory.StackItemUidPrefix + goodId);
            if (rest != null)
            {
                var origin = _hand.OriginSlot;
                if (origin != null && origin.IsCargo
                    && (!_cargoSlotMap.ContainsKey(origin.CargoIndex) || _cargoSlotMap[origin.CargoIndex] == null))
                    _cargoSlotMap[origin.CargoIndex] = rest;
                else
                    AssignToFirstFreeCargoCell(rest);
            }

            ClearHand();
            Refresh();
            OnEquipmentChanged?.Invoke();
        }

        /// <summary>
        /// Активация предмета: возвращает его в исходный слот/трюм и запускает
        /// EquipmentSystem.ActivateItem (форсаж → ship.ForsageActive = true; в будущем
        /// здесь будет произвольный скрипт предмета).
        /// </summary>
        private void ActivateHandItem(ItemInstance item)
        {
            if (_ship == null || item == null) return;
            var origin = _hand.OriginSlot;

            bool returned = false;
            if (origin != null && origin.IsCargo)
            {
                if (!_cargoSlotMap.ContainsKey(origin.CargoIndex)
                    || _cargoSlotMap[origin.CargoIndex] == null)
                    _cargoSlotMap[origin.CargoIndex] = item;
                else
                    AssignToFirstFreeCargoCell(item);
                if (!InventoryService.InventoryContains(_ship, item.Uid))
                    InventoryService.AddToInventory(_ship, item);
                returned = true;
            }
            else if (origin != null && _ship.Equipment.Slots.ContainsKey(origin.SlotKey)
                     && EquipmentSystem.GetEquipped(_ship, origin.SlotKey) == null
                     && item.CanFitInSlotCategory(EquipmentSystem.SlotCategory(origin.SlotKey)))
            {
                InventoryService.EquipToSlot(_ship, origin.SlotKey, item);
                returned = true;
            }
            else
            {
                PutToInventoryAtFreeCell(item);
                returned = true;
            }

            if (returned)
            {
                bool activated = EquipmentSystem.ActivateItem(_ship, item);
                // Активация форсажа меняет скорость → пересчитать метки дней на маршруте.
                if (activated && item.Category == EquipmentCategory.Forsage && PlayerShip.Instance != null)
                    PlayerShip.Instance.RefreshPlannedRoute();
                // Дрон-обёртка потребляется при развёртывании → перестроить cargo-map чтобы
                // не осталась иконка в клетке трюма.
                if (activated && item.Category == EquipmentCategory.CompanionDrone)
                {
                    RebuildCargoMap();
                    RefreshCargo();
                }
            }

            ClearHand();
        }

        /// <summary>Показывает/скрывает слот активации в зависимости от того, активируемый ли
        /// предмет сейчас в руке.</summary>
        private void UpdateActivateSlotVisibility()
        {
            if (_activateSlot == null) return;
            bool visible = _hand.Item != null && _hand.Item.Activatable;
            if (_activateSlot.Root.gameObject.activeSelf != visible)
                _activateSlot.Root.gameObject.SetActive(visible);
        }

        private void PlaceToCargo(ItemInstance item, SlotWidget targetSlot)
        {
            int idx = targetSlot.CargoIndex;
            // Swap: если в ячейке уже что-то лежит — оно уходит в руку.
            if (_cargoSlotMap.TryGetValue(idx, out var existing) && existing != null && existing != item)
            {
                // existing уходит из инвентаря в руку; item занимает её место.
                InventoryService.RemoveFromInventory(_ship, existing.Uid);
                _cargoSlotMap.Remove(idx);

                _cargoSlotMap[idx] = item;
                if (!InventoryService.InventoryContains(_ship, item.Uid))
                    InventoryService.AddToInventory(_ship, item);

                SetHand(existing, targetSlot);
                return;
            }

            _cargoSlotMap[idx] = item;
            if (!InventoryService.InventoryContains(_ship, item.Uid))
                InventoryService.AddToInventory(_ship, item);
            ClearHand();
        }

        private void SetHand(ItemInstance item, SlotWidget origin)
        {
            _hand.Item = item;
            _hand.OriginSlot = origin;
            var sprite = LoadItemStaticSprite(item);
            _handIcon.sprite = sprite;
            _handIcon.color = sprite != null
                ? new Color(1f, 1f, 1f, 0.95f)
                : new Color(0.9f, 0.85f, 0.4f, 0.7f);
            _handIcon.enabled = true;
            _handIcon.preserveAspect = false;
            // В руке оборудование показывается в натуральном размере спрайта _i, без скейла.
            if (sprite != null)
                _handRoot.sizeDelta = sprite.rect.size;
            else
                _handRoot.sizeDelta = new Vector2(SlotSize, SlotSize);
            _handRoot.gameObject.SetActive(true);
            _handRoot.SetAsLastSibling();
            RefreshEmbedBadge(_handEmbedBadge, item);
            UpdateHandPosition();
            UpdateActivateSlotVisibility();
            UpdateEmbedTargetHighlight();
        }

        private void ClearHand()
        {
            _hand = default;
            RefreshEmbedBadge(_handEmbedBadge, null);
            _handRoot.gameObject.SetActive(false);
            UpdateActivateSlotVisibility();
            UpdateEmbedTargetHighlight();
            // Рука ушла — диалог количества больше не имеет смысла (в т.ч. при
            // принудительном сбросе руки извне: DropHandToSource при закрытии панели).
            _throwDialog?.CloseSilent();
        }

        /// <summary>Пока в руке встраиваемый предмет (микромодуль/встраиваемый артефакт) — слоты
        /// оборудования с носителем, куда его можно вставить, подсвечиваются зелёным (SlotGreen).
        /// При сбросе руки — визуал слотов восстанавливается через <see cref="RestoreSlotBg"/>.</summary>
        private void UpdateEmbedTargetHighlight()
        {
            var hand = _hand.Item;
            bool active = hand != null && hand.IsEmbeddable;
            foreach (var slot in _slots)
            {
                if (slot.IsCargo) continue;
                if (slot.SlotKey == SlotKeys.Throw || slot.SlotKey == SlotKeys.Activate) continue;
                if (slot.IsBlocked) continue;
                if (!active)
                {
                    RestoreSlotBg(slot);
                    continue;
                }
                if (slot.Item != null && slot.Item.AllowEmbeds
                    && EmbedService.CanInstall(slot.Item, hand).Ok)
                {
                    ApplyBg(slot, SlotVisualState.Green);
                }
                else
                {
                    RestoreSlotBg(slot);
                }
            }
        }

        private void CancelHand()
        {
            if (_hand.Item == null)
            {
                if (_handRoot != null) _handRoot.gameObject.SetActive(false);
                return;
            }

            var item = _hand.Item;
            var origin = _hand.OriginSlot;
            if (origin != null && origin.IsCargo)
            {
                // Возврат в ту же ячейку трюма, если она свободна; иначе — в первую свободную.
                if (!_cargoSlotMap.ContainsKey(origin.CargoIndex)
                    || _cargoSlotMap[origin.CargoIndex] == null)
                    _cargoSlotMap[origin.CargoIndex] = item;
                else
                    AssignToFirstFreeCargoCell(item);
                if (_ship != null && !_ship.Inventory.Contains(item.Uid))
                    _ship.Inventory.Add(item);
            }
            else if (origin != null)
            {
                if (_ship != null && _ship.Equipment.Slots.ContainsKey(origin.SlotKey)
                    && EquipmentSystem.GetEquipped(_ship, origin.SlotKey) == null)
                {
                    _ship.Equipment.Set(origin.SlotKey, item.Uid);
                    _ship.AllItems[item.Uid] = item;
                    if (item.Category == EquipmentCategory.Shield)
                        WeaponSystem.RebuildShieldState(_ship);
                    _ship.InvalidateWeaponSlotsCache();
                }
                else
                {
                    PutToInventoryAtFreeCell(item);
                }
            }
            else
            {
                PutToInventoryAtFreeCell(item);
            }

            ClearHand();
            Refresh();
        }

        private void PutToInventoryAtFreeCell(ItemInstance item)
        {
            if (item == null || _ship?.Inventory == null) return;
            if (!_ship.Inventory.Contains(item.Uid))
                _ship.Inventory.Add(item);
            AssignToFirstFreeCargoCell(item);
        }

        private void AssignToFirstFreeCargoCell(ItemInstance item)
        {
            int cap = CargoColumns * CargoRows;
            for (int i = 0; i < cap; i++)
            {
                if (!_cargoSlotMap.ContainsKey(i) || _cargoSlotMap[i] == null)
                {
                    _cargoSlotMap[i] = item;
                    return;
                }
            }
        }

        private void RestoreSlotBg(SlotWidget slot)
        {
            if (slot.IsCargo)
            {
                if (slot.Item == null)
                    ApplyBg(slot, SlotVisualState.Usual);
                else
                    ApplyBg(slot, slot.Item.IsWorking ? SlotVisualState.Usual : SlotVisualState.Broken);
                return;
            }
            if (_ship?.Equipment == null || !_ship.Equipment.Slots.ContainsKey(slot.SlotKey))
                ApplyBg(slot, SlotVisualState.Blocked);
            else if (slot.Item == null)
                ApplyBg(slot, SlotVisualState.Usual);
            else if (IsEmbedTarget(slot))
                ApplyBg(slot, SlotVisualState.Green);
            else
                ApplyBg(slot, slot.Item.IsWorking ? SlotVisualState.Usual : SlotVisualState.Broken);
        }

        /// <summary>true, если в руке встраиваемый предмет, а на слоте — совместимый носитель.</summary>
        private bool IsEmbedTarget(SlotWidget slot)
        {
            var hand = _hand.Item;
            if (hand == null || !hand.IsEmbeddable) return false;
            if (slot.Item == null || !slot.Item.AllowEmbeds) return false;
            return EmbedService.CanInstall(slot.Item, hand).Ok;
        }

        // ── Popup ─────────────────────────────────────────────────────────────────

        private void ShowPopup(ItemInstance item)
        {
            if (_popupRoot == null || _popupText == null || item == null) return;
            _popupText.text = BuildItemDescription(item);
            _popupRoot.gameObject.SetActive(true);
            _popupRoot.SetAsLastSibling();
            UpdatePopupPosition();
        }

        private void HidePopup()
        {
            if (_popupRoot != null) _popupRoot.gameObject.SetActive(false);
        }

        private void Update()
        {
            // Пока открыт диалог количества — рука «заморожена»; Esc обрабатывает сам диалог.
            if (_throwDialog != null && _throwDialog.IsOpen)
                return;

            if (_hand.Item != null)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                    CancelHand();
                UpdateHandPosition();
            }

            if (_popupRoot != null && _popupRoot.gameObject.activeSelf)
                UpdatePopupPosition();

            HandleCargoScroll();
        }

        /// <summary>Прокрутка трюма: колёсиком мыши (когда курсор над сеткой трюма) или клавишами
        /// PgUp/PgDown. Один шаг — одна строка. При изменении _cargoScrollRows пересобираем карту
        /// и перерисовываем трюм.</summary>
        private void HandleCargoScroll()
        {
            if (_ship?.Inventory?.Items == null) return;
            int totalRows = Mathf.CeilToInt(_ship.Inventory.Items.Count / (float)CargoColumns);
            int maxScroll = Mathf.Max(0, totalRows - CargoRows);
            if (maxScroll <= 0) return;

            int delta = 0;
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && IsMouseOverCargo(Input.mousePosition))
                delta = wheel > 0 ? -1 : 1;
            else if (Input.GetKeyDown(KeyCode.PageUp))   delta = -1;
            else if (Input.GetKeyDown(KeyCode.PageDown)) delta = 1;

            if (delta == 0) return;
            int prev = _cargoScrollRows;
            _cargoScrollRows = Mathf.Clamp(_cargoScrollRows + delta, 0, maxScroll);
            if (_cargoScrollRows != prev)
            {
                RebuildCargoMap();
                RefreshCargo();
            }
        }

        private void UpdateHandPosition()
        {
            if (_handRoot == null) return;
            Vector2 local = ScreenToRootLocal(Input.mousePosition);
            _handRoot.anchoredPosition = local;
        }

        private void UpdatePopupPosition()
        {
            Vector2 local = ScreenToRootLocal(Input.mousePosition);
            Vector2 size = _popupRoot.sizeDelta;
            const float offset = 18f;
            Vector2 desired = local + new Vector2(offset, -offset);
            float halfW = RefWidth * 0.5f;
            float halfH = RefHeight * 0.5f;
            if (desired.x + size.x > halfW) desired.x = local.x - offset - size.x;
            if (desired.y - size.y < -halfH) desired.y = local.y + offset + size.y;
            _popupRoot.anchoredPosition = desired;
        }

        private Vector2 ScreenToRootLocal(Vector2 screenPos)
        {
            var canvas = _root.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : canvas?.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screenPos, cam, out var local);
            return local;
        }

        // ── Описание элемента ─────────────────────────────────────────────────────

        public static string BuildItemDescription(ItemInstance item)
        {
            var sb = new StringBuilder(384);
            sb.Append(item.Name).Append("  |  ТУ").Append(item.TechLevel);
            sb.Append('\n');
            sb.Append("Тип: ").Append(LocalizeCategory(item.Category));
            sb.Append("    Вес: ").Append(item.Weight);
            sb.Append('\n');
            sb.Append("Цена: ").Append(item.Price).Append(" кр.");
            sb.Append('\n');
            if (item.NoWear)
                sb.Append("Прочность: без износа");
            else
                sb.Append("Прочность: ").Append(item.Durability).Append('/').Append(item.MaxDurability);

            string manufacturer = ResolveManufacturerLabel(item);
            if (!string.IsNullOrEmpty(manufacturer))
                sb.Append('\n').Append("Производитель: ").Append(manufacturer);

            string extra = ExtraStatLine(item);
            if (!string.IsNullOrEmpty(extra))
                sb.Append('\n').Append(extra);

            // Пометка об SB-апгрейде (значения улучшенных статов уже подсвечены зелёным в FormatStat).
            if (item.ImprovedAttributes != null && item.ImprovedAttributes.Count > 0)
            {
                sb.Append('\n').Append("<color=#4DDA73>Улучшено на SB");
                if (!string.IsNullOrEmpty(item.ImprovementTier)) sb.Append(" (").Append(item.ImprovementTier).Append(")");
                sb.Append("</color>");
            }

            AppendEmbedInfo(sb, item);
            AppendIntrinsicBonusInfo(sb, item);

            if (!string.IsNullOrEmpty(item.Description))
                sb.Append('\n').Append('\n').Append(item.Description);

            return sb.ToString();
        }

        /// <summary>Показать блок «встроенных» бонусов из конфига предмета: IntrinsicEmbed
        /// (безусловные) и SlotCode (пока предмет стоит в слоте). Не путать со встроенными ММ.</summary>
        private static void AppendIntrinsicBonusInfo(StringBuilder sb, ItemInstance item)
        {
            var eq = GalaxyManager.Instance?.Context?.ItemsConfig;
            var cfg = eq?.GetItem(item.Category, item.ItemId);
            if (cfg == null) return;
            bool wroteHeader = false;
            AppendEffectBlock(sb, cfg.IntrinsicEmbed, ref wroteHeader);
            AppendEffectBlock(sb, cfg.SlotCode, ref wroteHeader);
        }

        private static void AppendEffectBlock(StringBuilder sb, EffectConfig e, ref bool wroteHeader)
        {
            if (e == null) return;
            if (!wroteHeader) { sb.Append('\n').Append("Особенности:"); wroteHeader = true; }
            if (e.Bonuses != null)
                foreach (var kv in e.Bonuses)
                    AppendBonusLine(sb, kv.Key, kv.Value);
            if (e.AddedWeaponEffects != null)
                foreach (var eff in e.AddedWeaponEffects)
                    if (eff != null && !string.IsNullOrEmpty(eff.Type))
                        sb.Append('\n').Append("Эффект: ").Append(WeaponEffectLabel(eff));
            if (e.WeaponFlags != null)
                foreach (var flag in e.WeaponFlags)
                    sb.Append('\n').Append(WeaponFlagLabel(flag));
            if (e.SlotBonuses != null)
                foreach (var kv in e.SlotBonuses)
                    if (kv.Value != 0)
                        sb.Append('\n').Append(SlotBonusLabel(kv.Key)).Append(": ")
                          .Append(kv.Value > 0 ? "+" : "").Append(kv.Value);
        }

        private static void AppendEmbedInfo(StringBuilder sb, ItemInstance item)
        {
            // Микромодуль/встраиваемый артефакт: SR-стиль описания действий
            if (item.IsEmbeddable && item.Embed != null)
            {
                var e = item.Embed;
                var compat = e.Compat;

                // Общая строка: тип, совместимость по категории носителя, ограничение по расе
                var header = new StringBuilder(128);
                header.Append("Встраиваемый");
                if (!string.IsNullOrEmpty(e.Tier)) header.Append(" [").Append(e.Tier).Append(']');
                if (compat?.CarrierCategories != null && compat.CarrierCategories.Count > 0)
                    header.Append(". Носитель: ").Append(FormatCarrierCategories(compat.CarrierCategories));
                if (compat != null && !ContainsWildcard(compat.CarrierRaces))
                    header.Append(". Только раса ").Append(string.Join("/", compat.CarrierRaces));
                if (compat != null && compat.CarrierSides != null && compat.CarrierSides.Count > 0
                    && !ContainsWildcard(compat.CarrierSides))
                    header.Append(". Сторона ").Append(string.Join("/", compat.CarrierSides));
                if (e.Removable == 0) header.Append(". Извлечение невозможно.");
                sb.Append('\n').Append(header);

                // Построчно — что даёт
                if (e.Bonuses != null)
                    foreach (var kv in e.Bonuses)
                        AppendBonusLine(sb, kv.Key, kv.Value);
                if (e.SlotBonuses != null)
                    foreach (var kv in e.SlotBonuses)
                        if (kv.Value != 0)
                            sb.Append('\n').Append(SlotBonusLabel(kv.Key)).Append(": ")
                              .Append(kv.Value > 0 ? "+" : "").Append(kv.Value);
                if (e.CarrierMods != null)
                {
                    AppendMulLine(sb, "Стоимость носителя", e.CarrierMods.PriceMul);
                    AppendMulLine(sb, "Вес носителя",       e.CarrierMods.SizeMul);
                    AppendMulLine(sb, "Прочность носителя", e.CarrierMods.DurabilityMul);
                }
                if (e.AddedWeaponEffects != null)
                    foreach (var eff in e.AddedWeaponEffects)
                        if (eff != null && !string.IsNullOrEmpty(eff.Type))
                            sb.Append('\n').Append("Эффект: ").Append(WeaponEffectLabel(eff));
                if (e.WeaponFlags != null)
                    foreach (var flag in e.WeaponFlags)
                        sb.Append('\n').Append(WeaponFlagLabel(flag));
            }

            // Носитель со встроенными: перечисляем содержимое
            if (item.Embeds != null && item.Embeds.Count > 0 && item.EmbedItems != null)
            {
                sb.Append('\n').Append("Встроено: ");
                int printed = 0;
                foreach (var uid in item.Embeds)
                {
                    if (!item.EmbedItems.TryGetValue(uid, out var em)) continue;
                    if (printed++ > 0) sb.Append(", ");
                    sb.Append(em.Name ?? em.ItemId);
                }
            }
        }

        // ── Форматирование бонусов для описания ММ ────────────────────────────

        private static void AppendBonusLine(StringBuilder sb, string key, float value)
        {
            if (value == 0f) return;
            string label = BonusLabel(key);
            if (label == null) return;
            sb.Append('\n').Append(label).Append(": ")
              .Append(value > 0 ? "+" : "").Append(value.ToString("0.##"));
            string unit = BonusUnit(key);
            if (unit != null) sb.Append(' ').Append(unit);
        }

        private static void AppendMulLine(StringBuilder sb, string label, float mul)
        {
            if (Mathf.Approximately(mul, 1f)) return;
            int pct = Mathf.RoundToInt((mul - 1f) * 100f);
            sb.Append('\n').Append(label).Append(": ").Append(pct > 0 ? "+" : "").Append(pct).Append('%');
        }

        private static bool ContainsWildcard(List<string> list) =>
            list != null && list.Contains("*");

        private static string FormatCarrierCategories(List<string> cats)
        {
            var parts = new List<string>(cats.Count);
            foreach (var c in cats) parts.Add(LocalizeCategory(c));
            return string.Join(", ", parts);
        }

        // ключ "<Category>.<ParamKey>" → человеческое имя
        private static string BonusLabel(string key)
        {
            switch (key)
            {
                case "Engine.Speed":              return "Скорость двигателя";
                case "Engine.JumpRange":          return "Дальность прыжка";
                case "FuelTank.Capacity":         return "Ёмкость бака";
                case "Radar.Range":               return "Дальность радара";
                case "Scanner.Power":             return "Мощность сканера";
                case "Droid.Efficiency":          return "Эффективность дроида";
                case "Droid.HealPerTurn":         return "Лечение дроида";
                case "Droid.Damage":              return "Урон дроида";
                case "CargoGrabber.Power":        return "Мощность захвата";
                case "CargoGrabber.Range":        return "Дальность захвата";
                case "Shield.BlockPercent":       return "Блок щита";
                case "Forsage.SpeedMul":          return "Множитель форсажа";
                case "Hull.Armor":                return "Броня корпуса";
                case "Hull.HP":                   return "HP корпуса";
                case "Weapons.MinDmg":            return "Минимальный урон";
                case "Weapons.MaxDmg":            return "Максимальный урон";
                case "Weapons.Range":             return "Дальность оружия";
                case "Weapons.ArmorPenetration":  return "Пробитие брони";
                case "Weapons.ShieldPenetration": return "Пробитие щита";
                case "Weapons.EquipmentHitChance":return "Шанс попасть по оборудованию";
                case "Weapons.EquipmentDamage":   return "Урон по оборудованию";
                case "Weapons.MaxAmmo":           return "Ёмкость боезапаса";
            }
            if (key.StartsWith("Hull.Vulnerability.", System.StringComparison.Ordinal))
                return "Уязвимость к " + key.Substring("Hull.Vulnerability.".Length);
            return key;
        }

        private static string BonusUnit(string key)
        {
            switch (key)
            {
                case "Shield.BlockPercent":
                case "Weapons.ShieldPenetration":
                case "Weapons.EquipmentHitChance":
                    return "%";
                case "Radar.Range":
                case "Engine.Speed":
                case "CargoGrabber.Range":
                case "Weapons.Range":
                    return "ед.";
                default: return null;
            }
        }

        private static string SlotBonusLabel(string cat) =>
            cat == "Embeds" ? "Слоты встраивания" : "Слоты " + LocalizeCategory(cat);

        private static string WeaponEffectLabel(WeaponEffectSpec e)
        {
            string type = e.Type switch
            {
                "Slow"          => "замедление",
                "Shutdown"      => "отключение (ЭМИ)",
                "ArmorDebuff"   => "снятие брони",
                "Jamming"       => "помеха",
                "EngineDisable" => "поломка двигателя",
                "Drain"         => "вампиризм",
                "BlockWeapon"   => "блок оружия",
                "BlockDroid"    => "блок дроида",
                "ExecuteBonus"  => "добивание",
                "LootFocus"     => "фокус трофеев",
                _               => e.Type,
            };
            var s = new StringBuilder(48);
            s.Append(type);
            if (e.Duration > 0) s.Append(", ").Append(e.Duration).Append(" хода");
            if (e.Magnitude != 0f) s.Append(", сила ").Append(e.Magnitude.ToString("0.##"));
            if (e.Chance > 0f && e.Chance < 1f) s.Append(", шанс ").Append(Mathf.RoundToInt(e.Chance * 100f)).Append('%');
            return s.ToString();
        }

        private static string WeaponFlagLabel(string flag) => flag switch
        {
            EmbedWeaponFlags.NoDamageDelta        => "Максимальный урон стабилен (без разброса)",
            EmbedWeaponFlags.IgnoreArmorAndShield => "Игнорирует броню и щит",
            EmbedWeaponFlags.NonLethal            => "Не может добить цель",
            EmbedWeaponFlags.AmmoFree             => "Не тратит боезапас",
            _                                     => flag,
        };

        /// <summary>Форматирует значение стата с бонусом от источников: "База (+бонус)" или просто число.
        /// Читает <see cref="ItemInstance.DisplayParams"/> (без скрытых бонусов); если DisplayParams
        /// пуст — фолбэк на Params. Дельта показывается только если она не нулевая и есть BaseParams.
        /// Если стат был поднят SB-апгрейдом (<see cref="ItemInstance.ImprovedAttributes"/>),
        /// значение окрашивается в зелёный (rich text — Unity UI Text это поддерживает).</summary>
        private static string FormatStat(ItemInstance item, string paramKey, string format = "F0", float defaultValue = 0f)
        {
            float cur = defaultValue;
            if (item.DisplayParams != null && item.DisplayParams.TryGetValue(paramKey, out var vd))
                cur = vd;
            else if (item.Params != null && item.Params.TryGetValue(paramKey, out var vp))
                cur = vp;

            string body;
            if (item.BaseParams != null && item.BaseParams.TryGetValue(paramKey, out var baseVal) && baseVal != cur)
            {
                float delta = cur - baseVal;
                string sign = delta > 0 ? "+" : "";
                body = $"{baseVal.ToString(format)} ({sign}{delta.ToString(format)})";
            }
            else body = cur.ToString(format);

            if (item.ImprovedAttributes != null && item.ImprovedAttributes.Contains(paramKey))
                return $"<color=#4DDA73>{body}</color>";
            return body;
        }

        /// <summary>Человекочитаемое имя производителя предмета: «<i>RaceDisplay</i> [Side]».
        /// null/пусто — если у предмета не задан ни <see cref="ItemInstance.ManufacturerRace"/>, ни
        /// <see cref="ItemInstance.ManufacturerSide"/>. Race резолвится через <c>TextsConfig.RaceNames</c>.</summary>
        private static string ResolveManufacturerLabel(ItemInstance item)
        {
            if (item == null) return null;
            string race = item.ManufacturerRace;
            string side = item.ManufacturerSide;
            if (string.IsNullOrEmpty(race) && string.IsNullOrEmpty(side)) return null;

            string raceLabel = race;
            var textCfg = GalaxyManager.Instance?.Context?.TextConfig;
            if (!string.IsNullOrEmpty(race) && textCfg?.RaceNames != null
                && textCfg.RaceNames.TryGetValue(race, out var displayName)
                && !string.IsNullOrEmpty(displayName))
                raceLabel = displayName;

            if (!string.IsNullOrEmpty(raceLabel) && !string.IsNullOrEmpty(side))
                return $"{raceLabel} [{side}]";
            return raceLabel ?? side;
        }

        private static string ExtraStatLine(ItemInstance item)
        {
            switch (item.Category)
            {
                case EquipmentCategory.Engine:
                    return $"Скорость: {FormatStat(item, "Speed")}   Прыжок: {FormatStat(item, "JumpRange")}";
                case EquipmentCategory.FuelTank:
                    return $"Объём: {FormatStat(item, "Capacity")}";
                case EquipmentCategory.Shield:
                    return $"Блок: {FormatStat(item, "BlockPercent")}%";
                case EquipmentCategory.Radar:
                    return $"Радиус: {FormatStat(item, "Range")}";
                case EquipmentCategory.Hull:
                    return $"HP: {FormatStat(item, "HP")}   Броня: {FormatStat(item, "Armor")}   [{item.GetParamString("HullType", "?")}]";
                case EquipmentCategory.Droid:
                    {
                        float h = item.GetParam("HealPerTurn");
                        float d = item.GetParam("Damage");
                        var sb = new StringBuilder(64);
                        if (h > 0f) sb.Append($"Лечение: +{FormatStat(item, "HealPerTurn")}/ход   ");
                        if (d > 0f) sb.Append($"Урон: {FormatStat(item, "Damage")}");
                        return sb.ToString();
                    }
                case EquipmentCategory.CargoGrabber:
                    return $"Мощн: {FormatStat(item, "Power")}   Дальн: {FormatStat(item, "Range")}";
                case EquipmentCategory.Forsage:
                    return $"Множитель износа двигателя: x{item.GetParam("EngineDurabilityBurnMult", 1f):F2}";
                case EquipmentCategory.Weapons:
                    {
                        var sb = new StringBuilder(96);
                        sb.Append($"Урон: {FormatStat(item, "MinDmg")}-{FormatStat(item, "MaxDmg")} ({item.GetParamString("DamageType", "?")})");
                        sb.Append($"   Дальн: {FormatStat(item, "Range")}");
                        float ap = item.GetParam("ArmorPenetration", 0f);
                        float sp = item.GetParam("ShieldPenetration", 0f);
                        if (ap > 0f) sb.Append($"   Пробитие: {FormatStat(item, "ArmorPenetration")}");
                        if (sp > 0f) sb.Append($"   Щит-проб: {FormatStat(item, "ShieldPenetration")}%");
                        return sb.ToString();
                    }
                case EquipmentCategory.Artefacts:
                    {
                        float sm = item.GetParam("SpeedMult", 1f);
                        float h = item.GetParam("HealPerTurn");
                        var sb = new StringBuilder(64);
                        if (sm != 1f) sb.Append($"Скор x{sm:F2}   ");
                        if (h > 0f) sb.Append($"Рем +{h:F0}/ход");
                        return sb.ToString();
                    }
                default: return "";
            }
        }

        private static string LocalizeCategory(string cat) => cat switch
        {
            EquipmentCategory.Hull => "Корпус",
            EquipmentCategory.Engine => "Двигатель",
            EquipmentCategory.FuelTank => "Топливный бак",
            EquipmentCategory.Forsage => "Форсаж",
            EquipmentCategory.Shield => "Генератор щита",
            EquipmentCategory.Radar => "Радар",
            EquipmentCategory.Scanner => "Сканер",
            EquipmentCategory.Droid => "Дроид",
            EquipmentCategory.CargoGrabber => "Грузовой захват",
            EquipmentCategory.Weapons => "Оружие",
            EquipmentCategory.Artefacts => "Артефакт",
            "Goods"   => "Товар",
            "Mineral" => "Минерал",
            _ => cat
        };

        // ── Загрузка спрайтов ─────────────────────────────────────────────────────

        private static Sprite[] LoadItemSpriteSheet(ItemInstance item)
        {
            if (item == null || string.IsNullOrEmpty(item.GraphicPath)) return null;
            var gm = GraphicsManager.Instance;
            // Путь на «_i» — статичная иконка (артефакты и др.), листа заведомо нет:
            // не зовём GetSpriteSheet, чтобы не поднимать false-positive LogError о
            // недостающем .png.json.
            if (gm != null && !item.GraphicPath.EndsWith("_i"))
            {
                var sheet = gm.GetSpriteSheet(item.GraphicPath);
                if (sheet != null && sheet.Length > 0) return sheet;
            }
            var single = gm?.TryGetSprite(item.GraphicPath);
            return single != null ? new[] { single } : null;
        }

        private static Sprite LoadItemStaticSprite(ItemInstance item)
        {
            if (item == null) return null;
            // Теневой предмет стека мог быть создан до назначения иконок в ItemsConfig
            // (старый сейв) — дорезолвиваем путь прямо при отрисовке.
            if (string.IsNullOrEmpty(item.GraphicPath) &&
                item.Uid != null && item.Uid.StartsWith(ShipInventory.StackItemUidPrefix))
                item.GraphicPath = StackGraphics.ResolveIcon(item.ItemId, item.Weight);
            if (string.IsNullOrEmpty(item.GraphicPath)) return null;
            var gm = GraphicsManager.Instance;
            string staticPath = SwapToStaticPath(item.GraphicPath);
            var sprite = gm?.TryGetSprite(staticPath);
            if (sprite != null) return sprite;
            if (!ReferenceEquals(staticPath, item.GraphicPath))
                sprite = gm?.TryGetSprite(item.GraphicPath);
            if (sprite == null)
            {
                var frames = LoadItemSpriteSheet(item);
                if (frames != null && frames.Length > 0) return frames[0];
            }
            return sprite;
        }

        /// <summary>Подменяет суффикс пути _a/_c на _i (статичная иконка вместо анимации).</summary>
        private static string SwapToStaticPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (path.EndsWith("_a") || path.EndsWith("_c"))
                return path.Substring(0, path.Length - 2) + "_i";
            return path;
        }

        private static void EnsureSpritesLoaded()
        {
            if (_spritesLoaded) return;
            _formSprite = GraphicsManager.Instance?.GetSprite(ResourceRoot + "FormShipMain1");
            LoadSlotConfig();
            _spritesLoaded = true;
        }

        private static void LoadSlotConfig()
        {
            _defaultRegular = BuildDefaultSet(isArt: false);
            _defaultArt     = BuildDefaultSet(isArt: true);
            _slotOverrides.Clear();

            var json = Resources.Load<TextAsset>(GalaxyConstants.PATH_SHIP_FORM_SLOTS_CFG);
            if (json == null) return;
            try
            {
                var root = Newtonsoft.Json.Linq.JObject.Parse(json.text);
                var defReg = root["DefaultRegular"] as Newtonsoft.Json.Linq.JObject;
                if (defReg != null) _defaultRegular = ParseSet(defReg, _defaultRegular);
                var defArt = root["DefaultArt"] as Newtonsoft.Json.Linq.JObject;
                if (defArt != null) _defaultArt = ParseSet(defArt, _defaultArt);

                var slots = root["Slots"] as Newtonsoft.Json.Linq.JObject;
                if (slots != null)
                {
                    foreach (var kv in slots)
                    {
                        if (kv.Value is Newtonsoft.Json.Linq.JObject obj)
                            _slotOverrides[kv.Key] = ParseSet(obj, _defaultRegular);
                    }
                }

                // Layout-секция (B4 рефакторинга): позиции слотов и панели в абсолютных
                // координатах 1920×1080. Любая запись опциональна — отсутствующие поля
                // оставляются на code-defaults (см. верх класса).
                var layout = root["Layout"] as Newtonsoft.Json.Linq.JObject;
                if (layout != null)
                    ApplyLayoutOverrides(layout);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ShipFormView] Failed to parse Config/ShipFormSlots.json: {ex.Message}");
            }
        }

        /// <summary>Применяет «Layout»-секцию из JSON: разрешает перекрыть отдельные позиции
        /// или массивы позиций. Любой отсутствующий ключ — fallback на code-defaults.</summary>
        private static void ApplyLayoutOverrides(Newtonsoft.Json.Linq.JObject layout)
        {
            Vector2? ParseVec(Newtonsoft.Json.Linq.JToken t)
            {
                if (t == null) return null;
                float x = t["X"]?.ToObject<float?>() ?? 0f;
                float y = t["Y"]?.ToObject<float?>() ?? 0f;
                if (t["X"] == null && t["Y"] == null) return null;
                return new Vector2(x, y);
            }

            // FixedSlots: массив объектов {Key, X, Y} — каждая запись перекрывает code-default
            // по ключу slotKey. Полностью отсутствующая секция → дефолт сохраняется.
            var fixedSlots = layout["FixedSlots"] as Newtonsoft.Json.Linq.JArray;
            if (fixedSlots != null)
            {
                var dict = new System.Collections.Generic.Dictionary<string, Vector2>(FixedSlotLayout.Length);
                foreach (var entry in FixedSlotLayout) dict[entry.slotKey] = entry.pos;
                foreach (var item in fixedSlots)
                {
                    string key = item["Key"]?.ToString();
                    if (string.IsNullOrEmpty(key)) continue;
                    var v = ParseVec(item);
                    if (v.HasValue) dict[key] = v.Value;
                }
                // Преобразуем обратно с сохранением исходного порядка (важно: dock-сцена ожидает
                // порядок обхода для построения z-стека слотов).
                for (int i = 0; i < FixedSlotLayout.Length; i++)
                {
                    var k = FixedSlotLayout[i].slotKey;
                    if (dict.TryGetValue(k, out var p)) FixedSlotLayout[i] = (k, p);
                }
            }

            Vector2[] ParseVecArray(string key, Vector2[] fallback)
            {
                var arr = layout[key] as Newtonsoft.Json.Linq.JArray;
                if (arr == null) return fallback;
                var result = new Vector2[arr.Count];
                for (int i = 0; i < arr.Count; i++)
                {
                    var v = ParseVec(arr[i]);
                    result[i] = v ?? (i < fallback.Length ? fallback[i] : Vector2.zero);
                }
                return result;
            }

            WeaponPositions   = ParseVecArray("WeaponPositions",   WeaponPositions);
            ArtefactPositions = ParseVecArray("ArtefactPositions", ArtefactPositions);

            ActivationSlotPos = ParseVec(layout["ActivationSlotPos"]) ?? ActivationSlotPos;
            CargoFirstCell    = ParseVec(layout["CargoFirstCell"])    ?? CargoFirstCell;
            ThrowSlotPos      = ParseVec(layout["ThrowSlotPos"])      ?? ThrowSlotPos;
            PilotPanelPos     = ParseVec(layout["PilotPanelPos"])     ?? PilotPanelPos;
        }

        private static SlotTextureSet BuildDefaultSet(bool isArt)
        {
            string prefix = isArt ? "SlotArt" : "Slot";
            var gm = GraphicsManager.Instance;
            return new SlotTextureSet
            {
                Usual        = gm?.GetSprite(ResourceRoot + prefix + "Usual"),
                MouseEntered = gm?.GetSprite(ResourceRoot + prefix + "MouseEntered"),
                Blocked      = gm?.GetSprite(ResourceRoot + prefix + "Blocked"),
                Broken       = gm?.GetSprite(ResourceRoot + prefix + "Broken"),
                Green        = gm?.GetSprite(ResourceRoot + prefix + "Green"),
                Width  = 96,
                Height = 96
            };
        }

        private static SlotTextureSet ParseSet(Newtonsoft.Json.Linq.JObject obj, SlotTextureSet fallback)
        {
            SlotTextureSet s = fallback;
            var gm = GraphicsManager.Instance;
            Sprite Load(string key)
            {
                var v = obj[key]?.ToString();
                return gm?.TryGetSprite(v);
            }
            var u  = Load("Usual");
            var me = Load("MouseEntered");
            var bl = Load("Blocked");
            var br = Load("Broken");
            var gr = Load("Green");
            return new SlotTextureSet
            {
                Usual        = u  != null ? u  : s.Usual,
                MouseEntered = me != null ? me : s.MouseEntered,
                Blocked      = bl != null ? bl : s.Blocked,
                Broken       = br != null ? br : s.Broken,
                Green        = gr != null ? gr : s.Green,
                Width  = obj["Width"]  != null ? obj["Width"].ToObject<float>()  : s.Width,
                Height = obj["Height"] != null ? obj["Height"].ToObject<float>() : s.Height
            };
        }

        private static SlotTextureSet GetSlotTextureSet(string slotKey, bool isArt)
        {
            if (_slotOverrides.TryGetValue(slotKey, out var s)) return s;
            return isArt ? _defaultArt : _defaultRegular;
        }

        private static Vector2 AbsToLocal(Vector2 abs) =>
            new Vector2(abs.x - RefWidth * 0.5f, RefHeight * 0.5f - abs.y);

        // ── Внутренние типы ───────────────────────────────────────────────────────

        private enum SlotVisualState { Usual, MouseEntered, Blocked, Broken, Green }

        internal struct HandState
        {
            public ItemInstance Item;
            public SlotWidget OriginSlot;
        }

        internal class SlotWidget
        {
            public string SlotKey;
            public bool IsArtefactStyle;
            public bool IsCargo;
            public int CargoIndex;
            public Image Bg;
            public Image RaycastFallback;
            public Image Icon;
            public Image EmbedBadge;
            public SlotIconAnimator IconAnimator;
            public RectTransform Root;
            public ItemInstance Item;
            public bool IsHovered;
            public bool IsBlocked;
            public SlotTextureSet TextureSet;
        }

        internal class SlotTextureSet
        {
            public Sprite Usual, MouseEntered, Blocked, Broken, Green;
            public float Width = 96f;
            public float Height = 96f;
        }

        internal class SlotInputHandler : MonoBehaviour,
            IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
        {
            public ShipFormView Owner;
            public SlotWidget Widget;
            public void OnPointerEnter(PointerEventData _) { if (Owner != null) Owner.OnSlotEnter(Widget); }
            public void OnPointerExit(PointerEventData _)  { if (Owner != null) Owner.OnSlotExit(Widget);  }
            public void OnPointerClick(PointerEventData e) { if (Owner != null) Owner.OnSlotClick(Widget, e); }
        }

        internal class SlotIconAnimator : MonoBehaviour
        {
            public Image Target;
            public float Fps = 12f;
            private Sprite[] _frames;
            private int _frame;
            private float _timer;
            private bool _playing;

            public void Configure(Sprite[] frames)
            {
                _frames = frames;
                _frame = 0;
                _timer = 0f;
                _playing = false;
                Apply();
            }

            public void SetPlaying(bool playing)
            {
                if (_frames == null || _frames.Length <= 1) { _playing = false; return; }
                _playing = playing;
                // На паузе кадр НЕ сбрасываем — остаётся на текущем до следующего hover
                // или до полного перезахода в форму (Configure заново).
            }

            public void ResetFrame()
            {
                _frame = 0;
                _timer = 0f;
                _playing = false;
                Apply();
            }

            private void Update()
            {
                if (!_playing || _frames == null || _frames.Length <= 1) return;
                _timer += Time.unscaledDeltaTime;
                float secPerFrame = 1f / Fps;
                if (_timer < secPerFrame) return;
                int advance = Mathf.FloorToInt(_timer / secPerFrame);
                _timer %= secPerFrame;
                _frame = (_frame + advance) % _frames.Length;
                Apply();
            }

            private void Apply()
            {
                if (Target == null || _frames == null || _frames.Length == 0) return;
                int idx = Mathf.Clamp(_frame, 0, _frames.Length - 1);
                Target.sprite = _frames[idx];
            }
        }
    }
}
