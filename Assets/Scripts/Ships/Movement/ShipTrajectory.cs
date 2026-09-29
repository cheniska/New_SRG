using UnityEngine;
using System.Collections.Generic;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Presentation.Common;
using SRG.Utils;

namespace SRG.Ships.Movement
{
    // Кинематический планировщик траектории корабля. Модель — «самолёт на плоскости»:
    // постоянная скорость и ограниченная угловая скорость разворота. Сторону разворота на каждом
    // шаге выбираем по длине кратчайшего пути «дуга + прямая» (Dubins-путь класса CS).
    // Препятствия: одно "солнце" (берётся из StarData) + произвольные CircleObstacle/PolygonObstacle.
    // Plan = рекурсивный обход: если прямой сегмент пересекает препятствие, ищем точку обхода
    // (касательная для круга, выпуклая вершина для полигона) и продолжаем от неё.
    public static class ShipTrajectory
    {
        // Плотность waypoint'ов на ход. Чем выше — тем плавнее линия и точнее движение.
        // 80 ≈ 4× больше шагов, чем сабтёрнов (10), это даёт буфер для интерполяции.
        public const int WaypointsPerTurn = 80;
        // Максимальная длина одного дугового сегмента (в waypoint'ах).
        // Реальная длина = MaxWaypointsPerSegment / WaypointsPerTurn ходов.
        private const int MaxWaypointsPerSegment = WaypointsPerTurn * 6;
        // Глубина рекурсивного обхода (сколько препятствий подряд).
        private const int MaxPlanDepth = 8;
        // Запас, который сохраняется снаружи каждого препятствия.
        public const float StarAvoidMargin = 0.4f;
        // По умолчанию для произвольных препятствий.
        public const float ObstacleMargin = 0.3f;
        // Эпсилон для проверки "достигли точки".
        private const float ArrivalEps = 0.0005f;

        // Глобально доступные обстоятельства (для отладки / спецсценариев).
        // Звезда добавляет своё круговое препятствие автоматически.
        private static readonly List<IPathObstacle> s_obstacleBuffer = new(8);

        // ─── Публичный API ─────────────────────────────────────────────────

        /// <summary>
        /// Строит путь от <paramref name="from"/> до <paramref name="to"/> с учётом препятствий
        /// в системе <paramref name="star"/>. Использует кинематическую модель: каждый узел —
        /// шаг длины speed/WaypointsPerTurn под углом, ограниченным turnSpeed/WaypointsPerTurn.
        /// </summary>
        public static List<Vector2> BuildPath(
            Vector2 from, Vector2 to,
            float headingRad,
            float speedPerTurn,
            float turnRadPerTurn,
            StarData star,
            List<Vector2> result = null)
        {
            if (result == null) result = new List<Vector2>(256);
            else result.Clear();

            if (speedPerTurn <= 1e-5f) { result.Add(from); return result; }
            if ((to - from).sqrMagnitude < ArrivalEps) { result.Add(to); return result; }

            var obstacles = CollectObstacles(star);

            // Если стартовая точка внутри какого-то препятствия — выталкиваем наружу
            // и трактуем это как первый "обход".
            Vector2 start = from;
            for (int i = 0; i < obstacles.Count; i++)
            {
                var obs = obstacles[i];
                if (obs.Contains(start, ObstacleMargin))
                    start = obs.PushOutside(start, ObstacleMargin);
            }

            float heading = float.IsNaN(headingRad)
                ? Mathf.Atan2(to.y - start.y, to.x - start.x)
                : headingRad;

            PlanRecursive(start, heading, to, obstacles, speedPerTurn, turnRadPerTurn, result, depth: 0);

            // Гарантируем, что цель присутствует в финале — иначе ship.Pos никогда не сольётся с TargetPosition.
            if (result.Count == 0 || (result[result.Count - 1] - to).sqrMagnitude > ArrivalEps)
                result.Add(to);

            return result;
        }

