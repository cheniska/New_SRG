using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Galaxy;
using SRG.Presentation.Common;

namespace SRG.Presentation.World
{
    /// <summary>
    /// Планета 3D-сферой: поверхность и облака — слои <see cref="PlanetSphereLayer"/> с той же
    /// развёрткой текстуры, вращением и наклоном оси, что у 2D (<see cref="PlanetVisualController"/>),
    /// плюс освещение: дневная сторона смотрит на звезду. Кольца/орбитальные объекты — спрайт,
    /// спутники — <see cref="SatelliteVisualController3D"/>. Размер — BaseScale размера планеты.
    /// </summary>
    public class PlanetVisualController3D : MonoBehaviour
    {
        private const float CloudsRadiusScale = 1.03f;

        private PlanetSphereLayer _surface;
        private PlanetSphereLayer _clouds;
        private Material _orbitalMaterial;
        private readonly List<SatelliteVisualController3D> _satellites = new List<SatelliteVisualController3D>();

        public void Setup(PlanetData data)
        {
            var gm = GraphicsManager.Instance;
            if (gm == null) return;

            float daySpeed = data.DaySpeed > 0 ? 1f / data.DaySpeed : 0f;
            float cloudsSpeed = data.CloudsSpeed > 0 ? 1f / data.CloudsSpeed : 0f;

            var surface = gm.GetSprite(data.Graphic);
            if (surface != null)
                _surface = new PlanetSphereLayer(transform, "Surface3D", surface.texture, data.AxialTilt, Color.white,
                    1f, SortingLayerRegistry.Get(SortLayer.PlanetSurface), daySpeed, Random.value);

            if (!string.IsNullOrEmpty(data.Atmosphere))
            {
                var clouds = gm.GetSprite(data.Atmosphere);
                if (clouds != null)
                    _clouds = new PlanetSphereLayer(transform, "Clouds3D", clouds.texture, data.AxialTilt,
                        new Color(1f, 1f, 1f, 0.9f), CloudsRadiusScale,
                        SortingLayerRegistry.Get(SortLayer.PlanetAtmosphere), cloudsSpeed, Random.value);
            }

            SetupOrbital(gm, data.OrbitalObjects);
            ApplyScale(gm, data.Size);

            foreach (var sat in data.Satellites ?? new List<SatelliteData>())
            {
                var go = new GameObject($"Sat_{sat.Name}");
                go.transform.SetParent(transform, false);
                var ctrl = go.AddComponent<SatelliteVisualController3D>();
                ctrl.Setup(sat);
                _satellites.Add(ctrl);
            }
        }

        private void SetupOrbital(GraphicsManager gm, string orbitalPath)
        {
            if (string.IsNullOrEmpty(orbitalPath)) return;
            var sprite = gm.GetSprite(orbitalPath);
            if (sprite == null) return;
            var go = new GameObject("Orbital_Slot");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = SortingLayerRegistry.Get(SortLayer.PlanetOrbital);
            _orbitalMaterial = new Material(Shader.Find("Sprites/Default"));
            sr.material = _orbitalMaterial;
        }

        private void ApplyScale(GraphicsManager gm, string sizeKey)
        {
            float scale = 1f;
            var config = gm.GetConfig();
            if (config?.Planets?.Sizes != null && config.Planets.Sizes.TryGetValue(sizeKey, out var data))
                scale = data.BaseScale;
            transform.localScale = Vector3.one * scale;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _surface?.Tick(dt);
            _clouds?.Tick(dt);
            foreach (var sat in _satellites)
                if (sat != null) sat.UpdateVisualOrbit(dt);
        }

        private void OnDestroy()
        {
            if (_orbitalMaterial != null) Destroy(_orbitalMaterial);
        }
    }
}
