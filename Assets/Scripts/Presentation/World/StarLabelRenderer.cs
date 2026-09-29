using UnityEngine;
using System.Collections.Generic;
using SRG.Galaxy;
using SRG.Presentation.Common;

namespace SRG.Presentation.World
{
    public class StarLabelRenderer : MonoBehaviour
    {
        [Header("Label Settings")]
        [SerializeField] private int fontSize = 14;
        [SerializeField] private float characterSize = 0.1f;
        [SerializeField] private Vector3 labelOffset = new Vector3(0f, -0.8f, 0f);
        [SerializeField] private string sortingLayerName = "Default";
        [SerializeField] private int sortingOrder = 50;

        [Header("Icon Settings")]
        [SerializeField] private float iconSize = 0.3f;
        [SerializeField] private Vector3 iconOffset = new Vector3(0.4f, 0f, 0f);

        private readonly List<GameObject> _segmentObjects = new();
        private GameObject _iconObject;

        public void Setup(StarData star)
        {
            Clear();
            if (star == null) return;

            var renderData = star.NameRenderData;

            if (renderData == null || renderData.Segments == null || renderData.Segments.Count == 0)
            {
                RenderFallback(star.Name ?? string.Empty);
                return;
            }

            RenderSegments(renderData);

            if (renderData.HasIcon && !string.IsNullOrEmpty(renderData.IconPath))
                RenderIcon(renderData.IconPath, renderData.Segments.Count);
        }

        public void Refresh(StarData star) => Setup(star);

        public void Clear()
        {
            foreach (var obj in _segmentObjects)
                if (obj != null) Destroy(obj);
            _segmentObjects.Clear();

            if (_iconObject != null) { Destroy(_iconObject); _iconObject = null; }
        }

        private void RenderFallback(string name)
        {
            var obj = CreateSegmentObject("Label_Fallback", 0f);
            var mesh = obj.GetComponent<TextMesh>();
            mesh.text = name;
            mesh.color = Color.white;
            _segmentObjects.Add(obj);
        }

        private void RenderSegments(StarNameRenderData data)
        {
            float totalWidth = 0f;
            foreach (var seg in data.Segments)
                totalWidth += (seg.Text?.Length ?? 0) * characterSize * fontSize * 0.1f;

            float currentX = -totalWidth * 0.5f;

            for (int i = 0; i < data.Segments.Count; i++)
            {
                var seg = data.Segments[i];
                if (string.IsNullOrEmpty(seg.Text)) continue;

                var obj = CreateSegmentObject($"Label_Seg{i}", currentX);
                var mesh = obj.GetComponent<TextMesh>();
                mesh.text = seg.Text;
                mesh.color = seg.Color;
                mesh.anchor = TextAnchor.MiddleLeft;

                float segWidth = seg.Text.Length * characterSize * fontSize * 0.1f;
                currentX += segWidth;

                _segmentObjects.Add(obj);
            }
        }

        private void RenderIcon(string iconPath, int segmentCount)
        {
            float totalWidth = 0f;
            foreach (var obj in _segmentObjects)
            {
                var mesh = obj.GetComponent<TextMesh>();
                if (mesh != null) totalWidth += (mesh.text?.Length ?? 0) * characterSize * fontSize * 0.1f;
            }

            var iconObj = new GameObject("Label_Icon");
            iconObj.transform.SetParent(transform, false);
            iconObj.transform.localPosition = labelOffset
                + new Vector3(totalWidth * 0.5f + iconOffset.x, iconOffset.y, iconOffset.z);

            var sr = iconObj.AddComponent<SpriteRenderer>();
            var sprite = GraphicsManager.Instance?.TryGetSprite(iconPath);
            if (sprite != null)
            {
                sr.sprite = sprite;
                sr.sortingLayerName = sortingLayerName;
                sr.sortingOrder = sortingOrder;
                iconObj.transform.localScale = Vector3.one * iconSize;
            }
            else
            {
                iconObj.SetActive(false);
            }

            _iconObject = iconObj;
        }

        private GameObject CreateSegmentObject(string name, float localX)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(transform, false);
            obj.transform.localPosition = labelOffset + new Vector3(localX, 0f, 0f);

            var mesh = obj.AddComponent<TextMesh>();
            mesh.fontSize = fontSize;
            mesh.characterSize = characterSize;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;

            var mr = obj.GetComponent<MeshRenderer>();
            mr.sortingLayerName = sortingLayerName;
            mr.sortingOrder = sortingOrder;

            return obj;
        }

        private void OnDestroy() => Clear();
    }
}
