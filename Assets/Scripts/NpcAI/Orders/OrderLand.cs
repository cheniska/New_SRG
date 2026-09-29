using UnityEngine;
using SRG.Core;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Ships.Services;
using SRG.Utils;

namespace SRG.NpcAI.Orders
{
    /// <summary>
    /// Атомарная посадка/стыковка НПС на любую <see cref="ILandingSite"/>: планета,
    /// станция или корабль-носитель. Двухфазный цикл (SR2HD §3–§7):
    ///   • PrepareForTurn (ShipVisualController) ставит ship.LandingPhase=Fading, когда
    ///     хвост анимации хода попадает внутрь <see cref="ILandingSite.LandingRadius"/>.
    ///   • На следующем Execute этот ордер увидит Fading → выполнит финализацию:
    ///       — для планеты: проставит LandedPlanetUid;
    ///       — для корабля: <see cref="ShipDockingService.Land"/> (LandedOnShipUid + freeze).
    /// Для НПС в чужой звезде (визуальный слой не работает) — fallback: финализация
    /// по прямой проверке distSq &lt;= LandingRadius², без анимации.
    ///
    /// Название параметра сохранено (<c>planetUid</c>) для совместимости с существующими
    /// вызывающими; фактически принимается UID любой посадочной цели.
    /// </summary>
    public class OrderLand : NpcOrder
    {
        private readonly string _targetUid;
        private bool _done;

        public OrderLand(string targetUid) => _targetUid = targetUid;
        public string PlanetUid => _targetUid;
        public string TargetUid => _targetUid;

        public override bool Execute(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            if (_done) return true;

            var site = LandingSiteRegistry.Find(star, _targetUid);
            if (site == null)
            {
                // Цель исчезла — снимаем оба возможных «желания сесть» и завершаемся.
                ship.LandingPlanetUid  = null;
                ship.LandingCarrierUid = null;
                ship.LandingPhase      = LandingPhase.None;
                _done = true;
                return true;
            }

            // Фаза 2 (финализация): PrepareForTurn прошлого хода поставил Fading,
            // либо мы прибыли в чужой звезде без визуального слоя.
            bool playerStar = star == GalaxyManager.Instance?.CurrentStar;
            bool offscreenArrival = !playerStar && site.IsWithinLandingRange(ship.Position);

            if (ship.LandingPhase == LandingPhase.Fading || offscreenArrival)
            {
                FinalizeArrival(ship, site);
                _done = true;
                return true;
            }

            // Фаза 1 (подлёт): помечаем цель посадки и прокладываем кинематический курс.
            // Планета — по SR2HD §5.2.C (jitter ±20° по seed-факторам). Корабль-носитель —
            // просто в центр (движется, но перепрокладка идёт следующим Execute).
            Vector2 target;
            if (site is PlanetData planet)
            {
                ship.LandingPlanetUid = planet.Uid;
                target = PlanetGeometry.PredictNpcLandingTarget(ship, planet);
            }
            else
            {
                ship.LandingCarrierUid = site.Uid;
                target = site.CenterPosition;
            }
            SetMoveTarget(ship, star, target);
            return false;
        }

        private static void FinalizeArrival(ShipData ship, ILandingSite site)
        {
            if (site is PlanetData planet)
            {
                ship.LandedPlanetUid = planet.Uid;
                ship.LandingPlanetUid  = null;
                ship.LandingCarrierUid = null;
                ship.LandingPhase      = LandingPhase.None;
                ship.TargetPosition    = ship.Position;
                ship.TargetQueue.Clear();
                ship.Waypoints.Clear();
                ship.WaypointIndex     = 0;
                return;
            }
            if (site is ShipData carrier)
            {
                // Единый путь стыковки: Land ставит LandedOnShipUid + freeze + сбрасывает Landing*Uid/Phase.
                ShipDockingService.Land(ship, carrier);
                ship.TargetQueue.Clear();
                ship.Waypoints.Clear();
                ship.WaypointIndex = 0;
            }
        }

        public override bool IsCompleted(ShipData ship, StarData star) => _done;

        public override string DebugName => $"Land({SpriteUtility.ShortId(_targetUid)})";
    }
}
