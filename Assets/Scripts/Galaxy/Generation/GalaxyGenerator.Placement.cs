using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.Galaxy.Generation
{
    public partial class GalaxyGenerator
    {
        private const int MaxPlacementAttempts = 12;
        private const float SectorStarDistCoef = 1.2f;
        private const float BorderZoneFrac = 0.22f;
        private const float CenterZoneRadFrac = 0.22f;
        private const float CornerZoneFrac = 0.18f;
        private const float NeighbourRadiusFrac = 0.20f;
        private const int SectorPlacementAttemptsPerSector = 300;
        private const int StarPlaceMaxIter = 10000;
        private const int DensityBalanceMaxPasses = 8;

        private bool TryPlaceSectorPositions(GalaxyData galaxy, GalaxyConfigData cfg)
        {
            float w   = galaxy.Width;
            float h   = galaxy.Height;
            float pad = Mathf.Min(w, h) * 0.04f;

            var ordered = new List<SectorData>(galaxy.Sectors);
            ordered.Sort((a, b) =>
            {
                int va = string.Equals(a.PlaceHint, "neighbour", System.StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                int vb = string.Equals(b.PlaceHint, "neighbour", System.StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                return va.CompareTo(vb);
            });

            var placed     = new Dictionary<string, Vector2>(galaxy.Sectors.Count);
            var placedList = new List<(Vector2 pos, int stars)>(galaxy.Sectors.Count);

            foreach (var sector in ordered)
            {
                int  stars   = sector.Stars.Count;
                bool success = false;
                var  hint    = sector.PlaceHint?.ToLowerInvariant();

                Vector2 neighbourPos = default;
                bool    hasNeighbour = hint == "neighbour" &&
                                       !string.IsNullOrEmpty(sector.NeighbourSectorUid) &&
                                       placed.TryGetValue(sector.NeighbourSectorUid, out neighbourPos);

                for (int attempt = 0; attempt < SectorPlacementAttemptsPerSector; attempt++)
                {
                    var candidate = hint switch
                    {
                        "border"    => SampleBorderPoint(w, h, pad),
                        "center"    => SampleCenterPoint(w, h, pad),
                        "corner"    => SampleCornerPoint(w, h, pad),
                        "neighbour" => hasNeighbour
                                           ? SampleNearPoint(neighbourPos, w, h, pad)
                                           : new Vector2(GameRng.Range(pad, w - pad),
                                                         GameRng.Range(pad, h - pad)),
                        _           => new Vector2(GameRng.Range(pad, w - pad),
                                                  GameRng.Range(pad, h - pad))
                    };

                    if (IsFarEnoughFromSectors(candidate, stars, placedList))
                    {
                        sector.Center = candidate;
                        placed[sector.Uid] = candidate;
                        placedList.Add((candidate, stars));
                        success = true;
                        break;
                    }
                }

                if (!success) return false;
            }
            return true;
        }

        private static bool IsFarEnoughFromSectors(Vector2 p, int stars,
            List<(Vector2 pos, int stars)> placed)
        {
            foreach (var (pos, otherStars) in placed)
            {
                float minDist   = (stars + otherStars) * SectorStarDistCoef;
                float minDistSq = minDist * minDist;
                if ((p - pos).sqrMagnitude < minDistSq) return false;
            }
            return true;
        }

        private static Vector2 SampleBorderPoint(float w, float h, float pad)
        {
            float bw     = Mathf.Min(w, h) * BorderZoneFrac;
            float aLeft  = bw * (h - 2 * pad);
            float aRight = aLeft;
            float aBot   = (w - 2 * bw) * bw;
            float aTop   = aBot;
            float total  = aLeft + aRight + aBot + aTop;

            float r = GameRng.Range(0f, total);
            if (r < aLeft)
                return new Vector2(GameRng.Range(pad, bw),
                                   GameRng.Range(pad, h - pad));
            r -= aLeft;
            if (r < aRight)
                return new Vector2(GameRng.Range(w - bw, w - pad),
                                   GameRng.Range(pad, h - pad));
            r -= aRight;
            if (r < aBot)
                return new Vector2(GameRng.Range(bw, w - bw),
                                   GameRng.Range(pad, bw));
            return new Vector2(GameRng.Range(bw, w - bw),
                               GameRng.Range(h - bw, h - pad));
        }

        private static Vector2 SampleCenterPoint(float w, float h, float pad)
        {
            float   radius = Mathf.Min(w, h) * CenterZoneRadFrac;
            Vector2 offset = GameRng.InsideUnitCircle * radius;
            return new Vector2(Mathf.Clamp(w * 0.5f + offset.x, pad, w - pad),
                               Mathf.Clamp(h * 0.5f + offset.y, pad, h - pad));
        }

        private static Vector2 SampleCornerPoint(float w, float h, float pad)
        {
            float cz = Mathf.Min(w, h) * CornerZoneFrac;
            return GameRng.Range(0, 4) switch
            {
                0 => new Vector2(GameRng.Range(pad, cz),     GameRng.Range(pad, cz)),
                1 => new Vector2(GameRng.Range(w - cz, w - pad), GameRng.Range(pad, cz)),
                2 => new Vector2(GameRng.Range(pad, cz),     GameRng.Range(h - cz, h - pad)),
                _ => new Vector2(GameRng.Range(w - cz, w - pad), GameRng.Range(h - cz, h - pad)),
            };
        }

        private static Vector2 SampleNearPoint(Vector2 target, float w, float h, float pad)
        {
            float   radius = Mathf.Min(w, h) * NeighbourRadiusFrac;
            Vector2 offset = GameRng.InsideUnitCircle * radius;
            return new Vector2(Mathf.Clamp(target.x + offset.x, pad, w - pad),
                               Mathf.Clamp(target.y + offset.y, pad, h - pad));
        }

        private List<Vector2> JitteredSectorPositions(int w, int h, int n)
        {
            if (n <= 0) return new List<Vector2>();

            int cols = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(n * (float)w / h)));
            int rows = Mathf.Max(1, Mathf.CeilToInt((float)n / cols));

            float cellW = (float)w / cols;
            float cellH = (float)h / rows;
            float jx    = cellW * 0.3f;
            float jy    = cellH * 0.3f;

            var all = new List<Vector2>(cols * rows);
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                float cx = (c + 0.5f) * cellW + GameRng.Range(-jx, jx);
                float cy = (r + 0.5f) * cellH + GameRng.Range(-jy, jy);
                all.Add(new Vector2(Mathf.Clamp(cx, 0, w), Mathf.Clamp(cy, 0, h)));
            }

            for (int i = all.Count - 1; i > 0; i--)
            {
                int j = GameRng.Range(0, i + 1);
                var tmp = all[i]; all[i] = all[j]; all[j] = tmp;
            }
            while (all.Count > n) all.RemoveAt(all.Count - 1);
            return all;
        }

        private void BuildVoronoi(GalaxyData galaxy)
        {
            int n = galaxy.Sectors.Count;
            if (n == 0) return;

            var sites = new Vector2[n];
            for (int i = 0; i < n; i++)
                sites[i] = galaxy.Sectors[i].Center;

            var bounds = new Rect(0f, 0f, galaxy.Width, galaxy.Height);

            for (int i = 0; i < n; i++)
            {
                var sector  = galaxy.Sectors[i];
                var polygon = VoronoiHelper.ComputeCell(i, sites, bounds);
                sector.VoronoiPolygon = polygon;
                sector.VoronoiArea    = polygon?.Count >= 3 ? PolygonArea(polygon) : 0f;

                int edgeCount = polygon.Count;
                var neighborSet = new System.Collections.Generic.HashSet<string>();
                for (int e = 0; e < edgeCount; e++)
                {
                    Vector2 mid      = (polygon[e] + polygon[(e + 1) % edgeCount]) * 0.5f;
                    int     neighbor = NearestSiteIndex(mid, sites, i);
                    string  uid      = neighbor >= 0 ? galaxy.Sectors[neighbor].Uid : null;
                    neighborSet.Add(uid);
                }
                sector.VoronoiEdgeNeighbors = new List<string>(neighborSet);
            }
        }

        private static int NearestSiteIndex(Vector2 point, Vector2[] sites, int excludeIdx)
        {
            int   best   = -1;
            float bestSq = float.MaxValue;
            for (int k = 0; k < sites.Length; k++)
            {
                if (k == excludeIdx) continue;
                float sq = (sites[k] - point).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = k; }
            }
            return best;
        }

        private void ReassignStarPositions(GalaxyData galaxy)
        {
            float shrink = _minStarDist * 0.5f;

            foreach (var sector in galaxy.Sectors)
            {
                List<Vector2> poly = null;
                Rect          bbox = default;

                if (sector.VoronoiPolygon?.Count >= 3)
                {
                    poly = ShrinkPolygon(sector.VoronoiPolygon, shrink);
                    if (poly == null || poly.Count < 3)
                        poly = ShrinkPolygon(sector.VoronoiPolygon, shrink * 0.5f);
                    if (poly != null && poly.Count >= 3)
                        bbox = PolygonBounds(poly);
                    else
                        poly = null;
                }

                var usedPos = new List<Vector2>(sector.Stars.Count);
                foreach (var star in sector.Stars)
                {
                    var (pos, iters) = poly != null
                        ? SampleStarInPolygon(poly, bbox, usedPos, galaxy)
                        : SampleStarInCircle(sector.Center, _sectorRadius, usedPos, galaxy);

                    star.Position = pos;
                    usedPos.Add(pos);
                }
            }
        }

        private void RedistributeTooCloseSystems(GalaxyData galaxy)
        {
            if (_minStarDistSq <= 0f) return;

            float borderThreshSq = (_minStarDist * 0.5f) * (_minStarDist * 0.5f);

            var allStars = new List<StarData>(galaxy.StarsMap.Values);
            var toRelocate = new HashSet<StarData>();

            for (int i = 0; i < allStars.Count; i++)
            for (int j = i + 1; j < allStars.Count; j++)
            {
                if ((allStars[i].Position - allStars[j].Position).sqrMagnitude < _minStarDistSq)
                {
                    var a = allStars[i];
                    var b = allStars[j];
                    if (a.IsPremade && b.IsPremade) continue;
                    StarData pick;
                    if (a.IsPremade) pick = b;
                    else if (b.IsPremade) pick = a;
                    else pick = a.ParentSector.Stars.Count >= b.ParentSector.Stars.Count ? a : b;
                    toRelocate.Add(pick);
                }
            }

            foreach (var star in allStars)
            {
                if (star.IsPremade || toRelocate.Contains(star)) continue;
                var poly = star.ParentSector?.VoronoiPolygon;
                if (poly == null || poly.Count < 3) continue;
                if (MinDistToPolygonEdgeSq(star.Position, poly) < borderThreshSq)
                    toRelocate.Add(star);
            }

            if (toRelocate.Count == 0) return;

            Debug.Log($"[GalaxyGenerator] Перераспределение {toRelocate.Count} систем, нарушающих минимальное расстояние.");

            var sectorAreas = new Dictionary<SectorData, float>(galaxy.Sectors.Count);
            foreach (var sector in galaxy.Sectors)
                sectorAreas[sector] = sector.VoronoiPolygon?.Count >= 3 ? PolygonArea(sector.VoronoiPolygon) : 0f;

            float shrink = _minStarDist * 0.5f;

            foreach (var star in toRelocate)
            {
                var oldSector = star.ParentSector;
                oldSector.Stars.Remove(star);

                var allPos = new List<Vector2>(galaxy.StarsMap.Count - 1);
                foreach (var s in galaxy.StarsMap.Values)
                    if (s != star) allPos.Add(s.Position);

                SectorData targetSector = null;
                float bestRatio = float.MinValue;
                foreach (var sector in galaxy.Sectors)
                {
                    if (!sectorAreas.TryGetValue(sector, out float area) || area <= 0f) continue;
                    float ratio = area / (sector.Stars.Count + 1);
                    if (ratio > bestRatio) { bestRatio = ratio; targetSector = sector; }
                }

                if (targetSector == null) { oldSector.Stars.Add(star); continue; }

                List<Vector2> poly = null;
                Rect bbox = default;
                if (targetSector.VoronoiPolygon?.Count >= 3)
                {
                    poly = ShrinkPolygon(targetSector.VoronoiPolygon, shrink);
                    if (poly?.Count >= 3) bbox = PolygonBounds(poly);
                    else poly = null;
                }

                var (pos, _) = poly != null
                    ? SampleStarInPolygon(poly, bbox, allPos, galaxy)
                    : SampleStarInCircle(targetSector.Center, _sectorRadius, allPos, galaxy);

                star.Position = pos;
                star.ParentSector = targetSector;
                targetSector.Stars.Add(star);
            }
        }

        private (Vector2 pos, int iters) SampleStarInPolygon(
            List<Vector2> poly, Rect bbox, List<Vector2> usedPos, GalaxyData galaxy)
        {
            float border = _minStarDist;
            for (int i = 0; i < StarPlaceMaxIter; i++)
            {
                var candidate = new Vector2(
                    GameRng.Range(bbox.xMin, bbox.xMax),
                    GameRng.Range(bbox.yMin, bbox.yMax));

                if (!PointInPolygon(candidate, poly)) continue;
                if (!IsFarFromAll(candidate, usedPos)) continue;
                if (candidate.x < border || candidate.x > galaxy.Width  - border ||
                    candidate.y < border || candidate.y > galaxy.Height - border) continue;

                return (candidate, i + 1);
            }

            Debug.LogWarning($"[StarPlace] Не удалось найти позицию за {StarPlaceMaxIter} итераций — используется центроид.");
            return (PolygonCentroid(poly), StarPlaceMaxIter);
        }

        private (Vector2 pos, int iters) SampleStarInCircle(
            Vector2 center, float radius, List<Vector2> usedPos, GalaxyData galaxy)
        {
            float border = _minStarDist;
            for (int i = 0; i < StarPlaceMaxIter; i++)
            {
                var candidate = GetRandomPos(galaxy.Width, galaxy.Height, center, radius);
                if (!IsFarFromAll(candidate, usedPos)) continue;
                if (candidate.x < border || candidate.x > galaxy.Width  - border ||
                    candidate.y < border || candidate.y > galaxy.Height - border) continue;
                return (candidate, i + 1);
            }
            return (center, StarPlaceMaxIter);
        }

        private bool IsFarFromAll(Vector2 p, List<Vector2> existing)
        {
            foreach (var e in existing)
                if ((p - e).sqrMagnitude < _minStarDistSq) return false;
            return true;
        }

        private static List<Vector2> ShrinkPolygon(List<Vector2> poly, float amount)
        {
            int n = poly.Count;
            if (n < 3) return null;
            if (amount <= 0f) return new List<Vector2>(poly);

            var centroid = PolygonCentroid(poly);
            var pts  = new Vector2[n];
            var dirs = new Vector2[n];

            for (int i = 0; i < n; i++)
            {
                Vector2 a    = poly[i];
                Vector2 b    = poly[(i + 1) % n];
                Vector2 dir  = (b - a).normalized;
                Vector2 perp = new Vector2(-dir.y, dir.x);
                if (Vector2.Dot(perp, centroid - a) < 0f) perp = -perp;
                pts[i]  = a + perp * amount;
                dirs[i] = dir;
            }

            var result = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                int prev = (i - 1 + n) % n;
                var pt = LineIntersect(pts[prev], dirs[prev], pts[i], dirs[i]);
                if (!pt.HasValue) return null;
                result.Add(pt.Value);
            }
            return result;
        }

        private static Vector2? LineIntersect(Vector2 p1, Vector2 d1, Vector2 p2, Vector2 d2)
        {
            float cross = d1.x * d2.y - d1.y * d2.x;
            if (Mathf.Abs(cross) < 1e-9f) return null;
            Vector2 diff = p2 - p1;
            float t = (diff.x * d2.y - diff.y * d2.x) / cross;
            return p1 + d1 * t;
        }

        private static bool PointInPolygon(Vector2 p, List<Vector2> poly)
        {
            int  n      = poly.Count;
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = poly[i].x, yi = poly[i].y;
                float xj = poly[j].x, yj = poly[j].y;
                if ((yi > p.y) != (yj > p.y) &&
                    p.x < (xj - xi) * (p.y - yi) / (yj - yi) + xi)
                    inside = !inside;
            }
            return inside;
        }

        private static Rect PolygonBounds(List<Vector2> poly)
        {
            float xMin = float.MaxValue, xMax = float.MinValue;
            float yMin = float.MaxValue, yMax = float.MinValue;
            foreach (var p in poly)
            {
                if (p.x < xMin) xMin = p.x; if (p.x > xMax) xMax = p.x;
                if (p.y < yMin) yMin = p.y; if (p.y > yMax) yMax = p.y;
            }
            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        private static Vector2 PolygonCentroid(List<Vector2> poly)
        {
            var sum = Vector2.zero;
            foreach (var p in poly) sum += p;
            return sum / poly.Count;
        }

        private static float PolygonArea(List<Vector2> poly)
        {
            float area = 0f;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
                area += (poly[j].x + poly[i].x) * (poly[j].y - poly[i].y);
            return Mathf.Abs(area) * 0.5f;
        }

        private static float MinDistToPolygonEdgeSq(Vector2 p, List<Vector2> poly)
        {
            float minSq = float.MaxValue;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 a = poly[j], b = poly[i];
                Vector2 ab = b - a;
                float abSq = ab.sqrMagnitude;
                float t = abSq > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / abSq) : 0f;
                float dSq = (p - (a + ab * t)).sqrMagnitude;
                if (dSq < minSq) minSq = dSq;
            }
            return minSq;
        }

        private void BalanceStarDensityAcrossSectors(GalaxyData galaxy, GalaxyConfigData cfg)
        {
            int n = galaxy.Sectors.Count;
            if (n <= 1) return;

            var areas = new float[n];
            float totalArea = 0f;
            for (int i = 0; i < n; i++)
            {
                areas[i]  = galaxy.Sectors[i].VoronoiArea;
                totalArea += areas[i];
            }
            if (totalArea <= 0f) return;

            int totalStars = galaxy.StarsMap.Count;

            int[] caps = new int[n];
            for (int i = 0; i < n; i++)
                caps[i] = GetSectorCapacity(galaxy.Sectors[i], cfg);

            int totalMoved = 0;

            for (int pass = 0; pass < DensityBalanceMaxPasses; pass++)
            {
                int[] targets = new int[n];
                for (int i = 0; i < n; i++)
                {
                    int premadeCount = 0;
                    foreach (var s in galaxy.Sectors[i].Stars)
                        if (s.IsPremade) premadeCount++;
                    float proportional = areas[i] / totalArea * totalStars;
                    targets[i] = Mathf.Clamp(
                        Mathf.RoundToInt(proportional),
                        Mathf.Max(cfg.MinStarsPerSector, premadeCount),
                        caps[i]);
                }

                bool anyMoved = false;

                var srcOrder = new List<int>(n);
                for (int i = 0; i < n; i++)
                    if (galaxy.Sectors[i].Stars.Count > targets[i]) srcOrder.Add(i);
                srcOrder.Sort((a, b) =>
                {
                    float da = areas[a] > 0f ? (float)galaxy.Sectors[a].Stars.Count / areas[a] : float.MaxValue;
                    float db = areas[b] > 0f ? (float)galaxy.Sectors[b].Stars.Count / areas[b] : float.MaxValue;
                    return db.CompareTo(da);
                });

                foreach (int si in srcOrder)
                {
                    var src     = galaxy.Sectors[si];
                    int surplus = src.Stars.Count - targets[si];
                    if (surplus <= 0) continue;

                    var movable = new List<StarData>(src.Stars.Count);
                    foreach (var s in src.Stars)
                        if (!s.IsPremade) movable.Add(s);
                    int toMove  = Mathf.Min(surplus, movable.Count);
                    if (toMove <= 0) continue;

                    var dstOrder = new List<int>(n);
                    for (int di = 0; di < n; di++)
                        if (di != si && galaxy.Sectors[di].Stars.Count < targets[di]) dstOrder.Add(di);
                    dstOrder.Sort((a, b) =>
                    {
                        float da = areas[a] > 0f ? (float)galaxy.Sectors[a].Stars.Count / areas[a] : float.MaxValue;
                        float db = areas[b] > 0f ? (float)galaxy.Sectors[b].Stars.Count / areas[b] : float.MaxValue;
                        return da.CompareTo(db);
                    });

                    int mi = movable.Count - 1;
                    foreach (int di in dstOrder)
                    {
                        if (toMove <= 0 || mi < 0) break;
                        var dst     = galaxy.Sectors[di];
                        int deficit = targets[di] - dst.Stars.Count;
                        int count   = Mathf.Min(toMove, deficit);

                        for (int k = 0; k < count && mi >= 0; k++, mi--)
                        {
                            var star = movable[mi];
                            src.Stars.Remove(star);
                            star.ParentSector = dst;
                            dst.Stars.Add(star);
                            totalMoved++;
                            anyMoved = true;
                        }
                        toMove -= count;
                    }
                }

                if (!anyMoved) break;
            }

            if (totalMoved > 0)
                Debug.Log($"[GalaxyGenerator] BalanceStarDensity: перераспределено {totalMoved} звёзд между секторами.");
        }
    }
}
