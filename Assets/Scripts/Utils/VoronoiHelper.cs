using System.Collections.Generic;
using UnityEngine;

namespace SRG.Utils
{
    /// <summary>
    /// Вычисление ячеек диаграммы Вороного методом пересечения полупространств (Sutherland-Hodgman).
    /// Работает быстро для малого числа сайтов (менее 100).
    /// </summary>
    public static class VoronoiHelper
    {
        /// <summary>
        /// Возвращает выпуклый многоугольник — ячейку Вороного для sites[index], обрезанную по bounds.
        /// Вершины в порядке CCW. При ошибке возвращает пустой список.
        /// </summary>
        public static List<Vector2> ComputeCell(int index, Vector2[] sites, Rect bounds)
        {
            if (sites == null || index < 0 || index >= sites.Length)
                return new List<Vector2>();

            // Начальный прямоугольник (CCW: BL → BR → TR → TL)
            var polygon = new List<Vector2>(8)
            {
                new Vector2(bounds.xMin, bounds.yMin),
                new Vector2(bounds.xMax, bounds.yMin),
                new Vector2(bounds.xMax, bounds.yMax),
                new Vector2(bounds.xMin, bounds.yMax),
            };

            Vector2 site = sites[index];

            for (int j = 0; j < sites.Length; j++)
            {
                if (j == index) continue;
                if ((sites[j] - site).sqrMagnitude < 0.0001f) continue; // дубликат
                polygon = ClipByBisector(polygon, site, sites[j]);
                if (polygon.Count < 3) return new List<Vector2>();
            }

            return polygon;
        }

        // Оставляет точки, которые ближе к 'a', чем к 'b' (обрезает по серединному перпендикуляру)
        private static List<Vector2> ClipByBisector(List<Vector2> polygon, Vector2 a, Vector2 b)
        {
            if (polygon.Count < 3) return polygon;

            Vector2 mid    = (a + b) * 0.5f;
            Vector2 normal = b - a; // направлен от a к b

            var result = new List<Vector2>(polygon.Count + 2);
            int n = polygon.Count;

            for (int i = 0; i < n; i++)
            {
                Vector2 curr = polygon[i];
                Vector2 next = polygon[(i + 1) % n];

                float dCurr = Vector2.Dot(curr - mid, normal);
                float dNext = Vector2.Dot(next - mid, normal);

                bool currIn = dCurr <= 0f;
                bool nextIn = dNext <= 0f;

                if (currIn) result.Add(curr);

                if (currIn != nextIn)
                {
                    // Ребро пересекает границу полупространства
                    float denom = dCurr - dNext;
                    if (Mathf.Abs(denom) > 1e-9f)
                    {
                        float t = dCurr / denom;
                        result.Add(Vector2.Lerp(curr, next, t));
                    }
                }
            }

            return result;
        }
    }
}
