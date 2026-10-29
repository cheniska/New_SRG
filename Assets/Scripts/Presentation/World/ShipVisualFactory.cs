using UnityEngine;
using SRG.Config;
using SRG.Galaxy;
using SRG.Simulation;

namespace SRG.Presentation.World
{
    /// <summary>Компонент на объекте корабля, который держит ссылку на его визуал
    /// (PlayerShip, NpcController) — узнаёт о замене визуала 2D ↔ 3D на лету.</summary>
    public interface IShipVisualOwner
    {
        void OnShipVisualReplaced(ShipVisualController visual);
    }

    /// <summary>
    /// Выбор визуала корпуса: <see cref="ShipVisualController2D"/> или <see cref="ShipVisualController3D"/>
    /// по <see cref="GameSettingsConfig.ShipRenderMode"/>. Режим можно переключить на лету
    /// (<see cref="Mode"/> + <see cref="Replace"/>) — без правки ассета настроек.
    /// </summary>
    public static class ShipVisualFactory
    {
        private static ShipRenderMode? _override;

        /// <summary>Текущий режим: переключённый на лету, иначе из настроек, иначе 2D.</summary>
        public static ShipRenderMode Mode
        {
            get => _override ?? GameWorld.Settings?.ShipRenderMode ?? ShipRenderMode.Sprite2D;
            set => _override = value;
        }

        /// <summary>Повесить на объект корабля визуал текущего режима и настроить его.</summary>
        public static ShipVisualController Attach(GameObject go, ShipData data, bool isPlayer)
        {
            if (go == null) return null;
            ShipVisualController visual = Mode == ShipRenderMode.Model3D
                ? go.AddComponent<ShipVisualController3D>()
                : go.AddComponent<ShipVisualController2D>();
            visual.Setup(data, isPlayer);
            return visual;
        }

        /// <summary>Заменить визуал корабля на визуал текущего режима (если тип отличается).
        /// Новый визуал сразу ставится в позицию и курс корабля; владельцы ссылки уведомляются.</summary>
        public static ShipVisualController Replace(ShipVisualController old, ShipData data, bool isPlayer)
        {
            if (old == null) return null;
            var wanted = Mode == ShipRenderMode.Model3D ? typeof(ShipVisualController3D) : typeof(ShipVisualController2D);
            if (old.GetType() == wanted) return old;

            var go = old.gameObject;
            old.Teardown();
            old.enabled = false;
            Object.Destroy(old);

            var visual = Attach(go, data, isPlayer);
            visual.SnapRotationToHeading();
            foreach (var owner in go.GetComponents<IShipVisualOwner>())
                owner.OnShipVisualReplaced(visual);
            return visual;
        }
    }
}