        /// <summary>
        /// Convenience-обёртка для текущего корабля.
        /// Тянет скорость / угловую скорость из ShipData; звезду — из переданной звезды.
        /// </summary>
        public static List<Vector2> BuildPath(
            ShipData ship, Vector2 target, StarData star, List<Vector2> result = null)
            => BuildPath(
                ship.Position, target,
                ship.CurrentHeading,
                EffectiveSpeedPerTurn(ship),
                EffectiveTurnRadPerTurn(ship),
                star, result);


        /// <summary>Линейная скорость корабля в единицах/ход.</summary>
        public static float EffectiveSpeedPerTurn(ShipData ship)
        {
            if (ship == null) return 0f;
            // Множитель форсажа уже учтён в ship.ActualSpeed (см. EquipmentSystem.GetActualSpeed).
            return SRUnits.ToWorld(ship.ActualSpeed);
        }

        /// <summary>Угловая скорость разворота корабля в радианах/ход.</summary>
        public static float EffectiveTurnRadPerTurn(ShipData ship)
        {
            if (ship == null) return Mathf.PI;
            float deg = ship.TurnSpeedDeg > 0f ? ship.TurnSpeedDeg : 180f;
            // Форсаж даёт прирост скорости ценой манёвренности — дуги шире.
            if (ship.ForsageActive) deg *= 0.5f;
            return deg * Mathf.Deg2Rad;
        }

        // ─── Препятствия ────────────────────────────────────────────────────

        private static List<IPathObstacle> CollectObstacles(StarData star)
        {
            s_obstacleBuffer.Clear();
            if (star == null) return s_obstacleBuffer;

            // Солнце системы — основное препятствие (с дополнительным margin'ом).
            if (star.StarRadius > 0f)
                s_obstacleBuffer.Add(new CircleObstacle(star.Position, star.StarRadius + StarAvoidMargin));

            if (star.CircleObstacles != null)
                foreach (var c in star.CircleObstacles) if (c != null) s_obstacleBuffer.Add(c);
            if (star.PolygonObstacles != null)
                foreach (var p in star.PolygonObstacles) if (p != null && p.Vertices.Count >= 3)
                    s_obstacleBuffer.Add(p);

            return s_obstacleBuffer;
        }

        private static IPathObstacle FirstBlocking(
            Vector2 a, Vector2 b, IReadOnlyList<IPathObstacle> obstacles, float margin)
        {
            for (int i = 0; i < obstacles.Count; i++)
            {
                var obs = obstacles[i];
                if (obs.SegmentBlocked(a, b, margin)) return obs;
            }
            return null;
        }

        // ─── Рекурсивный планировщик с обходом препятствий ───────────────────

        private static void PlanRecursive(
            Vector2 from, float headingRad, Vector2 target,
            IReadOnlyList<IPathObstacle> obstacles,
            float speedPerTurn, float turnRadPerTurn,
            List<Vector2> output, int depth)
        {
            var blocking = FirstBlocking(from, target, obstacles, ObstacleMargin);
            if (blocking == null || depth >= MaxPlanDepth)
            {
                GenerateKinematicSpline(from, headingRad, target, speedPerTurn, turnRadPerTurn, output);
                return;
            }

            if (!blocking.TryGetAvoidanceWaypoint(from, target, ObstacleMargin, out Vector2 detour))
            {
                GenerateKinematicSpline(from, headingRad, target, speedPerTurn, turnRadPerTurn, output);
                return;
            }

            int before = output.Count;
            GenerateKinematicSpline(from, headingRad, detour, speedPerTurn, turnRadPerTurn, output);
            if (output.Count == before)
            {
                // Спайн ничего не добавил — не зацикливаемся.
                return;
            }

            Vector2 newFrom = output[output.Count - 1];
            Vector2 prev = output.Count >= 2 ? output[output.Count - 2] : from;
            float newHeading = Mathf.Atan2(newFrom.y - prev.y, newFrom.x - prev.x);
            if (float.IsNaN(newHeading)) newHeading = headingRad;

            PlanRecursive(newFrom, newHeading, target, obstacles,
                speedPerTurn, turnRadPerTurn, output, depth + 1);
        }

