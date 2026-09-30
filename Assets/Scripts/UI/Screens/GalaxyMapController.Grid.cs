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
        // ── Сетка парсеков ────────────────────────────────────────────────────────
        private void UpdateGrid()
        {
            if (_voronoi == null || _galaxy == null) return;

            _voronoi.GridLinesX.Clear();
            _voronoi.GridLinesY.Clear();

            var   ms     = MapSize;
            const float stepPc  = 5f;
            float stepPxX = stepPc * ms.x / Mathf.Max(1f, _galaxy.Width);
            float stepPxY = stepPc * ms.y / Mathf.Max(1f, _galaxy.Height);

            for (float x = 0f; x <= ms.x + 0.5f; x += stepPxX) _voronoi.GridLinesX.Add(x);
            for (float y = 0f; y <= ms.y + 0.5f; y += stepPxY) _voronoi.GridLinesY.Add(y);
        }

        // ── Debug: координатные подписи вершин Вороного ───────────────────────────

        private void SpawnDebugLabels()
        {
            foreach (var (rt, _) in _debugLabelRTs)
                if (rt != null) Destroy(rt.gameObject);
            _debugLabelRTs.Clear();

            if (_debugLabelContainer == null || _galaxy?.Sectors == null) return;

            var seen = new System.Collections.Generic.HashSet<string>();

            foreach (var sector in _galaxy.Sectors)
            {
                var poly = sector.VoronoiPolygon;
                if (poly == null || poly.Count < 2) continue;

                for (int i = 0; i < poly.Count; i++)
                {
                    Vector2 v = poly[i];
                    // Дедупликация: одна вершина появляется в 2-3 ячейках
                    string key = $"{v.x:F2}_{v.y:F2}";
                    if (!seen.Add(key)) continue;

                    var go = new GameObject($"VV_{key}", typeof(RectTransform));
                    go.transform.SetParent(_debugLabelContainer, false);
                    var rt = go.GetComponent<RectTransform>();
                    rt.anchorMin = rt.anchorMax = Vector2.zero;
                    rt.pivot     = new Vector2(0f, 0.5f);
                    rt.sizeDelta = new Vector2(72f, 9f);
                    rt.anchoredPosition = GridToMap(v);

                    var tx = go.AddComponent<Text>();
                    tx.text          = $"({Mathf.RoundToInt(v.x)},{Mathf.RoundToInt(v.y)})";
                    tx.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    tx.fontSize      = 7;
                    tx.color         = new Color(0.45f, 0.55f, 0.85f, 0.75f);
                    tx.raycastTarget = false;

                    _debugLabelRTs.Add((rt, v));
                }
            }
        }

        // ── Логирование координат ─────────────────────────────────────────────────

        private void LogCoordinates()
        {
            if (_galaxy?.Sectors == null) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Map] ══ КООРДИНАТЫ ЗВЁЗД (пк) ══");
            foreach (var sector in _galaxy.Sectors)
                foreach (var star in sector.Stars)
                    sb.AppendLine($"[Map] ★ {star.Name,-24} ({Mathf.RoundToInt(star.Position.x),5} пк, {Mathf.RoundToInt(star.Position.y),5} пк)  сектор: {sector.Name}");

            sb.AppendLine("[Map] ══ ВЕРШИНЫ ВОРОНОГО (пк) ══");
            foreach (var sector in _galaxy.Sectors)
            {
                var poly = sector.VoronoiPolygon;
                if (poly == null || poly.Count < 2) continue;
                sb.AppendLine($"[Map]   Сектор '{sector.Name}' ({poly.Count} вершин):");
                for (int i = 0; i < poly.Count; i++)
                {
                    Vector2 a = poly[i];
                    Vector2 b = poly[(i + 1) % poly.Count];
                    sb.AppendLine($"[Map]     [{i}] ({Mathf.RoundToInt(a.x)},{Mathf.RoundToInt(a.y)}) пк → ({Mathf.RoundToInt(b.x)},{Mathf.RoundToInt(b.y)}) пк");
                }
            }

            Debug.Log(sb.ToString());
        }
    }
}
