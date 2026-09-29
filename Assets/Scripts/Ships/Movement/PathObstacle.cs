using System;
using System.Collections.Generic;
using UnityEngine;
using SRG.Galaxy;
using SRG.Utils;

namespace SRG.Ships.Movement
{
    // Препятствие на пути корабля. Используется планировщиком траектории.
    // Сериализуется как часть StarData, поэтому поля плоские (без интерфейсов в JSON).
    public interface IPathObstacle
    {
        bool SegmentBlocked(Vector2 a, Vector2 b, float margin);
        bool Contains(Vector2 p, float margin);
        Vector2 PushOutside(Vector2 p, float margin);
        // Возвращает точку обхода (на той же стороне, что и target),
        // через которую путь from→detour→to не пересекает само препятствие.
        // outHeading подскажет планировщику курс на выходе из обхода.
        bool TryGetAvoidanceWaypoint(Vector2 from, Vector2 to, float margin,
            out Vector2 detour);
    }

    [Serializable]
    public sealed class CircleObstacle : IPathObstacle
    {
        public Vector2 Center;
        public float Radius;

        public CircleObstacle() { }
        public CircleObstacle(Vector2 center, float radius) { Center = center; Radius = radius; }

        public bool SegmentBlocked(Vector2 a, Vector2 b, float margin)
        {
            float r = Radius + margin;
            return ShipTrajectory.SegmentIntersectsCircle(a, b, Center, r);
        }

        public bool Contains(Vector2 p, float margin)
        {
            float r = Radius + margin;
            return (p - Center).sqrMagnitude < r * r;
        }

        public Vector2 PushOutside(Vector2 p, float margin)
        {
            float r = Radius + margin;
            Vector2 d = p - Center;
            float sq = d.sqrMagnitude;
            if (sq >= r * r) return p;
            if (sq < 1e-8f) return Center + new Vector2(r, 0f);
            return Center + d / Mathf.Sqrt(sq) * r;
        }

        public bool TryGetAvoidanceWaypoint(Vector2 from, Vector2 to, float margin, out Vector2 detour)
        {
            float r = Radius + margin;
            // Касательные от внешних точек к окружности.
            if (!ComputeTangentPoints(from, r, out var fa, out var fb)
                || !ComputeTangentPoints(to, r, out var ta, out var tb))
            {
                detour = from;
                return false;
            }
            // Подбираем сторону: пара касательных, у которой start и target — рядом.
            float dAA = (fa - ta).sqrMagnitude + (fb - tb).sqrMagnitude;
            float dAB = (fa - tb).sqrMagnitude + (fb - ta).sqrMagnitude;
            detour = dAA < dAB ? fa : fb;
            return true;
        }

        private bool ComputeTangentPoints(Vector2 external, float r, out Vector2 a, out Vector2 b)
        {
            Vector2 d = Center - external;
            float distSq = d.sqrMagnitude;
            if (distSq <= r * r + 1e-6f) { a = b = external; return false; }
            float dist = Mathf.Sqrt(distSq);
            float theta = Mathf.Acos(r / dist);          // полуугол конуса касания
            float baseA = Angles.Of(d);          // направление на центр окружности
            // Точки касания — на самой окружности.
            float a1 = baseA + theta;
            float a2 = baseA - theta;
            Vector2 r1 = Angles.Dir(a1 + Mathf.PI);
            Vector2 r2 = Angles.Dir(a2 + Mathf.PI);
            a = Center + r1 * r;
            b = Center + r2 * r;
            return true;
        }
    }

    [Serializable]
    public sealed class PolygonObstacle : IPathObstacle
    {
        // Вершины в любом порядке (CW или CCW). Полигон считается замкнутым.
        public List<Vector2> Vertices = new();

        public PolygonObstacle() { }
        public PolygonObstacle(IEnumerable<Vector2> vertices) { Vertices = new List<Vector2>(vertices); }

        private bool IsCCW()
        {
            float sum = 0f;
            int n = Vertices.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 v0 = Vertices[i];
                Vector2 v1 = Vertices[(i + 1) % n];
                sum += (v1.x - v0.x) * (v1.y + v0.y);
            }
            return sum < 0f;
        }

