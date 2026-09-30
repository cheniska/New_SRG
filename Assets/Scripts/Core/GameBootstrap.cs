using UnityEngine;
using SRG.Controllers;
using SRG.Presentation.World;
using SRG.Scripting;
using SRG.Simulation;

namespace SRG.Core
{
    /// <summary>
    /// Composition root клиента: связывает симуляцию с верхними слоями до загрузки первой сцены.
    ///
    /// Хосты-MonoBehaviour (GalaxyManager, PlayerManager, SystemViewManager, GraphicsManager,
    /// DialogUIController) регистрируются в <see cref="GameWorld"/> сами в Awake — здесь только то,
    /// что не привязано к объекту сцены и нужно раньше любого Awake.
    /// </summary>
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            // Owner/Race-варианты графики корпуса (ShipData.RefreshSpritesheetPath).
            GameWorld.ShipSheetResolver = ShipGraphicsResolver.ResolveFromBase;

            // Статические API верхнего слоя, доступные из Lua (консоль, моды).
            LuaHost.RegisterStaticApi(typeof(GalaxyManager));
            LuaHost.RegisterStaticApi(typeof(PlayerManager));
            LuaHost.RegisterStaticApi(typeof(PlayerShip));
        }
    }
}
