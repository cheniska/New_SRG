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
    public partial class ShipFormView : MonoBehaviour
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

    }
}
