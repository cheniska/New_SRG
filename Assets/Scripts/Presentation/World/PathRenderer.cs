using UnityEngine;
using System.Collections.Generic;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Presentation.Common;
using SRG.Utils;
using SRG.Ships.Movement;

namespace SRG.Presentation.World
{
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
