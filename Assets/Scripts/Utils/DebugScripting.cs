using SRG.Galaxy;
using SRG.Galaxy.Simulation;
using SRG.Simulation;

namespace SRG.Utils
{
    /// <summary>
    /// Короткие глобальные helper-функции для дебаг-скриптов и однострочников. Импортируйте
    /// <c>using static SRG.Utils.DebugScripting;</c> и обращайтесь без префикса —
    /// <c>Player()</c>, <c>CurStar(planet)</c>, <c>StarGalaxy(star)</c>. Композиция:
    /// <c>StarGalaxy(CurStar(Player()))</c> — галактика, в которой сейчас игрок.
    /// </summary>
    public static class DebugScripting
    {
        /// <summary>Корабль игрока. null если игрок ещё не заспавнен (главное меню).</summary>
        public static ShipData Player() => GameWorld.PlayerShip;

        /// <summary>Текущая (активно загруженная) галактика. Эквивалент
        /// <c>StarGalaxy(CurStar(Player()))</c> в single-galaxy runtime.</summary>
        public static GalaxyData Galaxy() => GameWorld.GeneratedGalaxy;

        /// <summary>
        /// Текущая звезда для любого объекта. Поддерживает:
        /// <see cref="StarData"/> (возвращает себя), <see cref="ShipData"/> (CurrentStar или по UID),
        /// <see cref="PlanetData"/> (ParentStar), <see cref="SatelliteData"/> (через хост-планету),
        /// <see cref="WormholeData"/> (через <see cref="WormholeService.TryFind"/>).
        /// null, если объект ни к какой звезде не привязан.
        /// </summary>
        public static StarData CurStar(object obj)
        {
            if (obj == null) return null;
            var g = GameWorld.GeneratedGalaxy;
            switch (obj)
            {
                case StarData s:
                    return s;

                case ShipData sh:
                    if (sh.CurrentStar != null) return sh.CurrentStar;
                    if (g != null && !string.IsNullOrEmpty(sh.CurrentStarUid)
                        && g.StarsMap.TryGetValue(sh.CurrentStarUid, out var byUid))
                        return byUid;
                    return null;

                case PlanetData p:
                    return p.ParentStar;

                case WormholeData wh:
                    return g != null && WormholeService.TryFind(g, wh.Uid, out _, out var src)
                        ? src : null;

                default:
                    return null;
            }
        }

        /// <summary>
        /// Галактика, к которой принадлежит звезда. В однографической реализации это
        /// <see cref="Galaxy()"/>, если звезда есть в её StarsMap; для чужой звезды — null.
        /// Функция существует для композиции вида <c>StarGalaxy(CurStar(Player()))</c> и для
        /// будущей мультигалактической системы.
        /// </summary>
        public static GalaxyData StarGalaxy(StarData star)
        {
            var g = GameWorld.GeneratedGalaxy;
            if (star == null) return g;
            if (g == null) return null;
            return g.StarsMap.ContainsKey(star.Uid) ? g : null;
        }
    }
}
