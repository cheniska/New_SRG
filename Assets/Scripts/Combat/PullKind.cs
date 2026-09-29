using SRG.Galaxy;

namespace SRG.Combat
{
    /// <summary>
    /// Режим текущего притягивания цели (<see cref="ShipData.PullMode"/>). Определяет, какой
    /// механизм/оборудование инициировал Pull и что произойдёт по достижении источника.
    ///
    /// <list type="bullet">
    ///   <item><b>None</b> — цель не притягивается (свободна).</item>
    ///   <item><b>Pickup</b> — захват грузовым захватом (<see cref="PickupSystem"/>). Цель летит
    ///         к ЦЕНТРУ буксирующего и при касании распаковывается в его инвентарь; контейнер
    ///         удаляется. Любой вес (если CargoGrabber.Power позволяет).</item>
    ///   <item><b>Tow</b> — буксир (<see cref="TowSystem"/>). Цель летит к свободному ЯКОРЮ
    ///         (Back/Front/Left/Right) и крепится там как груз; не разгружается. Работает с
    ///         кораблями и тяжёлыми предметами.</item>
    /// </list>
    /// </summary>
    public enum PullKind
    {
        None = 0,
        Pickup = 1,
        Tow = 2,
    }
}
