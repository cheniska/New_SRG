using UnityEngine;
using SRG.Combat;
using SRG.Config;
using SRG.Economy;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Ships;
using SRG.Simulation;

namespace SRG.Equipment
{
    /// <summary>
    /// Единая точка для работы с игровыми предметами (создание из конфигов + выдача кораблю).
    /// Убирает дубли <c>new ItemStack{...}</c> и прямые вызовы <see cref="ItemFactory.Create"/> /
    /// <see cref="MicroModuleFactory.Create"/> по коду. Все «выдачи» — квесты, дропы, магазины,
    /// консоль <c>give</c> — проходят через одни и те же билдеры/фабрики.
    ///
    /// Терминология:
    /// <list type="bullet">
    /// <item><b>Стек</b> (<see cref="ItemStack"/>) — стакабельный предмет: товары и минералы (в т.ч. ноды).
    ///   Не имеют уникального Uid, агрегируются по <see cref="ItemStack.ItemId"/>.
    ///   <see cref="ItemStack.IsGoods"/> отличает «торговый товар» от useless-стека.</item>
    /// <item><b>Экземпляр</b> (<see cref="ItemInstance"/>) — уникальный предмет: оборудование, микромодули,
    ///   квестовые вещи. Живут в <see cref="ShipData.AllItems"/> и либо в слоте, либо в трюме.</item>
    /// </list>
    /// </summary>
    public static class ItemGrantService
    {
        // ═══════════════════════════════════════════════════════════════════════
        //  СТЕКИ (стакабельные: товары/минералы/ноды)
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Собрать стакабельный стек. Общий билдер для всего стакабельного: товары
        /// (Category="Goods", IsGoods=true), минералы и ноды (Category="Mineral").
        /// <paramref name="isGoods"/> определяет торговую пригодность (ноды — false).
        /// </summary>
        public static ItemStack BuildStack(string itemId, string category, string name,
                                           int unitPrice, int amount, bool isGoods) =>
            new ItemStack
            {
                ItemId      = itemId,
                Category    = category,
                Name        = name,
                TotalWeight = amount,
                BasePrice   = unitPrice,
                IsGoods     = isGoods,
            };

