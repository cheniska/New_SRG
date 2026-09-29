using SRG.Galaxy;
using SRG.Utils;

namespace SRG.Equipment
{
    /// <summary>
    /// Радар и сканер. Радар — «видимость» в текущей системе (дистанция в мировых
    /// единицах). Сканер — «сила», выражена в процентах и используется как
    /// прибавка к shield-penetration оружия (см. <see cref="Combat.WeaponSystem"/>);
    /// сканер не имеет дальности — для проверок «может ли корабль обнаружить»
    /// используется радар.
    /// </summary>
    public static partial class EquipmentSystem
    {
        /// <summary>Дальность радара в мировых единицах. Конфиг хранит SR — делим через SRUnits.</summary>
        public static float GetRadarRange(ShipData ship)
        {
            var radar = GetEquipped(ship, SlotKeys.Radar);
            if (radar == null || !radar.IsWorking) return 0f;
            return SRUnits.ToWorld(radar.GetParam("Range", 0f));
        }

        /// <summary>Сила сканера в процентах (0..100). Определяет, на сколько
        /// процентов сканер пробивает щит цели при выстрелах атакующего.</summary>
        public static float GetScannerPower(ShipData ship)
        {
            var scanner = GetEquipped(ship, SlotKeys.Scanner);
            if (scanner == null || !scanner.IsWorking) return 0f;
            return scanner.GetParam("Power", 0f);
        }
    }
}