        // ─── Кинематический генератор waypoint'ов ───────────────────────────

        // Идёт от cur к target с фиксированным шагом и ограниченной угловой скоростью.
        // Каждая итерация: выбрали сторону разворота, повернулись на ≤ stepTurn, шагнули на stepLen.
        //
        // Сторона разворота — та, для которой короче путь «дуга минимального радиуса + касательная
        // прямая до цели» (см. ArcThenLineLength). Если цель внутри обеих окружностей разворота
        // (оба пути невозможны), корабль идёт прямо и набирает дистанцию, пока цель не выйдет
        // из окружностей. Страховка от зацикливания — окно «нет прогресса»: по его истечении
        // цель добавляется финальным waypoint'ом, дальше ShipMoveStep дойдёт по прямой.
        private static void GenerateKinematicSpline(
            Vector2 from, float headingRad, Vector2 target,
            float speedPerTurn, float turnRadPerTurn,
            List<Vector2> output)
        {
            if (speedPerTurn <= 1e-5f) return;
            float stepLen = speedPerTurn / WaypointsPerTurn;
            float stepTurn = turnRadPerTurn / WaypointsPerTurn;
            if (stepLen < 1e-6f || stepTurn < 1e-6f) return;

            float turnRadius = stepLen / stepTurn;   // радиус дуги при максимальном развороте

            Vector2 cur = from;
            float curA = float.IsNaN(headingRad) ? Angles.Toward(from, target) : headingRad;

            // Окно «нет прогресса»: полный оборот + полхода запаса — этого хватает на
            // облёт почти на 360° с последующей прямой.
            float minDistSq = (target - cur).sqrMagnitude;
            int iterSinceImprovement = 0;
            int stuckLimit = Mathf.CeilToInt(Angles.TwoPi / stepTurn) + WaypointsPerTurn / 2;
            const float ImprovementEps = 1e-5f;

            for (int i = 0; i < MaxWaypointsPerSegment; i++)
            {
                float distSq = (target - cur).sqrMagnitude;
                if (distSq <= stepLen * stepLen)
                {
                    output.Add(target);
                    return;
                }

                if (distSq + ImprovementEps < minDistSq)
                {
                    minDistSq = distSq;
                    iterSinceImprovement = 0;
                }
                else if (++iterSinceImprovement > stuckLimit)
                {
                    output.Add(target);
                    return;
                }

                float desiredA = Angles.Toward(cur, target, curA);
                if (Mathf.Abs(Angles.WrapPi(desiredA - curA)) <= stepTurn)
                {
                    curA = desiredA;   // уже выровнялись — прямой шаг
                }
                else
                {
                    float left  = ArcThenLineLength(cur, curA, target, turnRadius, +1);
                    float right = ArcThenLineLength(cur, curA, target, turnRadius, -1);
                    if (!float.IsInfinity(left) || !float.IsInfinity(right))
                        curA += (left <= right ? +1 : -1) * stepTurn;
                    // иначе — цель внутри обеих окружностей: держим курс и отходим.
                }

                cur += Angles.Dir(curA) * stepLen;
                output.Add(cur);
            }
        }

