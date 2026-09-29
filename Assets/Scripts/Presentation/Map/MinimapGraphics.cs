using UnityEngine;
using UnityEngine.UI;

namespace SRG.Presentation.Map
{
    [RequireComponent(typeof(CanvasRenderer))]
    public class EllipseOrbitGraphic : MaskableGraphic
    {
        [Range(0f, 1f)] public float Eccentricity = 0f;
        public float LineWidth = 1.5f;
        public int Segments = 128;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (Segments < 8) return;

            float e = Mathf.Clamp(Eccentricity, 0f, 0.99f);
            float a = rectTransform.rect.width * 0.5f;
            float b = a * Mathf.Sqrt(1f - e * e);
            float half = LineWidth * 0.5f;

            for (int i = 0; i < Segments; i++)
            {
                float t0 = (float)i / Segments * Mathf.PI * 2f;
                float t1 = (float)(i + 1) / Segments * Mathf.PI * 2f;

                Vector2 p0 = new Vector2(Mathf.Cos(t0) * a, Mathf.Sin(t0) * b);
                Vector2 p1 = new Vector2(Mathf.Cos(t1) * a, Mathf.Sin(t1) * b);
                Vector2 dir = (p1 - p0).normalized;
                Vector2 normal = new Vector2(-dir.y, dir.x);

                int idx = i * 4;
                vh.AddVert(p0 + normal * half, color, Vector2.zero);
                vh.AddVert(p0 - normal * half, color, Vector2.zero);
                vh.AddVert(p1 - normal * half, color, Vector2.zero);
                vh.AddVert(p1 + normal * half, color, Vector2.zero);

                vh.AddTriangle(idx, idx + 1, idx + 2);
                vh.AddTriangle(idx, idx + 2, idx + 3);
            }
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public class RadarFogGraphic : MaskableGraphic
    {

        public Vector2 HoleCenter;
        public float HoleRadius = 50f;
        public Color FogColor = new Color(0f, 0f, 0f, 0.6f);
        public int Segments = 64;
        public float EdgeFadeWidth = 5f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (Segments < 8) return;

            Rect r = rectTransform.rect;
            float outerR = Mathf.Sqrt(r.width * r.width + r.height * r.height) * 0.5f * 1.2f;

            float innerR = Mathf.Max(0f, HoleRadius);
            float fadeR = innerR + Mathf.Max(0f, EdgeFadeWidth);

            Color cTransparent = new Color(FogColor.r, FogColor.g, FogColor.b, 0f);
            Color cFade = new Color(FogColor.r, FogColor.g, FogColor.b, FogColor.a * 0.12f);
            Color cOpaque = new Color(FogColor.r, FogColor.g, FogColor.b, FogColor.a);

            for (int i = 0; i < Segments; i++)
            {
                float a0 = (float)i / Segments * Mathf.PI * 2f;
                float a1 = (float)(i + 1) / Segments * Mathf.PI * 2f;

                Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                Vector2 d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));

                Vector2 pInner0 = HoleCenter + d0 * innerR;
                Vector2 pInner1 = HoleCenter + d1 * innerR;
                Vector2 pFade0 = HoleCenter + d0 * fadeR;
                Vector2 pFade1 = HoleCenter + d1 * fadeR;
                Vector2 pOuter0 = HoleCenter + d0 * outerR;
                Vector2 pOuter1 = HoleCenter + d1 * outerR;

                int b = i * 8;
                vh.AddVert(pInner0, cTransparent, Vector2.zero); // b+0
                vh.AddVert(pInner1, cTransparent, Vector2.zero); // b+1
                vh.AddVert(pFade0, cFade, Vector2.zero); // b+2
                vh.AddVert(pFade1, cFade, Vector2.zero); // b+3
                vh.AddVert(pFade0, cFade, Vector2.zero); // b+4
                vh.AddVert(pFade1, cFade, Vector2.zero); // b+5
                vh.AddVert(pOuter0, cOpaque, Vector2.zero); // b+6
                vh.AddVert(pOuter1, cOpaque, Vector2.zero); // b+7
                vh.AddTriangle(b + 0, b + 2, b + 1);
                vh.AddTriangle(b + 1, b + 2, b + 3);
                vh.AddTriangle(b + 4, b + 6, b + 5);
                vh.AddTriangle(b + 5, b + 6, b + 7);
            }
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public class FilledRingGraphic : MaskableGraphic
    {
        public float InnerRadius = 30f;
        public float OuterRadius = 60f;
        public int Segments = 64;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (Segments < 8 || OuterRadius <= InnerRadius) return;

            for (int i = 0; i < Segments; i++)
            {
                float a0 = (float)i / Segments * Mathf.PI * 2f;
                float a1 = (float)(i + 1) / Segments * Mathf.PI * 2f;

                Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                Vector2 d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));

                int idx = i * 4;
                vh.AddVert(d0 * InnerRadius, color, Vector2.zero);
                vh.AddVert(d1 * InnerRadius, color, Vector2.zero);
                vh.AddVert(d0 * OuterRadius, color, Vector2.zero);
                vh.AddVert(d1 * OuterRadius, color, Vector2.zero);

                vh.AddTriangle(idx, idx + 2, idx + 1);
                vh.AddTriangle(idx + 1, idx + 2, idx + 3);
            }
        }
    }

    /// <summary>
    /// Сплошной диск с опциональной обводкой. Радиус берётся из rectTransform.rect.width/2,
    /// если Radius ≤ 0. Если OutlineThickness &gt; 0 и OutlineColor.a &gt; 0, рисует кольцо
    /// поверх диска от (Radius-OutlineThickness) до Radius — используется для отличия
    /// корабельных маркеров (диск + белая «гало» обводка) от плоских планетарных кружков.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class FilledDiskGraphic : MaskableGraphic
    {
        public int Segments = 24;
        public float Radius = -1f;
        public float OutlineThickness = 0f;
        public Color OutlineColor = Color.clear;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (Segments < 6) return;

            float r = Radius > 0f ? Radius : rectTransform.rect.width * 0.5f;
            if (r <= 0f) return;

            float innerR = Mathf.Max(0f, r - Mathf.Max(0f, OutlineThickness));
            bool drawOutline = OutlineThickness > 0f && OutlineColor.a > 0f && innerR < r;

            // Веер: центр + точки по окружности (innerR).
            vh.AddVert(Vector2.zero, color, Vector2.zero);
            for (int i = 0; i <= Segments; i++)
            {
                float a = (float)i / Segments * Mathf.PI * 2f;
                vh.AddVert(new Vector2(Mathf.Cos(a) * innerR, Mathf.Sin(a) * innerR), color, Vector2.zero);
            }
            for (int i = 0; i < Segments; i++)
                vh.AddTriangle(0, 1 + i, 1 + i + 1);

            if (!drawOutline) return;

            int baseIdx = Segments + 2;
            for (int i = 0; i < Segments; i++)
            {
                float a0 = (float)i / Segments * Mathf.PI * 2f;
                float a1 = (float)(i + 1) / Segments * Mathf.PI * 2f;
                Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                Vector2 d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));

                vh.AddVert(d0 * innerR, OutlineColor, Vector2.zero);
                vh.AddVert(d1 * innerR, OutlineColor, Vector2.zero);
                vh.AddVert(d0 * r,      OutlineColor, Vector2.zero);
                vh.AddVert(d1 * r,      OutlineColor, Vector2.zero);

                int b = baseIdx + i * 4;
                vh.AddTriangle(b, b + 2, b + 1);
                vh.AddTriangle(b + 1, b + 2, b + 3);
            }
        }
    }
}
