using System.Collections.Generic;
using SRG.Ships.Services;

namespace SRG.Galaxy
{
    /// <summary>
    /// Резолвер посадочных целей в пределах звезды. Позволяет landing pipeline работать
    /// с ILandingSite, не заботясь о том, планета это или корабль/станция: единый способ
    /// найти site по UID (см. <see cref="PlayerShip.Turn"/>, <see cref="Presentation.World.ShipVisualController"/>).
    ///
    /// Приоритет: сначала планеты, потом корабли. UID-конфликтов не должно быть (все UID
    /// генерятся через <see cref="System.Guid"/>), но порядок фиксирует поведение.
    /// </summary>
    public static class LandingSiteRegistry
    {
        /// <summary>Найти site по UID. null, если ничего не нашли.</summary>
        public static ILandingSite Find(StarData star, string uid)
        {
            if (star == null || string.IsNullOrEmpty(uid)) return null;

            var planets = star.Planets;
            if (planets != null)
                for (int i = 0; i < planets.Count; i++)
                    if (planets[i].Uid == uid) return planets[i];

            var ships = star.Ships;
            if (ships != null)
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i];
                    // Только «живые» посадочные цели: мёртвая станция/линкор — не посадка.
                    if (s.Uid == uid && s.CurrentHull > 0 && ShipDockingService.TypeAllowsLanding(s))
                        return s;
                }
            return null;
        }

        /// <summary>Разрешает текущую активную landing-цель корабля: LandingPlanetUid либо
        /// LandingCarrierUid (что первое непустое). null, если корабль ни к чему не подлетает.</summary>
        public static ILandingSite ResolveActiveTarget(ShipData ship, StarData star)
        {
            if (ship == null) return null;
            if (!string.IsNullOrEmpty(ship.LandingPlanetUid))
                return Find(star, ship.LandingPlanetUid);
            if (!string.IsNullOrEmpty(ship.LandingCarrierUid))
                return Find(star, ship.LandingCarrierUid);
            return null;
        }
    }
}
