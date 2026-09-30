using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.NpcAI.Orders;
using SRG.Ships.Movement;
using SRG.Utils;

namespace SRG.NpcAI.Actions
{
    /// <summary>
    /// Универсальное «искать укрытие» — реакция ИИ на InFear. Выбирает приоритет по классу
    /// и наличию укрытий в звезде:
    ///   1) FriendlyStation в текущей звезде (у пиратов и остальных — одна и та же логика);
    ///   2) FriendlyPlanet в текущей звезде (в т.ч. для пиратов, если станции нет);
    ///   3) HyperNavigation → ближайшая соседняя система с не-враждебным контроллёром;
    ///   4) fallback ActionFlee от главной угрозы — если ни укрытий, ни куда прыгать.
    ///
    /// Не хранит cross-turn данные о конкретной посадочной цели: каждый Tick пересобирает
    /// ChooseInitialPhase, чтобы адаптироваться к смене обстановки (станция уничтожена,
    /// планета захвачена, друг прилетел рядом). Внутри выбранной фазы делегирует в
    /// OrderLand / HyperNavigation.ActionToward / ActionFlee — они сами тикают до конца.
    ///
    /// Для приземления делегирует в ActionLandResupply на завершающей фазе, чтобы
    /// корабль дозаправился/починился/апгрейднулся, а не «встал у порога» — но только
    /// когда уже приземлился (LandedPlanetUid установлен). Иначе просто OrderLand до касания.
    /// </summary>
    public class ActionSeekShelter : NpcAction
    {
        private enum Phase { Choose, Land, Jump, Flee }
        private Phase _phase = Phase.Choose;
        private OrderLand _landOrder;
        private ActionLandResupply _resupply;     // подхватываем после посадки на планету
        private NpcAction _jumpAction;
        private ActionFlee _fleeAction;
        private readonly string _mainThreatUid;   // для fallback Flee (может быть null → OrderFlee выберет ближайшего)

        public ActionSeekShelter(string mainThreatUid = null) => _mainThreatUid = mainThreatUid;

        public override string DebugName => $"SeekShelter(phase={_phase})";

        public override bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            if (ship == null || star == null) { IsCompleted = true; return true; }

            if (_phase == Phase.Choose) _phase = ChoosePhase(ship, star);

            switch (_phase)
            {
                case Phase.Land: return TickLand(ship, star, ctx);
                case Phase.Jump: return TickJump(ship, star, ctx);
                case Phase.Flee: return TickFlee(ship, star, ctx);
                default:         IsCompleted = true; return true;
            }
        }

        /// <summary>
        /// Выбор приоритета укрытия. Одинаковая цепочка для всех классов кораблей
        /// (Pirate, Warrior, Military, Mercenary, Transport/Liner/Diplomat/Civilian):
        /// станция → планета → гиперпрыжок → fallback flee. Классовые особенности, если
        /// понадобятся, добавляются модами через мод-точки.
        /// </summary>
        private Phase ChoosePhase(ShipData ship, StarData star)
        {
            // Если уже на планете (карго-хук/поздний тик) — прогоним resupply-хвост.
            if (!string.IsNullOrEmpty(ship.LandedPlanetUid))
            {
                _resupply = new ActionLandResupply();
                return Phase.Land;
            }

            // Единый выбор укрытия: station → planet → jump → fallback flee.
            // ShelterPicker живёт в NpcAI/ShelterPicker.cs.
            var pick = ShelterPicker.Pick(ship, star, includeJump: true);
            switch (pick.Kind)
            {
                case ShelterPicker.ShelterKind.Station:
                    _landOrder = new OrderLand(((ShipData)pick.Payload).Uid);
                    return Phase.Land;
                case ShelterPicker.ShelterKind.Planet:
                    _landOrder = new OrderLand(((PlanetData)pick.Payload).Uid);
                    return Phase.Land;
                case ShelterPicker.ShelterKind.FriendlyStar:
                    _jumpAction = HyperNavigation.ActionToward(ship, star, ((StarData)pick.Payload).Uid);
                    if (_jumpAction != null) return Phase.Jump;
                    break;
            }

            _fleeAction = new ActionFlee(_mainThreatUid);
            return Phase.Flee;
        }

        private bool TickLand(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            // Если уже приземлились (пришли через LandedPlanetUid или дошли OrderLand'ом) —
            // делегируем в ActionLandResupply: он выполнит продажу-ремонт-заправку-взлёт.
            if (!string.IsNullOrEmpty(ship.LandedPlanetUid))
            {
                _resupply ??= new ActionLandResupply();
                bool done = _resupply.Tick(ship, star, ctx);
                if (done) { IsCompleted = true; return true; }
                return false;
            }

            // Пристыкованы к станции/носителю: LandedOnShipUid стоит, Brain следующий тик
            // пропустит нас через carrier-gate — делать больше нечего.
            if (!string.IsNullOrEmpty(ship.LandedOnShipUid))
            {
                IsCompleted = true;
                return true;
            }

            if (_landOrder == null) { IsCompleted = true; return true; }
            _landOrder.Execute(ship, star, ctx);
            return false;
        }

        private bool TickJump(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            if (_jumpAction == null) { IsCompleted = true; return true; }
            bool done = _jumpAction.Tick(ship, star, ctx);
            if (done)
            {
                // После прыжка в новую систему — переоценить: возможно, укрытие уже здесь.
                _jumpAction = null;
                _phase = Phase.Choose;
                IsCompleted = true; // Brain пересоберёт activity в следующем ходу
                return true;
            }
            return false;
        }

        private bool TickFlee(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            if (_fleeAction == null) { IsCompleted = true; return true; }
            bool done = _fleeAction.Tick(ship, star, ctx);
            if (done) { IsCompleted = true; return true; }
            return false;
        }
    }
}
