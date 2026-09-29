using UnityEngine;
using Random = UnityEngine.Random;
using System.Collections.Generic;
using SRG.Config;
using SRG.Presentation.Common;
using SRG.Utils;

namespace SRG.Presentation.World
{
    public class BackgroundVisualController : MonoBehaviour
    {
        // три цветовые гаммы туманностей: (ядро, середина)
        static readonly (Color a, Color b)[] NebulaPalettes =
        {
            (new Color(0.30f, 1.00f, 1.00f), new Color(0.00f, 0.48f, 0.70f)), // голубо-бирюзовая
            (new Color(1.00f, 0.20f, 0.06f), new Color(0.58f, 0.05f, 0.00f)), // красная
            (new Color(1.00f, 0.88f, 0.12f), new Color(1.00f, 0.36f, 0.00f)), // жёлто-оранжевая
        };

        // --- состояние системы ---
        private int   _systemSeed;
        private int   _paletteIndex;
        private float _systemRadius;             // радиус системы в мировых единицах
        private float _cellSize = 100f;          // размер ячейки сетки; перезаписывается в Setup

        private readonly HashSet<(int, int)>                                  _generatedCells = new();
        private readonly List<(Transform t, SpriteRenderer sr, float speed)> _parallaxLayers = new();
        private readonly List<(Transform t, Vector2 worldPos, float speed)>  _nebulaLayers   = new();

        private Transform      _blackVoid;
        private SpriteRenderer _blackVoidRenderer;
        private Camera         _camera;

        private readonly List<Texture2D> _ownedTextures  = new();
        private readonly List<Sprite>    _ownedSprites   = new();
        private readonly List<Material>  _ownedMaterials = new();

        // =========================================================
        //  Setup / Destroy
        // =========================================================

        public void Setup(Camera targetCamera, int seed, float systemRadius = 100f)
        {
            _camera       = targetCamera;
            _systemRadius = Mathf.Max(100f, systemRadius);
            _cellSize     = 1000f; // для фоновых звёзд сетка остаётся мелкой

            DestroyOwnedAssets();
            foreach (Transform child in transform) Destroy(child.gameObject);
            _parallaxLayers.Clear();

            _systemSeed   = seed;
            _paletteIndex = Mathf.Abs(seed % NebulaPalettes.Length);

            Random.InitState(seed);
            _blackVoid = CreateBlackVoid();

            CreateStarLayer("Stars_Far",  SortingLayerRegistry.Get(SortLayer.BackgroundStarsFar),
                GalaxyConstants.BG_PARALLAX_FAR,  GalaxyConstants.BG_STAR_COUNT_FAR,  GalaxyConstants.BG_TEX_SIZE_FAR,  glowRadius: 2, bigStarRadius: 1, bigStarChance: 0.05f);
            CreateStarLayer("Stars_Mid",     SortingLayerRegistry.Get(SortLayer.BackgroundStarsMid),
                GalaxyConstants.BG_PARALLAX_MID,      GalaxyConstants.BG_STAR_COUNT_MID,      GalaxyConstants.BG_TEX_SIZE_FAR,  glowRadius: 1, bigStarRadius: 1, bigStarChance: 0.10f);
            CreateStarLayer("Stars_MidNear", SortingLayerRegistry.Get(SortLayer.BackgroundStarsMidNear),
                GalaxyConstants.BG_PARALLAX_MID_NEAR, GalaxyConstants.BG_STAR_COUNT_MID_NEAR, GalaxyConstants.BG_TEX_SIZE_NEAR, glowRadius: 0, bigStarRadius: 3, bigStarChance: 0.25f);
            CreateStarLayer("Stars_Near",    SortingLayerRegistry.Get(SortLayer.BackgroundStarsNear),
                GalaxyConstants.BG_PARALLAX_NEAR,     GalaxyConstants.BG_STAR_COUNT_NEAR,     GalaxyConstants.BG_TEX_SIZE_NEAR, glowRadius: 0, bigStarRadius: 2, bigStarChance: 0.15f);

            // Туманности генерируются напрямую вблизи центра системы, не через сетку ячеек.
            // Сетка ячеек осталась для фоновых звёзд, но для туманностей при systemRadius-масштабе
            // она давала бы центры в (±R/2, ±R/2) и случайное число нулей.
            SpawnSystemNebulae();

            // Крупные фоновые звёзды с мерцанием — поверх туманностей
            //CreateLargeStarLayer("Stars_Big",
                //SortingLayerRegistry.Get(SortLayer.BackgroundStarsBig),
                //GalaxyConstants.BG_PARALLAX_STAR_BIG, 500f);
        }

