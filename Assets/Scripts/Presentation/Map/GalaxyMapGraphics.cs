using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SRG.Presentation.Map
{
    /// <summary>
    /// Кастомный UI-Graphic для отрисовки галакарты:
    /// — закрашенные ячейки Вороного (фракции/расы)
    /// — границы секторов
    /// — линии маршрута / линейки
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class GalaxyMapGraphics : MaskableGraphic
    {
        public struct SectorCell
        {
            public List<Vector2> Polygon;
            public Color         FillColor;
            public bool          IsOpen;          // false → сектор закрыт (туман войны)
            public List<bool>    ShowBorderEdge;  // null → рисовать все рёбра; иначе per-edge флаг
        }

        public struct ExpansionLine
        {
            public Vector2 From;
            public Vector2 To;
            public Color   Color;
        }

        [HideInInspector] public List<SectorCell>                    Cells          = new();
        [HideInInspector] public List<(Vector2 from, Vector2 to)>   RouteLines     = new();
        [HideInInspector] public Color  RouteLineColor  = new Color(0.2f, 0.85f, 1f, 0.9f);
        [HideInInspector] public float  RouteLineWidth  = 2.5f;
        [HideInInspector] public Color  BorderColor     = new Color(0.65f, 0.65f, 0.75f, 0.35f);
        [HideInInspector] public float  BorderWidth     = 1.5f;
        [HideInInspector] public List<ExpansionLine>                 ExpansionLines = new();

        // Сетка парсеков
        [HideInInspector] public List<float> GridLinesX    = new();  // вертикальные (x, map-px)
        [HideInInspector] public List<float> GridLinesY    = new();  // горизонтальные (y, map-px)
        [HideInInspector] public Color       GridLineColor = new Color(0.30f, 0.38f, 0.60f, 0.45f);
        [HideInInspector] public float       GridLineWidth = 1.5f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            // 0. Сетка парсеков (позади всего)
            if (GridLinesX.Count > 0 || GridLinesY.Count > 0)
            {
                var r = rectTransform.rect;
                foreach (float x in GridLinesX)
                    DrawLine(vh, new Vector2(x, r.yMin), new Vector2(x, r.yMax), GridLineColor, GridLineWidth);
                foreach (float y in GridLinesY)
                    DrawLine(vh, new Vector2(r.xMin, y), new Vector2(r.xMax, y), GridLineColor, GridLineWidth);
            }

            // 1. Закрашенные ячейки
            foreach (var cell in Cells)
            {
                if (cell.Polygon == null || cell.Polygon.Count < 3) continue;

                Color fill = cell.IsOpen
                    ? cell.FillColor
                    : new Color(0.04f, 0.04f, 0.08f, 0.88f);

                FillPolygon(vh, cell.Polygon, fill);
            }

            // 2. Границы секторов (только рёбра между разными секторами)
            foreach (var cell in Cells)
            {
                if (cell.Polygon == null || cell.Polygon.Count < 2) continue;
                int n = cell.Polygon.Count;
                for (int i = 0; i < n; i++)
                {
                    bool draw = cell.ShowBorderEdge == null || (i < cell.ShowBorderEdge.Count && cell.ShowBorderEdge[i]);
                    if (draw)
                        DrawLine(vh, cell.Polygon[i], cell.Polygon[(i + 1) % n], BorderColor, BorderWidth);
                }
            }

            // 3. Линии экспансии рас (со стрелками в середине)
            foreach (var el in ExpansionLines)
            {
                Vector2 dir = el.To - el.From;
                if (dir.sqrMagnitude < 1e-9f) continue;
                dir.Normalize();
                DrawLine(vh, el.From, el.To, el.Color, 1.5f);
                DrawArrowHead(vh, el.From, el.To, el.Color, 5f);
            }

            // 4. Линии маршрута / линейки
            foreach (var (f, t) in RouteLines)
                DrawLine(vh, f, t, RouteLineColor, RouteLineWidth);
        }

        // ─────────────────────────────────────────────────────────────────────────

        private static void FillPolygon(VertexHelper vh, List<Vector2> poly, Color c)
        {
            int start = vh.currentVertCount;
            foreach (var p in poly)
                vh.AddVert(p, c, Vector2.zero);
            for (int i = 1; i < poly.Count - 1; i++)
                vh.AddTriangle(start, start + i, start + i + 1);
        }

        private static void DrawArrowHead(VertexHelper vh, Vector2 a, Vector2 b, Color c, float size)
        {
            Vector2 d = b - a;
            if (d.sqrMagnitude < 1e-9f) return;
            d.Normalize();
            Vector2 n = new Vector2(-d.y, d.x);
            Vector2 mid = (a + b) * 0.5f;
            int s = vh.currentVertCount;
            vh.AddVert(mid + d * size,                 c, Vector2.zero);
            vh.AddVert(mid - d * size + n * size,      c, Vector2.zero);
            vh.AddVert(mid - d * size - n * size,      c, Vector2.zero);
            vh.AddTriangle(s, s + 1, s + 2);
        }

        private static void DrawLine(VertexHelper vh, Vector2 a, Vector2 b, Color c, float width)
        {
            Vector2 d = b - a;
            if (d.sqrMagnitude < 1e-9f) return;
            d.Normalize();
            Vector2 n = new Vector2(-d.y, d.x) * (width * 0.5f);

            int s = vh.currentVertCount;
            vh.AddVert(a + n, c, Vector2.zero);
            vh.AddVert(a - n, c, Vector2.zero);
            vh.AddVert(b - n, c, Vector2.zero);
            vh.AddVert(b + n, c, Vector2.zero);
            vh.AddTriangle(s, s + 1, s + 2);
            vh.AddTriangle(s, s + 2, s + 3);
        }

        /// <summary>Перерисовать меш на следующем кадре.</summary>
        public void Refresh() => SetVerticesDirty();
    }
}
