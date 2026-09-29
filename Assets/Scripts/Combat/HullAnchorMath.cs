using UnityEngine;
using SRG.Config;
using SRG.Equipment;
using SRG.Galaxy;

namespace SRG.Combat
{
    /// <summary>
    /// Общие хелперы для работы с локальными точками корпуса: BoardAnchor (абордаж),
    /// TowAnchors (буксир/якорь груза). Локальные координаты заданы в HullTypeDef в пикселях,
    /// делятся на 100 для перевода в мировые единицы.
    /// Используется <see cref="BoardingSystem"/>, <see cref="TowSystem"/>, <see cref="PickupSystem"/>.
    /// </summary>
    public static class HullAnchorMath
    {
        public static HullTypeDef GetHullTypeDef(ShipData ship, ItemsConfig equipConfig)
        {
            if (ship == null || equipConfig == null) return null;
            var hull = EquipmentSystem.GetEquipped(ship, SlotKeys.Hull);
            string code = hull?.GetParamString("HullType");
            if (code == null) return null;
            var tpl = equipConfig.GetTemplate(EquipmentCategory.Hull);
            if (tpl?.HullTypes == null) return null;
            tpl.HullTypes.TryGetValue(code, out var def);
            return def;
        }

        public static Vector2 LocalToWorld(ShipData ship, float[] localXY)
            => LocalToWorldAt(ship.Position, ship.CurrentHeading, localXY);

        /// <summary>
        /// Перевод локальной точки корпуса в мировые координаты, независимо от текущего ShipData.
        /// Используется при построении плотных render-путей буксируемых объектов.
        /// </summary>
        public static Vector2 LocalToWorldAt(Vector2 origin, float headingRad, float[] localXY)
        {
            if (localXY == null || localXY.Length < 2) return origin;
            float ang = float.IsNaN(headingRad) ? 0f : headingRad - Mathf.PI * 0.5f;
            float cos = Mathf.Cos(ang), sin = Mathf.Sin(ang);
            float lx = localXY[0] * 0.01f, ly = localXY[1] * 0.01f;
            return origin + new Vector2(cos * lx - sin * ly, sin * lx + cos * ly);
        }

        public static float ComputeHeadingAt(System.Collections.Generic.List<Vector2> path, int k, float fallback)
        {
            if (k > 0)
            {
                var d = path[k] - path[k - 1];
                if (d.sqrMagnitude > 1e-6f) return Mathf.Atan2(d.y, d.x);
            }
            if (k + 1 < path.Count)
            {
                var d = path[k + 1] - path[k];
                if (d.sqrMagnitude > 1e-6f) return Mathf.Atan2(d.y, d.x);
            }
            return fallback;
        }
    }
}
