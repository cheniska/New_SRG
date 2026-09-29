using UnityEngine;
using SRG.Galaxy;

namespace SRG.Equipment
{
    public static partial class EquipmentSystem
    {
        public static int GetCurrentFuel(ShipData ship)
        {
            var tank = GetEquipped(ship, SlotKeys.FuelTank);
            return tank?.CurrentFuel ?? 0;
        }

        public static int GetFuelCapacity(ShipData ship)
        {
            var tank = GetEquipped(ship, SlotKeys.FuelTank);
            return tank != null ? Mathf.RoundToInt(tank.GetParam("Capacity")) : 0;
        }

        public static bool ConsumeFuel(ShipData ship, int amount)
        {
            var tank = GetEquipped(ship, SlotKeys.FuelTank);
            if (tank == null || !tank.IsWorking || tank.CurrentFuel < amount) return false;
            tank.CurrentFuel -= amount;
            return true;
        }

        /// <summary>Залить amount единиц в бак (не превышая Capacity). Возвращает фактически залитое.</summary>
        public static int AddFuel(ShipData ship, int amount)
        {
            if (amount <= 0) return 0;
            var tank = GetEquipped(ship, SlotKeys.FuelTank);
            if (tank == null || !tank.IsWorking) return 0;
            int cap = Mathf.RoundToInt(tank.GetParam("Capacity"));
            int free = Mathf.Max(0, cap - tank.CurrentFuel);
            int take = Mathf.Min(amount, free);
            if (take <= 0) return 0;
            tank.CurrentFuel += take;
            return take;
        }

        public static int GetJumpRange(ShipData ship)
        {
            var engine = GetEquipped(ship, SlotKeys.Engine);
            return engine != null && engine.IsWorking ? Mathf.RoundToInt(engine.GetParam("JumpRange")) : 0;
        }

        /// <summary>Базовые условия для гиперпрыжка: живой корабль, двигатель с JumpRange,
        /// рабочий бак, ненулевое топливо. Без проверки дистанции до конкретной цели.</summary>
        public static bool HasHyperjumpCapability(ShipData ship)
        {
            if (ship == null || ship.CurrentHull <= 0) return false;
            if (GetJumpRange(ship) <= 0) return false;
            var tank = GetEquipped(ship, SlotKeys.FuelTank);
            if (tank == null || !tank.IsWorking) return false;
            return GetCurrentFuel(ship) > 0;
        }

    }
}
