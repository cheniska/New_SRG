using System.Collections.Generic;
using UnityEngine;
using SRG.Galaxy;

namespace SRG.NpcAI
{
    /// <summary>
    /// Ленивая сетка кораблей в звезде для radius-queries (fear/escort/attack-nearest).
    /// Строится раз в ход при первом обращении и кэшируется до конца хода
    /// (детектируется по <see cref="SRG.Core.GalaxyManager.Instance.GeneratedGalaxy.CurrentTurn"/>).
    ///
    /// Клетка = <c>SystemSizeWorld / GridSize</c>. Query возвращает корабли 3×3 клеток вокруг
    /// точки — с ложноположительными на границе, вызывающий фильтрует по sqrMagnitude.
    ///
    /// Использование:
    /// <code>
    ///   foreach (var s in ShipSpatialHash.Nearby(star, pos, radius))
    ///       if ((s.Position - pos).sqrMagnitude &lt;= radius*radius) …
    /// </code>
    ///
    /// Опция off: если <c>MinShipsToUse</c>=∞ (или ships.Count меньше порога) — Nearby
    /// линейно перебирает star.Ships (без построения сетки). Так на пустых системах нет
    /// накладных расходов.
    /// </summary>
    public static class ShipSpatialHash
    {
        private const int GridSize = 4;               // 4×4 клетки на звезду
        private const int MinShipsToUse = 12;         // при меньшем числе — не строим сетку
        private const int PurgeAfterTurns = 2;        // грид старше N ходов — удалить (multi-galaxy safety)

        private class StarGrid
        {
            public int Turn;
            public float CellSize;
            public int Size;                          // = GridSize
            public List<ShipData>[,] Cells;           // Size × Size
            public float HalfExtent;                  // = Size * CellSize / 2
        }

        private static readonly Dictionary<string, StarGrid> _grids = new();
        private static int _lastCleanupTurn = -1;

        public static IEnumerable<ShipData> Nearby(StarData star, Vector2 pos, float radius)
        {
            if (star == null) yield break;
            var ships = star.Ships;
            if (ships.Count < MinShipsToUse)
            {
                // Линейный fallback: слишком мало кораблей, чтобы окупить построение сетки.
                for (int i = 0; i < ships.Count; i++) yield return ships[i];
                yield break;
            }

            int turn = SRG.Core.GalaxyManager.Instance?.GeneratedGalaxy?.CurrentTurn ?? 0;
            var grid = EnsureGrid(star, turn);
            if (grid == null)
            {
                // Дегенеративная звезда (SystemSize=0) — линейный fallback, чтобы не пропустить корабли.
                for (int i = 0; i < ships.Count; i++) yield return ships[i];
                yield break;
            }

            int cx = ToCell(pos.x, grid);
            int cy = ToCell(pos.y, grid);
            int cellsRadius = Mathf.Max(1, Mathf.CeilToInt(radius / grid.CellSize));

            int x0 = Mathf.Max(0, cx - cellsRadius);
            int y0 = Mathf.Max(0, cy - cellsRadius);
            int x1 = Mathf.Min(grid.Size - 1, cx + cellsRadius);
            int y1 = Mathf.Min(grid.Size - 1, cy + cellsRadius);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var cell = grid.Cells[x, y];
                    if (cell == null) continue;
                    for (int i = 0; i < cell.Count; i++) yield return cell[i];
                }
        }

        private static StarGrid EnsureGrid(StarData star, int turn)
        {
            if (turn != _lastCleanupTurn) CleanupStale(turn);

            if (_grids.TryGetValue(star.Uid, out var grid) && grid.Turn == turn) return grid;

            float half = SRG.Utils.SRUnits.ToWorld(star.SystemSize) * 1.05f;
            if (half < 1f) return null; // дегенеративная звезда — линейный fallback вызывающего
            grid = new StarGrid
            {
                Turn = turn,
                Size = GridSize,
                HalfExtent = half,
            };
            grid.CellSize = grid.HalfExtent * 2f / grid.Size;
            grid.Cells = new List<ShipData>[grid.Size, grid.Size];
            var ships = star.Ships;
            for (int i = 0; i < ships.Count; i++)
            {
                var s = ships[i];
                if (s == null) continue;
                int cx = ToCell(s.Position.x, grid);
                int cy = ToCell(s.Position.y, grid);
                var cell = grid.Cells[cx, cy] ??= new List<ShipData>(4);
                cell.Add(s);
            }
            _grids[star.Uid] = grid;
            return grid;
        }

        private static int ToCell(float coord, StarGrid grid)
        {
            float normalized = coord + grid.HalfExtent;
            int idx = (int)(normalized / grid.CellSize);
            if (idx < 0) return 0;
            if (idx >= grid.Size) return grid.Size - 1;
            return idx;
        }

        private static void CleanupStale(int currentTurn)
        {
            _lastCleanupTurn = currentTurn;
            List<string> stale = null;
            foreach (var kv in _grids)
                if (currentTurn - kv.Value.Turn > PurgeAfterTurns)
                    (stale ??= new List<string>()).Add(kv.Key);
            if (stale != null)
                for (int i = 0; i < stale.Count; i++) _grids.Remove(stale[i]);
        }

        /// <summary>Полный сброс — вызывать при SwapGalaxy (multi-galaxy) или загрузке сейва.</summary>
        public static void ResetAll()
        {
            _grids.Clear();
            _lastCleanupTurn = -1;
        }
    }
}
