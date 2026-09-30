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
    public partial class ShipFormView
    {
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
    }
}
