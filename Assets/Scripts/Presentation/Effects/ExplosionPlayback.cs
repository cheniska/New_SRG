using UnityEngine;
using SRG.Config;
using SRG.Presentation.Common;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.Presentation.Effects
{
    /// <summary>
    /// Воспроизводит покадровую анимацию взрыва на существующем SpriteRenderer и уничтожает
    /// GameObject по завершении. Используется для всех типов взрывов в игре (астероиды, корабли,
    /// ракеты): родительский визуальный контроллер отключается, ExplosionPlayback забирает
    /// рендерер и проигрывает кадры, потом сам сносит GO.
    ///
    /// Для случаев, когда нет существующего GO (например, гибель игрока — GO DontDestroyOnLoad
    /// нельзя уничтожать), используйте SpawnAt — создаёт временный GO и проигрывает на нём.
    /// </summary>
    public class ExplosionPlayback : MonoBehaviour
    {
        private SpriteRenderer _sr;
        private FrameStepper _step;

        /// <summary>Загружает пакет кадров + шаг времени взрыва через общий GraphicsManager
        /// (тот же путь, что для астероидов/кораблей/червоточин: сопроводительный .png.json
        /// c FrameCount/Cols/Rows и построчная нарезка). Путь — Resources-путь к листу без
        /// расширения, напр. "Graphics/Effects/Explosions/Asteroids/Default/001". Прогрев —
        /// через GraphicsManager.PreloadSheets, отдельного Preload здесь нет.</summary>
        public static SheetHandle LoadSheet(string path, float fps)
        {
            if (string.IsNullOrEmpty(path) || GraphicsManager.Instance == null) return SheetHandle.Empty;
            return GraphicsManager.Instance.GetSheetHandle(path, fps);
        }

        /// <summary>Запускает взрыв на существующем GameObject: использует его SpriteRenderer
        /// (или добавляет, если нет), подменяет спрайты на кадры взрыва, по окончании
        /// уничтожает GameObject.</summary>
        public static ExplosionPlayback PlayOn(GameObject go, string explosionPath = null)
        {
            if (go == null) return null;
            // Объект мог быть скрыт другой логикой (например, ракета прячется сразу после
            // сабтёрна смерти). На отключённом GO Update не идёт — анимация взрыва замрёт.
            if (!go.activeSelf) go.SetActive(true);
            var pb = go.GetComponent<ExplosionPlayback>() ?? go.AddComponent<ExplosionPlayback>();
            pb.Begin(explosionPath);
            return pb;
        }

        /// <summary>Создаёт временный GO под parent и проигрывает на нём взрыв.
        /// Нужен, когда у объекта-носителя нельзя забирать визуал (например, гибель игрока).</summary>
        public static ExplosionPlayback SpawnAt(Transform parent, Vector2 position, string explosionPath = null)
        {
            var go = new GameObject("Explosion");
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(position.x, position.y, 0f);
            return PlayOn(go, explosionPath);
        }

        private void Begin(string explosionPath)
        {
            var settings = GameWorld.Settings;
            string path = !string.IsNullOrEmpty(explosionPath)
                ? explosionPath
                : (settings != null && !string.IsNullOrEmpty(settings.ExplosionSpritePath))
                    ? settings.ExplosionSpritePath
                    : GalaxyConstants.EXPLOSION_SPRITE_PATH;
            float fps = settings != null && settings.ExplosionFPS > 0f
                ? settings.ExplosionFPS
                : GalaxyConstants.EXPLOSION_FPS;
            float scale = settings != null && settings.ExplosionScale > 0f
                ? settings.ExplosionScale
                : GalaxyConstants.EXPLOSION_SCALE;

            if (!TryGetComponent(out _sr))
                _sr = gameObject.AddComponent<SpriteRenderer>();

            _sr.color = Color.white;
            _sr.sortingOrder = SortingLayerRegistry.Get(SortLayer.HelperOverlay);
            transform.localScale = Vector3.one * scale;
            transform.localRotation = Quaternion.identity;

            var sheet = LoadSheet(path, fps);
            if (!sheet.IsValid)
            {
                Debug.LogWarning($"[ExplosionPlayback] No explosion sprites at '{path}' — using procedural fallback.");
                var dot = SpriteUtility.CreateCircleSprite(32, Color.white, FilterMode.Point);
                sheet = new SheetHandle(new[] { dot }, 0.4f);
            }

            _step = new FrameStepper(sheet, loop: false);
            _sr.sprite = _step.CurrentSprite;
        }

        private void Update()
        {
            if (_step.Frames == null) return;
            if (_step.Advance(Time.deltaTime))
                _sr.sprite = _step.CurrentSprite;
            if (_step.Finished) Destroy(gameObject);
        }
    }
}
