using UnityEngine;
using UnityEngine.UI;
using System.Text;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Ships.Player;
using SRG.UI.Screens;

namespace SRG.UI.HUD
{
    // Окно сканирования чужого корабля (аналог инвентаря игрока).
    // Открывается через ShipScanUIController.Instance.OpenFor(ship).
    public class ShipScanUIController : MonoBehaviour
    {
        public static ShipScanUIController Instance { get; private set; }

        private Canvas _canvas;
        private GameObject _panel;
        private Text _titleText;
        private Text _statsText;
        private ShipFormView _formView;
        private bool _isVisible;
        private ShipData _currentShip;

        public bool IsOpen => _isVisible;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void Start()
        {
            BuildUI();
            _panel.SetActive(false);
        }

        private void OnEnable()  => GalaxyManager.OnTurnCalculate += OnTurnCalculate;
        private void OnDisable() => GalaxyManager.OnTurnCalculate -= OnTurnCalculate;

        private void OnTurnCalculate(TurnAnimationData _)
        {
            if (_isVisible) Close();
        }

        private void Update()
        {
            if (!_isVisible) return;
            if (GameConsoleController.IsOpen) return;

            KeyCode toggleKey = GalaxyManager.Instance?.Settings?.InventoryKey ?? KeyCode.I;
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(toggleKey))
                Close();
        }

        public void OpenFor(ShipData ship)
        {
            if (GalaxyManager.Instance?.Phase == TurnPhase.Simulation) return;
            // Item-контейнер не сканируется — нет корпуса/слотов/экипажа.
            // Защита от вызовов из старых веток UI; основной запрет — в ObjectInfoPopup.
            if (ship == null || ship.IsItem) return;
            _currentShip = ship;
            _isVisible = true;
            _panel.SetActive(true);

            // Если этот корабль сейчас абордирован игроком — включаем режим «грабежа»:
            // взаимодействие разрешено, ЛКМ забирает предмет в инвентарь игрока.
            var player = PlayerShip.Instance?.ShipData;
            bool isBoardedByPlayer = player != null && ship.BoardedByUid == player.Uid;
            if (_formView != null)
            {
                _formView.AllowInteraction = isBoardedByPlayer;
                _formView.BoardingLootRecipient = isBoardedByPlayer ? player : null;
            }

            if (_titleText != null)
                _titleText.text = isBoardedByPlayer
                    ? $"АБОРДАЖ: {ship.Name}  (ЛКМ — забрать в трюм)"
                    : $"Сканирование: {ship.Name}";

            _statsText.text = BuildStatsText(ship);
            _formView?.SetShip(ship);
        }

        public void Close()
        {
            _isVisible = false;
            _currentShip = null;
            if (_panel != null) _panel.SetActive(false);
        }

        private static string BuildStatsText(ShipData ship)
        {
            float speed = ship.ActualSpeed;
            int equippedW = EquipmentSystem.GetEquippedWeight(ship);
            int inventoryW = ship.Inventory.TotalWeight();
            int totalW = equippedW + inventoryW;
            int hullCap = EquipmentSystem.GetHullCapacity(ship);
            int freeSpace = EquipmentSystem.GetFreeSpace(ship);
            int fuel = EquipmentSystem.GetCurrentFuel(ship);
            int fuelCap = EquipmentSystem.GetFuelCapacity(ship);
            int jumpRange = EquipmentSystem.GetJumpRange(ship);

            var sb = new StringBuilder(256);
            sb.AppendLine($"{ship.Name}    Раса: {ship.Race ?? "—"}    Владелец: {ship.Owner ?? "—"}");
            sb.AppendLine($"HP: {ship.CurrentHull}/{ship.MaxHull}    Скорость: {speed:F0}    Деньги: {ship.Money:N0} кр.");
            sb.AppendLine($"Вес: {totalW} (снар {equippedW} + груз {inventoryW})    Корпус: {hullCap} свободно: {freeSpace}");
            if (fuelCap > 0)
                sb.AppendLine($"Топливо: {fuel}/{fuelCap}    Прыжок: {jumpRange}");
            return sb.ToString().TrimEnd();
        }

        private void BuildUI()
        {
            var canvasGo = new GameObject("ShipScanCanvas");
            DontDestroyOnLoad(canvasGo);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 55;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            _panel = new GameObject("ShipScanPanel");
            _panel.transform.SetParent(canvasGo.transform, false);

            var panelRt = _panel.AddComponent<RectTransform>();
            panelRt.anchorMin = Vector2.zero;
            panelRt.anchorMax = Vector2.one;
            panelRt.offsetMin = Vector2.zero;
            panelRt.offsetMax = Vector2.zero;

            _panel.AddComponent<Image>().color = new Color(0.02f, 0.05f, 0.04f, 0.92f);

            _formView = _panel.AddComponent<ShipFormView>();
            _formView.AllowInteraction = false;
            _formView.Build(panelRt);

            {
                var go = new GameObject("Title");
                go.transform.SetParent(_panel.transform, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(-40f, 30f);
                rt.anchoredPosition = new Vector2(0f, -4f);
                _titleText = go.AddComponent<Text>();
                _titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                _titleText.text = "Сканирование корабля";
                _titleText.fontSize = 17;
                _titleText.fontStyle = FontStyle.Bold;
                _titleText.color = new Color(0.55f, 1f, 0.65f);
                _titleText.alignment = TextAnchor.MiddleCenter;
            }

            {
                var go = new GameObject("Stats");
                go.transform.SetParent(_panel.transform, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(-40f, 100f);
                rt.anchoredPosition = new Vector2(0f, -40f);
                _statsText = go.AddComponent<Text>();
                _statsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                _statsText.fontSize = 14;
                _statsText.color = new Color(0.85f, 0.95f, 0.85f);
                _statsText.alignment = TextAnchor.UpperLeft;
                _statsText.horizontalOverflow = HorizontalWrapMode.Wrap;
                _statsText.verticalOverflow = VerticalWrapMode.Overflow;
            }

            {
                var go = new GameObject("CloseBtn");
                go.transform.SetParent(_panel.transform, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.sizeDelta = new Vector2(36f, 36f);
                rt.anchoredPosition = new Vector2(-4f, -4f);
                go.AddComponent<Image>().color = new Color(0.55f, 0.1f, 0.1f, 0.9f);
                var btn = go.AddComponent<Button>();
                btn.onClick.AddListener(Close);

                var tgo = new GameObject("X");
                tgo.transform.SetParent(go.transform, false);
                var trt = tgo.AddComponent<RectTransform>();
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.sizeDelta = Vector2.zero;
                var ttxt = tgo.AddComponent<Text>();
                ttxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                ttxt.text = "X"; ttxt.fontSize = 16; ttxt.color = Color.white;
                ttxt.alignment = TextAnchor.MiddleCenter;
            }
        }
    }
}
