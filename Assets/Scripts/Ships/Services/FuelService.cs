using UnityEngine;
using SRG.Core;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy;

namespace SRG.Ships.Services
{
    /// <summary>
    /// Заправка корабля топливом у планеты. Планеты имеют бесконечный запас топлива,
    /// корабль платит за единицу — см. FuelCostPerUnit. Используется автоматически
    /// трейдерами при посадке, может быть вызвана из UI для игрока.
    /// </summary>
    public static class FuelService
    {
        /// <summary>Цена 1 единицы топлива на любой планете (кредитов).</summary>
        public const int FuelCostPerUnit = 5;

        /// <summary>
        /// Залить корабль до максимума на данной посадочной цели (планета/станция/носитель).
        /// Списывает деньги корабля, пишет в EconomicLog при заправке &gt;0.
        /// Возвращает фактическое число залитых единиц.
        /// </summary>
        public static int RefillToFull(ShipData ship, ILandingSite site)
        {
            if (ship == null || site == null) return 0;
            int cur = EquipmentSystem.GetCurrentFuel(ship);
            int cap = EquipmentSystem.GetFuelCapacity(ship);
            int missing = cap - cur;
            if (missing <= 0) return 0;

            int affordable = Mathf.Max(0, ship.Money) / FuelCostPerUnit;
            int take = Mathf.Min(missing, affordable);
            if (take <= 0) return 0;

            int filled = EquipmentSystem.AddFuel(ship, take);
            if (filled <= 0) return 0;

            int cost = filled * FuelCostPerUnit;
            ship.Money -= cost;

            int turn = GalaxyManager.Instance?.GeneratedGalaxy?.CurrentTurn ?? 0;
            EconomicLog.Trade(turn, EconomicLog.Safe(ship.Name), "REFUEL",
                $"site={EconomicLog.Safe(site.Name)} units={filled} unit_cost={FuelCostPerUnit} " +
                $"total={cost} money_after={ship.Money} fuel_after={cur + filled}/{cap}");
            return filled;
        }
    }
}
