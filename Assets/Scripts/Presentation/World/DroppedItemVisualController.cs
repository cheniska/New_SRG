using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Config;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Presentation.Common;
using SRG.Controllers;

namespace SRG.Presentation.World
{
    // =====================================================================
    // DroppedItemVisualController.cs
    // Визуальный контроллер предмета, лежащего в открытом космосе.
    //
    // Поддерживает два типа предметов:
    //   ItemStack   — стек минералов (stackable). Спрайт выбирается по тиру:
    //                 {ItemConfig.SpritePath}/Tier_{1..N} где N зависит от TotalWeight.
    //                 Пути задаются в ItemsConfig.json → ItemConfig.SpritePath.
    //   ItemInstance — единичный предмет оборудования (Equipment drop).
    //                 Спрайт берётся из ItemConfig через GraphicsManager,
    //                 или рисуется цветной ромб-заглушка.
    //
    // Поведение:
    //   • Дрейфует от точки взрыва с начальным вектором drift, замедляется
    //   • Медленно вращается
    //   • Пульсирует (alpha) — заметен на фоне космоса
    //   • За последние 30% времени жизни постепенно угасает
    //   • При сближении с кораблём игрока на расстояние PickupRadius — подбирается
    //
    // Спавнится из SystemViewManager.SpawnDroppedItems().
    // Уничтожает себя сам по истечении GalaxyConstants.DROPPED_ITEM_LIFETIME.
    // =====================================================================

    public class DroppedItemVisualController : MonoBehaviour
    {
        // ── Data (один из двух, второй null) ─────────────────────────────────────
        private ItemStack _stack;      // минерал
        private ItemInstance _item;       // оборудование
        private string _label;      // для отладки

        // ── Visual ────────────────────────────────────────────────────────────────
        private SpriteRenderer _sr;
        private Sprite[] _animFrames;
        private float _animSecPerFrame;
        private float _animTimer;
        private int _animFrame;

        // ── Motion ────────────────────────────────────────────────────────────────
        private Vector2 _velocity;
        private float _rotationSpeed;

        // ── Lifetime ──────────────────────────────────────────────────────────────
        private float _lifetime;
        private float _timer;

        // ── Pickup ────────────────────────────────────────────────────────────────
        private bool _collected;
        private const float PickupRadius = 0.4f;

        // ─────────────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Инициализирует объект для минерального стека.
        /// drift — вектор начального дрейфа (из точки взрыва).
        /// itemsCtx — нужен для получения ItemConfig.SpritePath.
        /// </summary>
        public void SetupStack(ItemStack stack, Vector2 drift, GalaxyGenerationContext itemsCtx)
        {
            _stack = stack;
            _label = $"{stack?.Name} x{stack?.TotalWeight}";
            _lifetime = GalaxyConstants.DROPPED_ITEM_LIFETIME;
            Init(drift);

            EnsureRenderer();
            LoadSpriteForStack(stack, itemsCtx);
            ApplyScaleForStack(stack);
        }