        /// <summary>
        /// Длина пути «дуга радиуса R в сторону side (+1 — влево/CCW, −1 — вправо/CW), затем
        /// прямая по касательной до цели». Если цель внутри окружности разворота — бесконечность.
        /// </summary>
        public static float ArcThenLineLength(Vector2 pos, float heading, Vector2 target, float radius, int side)
        {
            Vector2 center = pos + Angles.Dir(heading + side * Mathf.PI * 0.5f) * radius;
            Vector2 toTarget = target - center;
            float d = toTarget.magnitude;
            if (d < radius) return float.PositiveInfinity;

            // Касательная из цели к окружности: точка касания видна из центра под углом
            // acos(R/d) от направления на цель.
            float tangentLen = Mathf.Sqrt(Mathf.Max(0f, d * d - radius * radius));
            float tangentAngle = Angles.Of(toTarget) - side * Mathf.Acos(Mathf.Clamp(radius / d, -1f, 1f));
            float startAngle = Angles.Of(pos - center);
            // Угол дуги, пройденной в сторону side от стартовой точки до точки касания, ∈ [0, 2π).
            float sweep = Mathf.Repeat(side * (tangentAngle - startAngle), Angles.TwoPi);
            return sweep * radius + tangentLen;
        }

        // ─── Утилиты ────────────────────────────────────────────────────────


        public static bool SegmentIntersectsCircle(Vector2 a, Vector2 b, Vector2 center, float radius)
        {
            Vector2 d = b - a;
            Vector2 f = a - center;
            float A = Vector2.Dot(d, d);
            if (A < 1e-10f)
                return Vector2.Dot(f, f) <= radius * radius;
            float B = 2f * Vector2.Dot(f, d);
            float C = Vector2.Dot(f, f) - radius * radius;
            float disc = B * B - 4f * A * C;
            if (disc < 0f) return false;
            float sq = Mathf.Sqrt(disc);
            float t1 = (-B - sq) / (2f * A);
            float t2 = (-B + sq) / (2f * A);
            return (t1 >= 0f && t1 <= 1f) || (t2 >= 0f && t2 <= 1f) || (t1 < 0f && t2 > 1f);
        }
    }

    public class PathRenderer : MonoBehaviour
    {
        [Header("Dots")]
        [Tooltip("Расстояние между точками вдоль траектории (в мировых единицах). " +
                 "Сквозное по всей цепочке: первый dot в начале пути, далее через dotSpacing.")]
        [SerializeField] private float dotSpacing = 0.45f;
        [SerializeField] private float dotSize = 0.10f;
        [SerializeField] private float markerSize = 0.16f;
        [SerializeField] private int texResolution = 32;

        [Header("Bezier (только для preSampled=false legacy-путей)")]
        [Tooltip("Коэффициент изгиба относительно длины шага. 0 = прямая линия.")]
        [SerializeField] private float bezierCurveFactor = 0.35f;
        [SerializeField] private int bezierSegments = 60;

        [Header("Colors")]
        [SerializeField] private Color colorGreen = new Color(0.2f, 0.9f, 0.3f, 1f);
        [SerializeField] private Color colorOrange = new Color(1.0f, 0.55f, 0.1f, 1f);

        [Header("Sorting")]
        [SerializeField] private string sortingLayerName = "Default";

        [Header("Day Label")]
        [SerializeField] private int labelFontSize = 14;
        [SerializeField] private Color labelColor = Color.white;

        private Sprite _spriteGreen;
        private Sprite _spriteOrange;
        private readonly List<GameObject> _pool = new();
        private readonly List<GameObject> _active = new();
        private GameObject _labelObj;
        private TextMesh _labelMesh;
        // Полилиния, по которой реально расставляются точки + кумулятивная длина в каждой её вершине.
        // Для preSampled=true — это сама входная цепочка. Для preSampled=false — Безье-сэмплы.
        private readonly List<Vector2> _renderLine = new(512);
        private readonly List<float>   _renderCumLen = new(512);