        private void OnDestroy() => DestroyOwnedAssets();

        private void DestroyOwnedAssets()
        {
            foreach (var tex in _ownedTextures)  if (tex  != null) Destroy(tex);
            _ownedTextures.Clear();
            foreach (var spr in _ownedSprites)   if (spr  != null) Destroy(spr);
            _ownedSprites.Clear();
            foreach (var mat in _ownedMaterials) if (mat  != null) Destroy(mat);
            _ownedMaterials.Clear();
            _nebulaLayers.Clear();
            _generatedCells.Clear();
        }

        // =========================================================
        //  Детерминированный PRNG (xorshift32) — не трогает Unity.Random
        // =========================================================

        static uint CellSeed(int sysSeed, int cx, int cy)
        {
            uint h = (uint)sysSeed;
            h ^= (uint)cx * 2654435761u;
            h ^= (uint)cy * 2246822519u;
            h ^= h >> 16; h *= 0x45d9f3bu; h ^= h >> 16;
            return h == 0 ? 1u : h;
        }

        static float NextF(ref uint s)
        {
            s ^= s << 13; s ^= s >> 17; s ^= s << 5;
            return (s & 0x7FFFFFFFu) / (float)0x80000000u;
        }

        static float NextFR(ref uint s, float lo, float hi) => lo + NextF(ref s) * (hi - lo);

        // =========================================================
        //  Динамическая генерация ячеек
        // =========================================================

        private void CheckAndGenerateCells(Vector3 camPos)
        {
            // радиус генерации — покрываем экран + запас
            float camW    = _camera.orthographicSize * _camera.aspect;
            float camR    = Mathf.Max(_camera.orthographicSize, camW) * 1.6f;
            int   cellR   = Mathf.CeilToInt(camR / _cellSize) + 1;
            int   camCx   = Mathf.FloorToInt(camPos.x / _cellSize);
            int   camCy   = Mathf.FloorToInt(camPos.y / _cellSize);
            int   created = 0;

            for (int cy = camCy - cellR; cy <= camCy + cellR; cy++)
            for (int cx = camCx - cellR; cx <= camCx + cellR; cx++)
            {
                if (_generatedCells.Contains((cx, cy))) continue;
                GenerateCellNebulae(cx, cy);
                if (++created >= 4) return; // не больше 4 ячеек за кадр
            }
        }