        /// <summary>
        /// Инициализирует объект для предмета оборудования.
        /// drift — вектор начального дрейфа (из точки взрыва).
        /// </summary>
        public void SetupItem(ItemInstance item, Vector2 drift)
        {
            _item = item;
            _label = item?.Name;
            _lifetime = GalaxyConstants.DROPPED_ITEM_LIFETIME;
            Init(drift);

            EnsureRenderer();
            LoadSpriteForItem(item);
            transform.localScale = Vector3.one * 0.25f;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Unity
        // ─────────────────────────────────────────────────────────────────────────

        private void Update()
        {
            if (_collected) return;

            float dt = Time.deltaTime;
            _timer += dt;

            // Движение: дрейф с затуханием
            transform.localPosition += new Vector3(_velocity.x, _velocity.y, 0f) * dt;
            _velocity = Vector2.Lerp(_velocity, Vector2.zero, dt * 2f);

            // Вращение
            transform.Rotate(0f, 0f, _rotationSpeed * dt);

            // Прозрачность: пульс + fade на последних 30% времени жизни
            float lifeT = _timer / _lifetime;
            float fadeAlpha = lifeT > 0.7f
                ? Mathf.Lerp(1f, 0f, (lifeT - 0.7f) / 0.3f)
                : 1f;
            float pulse = 0.7f + 0.3f * Mathf.Sin(_timer * 3.5f);
            if (_sr != null)
                _sr.color = new Color(1f, 1f, 1f, fadeAlpha * pulse);

            // Анимация кадров
            if (_animFrames != null && _animFrames.Length > 1)
            {
                _animTimer += dt;
                if (_animTimer >= _animSecPerFrame)
                {
                    int advance = Mathf.FloorToInt(_animTimer / _animSecPerFrame);
                    _animTimer %= _animSecPerFrame;
                    _animFrame = (_animFrame + advance) % _animFrames.Length;
                    if (_sr != null) _sr.sprite = _animFrames[_animFrame];
                }
            }

            // Авто-подбор при сближении с игроком
            if (!_collected && PlayerShip.Instance != null)
            {
                float dist = Vector2.Distance(transform.position, PlayerShip.Instance.transform.position);
                if (dist < PickupRadius)
                    Collect();
            }

            if (_timer >= _lifetime)
                Destroy(gameObject);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Pickup
        // ─────────────────────────────────────────────────────────────────────────

        private void Collect()
        {
            _collected = true;
            var ship = PlayerShip.Instance?.ShipData;
            if (ship == null) { Destroy(gameObject); return; }

            if (_stack != null)
                ship.Inventory.AddStack(_stack);
            else if (_item != null)
                InventoryService.PutItem(ship, _item);

            Destroy(gameObject);
        }

        private void Init(Vector2 drift)
        {
            _velocity = drift + Random.insideUnitCircle * 0.25f;
            _rotationSpeed = Random.Range(-50f, 50f);
            _timer = 0f;
            _collected = false;
        }

        private void EnsureRenderer()
        {
            if (!TryGetComponent(out _sr))
                _sr = gameObject.AddComponent<SpriteRenderer>();
            _sr.sortingOrder = SortingLayerRegistry.Get(SortLayer.HelperOverlay);
        }

        private void LoadSpriteForStack(ItemStack stack, GalaxyGenerationContext ctx)
        {
            if (stack == null) { _sr.sprite = CreateFallbackSprite(Color.gray); return; }

            // Ступени графики по количеству (GraphicSteps в ItemsConfig) — приоритет;
            // без них — легаси-схема {SpritePath}/Tier_{N} по DropWeightMax.
            // NaturalOrigin (стек из астероида) даёт приоритет NaturalSprite над обычным.
            string path = StackGraphics.ResolveSprite(stack.ItemId, stack.TotalWeight, stack.NaturalOrigin);
            if (string.IsNullOrEmpty(path))
            {
                int tierCount = GalaxyConstants.MINERAL_SPRITE_TIER_COUNT;
                int tier = ResolveMineralTier(stack.TotalWeight, tierCount, ctx, stack.ItemId);
                string basePath = ResolveMineralBasePath(stack.ItemId, ctx);
                path = $"{basePath}/Tier_{tier}";
            }

            if (GraphicsManager.Instance != null)
            {
                var frames = GraphicsManager.Instance.GetSpriteSheet(path);
                if (frames != null && frames.Length > 0)
                {
                    _animFrames = frames;
                    _animSecPerFrame = 1f / 12f;
                    _animFrame = 0;
                    _sr.sprite = frames[0];
                    return;
                }
            }

            Debug.LogWarning($"[DroppedItem] No sprites found at '{path}' for mineral '{stack.ItemId}'. Using procedural fallback.");
            _sr.sprite = CreateFallbackSprite(new Color(0.55f, 0.75f, 0.95f));
        }

        private void LoadSpriteForItem(ItemInstance item)
        {
            if (item == null) { _sr.sprite = CreateFallbackSprite(Color.white); return; }
            Debug.LogWarning($"[DroppedItem] No world sprite for equipment '{item.Name}' ({item.Category}/{item.ItemId}). Using procedural fallback.");
            _sr.sprite = CreateEquipmentFallbackSprite(item);
        }

        private void ApplyScaleForStack(ItemStack stack)
        {
            if (stack == null) { transform.localScale = Vector3.one * 0.15f; return; }
            int tierCount = GalaxyConstants.MINERAL_SPRITE_TIER_COUNT;
            float maxWeight = 50f;
            float t = Mathf.Clamp01(stack.TotalWeight / maxWeight);
            float scale = Mathf.Lerp(0.12f, 0.32f, t);
            transform.localScale = Vector3.one * scale;
        }

        private static int ResolveMineralTier(int totalWeight, int tierCount,
            GalaxyGenerationContext ctx, string itemId)
        {
            if (tierCount <= 1) return 1;

            int dropMax = 50; // дефолт
            var mineralCfg = ctx?.ItemsConfig?.GetItem(itemId);
            if (mineralCfg != null && mineralCfg.DropWeightMax > 0)
                dropMax = mineralCfg.DropWeightMax;

            float step = (float)dropMax / tierCount;
            int tier = Mathf.Clamp(Mathf.CeilToInt(totalWeight / step), 1, tierCount);
            return tier;
        }

        private static string ResolveMineralBasePath(string itemId, GalaxyGenerationContext ctx)
        {
            var mineralCfg = ctx?.ItemsConfig?.GetItem(itemId);
            if (mineralCfg?.SpritePath != null && mineralCfg.SpritePath.Length > 0)
                return mineralCfg.SpritePath;
            return $"{GalaxyConstants.MINERAL_SPRITES_PATH}/{itemId}";
        }

        private static Sprite CreateFallbackSprite(Color color)
        {
            const int res = 32;
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            var pixels = new Color[res * res];
            float center = res * 0.5f;

            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float dx = Mathf.Abs(x - center + 0.5f);
                    float dy = Mathf.Abs(y - center + 0.5f);
                    bool inside = (dx + dy) < center - 1f;
                    bool border = inside && (dx + dy) > center - 4f;
                    if (!inside) continue;
                    pixels[y * res + x] = border
                        ? new Color(color.r, color.g, color.b, 1f)
                        : new Color(color.r * 0.55f, color.g * 0.55f, color.b * 0.55f, 0.9f);
                }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
        }

        private static Sprite CreateEquipmentFallbackSprite(ItemInstance item)
        {
            Color color = item?.Category switch
            {
                EquipmentCategory.Weapons => new Color(1.0f, 0.3f, 0.3f),
                EquipmentCategory.Shield => new Color(0.3f, 0.6f, 1.0f),
                EquipmentCategory.Engine => new Color(0.4f, 1.0f, 0.6f),
                EquipmentCategory.Artefacts => new Color(0.9f, 0.5f, 1.0f),
                EquipmentCategory.Hull => new Color(0.8f, 0.8f, 0.8f),
                _ => new Color(0.7f, 0.7f, 0.5f),
            };
            return CreateFallbackSprite(color);
        }
    }
}
