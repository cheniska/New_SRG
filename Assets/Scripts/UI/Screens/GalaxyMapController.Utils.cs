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
using SRG.UI.Logic;

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

        private static Color StarColor(string name) => GalaxyMapPresenter.StarColor(name);

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
