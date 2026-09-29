using SRG.Core;
using SRG.Galaxy;
using SRG.NpcAI;

namespace SRG.UI.Common
{
    // Маркер кликабельного объекта сцены — хранит ссылку на данные объекта.
    // Добавляется ко всем спавн-объектам в SystemViewManager.
    public class ClickableInfo : UnityEngine.MonoBehaviour
    {
        public StarData     Star;
        public PlanetData   Planet;
        public ShipData     Ship;
        public AsteroidData Asteroid;
        public WormholeData Wormhole;
        /// <summary>Родительская звезда червоточины (нужна для запроса прыжка через HyperjumpController).
        /// Заполняется вместе с полем <see cref="Wormhole"/>.</summary>
        public StarData     WormholeStar;
        public NpcController NpcController; // для NPC-кораблей
    }
}