        public bool SegmentBlocked(Vector2 a, Vector2 b, float margin)
        {
            if (Vertices.Count < 3) return false;
            if (Contains(a, margin) || Contains(b, margin)) return true;

            int n = Vertices.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 p1 = Vertices[i];
                Vector2 p2 = Vertices[(i + 1) % n];
                if (margin > 0f)
                {
                    Vector2 outward = OutwardEdgeNormal(p1, p2);
                    p1 += outward * margin;
                    p2 += outward * margin;
                }
                if (SegmentsIntersect(a, b, p1, p2)) return true;
            }
            return false;
        }

        public bool Contains(Vector2 p, float margin)
        {
            int n = Vertices.Count;
            if (n < 3) return false;
            // Inflated containment: точка внутри полигона ИЛИ ближе margin к ребру.
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 vi = Vertices[i], vj = Vertices[j];
                if (((vi.y > p.y) != (vj.y > p.y)) &&
                    (p.x < (vj.x - vi.x) * (p.y - vi.y) / (vj.y - vi.y) + vi.x))
                    inside = !inside;
            }
            if (inside) return true;
            if (margin <= 0f) return false;
            float marginSq = margin * margin;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = Vertices[i];
                Vector2 b = Vertices[(i + 1) % n];
                if (DistanceSqPointToSegment(p, a, b) < marginSq) return true;
            }
            return false;
        }

        public Vector2 PushOutside(Vector2 p, float margin)
        {
            if (!Contains(p, margin)) return p;
            int n = Vertices.Count;
            float bestDistSq = float.MaxValue;
            Vector2 bestPush = p;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = Vertices[i];
                Vector2 b = Vertices[(i + 1) % n];
                Vector2 closest = ClosestPointOnSegment(p, a, b);
                Vector2 outward = OutwardEdgeNormal(a, b);
                Vector2 candidate = closest + outward * (margin + 0.01f);
                float dsq = (candidate - p).sqrMagnitude;
                if (dsq < bestDistSq)
                {
                    bestDistSq = dsq;
                    bestPush = candidate;
                }
            }
            return bestPush;
        }

        public bool TryGetAvoidanceWaypoint(Vector2 from, Vector2 to, float margin, out Vector2 detour)
        {
            int n = Vertices.Count;
            if (n < 3) { detour = from; return false; }

            // Кандидаты — вершины, сдвинутые наружу на margin.
            float bestScore = float.MaxValue;
            detour = from;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = Vertices[(i + n - 1) % n];
                Vector2 cur  = Vertices[i];
                Vector2 next = Vertices[(i + 1) % n];
                Vector2 outward = AverageOutwardNormal(prev, cur, next);
                Vector2 candidate = cur + outward * (margin + 0.01f);

                // Скоринг — длина обходного пути.
                float score = (candidate - from).magnitude + (candidate - to).magnitude;
                if (score < bestScore)
                {
                    bestScore = score;
                    detour = candidate;
                    found = true;
                }
            }
            return found;
        }

        private Vector2 OutwardEdgeNormal(Vector2 a, Vector2 b)
        {
            Vector2 edge = b - a;
            Vector2 left = new Vector2(-edge.y, edge.x);  // нормаль слева от направления a→b
            if (left.sqrMagnitude > 1e-10f) left.Normalize();
            // У CCW-полигона "внутрь" смотрит left → outward = -left.
            return IsCCW() ? -left : left;
        }

        private Vector2 AverageOutwardNormal(Vector2 prev, Vector2 cur, Vector2 next)
        {
            Vector2 n1 = OutwardEdgeNormal(prev, cur);
            Vector2 n2 = OutwardEdgeNormal(cur, next);
            Vector2 sum = n1 + n2;
            return sum.sqrMagnitude > 1e-6f ? sum.normalized : n1;
        }

        private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;
            Vector2 r = b - a, s = d - c;
            float rs = Cross(r, s);
            Vector2 ac = c - a;
            if (Mathf.Abs(rs) < 1e-10f) return false;       // параллельны — игнорируем касание
            float t = Cross(ac, s) / rs;
            float u = Cross(ac, r) / rs;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        private static float DistanceSqPointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float L = ab.sqrMagnitude;
            if (L < 1e-10f) return (p - a).sqrMagnitude;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / L);
            return (p - (a + ab * t)).sqrMagnitude;
        }

        private static Vector2 ClosestPointOnSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float L = ab.sqrMagnitude;
            if (L < 1e-10f) return a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / L);
            return a + ab * t;
        }
    }
}