        /// <summary>
        /// Автоматически создать стек по id: ищет предмет в <see cref="ItemsConfig.Items"/>.
        /// Категория стека выбирается по <see cref="ItemConfig.Kind"/>: Goods → грузовой отсек,
        /// Useless (Ноды/находки) → отдельная категория "Mineral". Возвращает null, если id не найден
        /// или предмет не стакается. Для магазинных/торговых сценариев с рантайм-ценой используйте
        /// <see cref="BuildStack"/>.
        /// </summary>
        public static ItemStack CreateStack(string itemId, int amount, GalaxyGenerationContext ctx)
        {
            if (string.IsNullOrEmpty(itemId) || amount <= 0 || ctx == null) return null;

            var cfg = ctx.ItemsConfig?.GetItem(itemId);
            if (cfg != null && cfg.Stackable)
            {
                if (cfg.Kind == ItemKind.Goods)
                    return BuildStack(itemId, CargoUtils.CargoCategory, cfg.Name ?? itemId, cfg.BasePrice, amount, cfg.IsGoods);
                return BuildStack(itemId, "Mineral", cfg.Name ?? itemId, cfg.BasePrice, amount, isGoods: false);
            }

            // Поддержка GalaxyConfig.Goods (иногда цены/имена там, а метаданные в ItemsConfig отсутствуют).
            if (ctx.Config?.Goods != null && ctx.Config.Goods.TryGetValue(itemId, out var galGood))
                return BuildStack(itemId, CargoUtils.CargoCategory, galGood.DisplayName ?? itemId, galGood.BasePrice, amount, isGoods: true);

            return null;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  ЭКЗЕМПЛЯРЫ (оборудование + микромодули)
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Собрать экземпляр оборудования из <see cref="ItemsConfig"/>. Тонкая обёртка
        /// над <see cref="ItemFactory.Create"/> — единая точка для всех «спавнов оборудования».</summary>
        public static ItemInstance CreateEquipment(string category, string itemId,
                                                   GalaxyGenerationContext ctx,
                                                   string raceOverride = null, int? gtlOverride = null)
        {
            if (ctx?.ItemsConfig == null || string.IsNullOrEmpty(category) || string.IsNullOrEmpty(itemId)) return null;
            return ItemFactory.Create(category, itemId, ctx.ItemsConfig, ctx.Config, gtlOverride, raceOverride);
        }

        /// <summary>Собрать экземпляр микромодуля (Kind=MicroModules).
        /// Тонкая обёртка над <see cref="MicroModuleFactory.Create"/>.</summary>
        public static ItemInstance CreateMicroModule(string itemId, GalaxyGenerationContext ctx)
        {
            if (ctx?.ItemsConfig == null || string.IsNullOrEmpty(itemId)) return null;
            return MicroModuleFactory.Create(itemId, ctx.ItemsConfig);
        }

        /// <summary>
        /// Создать экземпляр по spec-у: <c>Category:Id</c> (Weapons:W_Laser, MicroModule:MM_T1_...)
        /// или просто id (сначала микромодуль, затем перебор категорий ItemsConfig).
        /// Возвращает null, если id не найден.
        /// </summary>
        public static ItemInstance CreateInstance(string spec, GalaxyGenerationContext ctx,
                                                  string raceOverride = null, int? gtlOverride = null)
        {
            if (string.IsNullOrEmpty(spec) || ctx == null) return null;

            int colon = spec.IndexOf(':');
            if (colon > 0)
            {
                string cat = spec.Substring(0, colon).Trim();
                string id  = spec.Substring(colon + 1).Trim();
                if (cat.Equals("MicroModule", System.StringComparison.OrdinalIgnoreCase))
                    return CreateMicroModule(id, ctx);
                return CreateEquipment(cat, id, ctx, raceOverride, gtlOverride);
            }

            // Auto: MicroModule → Equipment (перебор категорий).
            if (ctx.ItemsConfig?.GetMicroModule(spec) != null)
                return CreateMicroModule(spec, ctx);

            if (ctx.ItemsConfig != null)
            {
                foreach (var (cat, itemId, _) in ctx.ItemsConfig.EnumerateAllItems())
                    if (string.Equals(itemId, spec, System.StringComparison.Ordinal))
                        return CreateEquipment(cat, spec, ctx, raceOverride, gtlOverride);
            }
            return null;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  РАЗМЕЩЕНИЕ НА КОРАБЛЕ (Give*)
        //  Каждый Give == Create* + разместить.
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Куда положить оборудование при выдаче.</summary>
        public enum GrantTarget
        {
            /// <summary>Свободный слот категории → иначе трюм.</summary>
            Auto,
            /// <summary>Только в трюм.</summary>
            Inventory,
            /// <summary>Конкретный слот (см. параметр <c>specificSlot</c>).</summary>
            Slot,
        }

        public struct GrantResult
        {
            public bool  Ok;
            public string Message;
            public string Placement; // "slot:Weapons_1" / "inventory" / "stack" / "error"
            public int    Amount;
        }

        /// <summary>
        /// Верхнеуровневая выдача: авто-детект стек/экземпляр по id или <c>Cat:Id</c>. Использует
        /// <see cref="CreateStack"/> / <see cref="CreateInstance"/> и <see cref="PutStack"/> /
        /// <see cref="PutInstance"/>. Для оборудования: слот (авто/явный) или трюм.
        /// </summary>
        public static GrantResult Give(ShipData ship, string spec, int amount = 1,
                                       GrantTarget target = GrantTarget.Auto, string specificSlot = null)
        {
            if (ship == null)                   return Fail("Ship == null.");
            if (string.IsNullOrEmpty(spec))     return Fail("Пустой id.");

            var ctx = GameWorld.Context;
            if (ctx == null)                    return Fail("GalaxyContext не готов.");

            // Явные псевдо-категории стеков.
            int colon = spec.IndexOf(':');
            if (colon > 0)
            {
                string cat = spec.Substring(0, colon).Trim();
                string id  = spec.Substring(colon + 1).Trim();
                if (cat.Equals("Goods",   System.StringComparison.OrdinalIgnoreCase) ||
                    cat.Equals("Good",    System.StringComparison.OrdinalIgnoreCase) ||
                    cat.Equals("Mineral", System.StringComparison.OrdinalIgnoreCase))
                {
                    var s = CreateStack(id, amount, ctx);
                    if (s == null) return Fail($"Стек '{id}' не найден в ItemsConfig.");
                    PutStack(ship, s);
                    return StackResult(id, amount);
                }
                // Иначе — экземпляр (Equipment/MicroModule).
                var inst = CreateInstance(spec, ctx, raceOverride: ship.Race);
                if (inst == null) return Fail($"'{spec}': не найден.");
                return PlaceInstance(ship, inst, spec, target, specificSlot);
            }

            // Автопоиск по id.
            // 1. Стек (Goods/Minerals).
            var stack = CreateStack(spec, amount, ctx);
            if (stack != null)
            {
                PutStack(ship, stack);
                return StackResult(spec, amount);
            }

            // 2. Экземпляр (MicroModule / Equipment).
            var instance = CreateInstance(spec, ctx, raceOverride: ship.Race);
            if (instance != null)
                return PlaceInstance(ship, instance, spec, target, specificSlot);

            return Fail($"'{spec}': не найден ни в ItemsConfig (Goods/Minerals/MicroModules), ни в ItemsConfig.");
        }

        /// <summary>Положить стек в трюм (тонкая обёртка над <see cref="ShipInventory.AddStack"/>
        /// — единая точка входа для стеков).</summary>
        public static bool PutStack(ShipData ship, ItemStack stack)
        {
            if (ship?.Inventory == null || stack == null) return false;
            ship.Inventory.AddStack(stack);
            return true;
        }

        /// <summary>Положить экземпляр-предмет в трюм с регистрацией в AllItems (алиас
        /// <see cref="InventoryService.PutItem"/> для симметрии с <see cref="PutStack"/>).</summary>
        public static bool PutInstance(ShipData ship, ItemInstance item) =>
            InventoryService.PutItem(ship, item);

        // ─── Экземпляр: слот/трюм ─────────────────────────────────────────────

        /// <summary>Разместить готовый экземпляр по правилу target/slot. Общая логика для
        /// give-команды и любых будущих скриптов выдачи оборудования.
        /// <paramref name="recompute"/>=false отложит <see cref="ShipBonusService.RecomputeShipEquipment"/>
        /// и связанные ребилды — используется при пакетной установке (starter kit),
        /// когда вызывающий сам делает один общий recompute после цикла.</summary>
        public static GrantResult PlaceInstance(ShipData ship, ItemInstance inst, string label,
                                                GrantTarget target, string specificSlot,
                                                bool recompute = true)
        {
            if (ship == null || inst == null) return Fail("PlaceInstance: null.");

            // Микромодуль / любой не-оборудуемый предмет → сразу в трюм.
            if (!inst.IsEquipment)
            {
                PutInstance(ship, inst);
                return Ok($"'{label}' → трюм.", "inventory");
            }

            // Hull ставить в слот на живом корабле нельзя (пересобирает SlotPlan).
            if (inst.Category == EquipmentCategory.Hull)
            {
                PutInstance(ship, inst);
                return Ok($"Hull '{label}' → трюм (надеть через UI, чтобы пересобрать слоты).", "inventory");
            }

            // Явный слот.
            if (target == GrantTarget.Slot && !string.IsNullOrEmpty(specificSlot))
            {
                if (!ship.Equipment.Slots.ContainsKey(specificSlot))
                    return Fail($"Слот '{specificSlot}' у корабля отсутствует.");
                var displaced = InventoryService.EquipToSlot(ship, specificSlot, inst);
                if (displaced != null) InventoryService.AddToInventory(ship, displaced);
                if (recompute) Recompute(ship);
                return Ok($"'{label}' → слот {specificSlot}" +
                          (displaced != null ? $" (вытеснил '{displaced.ItemId}' → трюм)." : "."),
                          $"slot:{specificSlot}");
            }

            // Auto: сначала родная категория, затем CompatibleSlots.
            if (target != GrantTarget.Inventory)
            {
                string slot = FindFreeSlot(ship, inst.Category);
                if (slot == null && inst.CompatibleSlots != null)
                    foreach (var altCat in inst.CompatibleSlots)
                    {
                        slot = FindFreeSlot(ship, altCat);
                        if (slot != null) break;
                    }
                if (slot != null)
                {
                    InventoryService.EquipToSlot(ship, slot, inst);
                    if (recompute) Recompute(ship);
                    return Ok($"'{label}' → слот {slot}.", $"slot:{slot}");
                }
            }

            PutInstance(ship, inst);
            return Ok($"'{label}' → трюм.", "inventory");
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Внутреннее
        // ═══════════════════════════════════════════════════════════════════════

        private static string FindFreeSlot(ShipData ship, string category)
        {
            foreach (var kv in ship.Equipment.Slots)
                if (kv.Key.StartsWith(category + "_", System.StringComparison.Ordinal) && kv.Value == null)
                    return kv.Key;
            return null;
        }

        private static void Recompute(ShipData ship)
        {
            WeaponSystem.RebuildShieldState(ship);
            ship.InvalidateWeaponSlotsCache();
            ShipBonusService.RecomputeShipEquipment(ship);
        }

        private static GrantResult Ok(string msg, string placement) =>
            new GrantResult { Ok = true, Message = msg, Placement = placement, Amount = 1 };

        private static GrantResult Fail(string msg) =>
            new GrantResult { Ok = false, Message = msg, Placement = "error" };

        private static GrantResult StackResult(string id, int amount) =>
            new GrantResult { Ok = true, Amount = amount, Placement = "stack",
                              Message = $"'{id}' ×{amount} → трюм (стек)." };
    }
}
