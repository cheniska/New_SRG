using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json;
using SRG.Config;
using SRG.Galaxy.Generation;
using SRG.Simulation;

namespace SRG.Presentation.Common
{
    [System.Serializable]
    public class SpriteSheetMeta
    {
        public int FrameCount;
        public int Cols;
        public int Rows;
    }

    /// <summary>Пачка кадров + шаг времени для покадровой анимации. Возвращается
    /// GraphicsManager.GetSheetHandle и используется потребителями (AnimatedSpriteRenderer,
    /// ExplosionPlayback, AsteroidVisualController) вместо жонглирования парой (Sprite[], float).
    /// IsValid = Frames заполнен. FrameCount = длина Frames.</summary>
    public readonly struct SheetHandle
    {
        public readonly Sprite[] Frames;
        public readonly float SecPerFrame;
        public int FrameCount => Frames?.Length ?? 0;
        public bool IsValid => Frames != null && Frames.Length > 0;
        public SheetHandle(Sprite[] frames, float secPerFrame)
        {
            Frames = frames;
            SecPerFrame = secPerFrame;
        }
        public static readonly SheetHandle Empty = new(null, 0f);
    }

    public class GraphicsManager : MonoBehaviour
    {
        public static GraphicsManager Instance;

        [Header("Animation Settings")]
        public float GlobalStarFPS = 10f;

        private readonly Dictionary<string, Sprite[]> _spriteSheetCache = new Dictionary<string, Sprite[]>();
        private readonly Dictionary<string, Sprite> _singleSpriteCache = new Dictionary<string, Sprite>();
        // Negative-caching: пути, для которых Resources.Load вернул null. Без него hot-path
        // (WeaponVisualSystem, миникарта) на каждом кадре бьёт по диску, если путь опциональный
        // и не задан или невалиден. LogError из GetSprite при этом всё равно поднимется один
        // раз — при первой попытке; повторно из кэша молча возвращается null.
        private readonly HashSet<string> _missingSprites = new HashSet<string>();
        private readonly HashSet<string> _missingSheets = new HashSet<string>();
        private GalaxyGenerationContext _ctx;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
        }

        public void Init(GalaxyGenerationContext context)
        {
            _ctx = context;
            Debug.Log("[GraphicsManager] Initialized.");
        }

        public GalaxyConfig GetConfig() => _ctx?.Config;

        #region Star Visuals

        public float GetStarScale(string typeName)
        {
            if (_ctx?.Config?.Stars?.Types != null &&
                _ctx.Config.Stars.Types.TryGetValue(typeName, out var typeData))
                return typeData.GraphicSizeMult;

            Debug.LogWarning($"[GraphicsManager] Star type '{typeName}' not found, defaulting scale to 1.0");
            return 1f;
        }

        public (Sprite[] frames, float animSpeed) GetStarVisuals(string color, int graphVariant)
        {
            float speed = 1f / Mathf.Max(GlobalStarFPS, 0.0001f);
            var colors = _ctx?.Config?.Stars?.Colors;
            if (colors == null || !colors.TryGetValue(color, out var colorData)) return (null, 1f);

            string path = $"{colorData.Path}/Var{graphVariant}";
            return (LoadSpriteSheet(path), speed);
        }

        #endregion

        #region Planet / General Visuals

        /// <summary>Загружает одиночный спрайт из Resources; при промахе логирует LogError
        /// ровно один раз (последующие вызовы возвращают null из missing-кэша молча).</summary>
        public Sprite GetSprite(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string normalized = NormalizePath(path);
            if (_missingSprites.Contains(normalized)) return null;
            var sp = LoadSingle(normalized);
            if (sp == null)
                Debug.LogError($"[GraphicsManager] Resource not found: {normalized}");
            return sp;
        }

        /// <summary>То же самое, что GetSprite, но при отсутствии ресурса возвращает null
        /// БЕЗ LogError. Для мест, где промах — норма (fallback-цепочки, опциональные иконки).</summary>
        public Sprite TryGetSprite(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return LoadSingle(NormalizePath(path));
        }

        private Sprite LoadSingle(string normalized)
        {
            if (_singleSpriteCache.TryGetValue(normalized, out Sprite cached)) return cached;
            if (_missingSprites.Contains(normalized)) return null;

            var loaded = Resources.Load<Sprite>(normalized);
            if (loaded != null) _singleSpriteCache[normalized] = loaded;
            else _missingSprites.Add(normalized);
            return loaded;
        }

        #endregion

        // Префабы 3D-моделей (ShipVisualController3D): позитивный и негативный кэш, как у спрайтов —
        // модели пока есть не у всех корпусов, и промах — норма (тогда рисуется заглушка).
        private readonly Dictionary<string, GameObject> _modelCache = new Dictionary<string, GameObject>();
        private readonly HashSet<string> _missingModels = new HashSet<string>();

