using UnityEngine;
using SRG.Galaxy;

namespace SRG.Ships.Services
{
    /// <summary>
    /// Единый «взлёт с планеты» для NPC-действий. До июля 2026 повторялся в
    /// ActionLandResupply.TickDepart и ActionGoodsTrader.HandleLanded:
    /// RecordLastVisitAndLaunch + outward TargetPosition + clear queue/waypoints.
    /// </summary>
    public static class LaunchService
    {
        /// <summary>Отделяет корабль от планеты и задаёт короткую траекторию наружу от центра
        /// (в 1.5 ед. по нормали от текущей позиции; если позиция в нуле — по +X).
        /// Не меняет визуал/фазу — только данные ShipData.</summary>
        public static void Depart(ShipData ship)
        {
            if (ship == null) return;
            ship.RecordLastVisitAndLaunch();
            Vector2 pos = ship.Position;
            if (pos.sqrMagnitude > 0.0001f)
                ship.TargetPosition = pos + pos.normalized * 1.5f;
            else
                ship.TargetPosition = new Vector2(1.5f, 0f);
            ship.TargetQueue.Clear();
            ship.Waypoints.Clear();
            ship.WaypointIndex = 0;
        }
    }
}