        private void GenerateCellNebulae(int cx, int cy)
        {
            _generatedCells.Add((cx, cy)); // помечаем сразу — исключаем повтор

            uint rng = CellSeed(_systemSeed, cx, cy);

            // 35% вероятность туманности в ячейке → равномерное, но не плотное поле
            if (NextF(ref rng) > 0.35f) return;

            int   sortFar = SortingLayerRegistry.Get(SortLayer.BackgroundNebulaFar);
            int   sortMid = SortingLayerRegistry.Get(SortLayer.BackgroundNebulaMid);
            var   (colA, colB) = NebulaPalettes[_paletteIndex];
            float baseCx = (cx + 0.5f) * _cellSize;
            float baseCy = (cy + 0.5f) * _cellSize;
            float worldR = SRUnits.ToWorld(_systemRadius);
            int   count  = 1;

            for (int i = 0; i < count; i++)
            {
                float px    = baseCx + NextFR(ref rng, -_cellSize * 0.45f, _cellSize * 0.45f);
                float py    = baseCy + NextFR(ref rng, -_cellSize * 0.45f, _cellSize * 0.45f);
                bool  isFar = NextF(ref rng) < 0.55f;
                float h = isFar
                    ? worldR * NextFR(ref rng, 0.35f, 0.52f)
                    : worldR * NextFR(ref rng, 0.22f, 0.36f);
                // Асимметричные аспекты → туманность всегда вытянутая, не круглая
                float aspWide   = NextFR(ref rng, 0.45f, 0.75f);
                float aspNarrow = NextFR(ref rng, 1.15f, 1.90f);
                bool  swapAsp   = NextF(ref rng) < 0.5f;
                float aspX      = swapAsp ? aspWide : aspNarrow;
                float aspY      = swapAsp ? aspNarrow : aspWide;
                float rot   = NextFR(ref rng, 0f, Mathf.PI * 2f);
                float swirl = NextFR(ref rng, 0.9f, 1.9f);
                // Несколько туманностей аддитивно суммируются → берём умеренную яркость
                float inten = isFar ? NextFR(ref rng, 0.50f, 0.75f) : NextFR(ref rng, 0.35f, 0.55f);
                var   off   = new Vector2(NextFR(ref rng, 0f, 100f), NextFR(ref rng, 0f, 100f));

                CreateNebulaBlob($"Nebula_{cx}_{cy}_{i}",
                    isFar ? sortFar : sortMid,
                    isFar ? GalaxyConstants.BG_PARALLAX_FAR : GalaxyConstants.BG_PARALLAX_MID,
                    colA, colB, inten, 3.8f, swirl, off, new Vector2(px, py), h, aspX, aspY, rot);
            }
        }

        // =========================================================
        //  Туманности системы — генерируются один раз при Setup
        // =========================================================

        private void SpawnSystemNebulae()
        {
            uint rng = CellSeed(_systemSeed, 7, 13);

            int sortFar = SortingLayerRegistry.Get(SortLayer.BackgroundNebulaFar);
            int sortMid = SortingLayerRegistry.Get(SortLayer.BackgroundNebulaMid);
            var (colA, colB) = NebulaPalettes[_paletteIndex];

            // OrbitMath делит орбитальные единицы на 100 перед использованием как мировые координаты.
            float worldR = SRUnits.ToWorld(_systemRadius);

            // Общая ориентация для всей системы — все туманности вытянуты в одну сторону.
            float baseRot     = NextFR(ref rng, 0f, Mathf.PI * 2f);
            float baseAspWide = NextFR(ref rng, 0.45f, 0.75f);
            float baseAspNarrow = NextFR(ref rng, 1.15f, 1.90f);
            bool  baseSwap    = NextF(ref rng) < 0.5f;

            int count = 2 + (int)(NextF(ref rng) * 2); // 2 или 3 туманности на систему

            for (int i = 0; i < count; i++)
            {
                float px    = NextFR(ref rng, -worldR * 0.06f, worldR * 0.06f);
                float py    = NextFR(ref rng, -worldR * 0.06f, worldR * 0.06f);
                bool  isFar = NextF(ref rng) < 0.55f;

                float h = isFar
                    ? worldR * NextFR(ref rng, 0.16f, 0.26f)
                    : worldR * NextFR(ref rng, 0.10f, 0.17f);

                // Небольшая вариация вокруг общей базовой ориентации системы
                float aspX = (baseSwap ? baseAspWide   : baseAspNarrow) * NextFR(ref rng, 0.88f, 1.12f);
                float aspY = (baseSwap ? baseAspNarrow : baseAspWide)   * NextFR(ref rng, 0.88f, 1.12f);
                float rot  = baseRot + NextFR(ref rng, -0.25f, 0.25f);

                float swirl = NextFR(ref rng, 0.9f, 1.9f);
                float inten = isFar ? NextFR(ref rng, 0.70f, 1.00f) : NextFR(ref rng, 0.50f, 0.80f);
                var   off   = new Vector2(NextFR(ref rng, 0f, 100f), NextFR(ref rng, 0f, 100f));

                // Шум: scale 7.6 (вдвое больше прежнего 3.8) → паттерн вдвое мельче
                CreateNebulaBlob($"Nebula_sys_{i}",
                    isFar ? sortFar : sortMid,
                    isFar ? GalaxyConstants.BG_PARALLAX_FAR : GalaxyConstants.BG_PARALLAX_MID,
                    colA, colB, inten, 7.6f, swirl, off, new Vector2(px, py), h, aspX, aspY, rot);
            }
        }