        /// <summary>Префаб модели из Resources или null (без LogError: модель опциональна).</summary>
        public GameObject TryGetModelPrefab(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_modelCache.TryGetValue(path, out var cached)) return cached;
            if (_missingModels.Contains(path)) return null;
            var loaded = Resources.Load<GameObject>(path);
            if (loaded != null) _modelCache[path] = loaded;
            else _missingModels.Add(path);
            return loaded;
        }

        public void ClearCache()
        {
            _spriteSheetCache.Clear();
            _singleSpriteCache.Clear();
            _missingSprites.Clear();
            _missingSheets.Clear();
            _modelCache.Clear();
            _missingModels.Clear();
            Resources.UnloadUnusedAssets();
        }

        /// <summary>Из расчёта хода (фоновый поток) — через главный поток: Resources и кэши только там.</summary>
        public Sprite[] GetSpriteSheet(string path)
            => MainThread.IsCurrent ? LoadSpriteSheet(path) : MainThread.Send(() => LoadSpriteSheet(path));

        /// <summary>Асинхронно прогревает кэш одного спрайт-листа. Тяжёлое —
        /// Resources.LoadAsync (декод текстуры) — уходит в фоновый поток; на главном потоке
        /// остаётся только Sprite.Create (~несколько мс на лист). Если лист уже в кэше или
        /// в missing-set — coroutine мгновенно завершается. Скипается на битой мете.</summary>
        public System.Collections.IEnumerator PreloadSheetAsync(string path)
        {
            if (string.IsNullOrEmpty(path)) yield break;
            if (_spriteSheetCache.ContainsKey(path)) yield break;
            if (_missingSheets.Contains(path)) yield break;

            var metaReq = Resources.LoadAsync<TextAsset>(path + ".png");
            yield return metaReq;
            var texReq = Resources.LoadAsync<Texture2D>(path);
            yield return texReq;

            // К моменту завершения coroutine кто-то другой мог синхронно догрузить этот лист.
            if (_spriteSheetCache.ContainsKey(path)) yield break;

            var metaAsset = metaReq.asset as TextAsset;
            var sheetTexture = texReq.asset as Texture2D;
            if (metaAsset == null || sheetTexture == null)
            {
                _missingSheets.Add(path);
                yield break;
            }
            TryBuildFromSpriteSheet(metaAsset, sheetTexture, path);
        }

        /// <summary>Возвращает лист + шаг времени одной пачкой. fps &lt;= 0 → 12 fps по умолчанию.
        /// Если лист не найден, возвращается SheetHandle.Empty (IsValid == false).</summary>
        public SheetHandle GetSheetHandle(string path, float fps = 12f)
        {
            var frames = LoadSpriteSheet(path);
            if (frames == null || frames.Length == 0) return SheetHandle.Empty;
            float sec = fps > 0f ? 1f / fps : 1f / 12f;
            return new SheetHandle(frames, sec);
        }

        /// <summary>Прогревает кэш спрайт-листов указанными путями (эквивалент N вызовов
        /// GetSpriteSheet). Для одиночных спрайтов используйте GetSprite напрямую — они и так
        /// грузятся быстро, прогрев не нужен. Cheap paths, не имеющие .png.json, дадут
        /// nothing-happens (warning из TryBuildFromSpriteSheet при парсинге пустой меты
        /// поднимается только на реально битой мете, не на её отсутствии).</summary>
        public void PreloadSheets(IEnumerable<string> paths)
        {
            if (paths == null) return;
            foreach (var path in paths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                GetSpriteSheet(path);
            }
        }

        private Sprite[] LoadSpriteSheet(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_spriteSheetCache.TryGetValue(path, out var cached)) return cached;
            if (_missingSheets.Contains(path)) return null;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var metaAsset = Resources.Load<TextAsset>(path + ".png");
            long msMeta = sw.ElapsedMilliseconds; sw.Restart();
            var sheetTexture = Resources.Load<Texture2D>(path);
            long msTex = sw.ElapsedMilliseconds; sw.Restart();

            if (metaAsset == null || sheetTexture == null)
            {
                _missingSheets.Add(path);
                Debug.LogError($"[GraphicsManager] Sheet load failed for '{path}': " +
                               $"meta{(metaAsset == null ? " MISSING" : " ok")}, " +
                               $"texture{(sheetTexture == null ? " MISSING" : " ok")}. " +
                               $"Ожидаются '{path}.png' и '{path}.png.json' (FrameCount/Cols/Rows) в Resources.");
                return null;
            }

            var frames = TryBuildFromSpriteSheet(metaAsset, sheetTexture, path);
            long msBuild = sw.ElapsedMilliseconds;
            if (msMeta + msTex + msBuild >= 20)
                SRG.Utils.PerfLog.Log($"[GfxLoad] path={path} meta={msMeta} tex={msTex} build={msBuild} frames={frames?.Length ?? 0}");
            if (frames == null) _missingSheets.Add(path);
            return frames;
        }

        private Sprite[] TryBuildFromSpriteSheet(TextAsset metaAsset, Texture2D sheet, string path)
        {
            try
            {
                var meta = JsonConvert.DeserializeObject<SpriteSheetMeta>(metaAsset.text);
                int maxFrames = meta.Cols * meta.Rows;
                if (meta.FrameCount > maxFrames)
                {
                    Debug.LogWarning($"[GraphicsManager] Spritesheet meta '{path}': FrameCount ({meta.FrameCount}) " +
                                     $"exceeds Cols×Rows ({meta.Cols}×{meta.Rows}={maxFrames}). Clamping.");
                    meta.FrameCount = maxFrames;
                }

                float w = (float)sheet.width / meta.Cols;
                float h = (float)sheet.height / meta.Rows;
                var frames = new Sprite[meta.FrameCount];

                for (int i = 0; i < meta.FrameCount; i++)
                {
                    int col = i % meta.Cols;
                    int row = (meta.Rows - 1) - i / meta.Cols;
                    // meshType=FullRect критично: дефолт Tight полигонизирует каждый кадр по пикселям
                    // (~500ms на 100-кадровый лист синтета). FullRect — простой квад, доли ms.
                    frames[i] = Sprite.Create(sheet, new Rect(col * w, row * h, w, h),
                        new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                }

                _spriteSheetCache[path] = frames;
                return frames;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[GraphicsManager] Failed to build spritesheet '{path}' " +
                                 $"(tex {sheet.width}x{sheet.height}): {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        private static string NormalizePath(string path)
        {
            if (path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
                path = path[..^4];
            return path.Replace('\\', '/');
        }
    }
}
