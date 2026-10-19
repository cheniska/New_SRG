using UnityEngine;
using SRG.Galaxy;
using SRG.Simulation;

namespace SRG.Presentation.Common
{
    /// <summary>
    /// Курсор → мир: точка на плоскости системы (z = 0) под экранной точкой и объект под ней.
    ///
    /// Точка — пересечение луча камеры с плоскостью, поэтому одинаково работает для
    /// ортографической (2D) и перспективной (3D) камеры. Объект выбирается по данным
    /// (<see cref="WorldPicker"/>), без коллайдеров на визуале.
    /// </summary>
    public static class WorldPointer
    {
        private static readonly Plane SystemPlane = new Plane(Vector3.back, Vector3.zero);

        /// <summary>Точка плоскости системы под экранной точкой. false — луч плоскость не пересекает
        /// (камера смотрит мимо неё).</summary>
        public static bool TryScreenToWorld(Camera cam, Vector3 screenPos, out Vector2 world)
        {
            world = default;
            if (cam == null) return false;
            var ray = cam.ScreenPointToRay(screenPos);
            if (!SystemPlane.Raycast(ray, out float enter)) return false;
            Vector3 p = ray.GetPoint(enter);
            world = new Vector2(p.x, p.y);
            return true;
        }

        /// <summary>Объект текущей системы под экранной точкой или null.</summary>
        public static WorldPick PickAtScreen(Camera cam, Vector3 screenPos)
            => TryScreenToWorld(cam, screenPos, out var world) ? PickAt(world) : null;

        /// <summary>Объект текущей системы в точке мира или null.</summary>
        public static WorldPick PickAt(Vector2 world)
            => WorldPicker.Pick(GameWorld.CurrentStar, world, GameWorld.Context?.Config, GameWorld.Settings);
    }
}
