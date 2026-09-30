using System.Collections.Generic;
using System.Text;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Ships.Services;

namespace SRG.UI.Logic
{
    /// <summary>Подписи кнопок ангара с актуальными ценами.</summary>
    public sealed class HangarButtonLabels
    {
        public string Refuel;
        public string Repair;
        public string Reload;
    }

    /// <summary>Чужой корабль, пришвартованный к объекту.</summary>
    public sealed class DockedShipModel
    {
        public ShipData Ship;
        public string Label;
        public string Race;
    }

    /// <summary>
    /// Логика экрана ангара (без UI): цены услуг, заправка/ремонт/зарядка, сводка по кораблю,
    /// список пришвартованных кораблей.
    /// </summary>
    public sealed class HangarPresenter
    {
        private readonly ILandingSite _site;

        public HangarPresenter(ILandingSite site) => _site = site;

        public static HangarButtonLabels ButtonLabels(ShipData ship)
        {
            int missingFuel = ship == null ? 0 : EquipmentSystem.GetFuelCapacity(ship) - EquipmentSystem.GetCurrentFuel(ship);
            int repairCost = RepairService.EstimateHullRepairCost(ship);

            string reload;
            if (ship == null || !AmmoService.HasReloadableWeapons(ship))
                reload = "Зарядить оружие\n(нет ракет)";
            else
            {
                int cost = AmmoService.EstimateReloadCost(ship);
                reload = cost > 0 ? $"Зарядить оружие\n{cost} кр." : "Зарядить оружие\n(полный)";
            }

            return new HangarButtonLabels
            {
                Refuel = missingFuel > 0 ? $"Дозаправиться\n{missingFuel * FuelService.FuelCostPerUnit} кр." : "Дозаправиться\n(полный)",
                Repair = repairCost > 0 ? $"Починить корпус\n{repairCost} кр." : "Починить корпус\n(цел)",
                Reload = reload,
            };
        }

        public UiActionResult Refuel(ShipData ship)
        {
            if (ship == null || _site == null) return UiActionResult.None();
            int missing = EquipmentSystem.GetFuelCapacity(ship) - EquipmentSystem.GetCurrentFuel(ship);
            if (missing <= 0) return UiActionResult.Fail("[Ангар] Бак уже полон.");

            int filled = FuelService.RefillToFull(ship, _site);
            if (filled <= 0) return UiActionResult.Fail("[Ангар] Недостаточно кредитов для дозаправки.");
            return UiActionResult.Ok($"[Ангар] Дозаправка: +{filled} ед. за {filled * FuelService.FuelCostPerUnit} кр.");
        }

        public UiActionResult RepairHull(ShipData ship)
        {
            if (ship == null || _site == null) return UiActionResult.None();
            if (ship.CurrentHull >= ship.MaxHull) return UiActionResult.Fail("[Ангар] Корпус цел.");

            int restored = RepairService.RepairHullOnly(ship, _site);
            if (restored <= 0) return UiActionResult.Fail("[Ангар] Недостаточно кредитов для ремонта.");
            return UiActionResult.Ok($"[Ангар] Ремонт корпуса: +{restored} HP.");
        }

        public UiActionResult ReloadWeapons(ShipData ship)
        {
            if (ship == null || _site == null) return UiActionResult.None();
            if (!AmmoService.HasReloadableWeapons(ship)) return UiActionResult.Fail("[Ангар] Ракетного оружия нет.");
            if (AmmoService.EstimateReloadCost(ship) <= 0) return UiActionResult.Fail("[Ангар] Боезапас полон.");

            int loaded = AmmoService.ReloadAllAmmo(ship, _site);
            if (loaded <= 0) return UiActionResult.Fail("[Ангар] Недостаточно кредитов для зарядки.");
            return UiActionResult.Ok($"[Ангар] Заряжено снарядов: {loaded}.");
        }

        /// <summary>Сводка по кораблю игрока (rich text).</summary>
        public static string ShipSummary(ShipData ship)
        {
            if (ship == null) return "Нет данных о корабле.";

            var sb = new StringBuilder(1024);
            sb.AppendLine($"<b>Корабль:</b> {ship.Name}");
            sb.AppendLine($"HP      : {ship.CurrentHull} / {ship.MaxHull}");
            sb.AppendLine($"Скорость: {ship.ActualSpeed:F0}");
            sb.AppendLine($"Кредиты : {ship.Money:N0}");
            sb.AppendLine();
            sb.AppendLine($"Грузовой трюм: свободно {EquipmentSystem.GetFreeSpace(ship)} ед.");
            sb.AppendLine();
            sb.AppendLine("─── Снаряжение ───");

            if (ship.Equipment?.Slots != null)
            {
                foreach (var slotKey in ship.Equipment.Slots.Keys)
                {
                    string uid = ship.Equipment.GetItemUid(slotKey);
                    if (uid == null || !ship.AllItems.TryGetValue(uid, out var item)) continue;
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
            return sb.ToString();
        }

        /// <summary>Чужие живые корабли, пришвартованные к этому объекту (к планете —
        /// LandedPlanetUid, к станции/носителю — LandedOnShipUid).</summary>
        public List<DockedShipModel> DockedShips(StarData star)
        {
            var result = new List<DockedShipModel>();
            if (star?.Ships == null || _site == null) return result;

            string uid = _site.Uid;
            bool siteIsShip = _site is ShipData;
            foreach (var s in star.Ships)
            {
                if (s == null || s.IsPlayer || s.CurrentHull <= 0) continue;
                bool onSite = siteIsShip ? s.LandedOnShipUid == uid : s.LandedPlanetUid == uid;
                if (!onSite) continue;
                string type = string.IsNullOrEmpty(s.ShipTypeId) ? "—" : s.ShipTypeId;
                result.Add(new DockedShipModel
                {
                    Ship = s,
                    Label = $"[{type}] {s.Name}",
                    Race = string.IsNullOrEmpty(s.Race) ? "—" : s.Race,
                });
            }
            return result;
        }

        public static string DockedHeader(int count) => count == 0 ? "Корабли на объекте: нет" : $"Корабли на объекте: {count}";
    }
}