        private void Awake()
        {
            _spriteGreen = CreateCircleSprite(colorGreen, texResolution);
            _spriteOrange = CreateCircleSprite(colorOrange, texResolution);

            _labelObj = new GameObject("PathDaysLabel");
            _labelObj.transform.SetParent(transform, false);
            _labelMesh = _labelObj.AddComponent<TextMesh>();
            _labelMesh.fontSize = labelFontSize;
            _labelMesh.color = labelColor;
            _labelMesh.anchor = TextAnchor.MiddleCenter;
            _labelMesh.alignment = TextAlignment.Center;
            _labelMesh.characterSize = 0.1f;

            var mr = _labelObj.GetComponent<MeshRenderer>();
            mr.sortingLayerName = sortingLayerName;
            mr.sortingOrder = SortingLayerRegistry.Offset(SortLayer.PathDot, 1);
            _labelObj.SetActive(false);
        }

        private void OnDestroy()
        {
            Destroy(_spriteGreen?.texture); Destroy(_spriteGreen);
            Destroy(_spriteOrange?.texture); Destroy(_spriteOrange);
        }
        public void DrawPath(List<Vector2> chain, float stepPerTurn, bool preSampled = false,
            Vector2? endMarkerOverride = null)
        {
            ReturnAllToPool();
            if (chain == null || chain.Count < 2) { _labelObj.SetActive(false); return; }

            BuildRenderLine(chain, stepPerTurn, preSampled);
            if (_renderLine.Count < 2) { _labelObj.SetActive(false); return; }

            float totalDist = _renderCumLen[_renderCumLen.Count - 1];
            int totalDays = stepPerTurn > 0f ? Mathf.Max(1, Mathf.CeilToInt(totalDist / stepPerTurn)) : 1;
            float spacing = Mathf.Max(0.01f, dotSpacing);

            // Точки на фиксированном расстоянии вдоль глобальной длины пути.
            // Первая точка ставится на расстоянии spacing от начала (не перекрывая корабль).
            int hint = 0;
            for (float d = spacing; d < totalDist; d += spacing)
            {
                int day = stepPerTurn > 0f ? Mathf.FloorToInt(d / stepPerTurn) : 0;
                if (day >= totalDays) break;

                float distToBound = stepPerTurn > 0f ? (day + 1) * stepPerTurn - d : float.MaxValue;
                bool isLastDay = day == totalDays - 1;

                Sprite sprite = day == 0 ? _spriteGreen : _spriteOrange;
                if (distToBound < spacing * 0.5f && !isLastDay) sprite = _spriteGreen;

                PlaceDot(SampleAtDistance(d, ref hint), sprite, dotSize);
            }

            // Маркеры границ дней (более крупные, всегда зелёные).
            if (stepPerTurn > 0f && totalDays > 1)
            {
                int markerHint = 0;
                for (int day = 1; day < totalDays; day++)
                {
                    float d = day * stepPerTurn;
                    if (d >= totalDist) break;
                    PlaceDot(SampleAtDistance(d, ref markerHint), _spriteGreen, markerSize);
                }
            }

            Vector2 markerPos = endMarkerOverride ?? chain[chain.Count - 1];
            PlaceDot(markerPos, _spriteGreen, markerSize);
            ShowLabel(markerPos, totalDays);
        }

        public void ClearPath() { ReturnAllToPool(); _labelObj.SetActive(false); }