        // =========================================================
        //  Создание объектов
        // =========================================================

        private Transform CreateBlackVoid()
        {
            var obj = new GameObject("Background_BlackVoid");
            obj.transform.SetParent(transform);
            var sr = obj.AddComponent<SpriteRenderer>();

            var tex = new Texture2D(1, 1, TextureFormat.RGB24, false);
            tex.SetPixel(0, 0, Color.black);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
            sr.sprite       = sprite;
            sr.sortingOrder = SortingLayerRegistry.Get(SortLayer.BackgroundVoid);

            _ownedTextures.Add(tex);
            _ownedSprites.Add(sprite);
            _blackVoidRenderer = sr;
            return obj.transform;
        }

        private void CreateNebulaBlob(string name, int sortOrder, float parallaxSpeed,
            Color colorA, Color colorB, float intensity, float scale, float swirlStrength,
            Vector2 seedOffset, Vector2 worldPos, float halfSize,
            float aspectX, float aspectY, float rotation)
        {

            var shader = Resources.Load<Shader>(GalaxyConstants.SHADER_NEBULA);
            if (shader == null)
            {
                Debug.LogWarning($"[BackgroundVisualController] Shader '{GalaxyConstants.SHADER_NEBULA}' not found.");
                return;
            }

            // Яркое ядро — усиленная версия основного цвета для hotspot-пятен
            var colorCore = new Color(
                Mathf.Clamp01(colorA.r * 1.35f + 0.12f),
                Mathf.Clamp01(colorA.g * 1.15f + 0.05f),
                Mathf.Clamp01(colorA.b * 1.10f + 0.05f));

            var mat = new Material(shader);
            mat.SetColor("_ColorOuter", colorA);
            mat.SetColor("_ColorMid",   colorB);
            mat.SetColor("_ColorCore",  colorCore);
            mat.SetFloat("_Intensity",  intensity);
            mat.SetFloat("_Scale",      scale);
            mat.SetVector("_Offset",    new Vector4(seedOffset.x, seedOffset.y, 0, 0));
            mat.SetFloat("_WarpAmt",    swirlStrength);
            mat.SetFloat("_Threshold",  0.28f);
            mat.SetFloat("_AspectX",    aspectX);
            mat.SetFloat("_AspectY",    aspectY);
            mat.SetFloat("_Rotation",   rotation);
            _ownedMaterials.Add(mat);

            // 1×1 белая текстура: PPU=1, scale задаёт размер в мире
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _ownedTextures.Add(tex);

            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
            _ownedSprites.Add(sprite);

            var obj = new GameObject(name);
            obj.transform.SetParent(transform);
            obj.transform.localScale = new Vector3(halfSize * 2f, halfSize * 2f, 1f);

            var sr = obj.AddComponent<SpriteRenderer>();
            sr.sprite       = sprite;
            sr.material     = mat;
            sr.sortingOrder = sortOrder;

            _nebulaLayers.Add((obj.transform, worldPos, parallaxSpeed));
        }

        // =========================================================
        //  Слои звёзд с опциональным свечением
        // =========================================================

