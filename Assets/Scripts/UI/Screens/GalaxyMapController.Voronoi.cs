using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SRG.Config;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Presentation.Common;
using SRG.Presentation.Map;
using SRG.Ships.Movement;

namespace SRG.UI.Screens
{
    public partial class GalaxyMapController
    {
        // ── Диаграмма Вороного ────────────────────────────────────────────────────

        /// <summary>
        /// Читает VoronoiPolygon/VoronoiEdgeNeighbors из SectorData (вычислено генератором),
        /// переводит координаты в пространство карты и заполняет _voronoi.Cells.
        /// Вся геометрия Вороного уже посчитана — здесь только рендеринг.
        /// </summary>
        private void RebuildVoronoi()
        {
            if (_voronoi == null || _galaxy?.Sectors == null) return;

            _voronoi.Cells.Clear();

            var ctx     = GalaxyManager.Instance?.Context;
            var sectors = _galaxy.Sectors;
            if (sectors.Count == 0) { _voronoi.Refresh(); return; }

            Color borderCol = ParseColor(_galCfg?.VoronoiBorderColor ?? "90,100,130");
            _voronoi.BorderColor = new Color(borderCol.r, borderCol.g, borderCol.b, 1f);
            _voronoi.BorderWidth = 1f;

            // Быстрый lookup uid → sector для определения наличия соседа
            var uidToSector = new System.Collections.Generic.Dictionary<string, SectorData>(sectors.Count);
            foreach (var s in sectors) uidToSector[s.Uid] = s;

            foreach (var sector in sectors)
            {
                var gridPoly = sector.VoronoiPolygon;
                if (gridPoly == null || gridPoly.Count < 3) continue;

                // Перевод из координат сетки в пиксели карты
                var mapPoly = new List<Vector2>(gridPoly.Count);
                foreach (var gp in gridPoly) mapPoly.Add(GridToMap(gp));

                Color fill = SectorFillColor(sector, ctx);

                // Определяем видимость рёбер: рисовать только на стыке разных секторов
                int       edgeCount  = mapPoly.Count;
                var       showBorder = new List<bool>(edgeCount);
                var       edgeNeigh  = sector.VoronoiEdgeNeighbors;
                for (int e = 0; e < edgeCount; e++)
                {
                    // Ребро граничит с другим сектором (или внешняя граница) → рисуем
                    string neighborUid = (edgeNeigh != null && e < edgeNeigh.Count) ? edgeNeigh[e] : null;
                    showBorder.Add(neighborUid == null || neighborUid != sector.Uid);
                }

                _voronoi.Cells.Add(new GalaxyMapGraphics.SectorCell
                {
                    Polygon        = mapPoly,
                    FillColor      = new Color(fill.r, fill.g, fill.b, 0.10f),
                    IsOpen         = true,
                    ShowBorderEdge = showBorder,
                });
            }

            // Линии экспансии рас
            _voronoi.ExpansionLines.Clear();
            if (_showExpansionLines && _galaxy.ExpansionEdges?.Count > 0)
            {
                var ectx = GalaxyManager.Instance?.Context;
                foreach (var edge in _galaxy.ExpansionEdges)
                {
                    Color ec = Color.white;
                    if (ectx?.Config?.Races?.TryGetValue(edge.RaceKey, out var rc) == true && !string.IsNullOrEmpty(rc.Color))
                        ec = ParseColor(rc.Color);
                    ec.a = 0.50f;
                    _voronoi.ExpansionLines.Add(new GalaxyMapGraphics.ExpansionLine
                    {
                        From  = GridToMap(edge.From),
                        To    = GridToMap(edge.To),
                        Color = ec,
                    });
                }
            }

            SpawnExpansionLabels();
            UpdateGrid();
            _voronoi.Refresh();
        }
    }
}
