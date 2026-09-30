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

        private void RefuelShip()
        {
            var ship = PlayerShip.Instance?.ShipData;
            if (ship == null || _site == null) return;

            int missing = EquipmentSystem.GetFuelCapacity(ship) - EquipmentSystem.GetCurrentFuel(ship);
            if (missing <= 0) { GameLog.Add("[Ангар] Бак уже полон."); return; }

            int filled = FuelService.RefillToFull(ship, _site);
            if (filled <= 0) { GameLog.Add("[Ангар] Недостаточно кредитов для дозаправки."); return; }

            GameLog.Add(
                $"[Ангар] Дозаправка: +{filled} ед. за {filled * FuelService.FuelCostPerUnit} кр.");
            UpdateMoneyDisplay();
            RefreshHangar();
        }

        private void RepairHull()
        {
            var ship = PlayerShip.Instance?.ShipData;
            if (ship == null || _site == null) return;

            if (ship.CurrentHull >= ship.MaxHull) { GameLog.Add("[Ангар] Корпус цел."); return; }

            int restored = RepairService.RepairHullOnly(ship, _site);
            if (restored <= 0) { GameLog.Add("[Ангар] Недостаточно кредитов для ремонта."); return; }

            GameLog.Add($"[Ангар] Ремонт корпуса: +{restored} HP.");
            UpdateMoneyDisplay();
            RefreshHangar();
        }

        private void ReloadWeapons()
        {
            var ship = PlayerShip.Instance?.ShipData;
            if (ship == null || _site == null) return;

            if (!AmmoService.HasReloadableWeapons(ship))
            { GameLog.Add("[Ангар] Ракетного оружия нет."); return; }
            if (AmmoService.EstimateReloadCost(ship) <= 0)
            { GameLog.Add("[Ангар] Боезапас полон."); return; }

            int loaded = AmmoService.ReloadAllAmmo(ship, _site);
            if (loaded <= 0) { GameLog.Add("[Ангар] Недостаточно кредитов для зарядки."); return; }

            GameLog.Add($"[Ангар] Заряжено снарядов: {loaded}.");
            UpdateMoneyDisplay();
            RefreshHangar();
        }

        // Обновляет метки кнопок ангара актуальными ценами (вызывается из RefreshHangar).
        private void RefreshHangarButtons(ShipData ship)
        {
            if (_hangarRefuelLabel != null)
            {
                int missing = ship == null ? 0
                    : EquipmentSystem.GetFuelCapacity(ship) - EquipmentSystem.GetCurrentFuel(ship);
                _hangarRefuelLabel.text = missing > 0
                    ? $"Дозаправиться\n{missing * FuelService.FuelCostPerUnit} кр."
                    : "Дозаправиться\n(полный)";
            }
            if (_hangarRepairLabel != null)
            {
                int cost = RepairService.EstimateHullRepairCost(ship);
                _hangarRepairLabel.text = cost > 0
                    ? $"Починить корпус\n{cost} кр."
                    : "Починить корпус\n(цел)";
            }
            if (_hangarReloadLabel != null)
            {
                if (ship == null || !AmmoService.HasReloadableWeapons(ship))
                    _hangarReloadLabel.text = "Зарядить оружие\n(нет ракет)";
                else
                {
                    int cost = AmmoService.EstimateReloadCost(ship);
                    _hangarReloadLabel.text = cost > 0
                        ? $"Зарядить оружие\n{cost} кр."
                        : "Зарядить оружие\n(полный)";
                }
            }
        }

        private void RefreshHangar()
        {
            if (!_bodyTexts.TryGetValue(SCR_HANGAR, out var txt)) return;
            var ship = PlayerShip.Instance?.ShipData;
            RefreshHangarButtons(ship);
            RefreshHangarShips();
            if (ship == null) { txt.text = "Нет данных о корабле."; return; }

            var equipCfg = GalaxyManager.Instance?.Context?.ItemsConfig;
            var sb = new StringBuilder(1024);

            sb.AppendLine($"<b>Корабль:</b> {ship.Name}");
            sb.AppendLine($"HP      : {ship.CurrentHull} / {ship.MaxHull}");
            sb.AppendLine($"Скорость: {ship.ActualSpeed:F0}");
            sb.AppendLine($"Кредиты : {ship.Money:N0}");
            sb.AppendLine();

            float freeW = EquipmentSystem.GetFreeSpace(ship);
            sb.AppendLine($"Грузовой трюм: свободно {freeW} ед.");
            sb.AppendLine();
            sb.AppendLine("─── Снаряжение ───");

            if (ship.Equipment?.Slots != null)
            {
                foreach (var slotKey in ship.Equipment.Slots.Keys)
                {
                    string uid = ship.Equipment.GetItemUid(slotKey);
                    if (uid == null || !ship.AllItems.TryGetValue(uid, out var item))
                        continue;
                    string dur = item.NoWear ? "без износа" : $"{item.Durability}/{item.MaxDurability}";
                    sb.AppendLine($"  [{slotKey.Split('_')[0]}] {item.Name}  ({dur})  ТУ{item.TechLevel}");
                }
            }

            if (ship.Inventory?.Stacks?.Count > 0)
            {
                sb.AppendLine(); sb.AppendLine("─── Грузы ───");
                foreach (var kv in ship.Inventory.Stacks)
                    sb.AppendLine($"  {kv.Value.Name}: {kv.Value.TotalWeight} ед.  x{kv.Value.BasePrice} = {kv.Value.TotalPrice}");
            }

            txt.text = sb.ToString();
        }

        /// <summary>Заполняет секцию «Корабли на объекте»: все чужие ShipData, у которых
        /// LandedPlanetUid равен uid этой планеты (для планет) либо LandedOnShipUid равен
        /// uid этого корабля-носителя/станции. У каждой строки — кнопка «Сканировать»,
        /// открывающая <see cref="ShipScanUIController"/> для выбранного корабля.</summary>
        private void RefreshHangarShips()
        {
            if (_hangarShipsContent == null) return;
            ClearChildren(_hangarShipsContent);

            var star = GalaxyManager.Instance?.CurrentStar;
            if (star?.Ships == null || _site == null) { UpdateHangarShipsHeader(0); return; }

            string uid = _site.Uid;
            bool siteIsShip = _site is ShipData;

            int count = 0;
            for (int i = 0; i < star.Ships.Count; i++)
            {
                var s = star.Ships[i];
                if (s == null || s.IsPlayer) continue;
                if (s.CurrentHull <= 0) continue;
                bool onSite = siteIsShip
                    ? s.LandedOnShipUid == uid
                    : s.LandedPlanetUid == uid;
                if (!onSite) continue;

                var row = MakeShopRow(_hangarShipsContent,
                    $"HangarShipRow_{s.Uid}", 28f, 6f, new RectOffset(6, 6, 2, 2));

                string type = string.IsNullOrEmpty(s.ShipTypeId) ? "—" : s.ShipTypeId;
                string race = string.IsNullOrEmpty(s.Race) ? "—" : s.Race;
                MakeRowLabel(row, $"[{type}] {s.Name}", 220f, flexible: true);
                MakeRowLabel(row, race, 80f);
                var scanShip = s;
                MakeSmallButton(row, "Сканировать", ColBtn, () => OpenShipScan(scanShip));
                count++;
            }
            UpdateHangarShipsHeader(count);
        }

        private void UpdateHangarShipsHeader(int count)
        {
            if (_hangarShipsHeader == null) return;
            _hangarShipsHeader.text = count == 0
                ? "Корабли на объекте: нет"
                : $"Корабли на объекте: {count}";
        }

        private static void OpenShipScan(ShipData ship)
        {
            if (ship == null) return;
            ShipScanUIController.Instance?.OpenFor(ship);
        }
    }
}