        private void CreateStarLayer(string name, int sortOrder, float parallaxSpeed,
            int starCount, int texSize, int glowRadius, int bigStarRadius = 0, float bigStarChance = 0f)
        {
            var layer = new GameObject(name);
            layer.transform.SetParent(transform);
            var sr = layer.AddComponent<SpriteRenderer>();

            // float-буфер для аккумуляции ореолов без клиппинга
            var buf = new float[texSize * texSize * 4]; // RGBA

            // радиус мягкого свечения для больших звёзд
            int bigGlowR = bigStarRadius > 0 ? bigStarRadius * 3 + 4 : 0;
            int margin   = Mathf.Max(glowRadius, bigGlowR) + 2;

            for (int i = 0; i < starCount; i++)
            {
                int   x = Random.Range(margin, texSize - margin);
                int   y = Random.Range(margin, texSize - margin);
                float b = Random.Range(0.4f, 1f);

                bool isBig = bigStarRadius > 0 && Random.value < bigStarChance;
                int  coreR = isBig ? bigStarRadius : 0;

                // центральный пиксель
                AccumPixel(buf, texSize, x, y, b, 1f);

                // диск ядра большой звезды + мягкое Gaussian-свечение
                if (isBig)
                {
                    for (int dy = -coreR; dy <= coreR; dy++)
                    for (int dx = -coreR; dx <= coreR; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        if (dist <= coreR)
                        {
                            float fade = Mathf.Lerp(0.85f, 0.4f, dist / coreR);
                            AccumPixel(buf, texSize, x + dx, y + dy, b, fade);
                        }
                    }

                    // мягкое свечение — чисто визуальное, не влияет на освещение сцены
                    for (int dy = -bigGlowR; dy <= bigGlowR; dy++)
                    for (int dx = -bigGlowR; dx <= bigGlowR; dx++)
                    {
                        float distFromEdge = Mathf.Max(0f, Mathf.Sqrt(dx * dx + dy * dy) - coreR);
                        if (distFromEdge < 0.5f) continue; // уже в ядре
                        float falloff = Mathf.Exp(-distFromEdge * 0.65f) * 0.3f;
                        AccumPixel(buf, texSize, x + dx, y + dy, b, falloff);
                    }
                }

                // ореол
                if (glowRadius > 0)
                {
                    for (int dy = -glowRadius; dy <= glowRadius; dy++)
                    for (int dx = -glowRadius; dx <= glowRadius; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        float dist    = Mathf.Sqrt(dx * dx + dy * dy);
                        float falloff = Mathf.Exp(-dist * 1.6f) * 0.5f;
                        AccumPixel(buf, texSize, x + dx, y + dy, b, falloff);
                    }
                }
            }

            // конвертируем буфер в пиксели
            var pixels = new Color[texSize * texSize];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color(buf[i*4], buf[i*4+1], buf[i*4+2], buf[i*4+3]);

            var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, texSize, texSize),
                Vector2.zero, 100f, 0, SpriteMeshType.FullRect);
            sr.sprite       = sprite;
            sr.drawMode     = SpriteDrawMode.Tiled;
            sr.tileMode     = SpriteTileMode.Continuous;
            sr.size         = new Vector2(GalaxyConstants.BG_LAYER_TILE_SIZE, GalaxyConstants.BG_LAYER_TILE_SIZE);
            sr.sortingOrder = sortOrder;

            // Аддитивный материал: чёрные пиксели = 0 вклад → нет тёмного ореола поверх туманности.
            var starShader = Resources.Load<Shader>(GalaxyConstants.SHADER_STAR_ADDITIVE);
            if (starShader != null)
            {
                var mat = new Material(starShader);
                sr.material = mat;
                _ownedMaterials.Add(mat);
            }

