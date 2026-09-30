using System.Collections.Generic;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.NpcAI;
using SRG.NpcAI.Actions;
using SRG.Simulation;

namespace SRG.Ships.Movement
{
    /// <summary>
    /// Навигация по галактике для AI: выбор следующего прыжка на пути к далёкой системе
    /// и «соседство» систем. Графа связей в данных нет — всё считается по физическим
    /// координатам (та же метрика, что HyperjumpController.CanRequestJump).
    /// </summary>
    public static class HyperNavigation
    {
        // Дистанция «одного хопа» для соседства систем: медианная дистанция до ближайшего
        // соседа по всей галактике × запас. Кэшируется на галактику.
        private const float HopDistanceFactor = 1.6f;
        private static GalaxyData _hopGalaxy;
        private static float _hopDistance;

        /// <summary>Дистанция, в пределах которой две системы считаются соседями
        /// (для BFS-стратегий ГШ). Медиана nearest-neighbor × HopDistanceFactor.</summary>
        public static float GetHopDistance(GalaxyData galaxy)
        {
            if (galaxy == null) return 0f;
            if (_hopGalaxy == galaxy) return _hopDistance;

            var nearest = new List<float>();
            foreach (var a in galaxy.StarsMap.Values)
            {
                float best = float.MaxValue;
                foreach (var b in galaxy.StarsMap.Values)
                {
                    if (a == b) continue;
                    float d = HyperjumpController.CalcDistance(a, b);
                    if (d < best) best = d;
                }
                if (best < float.MaxValue) nearest.Add(best);
            }
            nearest.Sort();
            _hopDistance = nearest.Count > 0 ? nearest[nearest.Count / 2] * HopDistanceFactor : 0f;
            _hopGalaxy = galaxy;
            return _hopDistance;
        }

        /// <summary>Соседи системы в радиусе одного хопа. O(n) на вызов — для редких
        /// стратегических оценок ГШ, не для per-ship горячих путей.</summary>
        public static List<StarData> GetNeighbors(StarData star, GalaxyData galaxy)
        {
            var list = new List<StarData>();
            if (star == null || galaxy?.StarsMap == null) return list;
            float hop = GetHopDistance(galaxy);
            if (hop <= 0f) return list;
            foreach (var st in galaxy.StarsMap.Values)
            {
                if (st == star) continue;
                if (HyperjumpController.CalcDistance(star, st) <= hop) list.Add(st);
            }
            return list;
        }

        /// <summary>Действие «продвигаться к системе destStarUid»: прямой прыжок, если дотягиваемся,
        /// иначе прыжок в промежуточную систему. null — продвинуться невозможно (нет двигателя /
        /// достижимой системы ближе к цели), корабль остаётся при своей обычной жизни.</summary>
        public static NpcAction ActionToward(ShipData ship, StarData currentStar, string destStarUid)
        {
            var galaxy = GameWorld.GeneratedGalaxy;
            if (galaxy?.StarsMap == null) return null;
            if (!galaxy.StarsMap.TryGetValue(destStarUid, out var destStar)) return null;
            var nextHop = PickNextHopToward(ship, currentStar, destStar, galaxy);
            return nextHop == null ? null : new ActionHyperJumpTo(nextHop.Uid);
        }

        /// <summary>Greedy pathfinder: если dest в JumpRange корабля — вернуть dest напрямую.
        /// Иначе перебрать все системы и вернуть ту, которая (а) в JumpRange от текущей и
        /// (б) минимизирует физическую дистанцию до dest. null = продвинуться невозможно
        /// (нет двигателя / все кандидаты дальше, чем текущая позиция).</summary>
        public static StarData PickNextHopToward(ShipData ship, StarData currentStar, StarData destStar, GalaxyData galaxy)
        {
            if (currentStar == null || destStar == null || galaxy?.StarsMap == null) return null;
            int jumpRange = EquipmentSystem.GetJumpRange(ship);
            if (jumpRange <= 0) return null;

            float directDist = HyperjumpController.CalcDistance(currentStar, destStar);
            if (directDist <= jumpRange) return destStar;

            StarData best = null;
            float bestDistToDest = directDist; // кандидат обязан сблизить с dest
            foreach (var st in galaxy.StarsMap.Values)
            {
                if (st == currentStar || st == destStar) continue;
                float hop = HyperjumpController.CalcDistance(currentStar, st);
                if (hop > jumpRange) continue;
                float toDest = HyperjumpController.CalcDistance(st, destStar);
                if (toDest < bestDistToDest) { bestDistToDest = toDest; best = st; }
            }
            return best;
        }
    }
}