        // Строит полилинию, по которой ставятся точки. Для preSampled — это сама цепочка
        // (плотный кинематический сплайн уже даёт визуально гладкую линию). Для legacy-режима
        // (preSampled=false) — Безье-сэмплы между разреженными waypoint'ами.
        private void BuildRenderLine(List<Vector2> chain, float stepPerTurn, bool preSampled)
        {
            _renderLine.Clear();
            _renderCumLen.Clear();
            _renderLine.Add(chain[0]);
            _renderCumLen.Add(0f);

            if (preSampled)
            {
                for (int i = 1; i < chain.Count; i++)
                {
                    Vector2 p = chain[i];
                    float dist = Vector2.Distance(_renderLine[_renderLine.Count - 1], p);
                    if (dist < 1e-6f) continue;
                    _renderLine.Add(p);
                    _renderCumLen.Add(_renderCumLen[_renderCumLen.Count - 1] + dist);
                }
                return;
            }

            // Legacy: кубический Безье между точками цепочки.
            float tangentScale = bezierCurveFactor * Mathf.Max(stepPerTurn, 0.0001f);
            int n = chain.Count;
            for (int seg = 1; seg < n; seg++)
            {
                Vector2 prev0 = seg - 1 > 0 ? chain[seg - 2] : chain[0] - (chain[1] - chain[0]);
                Vector2 next1 = seg + 1 < n ? chain[seg + 1] : chain[n - 1] + (chain[n - 1] - chain[n - 2]);
                Vector2 p0 = chain[seg - 1], p3 = chain[seg];
                Vector2 t0 = (chain[seg]     - prev0) * 0.5f * tangentScale;
                Vector2 t1 = (next1 - chain[seg - 1]) * 0.5f * tangentScale;
                Vector2 ctrl1 = p0 + t0 / 3f;
                Vector2 ctrl2 = p3 - t1 / 3f;

                for (int i = 1; i <= bezierSegments; i++)
                {
                    float t  = (float)i / bezierSegments;
                    float u  = 1f - t;
                    Vector2 pt = u * u * u * p0
                               + 3f * u * u * t * ctrl1
                               + 3f * u * t * t * ctrl2
                               + t * t * t * p3;
                    float dist = Vector2.Distance(_renderLine[_renderLine.Count - 1], pt);
                    if (dist < 1e-6f) continue;
                    _renderLine.Add(pt);
                    _renderCumLen.Add(_renderCumLen[_renderCumLen.Count - 1] + dist);
                }
            }
        }

        // Возвращает точку на полилинии на расстоянии d от начала. `hint` — индекс начала
        // последнего использованного сегмента (для O(1) при возрастающих d).
        private Vector2 SampleAtDistance(float d, ref int hint)
        {
            int count = _renderLine.Count;
            if (count == 0) return Vector2.zero;
            if (hint < 0) hint = 0;
            if (hint >= count - 1) return _renderLine[count - 1];

            while (hint < count - 2 && _renderCumLen[hint + 1] < d) hint++;
            float segStart = _renderCumLen[hint];
            float segEnd   = _renderCumLen[hint + 1];
            float segLen = segEnd - segStart;
            if (segLen < 1e-6f) return _renderLine[hint];
            float t = Mathf.Clamp01((d - segStart) / segLen);
            return Vector2.Lerp(_renderLine[hint], _renderLine[hint + 1], t);
        }

        private void ShowLabel(Vector2 pos, int days)
        {
            _labelMesh.text = days.ToString();
            _labelObj.transform.position = new Vector3(pos.x + 0.25f, pos.y + 0.25f, 0f);
            _labelObj.SetActive(true);
        }

        private void PlaceDot(Vector2 worldPos, Sprite sprite, float size)
        {
            var obj = GetFromPool();
            obj.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
            obj.transform.localScale = Vector3.one * size;
            var sr = obj.GetComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerName = sortingLayerName;
            sr.sortingOrder = SortingLayerRegistry.Get(SortLayer.PathDot);
            obj.SetActive(true);
            _active.Add(obj);
        }

        private GameObject GetFromPool()
        {
            if (_pool.Count > 0) { var obj = _pool[^1]; _pool.RemoveAt(_pool.Count - 1); return obj; }
            return CreateDotObject();
        }

        private void ReturnAllToPool() { foreach (var obj in _active) { obj.SetActive(false); _pool.Add(obj); } _active.Clear(); }

        private GameObject CreateDotObject()
        {
            var obj = new GameObject("PathDot");
            obj.transform.SetParent(transform, worldPositionStays: false);
            obj.AddComponent<SpriteRenderer>();
            obj.SetActive(false);
            return obj;
        }

        private static Sprite CreateCircleSprite(Color color, int res)
            => SpriteUtility.CreateCircleSprite(res, color);
    }
}
