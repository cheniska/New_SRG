using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Galaxy;
using SRG.Presentation.Common;

namespace SRG.Presentation.World
{
    /// <summary>
    /// Спутник 3D-сферой: та же видимая орбита (<see cref="SatelliteOrbit"/>) и вращение, что у
    /// 2D (<see cref="SatelliteVisualController"/>), освещение — от звезды.
    /// </summary>
    public class SatelliteVisualController3D : MonoBehaviour
    {
        private SatelliteData _data;
        private PlanetSphereLayer _surface;
        private float _orbitAngle;   // градусы
        private float _baseScale;

        public void Setup(SatelliteData data)
        {
            _data = data;
            _orbitAngle = data.CurrentAngle;
            _baseScale = SatelliteOrbit.BaseScale(data.Size);

            var texture = GraphicsManager.Instance?.GetSprite(data.Graphic);
            if (texture != null)
            {
                float speed = data.DaySpeed > 0 ? 1f / data.DaySpeed : Random.Range(0.5f, 2f);
                _surface = new PlanetSphereLayer(transform, "Surface3D", texture.texture, 0f, Color.white,
                    1f, SortingLayerRegistry.Get(SortLayer.SatelliteDown), speed, Random.value);
            }
            Place();
        }

        public void UpdateVisualOrbit(float dt)
        {
            if (_data == null) return;
            _orbitAngle = SatelliteOrbit.Advance(_data, _orbitAngle, dt);
            Place();
            _surface?.Tick(dt);
        }

        private void Place()
        {
            int order = SatelliteOrbit.Place(transform, _data, _orbitAngle, _baseScale);
            if (_surface != null) _surface.SortingOrder = order;
        }
    }
}
