using UnityEngine;
using SRG.Galaxy;
using SRG.Presentation.Common;
using SRG.Utils;

namespace SRG.Presentation.World
{
    public class SatelliteVisualController : BaseVisualController
    {
        [Header("Renderers")]
        public SpriteRenderer SurfaceRenderer;
        public SpriteRenderer AtmosphereRenderer;
        // ShapeMask унаследовано из BaseVisualController.

        private Material _material;
        private MaterialPropertyBlock _propBlock;

        private static readonly int RotationID = Shader.PropertyToID("_Rotation");
        private static readonly int MainTexID = Shader.PropertyToID("_MainTex");
        private static Shader _planetShader;
        private static Shader PlanetShader => _planetShader ??= Shader.Find("Custom/PlanetRotation");

        private SatelliteData _data;
        private float _selfRotationSpeed;   // оборотов в секунду для шейдера
        private float _currentRotation;     // текущее значение _Rotation [0..1]
        private float _visualOrbitAngle;    // текущий угол на орбите (градусы), обновляется в UpdateVisualOrbit
        private float _baseScale;           // базовый масштаб из конфига (по ключу Size)

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        public void Setup(SatelliteData data, PlanetData parent)
        {
            _data = data;
            _visualOrbitAngle = data.CurrentAngle;

            _material = new Material(PlanetShader);

            Sprite mask = GraphicsManager.Instance.GetSprite(parent.MaskGraphic);
            EnsureMask(mask);

            SurfaceRenderer = EnsureRenderer(SurfaceRenderer, "Surface_Slot",
                SortingLayerRegistry.Get(SortLayer.PlanetSurface), useMask: true);
            Sprite texture = GraphicsManager.Instance.GetSprite(data.Graphic);

            if (texture != null)
            {
                SetupLayer(SurfaceRenderer, texture, mask);
                _selfRotationSpeed = data.DaySpeed > 0 ? 1f / data.DaySpeed : Random.Range(0.5f, 2f);
                _currentRotation = Random.value; // случайная начальная фаза
            }

            ApplyScale(data.Size);
            UpdateWorldPosition(_visualOrbitAngle);
        }

        public void UpdateVisualOrbit(float dt)
        {
            if (_data == null) return;

            float step = Mathf.Abs(_data.OrbitSpeed) < 0.01f ? 0f : 360f / _data.OrbitSpeed;
            _visualOrbitAngle = WrapAngle(_visualOrbitAngle + step * dt);
            UpdateWorldPosition(_visualOrbitAngle);

            if (SurfaceRenderer != null && SurfaceRenderer.gameObject.activeSelf)
            {
                _currentRotation += _selfRotationSpeed * dt;
                if (_currentRotation > 1f) _currentRotation -= 1f;

                SurfaceRenderer.GetPropertyBlock(_propBlock);
                _propBlock.SetFloat(RotationID, _currentRotation);
                SurfaceRenderer.SetPropertyBlock(_propBlock);
            }
        }

        private void UpdateWorldPosition(float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            float r = SRUnits.ToWorld(_data.OrbitRadius);
            float incl = _data.OrbitInclination * Mathf.Deg2Rad;

            float xBase = Mathf.Cos(rad) * r;
            float yBase = Mathf.Sin(rad) * r * 0.4f;

            transform.localPosition = new Vector3(
                xBase * Mathf.Cos(incl) - yBase * Mathf.Sin(incl),
                xBase * Mathf.Sin(incl) + yBase * Mathf.Cos(incl),
                0f
            );

            float depthFactor = Mathf.Sin(rad);
            float scaleMult = 1f - depthFactor * 0.25f;
            transform.localScale = Vector3.one * (_baseScale * scaleMult);

            SetSortingOrders(depthFactor > 0
                ? SortingLayerRegistry.Get(SortLayer.SatelliteUp)
                : SortingLayerRegistry.Get(SortLayer.SatelliteDown));
        }

        private void SetupLayer(SpriteRenderer r, Sprite texture, Sprite mask)
        {
            r.gameObject.SetActive(true);
            r.material = _material;
            r.sprite = mask; // маска задаёт форму, текстура — через MaterialPropertyBlock

            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
            r.GetPropertyBlock(_propBlock);
            _propBlock.SetTexture(MainTexID, texture.texture);
            r.SetPropertyBlock(_propBlock);
        }

        // EnsureRenderer унаследован из BaseVisualController.

        private void ApplyScale(string sizeKey)
        {
            float scale = 1f;
            var config = GraphicsManager.Instance.GetConfig();
            if (config?.Satellites?.Sizes != null &&
                config.Satellites.Sizes.TryGetValue(sizeKey, out var data))
                scale = data.BaseScale;
            _baseScale = scale;
            transform.localScale = Vector3.one * scale;
        }

        private void SetSortingOrders(int order)
        {
            if (SurfaceRenderer) SurfaceRenderer.sortingOrder = order;
            if (AtmosphereRenderer) AtmosphereRenderer.sortingOrder = order + 1;
        }

        private static float WrapAngle(float angle) => Mathf.Repeat(angle, 360f);
    }
}
