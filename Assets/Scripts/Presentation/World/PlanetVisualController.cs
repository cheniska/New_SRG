using UnityEngine;
using Random = UnityEngine.Random;
using System.Collections.Generic;
using SRG.Galaxy;
using SRG.Presentation.Common;

namespace SRG.Presentation.World
{
    public class PlanetVisualController : BaseVisualController
    {
        [Header("Renderers")]
        public SpriteRenderer SurfaceRenderer;
        public SpriteRenderer AtmosphereRenderer;
        public SpriteRenderer OrbitalRenderer;
        // ShapeMask унаследовано из BaseVisualController.
        private Material _planetMaterial;
        private Material _orbitalMaterial;
        private MaterialPropertyBlock _propBlock;
        private static readonly int RotationID = Shader.PropertyToID("_Rotation");
        private static readonly int MainTexID = Shader.PropertyToID("_MainTex");
        private static readonly int AxialTiltID = Shader.PropertyToID("_AxialTilt");
        private static Shader _planetShader;
        private static Shader _defaultShader;
        private static Shader PlanetShader => _planetShader ??= Shader.Find("Custom/PlanetRotation");
        private static Shader DefaultShader => _defaultShader ??= Shader.Find("Sprites/Default");

        private float _surfaceRotationSpeed;
        private float _currentSurfaceRotation;
        private float _cloudsRotationSpeed;
        private float _currentCloudsRotation;
        private float _axialTilt;

        private readonly List<SatelliteVisualController> _satelliteControllers = new List<SatelliteVisualController>();

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
        }

        private void OnDestroy()
        {
            if (_planetMaterial != null) Destroy(_planetMaterial);
            if (_orbitalMaterial != null) Destroy(_orbitalMaterial);
        }

        public void Setup(PlanetData data, GameObject satellitePrefab)
        {
            _surfaceRotationSpeed = data.DaySpeed > 0 ? 1f / data.DaySpeed : 0f;
            _cloudsRotationSpeed = data.CloudsSpeed > 0 ? 1f / data.CloudsSpeed : 0f;
            _axialTilt = data.AxialTilt;

            Sprite maskSprite = GraphicsManager.Instance.GetSprite(data.MaskGraphic);
            if (maskSprite == null) return;

            EnsureMask(maskSprite);
            EnsureMaterial();

            SetupSurfaceLayer(data.Graphic, maskSprite);
            SetupAtmosphereLayer(data.Atmosphere, maskSprite);
            SetupOrbitalLayer(data.OrbitalObjects);

            ApplyScale(data.Size);
            SpawnSatellites(data, satellitePrefab);
        }

        private void EnsureMaterial()
        {
            if (_planetMaterial == null)
                _planetMaterial = new Material(PlanetShader);
        }

        private void SetupSurfaceLayer(string graphicPath, Sprite maskSprite)
        {
            SurfaceRenderer = EnsureRenderer(SurfaceRenderer, "Surface_Slot",
                SortingLayerRegistry.Get(SortLayer.PlanetSurface), useMask: true);
            Sprite texture = GraphicsManager.Instance.GetSprite(graphicPath);

            if (texture != null)
            {
                SetupSphericalLayer(SurfaceRenderer, texture, maskSprite);
                _currentSurfaceRotation = Random.value;
                SetLayerRotation(SurfaceRenderer, _currentSurfaceRotation);
            }
            else
            {
                SurfaceRenderer.gameObject.SetActive(false);
            }
        }

        private void SetupAtmosphereLayer(string atmospherePath, Sprite maskSprite)
        {
            if (!string.IsNullOrEmpty(atmospherePath))
            {
                AtmosphereRenderer = EnsureRenderer(AtmosphereRenderer, "Atmo_Slot",
                    SortingLayerRegistry.Get(SortLayer.PlanetAtmosphere), useMask: true);
                Sprite texture = GraphicsManager.Instance.GetSprite(atmospherePath);

                if (texture != null)
                {
                    SetupSphericalLayer(AtmosphereRenderer, texture, maskSprite);
                    AtmosphereRenderer.color = new Color(1, 1, 1, 0.9f);
                    _currentCloudsRotation = Random.value;
                    SetLayerRotation(AtmosphereRenderer, _currentCloudsRotation);
                }
                else
                {
                    AtmosphereRenderer.gameObject.SetActive(false);
                }
            }
            else
            {
                if (AtmosphereRenderer != null && AtmosphereRenderer)
                    AtmosphereRenderer.gameObject.SetActive(false);
            }
        }

