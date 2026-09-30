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
using SRG.UI.Logic;

namespace SRG.UI.Screens
{
    public partial class PlanetUIController
    {
        // ── Hangar ─────────────────────────────────────────────────
        //
        // Ряд кнопок-действий сверху (взлёт/дозаправка/ремонт корпуса/дозарядка ракет —
        // с актуальными ценами) + текстовая сводка по кораблю под ними.

        private void BuildHangarPanel(Transform parent)
        {
            var panel = MakePanel(parent, $"Screen_{SCR_HANGAR}", Color.clear);
            SetAnchors(panel, 0f, 0f, 1f, 1f);
            panel.SetActive(false);
            _panels[SCR_HANGAR] = panel;

            // Ряд кнопок-действий (верхняя полоса)
            var launchBtn = MakeButton(panel.transform, "HangarLaunch", "Взлететь", 12, ColBtnAlt);
            SetAnchors(launchBtn.gameObject, 0.005f, 0.9f, 0.245f, 0.99f);
            launchBtn.onClick.AddListener(LaunchFromHangar);

            var refuelBtn = MakeButton(panel.transform, "HangarRefuel", "Дозаправиться", 12, ColBtn);
            SetAnchors(refuelBtn.gameObject, 0.255f, 0.9f, 0.495f, 0.99f);
            refuelBtn.onClick.AddListener(RefuelShip);
            _hangarRefuelLabel = refuelBtn.GetComponentInChildren<Text>();

            var repairBtn = MakeButton(panel.transform, "HangarRepair", "Починить корпус", 12, ColBtn);
            SetAnchors(repairBtn.gameObject, 0.505f, 0.9f, 0.745f, 0.99f);
            repairBtn.onClick.AddListener(RepairHull);
            _hangarRepairLabel = repairBtn.GetComponentInChildren<Text>();

            var reloadBtn = MakeButton(panel.transform, "HangarReload", "Зарядить оружие", 12, ColBtn);
            SetAnchors(reloadBtn.gameObject, 0.755f, 0.9f, 0.995f, 0.99f);
            reloadBtn.onClick.AddListener(ReloadWeapons);
            _hangarReloadLabel = reloadBtn.GetComponentInChildren<Text>();

            // Секция «Корабли на объекте»: перечень пришвартованных сюда чужих кораблей
            // (для планеты — LandedPlanetUid, для станции/носителя — LandedOnShipUid), у каждого
            // строка с кнопкой «Сканировать» (открывает <see cref="ShipScanUIController"/>).
            _hangarShipsHeader = MakeText(panel.transform, "HangarShipsHeader",
                "Корабли на объекте:", 12, FontStyle.Bold, ColAccent);
            SetAnchors(_hangarShipsHeader.gameObject, 0.005f, 0.865f, 0.995f, 0.895f);

            var shipsScrollPanel = MakePanel(panel.transform, "HangarShipsScrollPanel", Color.clear);
            SetAnchors(shipsScrollPanel, 0f, 0.68f, 1f, 0.86f);
            _hangarShipsContent = BuildShopScroll(shipsScrollPanel.transform,
                "HangarShipsScroll", 2f, out _);

            // Текстовая сводка по своему кораблю — ниже списка.
            var textArea = MakePanel(panel.transform, "HangarText", Color.clear);
            SetAnchors(textArea, 0f, 0f, 1f, 0.67f);
            var scroll = BuildScrollRect(textArea.transform, out var contentText);
            _scrollRects[SCR_HANGAR] = scroll;
            _bodyTexts[SCR_HANGAR] = contentText;
        }

        private void LaunchFromHangar()
        {
            if (PlayerManager.Instance == null) return;
            var ship = PlayerShip.Instance?.ShipData;
            ship?.RecordLastVisitAndLaunch();
            PlayerManager.Instance.LeavePlanet();
        }

        // Логика ангара — HangarPresenter (SRG.UI.Logic); здесь только отрисовка и лог.

        private void RefuelShip()
            => ApplyHangarResult(new HangarPresenter(_site).Refuel(PlayerShip.Instance?.ShipData));

        private void RepairHull()
            => ApplyHangarResult(new HangarPresenter(_site).RepairHull(PlayerShip.Instance?.ShipData));

        private void ReloadWeapons()
            => ApplyHangarResult(new HangarPresenter(_site).ReloadWeapons(PlayerShip.Instance?.ShipData));

        private void ApplyHangarResult(UiActionResult result)
        {
            foreach (var m in result.Messages) GameLog.Add(m);
            if (!result.Success) return;
            UpdateMoneyDisplay();
            RefreshHangar();
        }

        // Обновляет метки кнопок ангара актуальными ценами (вызывается из RefreshHangar).
        private void RefreshHangarButtons(ShipData ship)
        {
            var labels = HangarPresenter.ButtonLabels(ship);
            if (_hangarRefuelLabel != null) _hangarRefuelLabel.text = labels.Refuel;
            if (_hangarRepairLabel != null) _hangarRepairLabel.text = labels.Repair;
            if (_hangarReloadLabel != null) _hangarReloadLabel.text = labels.Reload;
        }

        private void RefreshHangar()
        {
            if (!_bodyTexts.TryGetValue(SCR_HANGAR, out var txt)) return;
            var ship = PlayerShip.Instance?.ShipData;
            RefreshHangarButtons(ship);
            RefreshHangarShips();
            txt.text = HangarPresenter.ShipSummary(ship);
        }

        /// <summary>Заполняет секцию «Корабли на объекте» (см. <see cref="HangarPresenter.DockedShips"/>).
        /// У каждой строки — кнопка «Сканировать», открывающая <see cref="ShipScanUIController"/>.</summary>
        private void RefreshHangarShips()
        {
            if (_hangarShipsContent == null) return;
            ClearChildren(_hangarShipsContent);

            var docked = new HangarPresenter(_site).DockedShips(GalaxyManager.Instance?.CurrentStar);
            foreach (var d in docked)
            {
                var row = MakeShopRow(_hangarShipsContent,
                    $"HangarShipRow_{d.Ship.Uid}", 28f, 6f, new RectOffset(6, 6, 2, 2));
                MakeRowLabel(row, d.Label, 220f, flexible: true);
                MakeRowLabel(row, d.Race, 80f);
                var scanShip = d.Ship;
                MakeSmallButton(row, "Сканировать", ColBtn, () => OpenShipScan(scanShip));
            }
            UpdateHangarShipsHeader(docked.Count);
        }

        private void UpdateHangarShipsHeader(int count)
        {
            if (_hangarShipsHeader == null) return;
            _hangarShipsHeader.text = HangarPresenter.DockedHeader(count);
        }

        private static void OpenShipScan(ShipData ship)
        {
            if (ship == null) return;
            ShipScanUIController.Instance?.OpenFor(ship);
        }
    }
}