            _ownedTextures.Add(tex);
            _ownedSprites.Add(sprite);
            _parallaxLayers.Add((layer.transform, sr, parallaxSpeed));
        }

        // аккумулирует пиксель в float-буфер (clamped max)
        static void AccumPixel(float[] buf, int size, int x, int y, float brightness, float intensity)
        {
            if (x < 0 || x >= size || y < 0 || y >= size) return;
            float v   = brightness * intensity;
            int   idx = (y * size + x) * 4;
            buf[idx]   = Mathf.Min(buf[idx]   + v, 1f); // R
            buf[idx+1] = Mathf.Min(buf[idx+1] + v, 1f); // G
            buf[idx+2] = Mathf.Min(buf[idx+2] + v, 1f); // B
            buf[idx+3] = Mathf.Min(buf[idx+3] + v, 1f); // A
        }

        // =========================================================
        //  Астероидные поля фона
        private void CreateLargeStarLayer(string name, int sortOrder, float parallaxSpeed, float quadSize)
        {
            var shader = Resources.Load<Shader>(GalaxyConstants.SHADER_STAR_LARGE);
            if (shader == null)
            {
                Debug.LogWarning($"[BackgroundVisualController] Shader '{GalaxyConstants.SHADER_STAR_LARGE}' not found.");
                return;
            }

            var mat = new Material(shader);
            mat.SetFloat("_GridCells",       GalaxyConstants.BG_STAR_BIG_GRID);
            mat.SetFloat("_Density",         0.00f);
            mat.SetFloat("_CoreSizeMin",     0.06f);
            mat.SetFloat("_CoreSizeMax",     0.22f);
            mat.SetFloat("_GlowFalloff",     6.0f);
            mat.SetFloat("_GlowStrength",    0.6f);
            mat.SetFloat("_FlickerChance",   0.45f);
            mat.SetFloat("_FlickerAmp",      0.38f);
            mat.SetFloat("_FlickerSpeedMin", 0.07f);
            mat.SetFloat("_FlickerSpeedMax", 0.16f);
            _ownedMaterials.Add(mat);

            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _ownedTextures.Add(tex);

            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
            _ownedSprites.Add(sprite);

            var obj = new GameObject(name);
            obj.transform.SetParent(transform);
            obj.transform.localScale = new Vector3(quadSize, quadSize, 1f);

            var sr = obj.AddComponent<SpriteRenderer>();
            sr.sprite       = sprite;
            sr.material     = mat;
            sr.sortingOrder = sortOrder;

            _nebulaLayers.Add((obj.transform, Vector2.zero, parallaxSpeed));
        }

        // =========================================================
        //  LateUpdate: параллакс + догенерация
        // =========================================================

        private void LateUpdate()
        {
            if (_camera == null || _blackVoid == null) return;
            Vector3 cam  = _camera.transform.position;
            float   camH = _camera.orthographicSize * 2f;
            float   camW = camH * _camera.aspect;

            _blackVoid.position   = new Vector3(cam.x, cam.y, 500f);
            _blackVoid.localScale = new Vector3(camW * GalaxyConstants.BG_VOID_SCALE_FACTOR,
                                                 camH * GalaxyConstants.BG_VOID_SCALE_FACTOR, 1f);

            foreach (var (t, sr, speed) in _parallaxLayers)
            {
                t.position = new Vector3(cam.x * (1 - speed) - sr.size.x * 0.5f,
                                         cam.y * (1 - speed) - sr.size.y * 0.5f, t.position.z);
            }

            // Параллакс туманностей: объект «закреплён» вблизи worldPos, но плавно следует
            // за камерой с коэффициентом (1-speed). Чем меньше speed — тем дальше кажется слой.
            // Та же логика, что у звёздных слоёв: pos = cam*(1-speed) + worldPos*(1-...)
            foreach (var (t, worldPos, speed) in _nebulaLayers)
            {
                t.position = new Vector3(
                    worldPos.x + cam.x * (1f - speed),
                    worldPos.y + cam.y * (1f - speed),
                    t.position.z);
            }

        }
    }
}
