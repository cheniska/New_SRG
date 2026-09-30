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
        // ══════════════════════════════════════════════════════════════════════════
        // Утилиты

        private Vector2 GridToMap(Vector2 gridPos)
        {
            var ms = MapSize;
            return new Vector2(
                gridPos.x / Mathf.Max(1, _galaxy.Width)  * ms.x,
                gridPos.y / Mathf.Max(1, _galaxy.Height) * ms.y);
        }

        private static Color StarColor(string name)
        {
            switch ((name ?? "").ToLowerInvariant())
            {
                case "red":    return new Color(1f,    0.35f, 0.25f);
                case "yellow": return new Color(1f,    0.92f, 0.45f);
                case "blue":   return new Color(0.45f, 0.70f, 1f);
                case "green":  return new Color(0.40f, 1f,    0.50f);
                case "white":  return new Color(0.90f, 0.92f, 1f);
                case "black":  return new Color(0.40f, 0.35f, 0.55f);
                case "purple": return new Color(0.75f, 0.40f, 1f);
                default:       return Color.white;
            }
        }

        private static void Label(Transform parent, string name, string text, int size,
            Vector2 oMin, Vector2 oMax, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = oMin; rt.offsetMax = oMax;
            var t = go.AddComponent<Text>();
            t.text          = text;
            t.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize      = size;
            t.color         = color;
            t.alignment     = anchor;
            t.raycastTarget = false;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }
    }
}
