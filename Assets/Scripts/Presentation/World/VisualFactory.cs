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
    /// Выбор визуалов объектов системы по <see cref="GameSettingsConfig.RenderMode"/>:
    /// 2D — спрайты (префабы сцены), 3D — модели и сферы. Звёзды и эффекты — спрайты в обоих
    /// режимах. Режим можно переключить на лету (<see cref="Mode"/>) — без правки ассета настроек;
    /// пересоздание визуалов системы — <c>SystemViewManager.SetRenderMode</c>.
    /// </summary>
    public static class VisualFactory
    {
        private static WorldRenderMode? _override;

        /// <summary>Текущий режим: переключённый на лету, иначе из настроек, иначе 2D.</summary>
        public static WorldRenderMode Mode
        {
            get => _override ?? GameWorld.Settings?.RenderMode ?? WorldRenderMode.Sprite2D;
            set => _override = value;
        }

        public static bool Is3D => Mode == WorldRenderMode.Model3D;

        // ── Корабли ──────────────────────────────────────────────────────────────

        /// <summary>Повесить на объект корабля визуал текущего режима и настроить его.</summary>
        public static ShipVisualController AttachShip(GameObject go, ShipData data, bool isPlayer)
        {
            if (go == null) return null;
            ShipVisualController visual = Is3D
                ? go.AddComponent<ShipVisualController3D>()
                : go.AddComponent<ShipVisualController2D>();
            visual.Setup(data, isPlayer);
            return visual;
        }

        /// <summary>Заменить визуал корабля на визуал текущего режима (если тип отличается).
        /// Новый визуал сразу ставится в курс корабля; владельцы ссылки уведомляются.</summary>
        public static ShipVisualController ReplaceShip(ShipVisualController old, ShipData data, bool isPlayer)
        {
            if (old == null) return null;
            var wanted = Is3D ? typeof(ShipVisualController3D) : typeof(ShipVisualController2D);
            if (old.GetType() == wanted) return old;

            var go = old.gameObject;
            old.Teardown();
            old.enabled = false;
            Object.Destroy(old);

            var visual = AttachShip(go, data, isPlayer);
            visual.SnapRotationToHeading();
            foreach (var owner in go.GetComponents<IShipVisualOwner>())
                owner.OnShipVisualReplaced(visual);
            return visual;
        }

        // ── Астероиды ────────────────────────────────────────────────────────────

        /// <summary>Объект астероида под <paramref name="parent"/>: в 2D — экземпляр префаба сцены
        /// с <see cref="AsteroidVisualController"/>, в 3D — камень. null — визуал не создан.</summary>
        public static AsteroidVisual CreateAsteroid(AsteroidData data, Transform parent, GameObject prefab2D)
        {
            AsteroidVisual visual;
            if (Is3D)
            {
                var go = new GameObject();
                go.transform.SetParent(parent, false);
                visual = go.AddComponent<AsteroidVisualController3D>();
            }
            else
            {
                if (prefab2D == null) return null;
                visual = Object.Instantiate(prefab2D, parent).GetComponent<AsteroidVisualController>();
                if (visual == null)
                {
                    Debug.LogError("[VisualFactory] AsteroidPrefab missing AsteroidVisualController component!");
                    return null;
                }
            }
            visual.gameObject.name = $"Asteroid_{data.Uid}";
            visual.Setup(data);
            return visual;
        }

        // ── Планеты ──────────────────────────────────────────────────────────────

        /// <summary>Объект планеты (со спутниками) под <paramref name="parent"/>: в 2D — префаб сцены
        /// с <see cref="PlanetVisualController"/>, в 3D — сфера (<see cref="PlanetVisualController3D"/>).</summary>
        public static GameObject CreatePlanet(PlanetData data, Transform parent, GameObject prefab2D, GameObject satellitePrefab2D)
        {
            GameObject go;
            if (Is3D)
            {
                go = new GameObject();
                go.transform.SetParent(parent, false);
                go.AddComponent<PlanetVisualController3D>().Setup(data);
            }
            else
            {
                if (prefab2D == null) return null;
                go = Object.Instantiate(prefab2D, parent);
                go.GetComponent<PlanetVisualController>()?.Setup(data, satellitePrefab2D);
            }
            go.name = $"Planet_{data.Name}";
            return go;
        }
    }
}