        private void SetupOrbitalLayer(string orbitalPath)
        {
            if (!string.IsNullOrEmpty(orbitalPath))
            {
                OrbitalRenderer = EnsureRenderer(OrbitalRenderer, "Orbital_Slot",
                    SortingLayerRegistry.Get(SortLayer.PlanetOrbital), useMask: false);
                Sprite texture = GraphicsManager.Instance.GetSprite(orbitalPath);

                if (texture != null)
                {
                    OrbitalRenderer.gameObject.SetActive(true);
                    OrbitalRenderer.sprite = texture;
                    OrbitalRenderer.transform.eulerAngles = Vector3.zero;
                    if (_orbitalMaterial == null)
                        _orbitalMaterial = new Material(DefaultShader);
                    OrbitalRenderer.material = _orbitalMaterial;
                }
            }
            else
            {
                if (OrbitalRenderer != null && OrbitalRenderer)
                    OrbitalRenderer.gameObject.SetActive(false);
            }
        }

        private void SpawnSatellites(PlanetData data, GameObject satellitePrefab)
        {
            foreach (var ctrl in _satelliteControllers)
                if (ctrl != null) Destroy(ctrl.gameObject);
            _satelliteControllers.Clear();

            if (data.Satellites == null || data.Satellites.Count == 0) return;
            if (satellitePrefab == null)
            {
                Debug.LogWarning("[PlanetVisualController] SatellitePrefab is null, satellites will not be spawned.");
                return;
            }

            foreach (var satData in data.Satellites)
            {
                var obj = Instantiate(satellitePrefab, transform);
                obj.name = $"Sat_{satData.Name}";
                if (obj.TryGetComponent<SatelliteVisualController>(out var ctrl))
                {
                    ctrl.Setup(satData, data);
                    _satelliteControllers.Add(ctrl);
                }
            }
        }

        private void Update()
        {
            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
            float dt = Time.deltaTime;

            TickLayerRotation(ref _currentSurfaceRotation, _surfaceRotationSpeed, SurfaceRenderer, dt);
            TickLayerRotation(ref _currentCloudsRotation, _cloudsRotationSpeed, AtmosphereRenderer, dt);

            foreach (var ctrl in _satelliteControllers)
                ctrl?.UpdateVisualOrbit(dt);
        }

        private void TickLayerRotation(ref float current, float speed, SpriteRenderer renderer, float dt)
        {
            if (speed == 0f || renderer == null || !renderer.gameObject.activeSelf) return;
            current += speed * dt;
            if (current > 1f) current -= 1f;
            SetLayerRotation(renderer, current);
        }

        private void SetupSphericalLayer(SpriteRenderer r, Sprite texture, Sprite mask)
        {
            r.gameObject.SetActive(true);
            r.material = _planetMaterial;
            r.sprite = mask;
            r.transform.localPosition = Vector3.zero;
            r.transform.localScale = Vector3.one;

            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
            r.GetPropertyBlock(_propBlock);
            _propBlock.SetTexture(MainTexID, texture.texture);
            _propBlock.SetFloat(AxialTiltID, _axialTilt);
            r.SetPropertyBlock(_propBlock);
        }

        private void SetLayerRotation(SpriteRenderer r, float value)
        {
            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
            r.GetPropertyBlock(_propBlock);
            _propBlock.SetFloat(RotationID, value);
            r.SetPropertyBlock(_propBlock);
        }

        // EnsureRenderer унаследован из BaseVisualController.

        private void ApplyScale(string sizeKey)
        {
            float scale = 1f;
            var config = GraphicsManager.Instance.GetConfig();
            if (config?.Planets?.Sizes != null && config.Planets.Sizes.TryGetValue(sizeKey, out var data))
                scale = data.BaseScale;
            transform.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
