using UnityEngine;
using UnityEngine.UI;
using System.Text;
using SRG.Config;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Controllers;
using SRG.UI.HUD;
using SRG.Simulation;

namespace SRG.UI.Screens
{
    public class InventoryUIController : MonoBehaviour
    {
        [Header("Настройки")] public GameSettingsConfig Settings;

        [Tooltip("Fallback-клавиша если Settings не назначен.")]
        public KeyCode FallbackKey = KeyCode.I;
        private GameObject _panel;
        private Text _statsText;
        private ShipFormView _formView;
        private bool _isVisible;
        private KeyCode _toggleKey;
        private void Start()
        {
            _toggleKey = (Settings != null) ? Settings.InventoryKey : FallbackKey;
            BuildUI();
            _panel.SetActive(false);
        }

        private void Update()
        {

            // Ход считается в фоне — мир меняется, не читаем и не трогаем его (см. GameWorld.IsCalculating).

            if (GameWorld.IsCalculating) return;
            if (GameConsoleController.IsOpen) return;
            if (!Input.GetKeyDown(_toggleKey)) return;
            if (ShipScanUIController.Instance != null && ShipScanUIController.Instance.IsOpen) return;
            Toggle();
        }

        private void OnEnable()
        {
            GameWorld.OnTurnCalculate += OnTurnCalculate;
        }

        private void OnDisable()
        {
            GameWorld.OnTurnCalculate -= OnTurnCalculate;
        }

        private void OnTurnCalculate(TurnAnimationData _)
        {
            if (_isVisible)
            {
                _formView?.DropHandToSource();
                _isVisible = false;
                _panel.SetActive(false);
            }
        }

        public void Toggle()
        {
            if (GalaxyManager.Instance?.Phase == TurnPhase.Simulation)
            {
                Debug.Log("[InventoryUIController] Cannot open inventory during turn animation.");
                return;
            }

            _isVisible = !_isVisible;
            _panel.SetActive(_isVisible);
            if (_isVisible)
                Refresh();
        }

        private void Refresh()
        {
            var ship = PlayerShip.Instance?.ShipData;
            if (ship == null)
            {
                _statsText.text = "Нет данных о корабле.";
                _formView?.SetShip(null);
                return;
            }

            _statsText.text = BuildStatsText(ship);
            _formView?.SetShip(ship);
        }

        private void RefreshStatsOnly()
        {
            var ship = PlayerShip.Instance?.ShipData;
            if (ship == null || _statsText == null) return;
            _statsText.text = BuildStatsText(ship);
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
            sb.AppendLine($"{ship.Name}");
            sb.AppendLine($"HP: {ship.CurrentHull}/{ship.MaxHull}    Скорость: {speed:F0}    Деньги: {ship.Money:N0} кр.");
            sb.AppendLine($"Вес: {totalW} (снар {equippedW} + груз {inventoryW})    Корпус: {hullCap} свободно: {freeSpace}");
            if (fuelCap > 0)
                sb.AppendLine($"Топливо: {fuel}/{fuelCap}    Прыжок: {jumpRange}");
            return sb.ToString().TrimEnd();
        }

        private void BuildUI()
        {
            _panel = new GameObject("InventoryPanel");
            _panel.transform.SetParent(transform, false);

            var panelRt = _panel.AddComponent<RectTransform>();
            panelRt.anchorMin = Vector2.zero;
            panelRt.anchorMax = Vector2.one;
            panelRt.offsetMin = Vector2.zero;
            panelRt.offsetMax = Vector2.zero;

            _panel.AddComponent<Image>().color = new Color(0.02f, 0.04f, 0.07f, 0.92f);

            _formView = _panel.AddComponent<ShipFormView>();
            _formView.Build(panelRt);
            _formView.OnEquipmentChanged = RefreshStatsOnly;

            {
                var go = new GameObject("Stats");
                go.transform.SetParent(_panel.transform, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(-80f, 70f);
                rt.anchoredPosition = new Vector2(0f, -8f);

                _statsText = go.AddComponent<Text>();
                _statsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                _statsText.fontSize = 15;
                _statsText.color = new Color(0.78f, 0.90f, 1f);
                _statsText.alignment = TextAnchor.UpperLeft;
                _statsText.horizontalOverflow = HorizontalWrapMode.Wrap;
                _statsText.verticalOverflow = VerticalWrapMode.Overflow;
                _statsText.raycastTarget = false;
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
                btn.onClick.AddListener(() =>
                {
                    _formView?.DropHandToSource();
                    _isVisible = false;
                    _panel.SetActive(false);
                });

                var tgo = new GameObject("X");
                tgo.transform.SetParent(go.transform, false);
                var trt = tgo.AddComponent<RectTransform>();
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.sizeDelta = Vector2.zero;
                var ttxt = tgo.AddComponent<Text>();
                ttxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                ttxt.text = "X"; ttxt.fontSize = 16; ttxt.color = Color.white;
                ttxt.alignment = TextAnchor.MiddleCenter;
                ttxt.raycastTarget = false;
            }
        }
    }
}
