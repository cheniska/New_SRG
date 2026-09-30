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
        // Наполнение экранов данными
        // ─────────────────────────────────────────────────────────

        private void RefreshAll()
        {
            if (_site == null) return;
            _titleText.text = _site.Name;

            RefreshOverview();
            RefreshHangar();
            RefreshInfoCenter();
            UpdateMoneyDisplay();
            // магазины обновляются при переключении на них
        }

        private void ShowScreen(string screenId)
        {
            foreach (var kv in _panels)
                kv.Value.SetActive(kv.Key == screenId);

            _currentScreen = screenId;

            // Подсветка кнопки
            foreach (var kv in _navBtns)
            {
                var img = kv.Value.GetComponent<Image>();
                if (img != null) img.color = (kv.Key == screenId) ? ColBtnSel : ColBtn;
            }

            // Заголовок экрана: сначала LandingUIConfig.ScreenTitles профиля (даёт разные подписи
            // для Planet/Station/Carrier без клонирования Screens-словаря), затем общий
            // PlanetUIConfig.Screens[id].Title, затем screenId как последний fallback.
            _screenTitleText.text = ResolveScreenTitle(screenId);

            // Ленивое обновление динамических экранов
            if (screenId == SCR_GOODS) RefreshGoodsShop();
            else if (screenId == SCR_EQUIP) RefreshEquipShop();
            else if (screenId == SCR_HANGAR) RefreshHangar();
            else if (screenId == SCR_INFO) RefreshInfoCenter();
        }
    }
}
