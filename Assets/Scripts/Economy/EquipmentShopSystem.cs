using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Config;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Generation;

namespace SRG.Economy
{
    /// <summary>
    /// Магазин оборудования на планете: каждые 7 дней ассортимент подтягивается к целевому числу
    /// предметов по каждой категории. Цель = Random(0..4) + EconomyMod + GovernmentMod (clamp 0..5).
    /// TL спавна выбирается случайно из [max(1, PTU-3), PTU].
    /// </summary>
    public static class EquipmentShopSystem
    {
        /// <summary>Категории, которые подлежат еженедельному обновлению.
        /// Артефакты НЕ обновляем (они приходят через квесты), как и алиасные BoardingHook/TowingRig.</summary>
        private static readonly string[] RefreshCategories =
        {
            EquipmentCategory.Hull,
            EquipmentCategory.Engine,
            EquipmentCategory.FuelTank,
            EquipmentCategory.Forsage,
            EquipmentCategory.Shield,
            EquipmentCategory.Radar,
            EquipmentCategory.Scanner,
            EquipmentCategory.Droid,
            EquipmentCategory.CargoGrabber,
            EquipmentCategory.Weapons,
        };

        private const int MinTargetPerCategory = 0;
        private const int MaxTargetPerCategory = 5; // верхняя граница после применения модификаторов
        private const int RandomMaxExclusive = 5;   // Random(0,5) → 0..4

        /// <summary>
        /// Однократное наполнение магазина при генерации планеты или при первой стыковке со станцией.
        /// </summary>
        public static void InitialFill(ILandingSite site, GalaxyGenerationContext ctx)
        {
            if (site == null || ctx == null) return;
            RefreshShop(site, ctx);
        }

        /// <summary>
        /// Ежедневный тик по галактике. Для каждой обитаемой планеты RefreshShop срабатывает
        /// раз в 7 дней, день недели стаббирован по uid планеты (чтобы избежать стампида).
        /// </summary>
        // Per-turn instrumentation. Спайк T322 (shop=128мс vs типичных 2-5) должен подсветить,
        // сколько планет одновременно попали в day-slot и сколько предметов родилось/удалилось.
        internal static int _diagShopsRefreshed;
        internal static int _diagItemsAdded;
        internal static int _diagItemsRemoved;

        public static void TickAll(GalaxyData galaxy, GalaxyGenerationContext ctx)
        {
            if (galaxy == null || ctx?.ItemsConfig == null) return;
            _diagShopsRefreshed = 0;
            _diagItemsAdded = 0;
            _diagItemsRemoved = 0;
            int day = galaxy.CurrentTurn;
            foreach (var star in galaxy.StarsMap.Values)
                foreach (var planet in star.Planets)
                {
                    if (!IsInhabited(planet)) continue;
                    int slot = Mathf.Abs(planet.Uid?.GetHashCode() ?? 0) % 7;
                    if ((day + slot) % 7 != 0) continue;
                    _diagShopsRefreshed++;
                    RefreshShop(planet, ctx);   // PlanetData implicitly = ILandingSite
                }
        }

        /// <summary>
        /// Подгоняет магазин к целевому числу предметов в каждой обновляемой категории.
        /// diff = target − current: положительный → доспаунить, отрицательный → удалить лишние.
        /// </summary>
        public static void RefreshShop(ILandingSite site, GalaxyGenerationContext ctx)
        {
            var equipConfig = ctx?.ItemsConfig;
            var galaxyConfig = ctx?.Config;
            if (equipConfig == null || galaxyConfig == null) return;
            var settlement = site?.Settlement;
            if (settlement?.EquipmentShop?.Items == null) return;

            // Планета ориентируется на свой ПТУ; станция ПТУ не имеет — берёт галактический ГТУ.
            int techBase = site.Kind == LandingSiteKind.Station
                ? (GalaxyManager.Instance?.GeneratedGalaxy?.GtuLevel ?? 5)
                : settlement.TechLevel;
            int ptu = Mathf.Clamp(techBase, 1, 10);
            int minTL = Mathf.Max(1, ptu - 3);
            int maxTL = ptu;

            EconomyTypeConfig econCfg = null;
            if (!string.IsNullOrEmpty(settlement.EconomyType) && galaxyConfig.Planets?.EconomyTypes != null)
                galaxyConfig.Planets.EconomyTypes.TryGetValue(settlement.EconomyType, out econCfg);

            GovernmentTypeConfig govCfg = null;
            if (!string.IsNullOrEmpty(settlement.Government) && galaxyConfig.Planets?.GovernmentTypes != null)
                galaxyConfig.Planets.GovernmentTypes.TryGetValue(settlement.Government, out govCfg);

            int added = 0, removed = 0;
            foreach (var category in RefreshCategories)
            {
                int target = ComputeTargetCount(category, econCfg, govCfg);
                int current = CountByCategory(settlement.EquipmentShop, category);
                int diff = target - current;

                if (diff > 0)
                {
                    AddItems(site, ctx, category, minTL, maxTL, diff);
                    added += diff;
                }
                else if (diff < 0)
                {
                    RemoveItems(site, category, -diff);
                    removed += -diff;
                }
            }

            // Микромодули: отдельный пул рядом с обычным оборудованием.
            RefreshMicroModules(site, ctx, ptu);

            _diagItemsAdded += added;
            _diagItemsRemoved += removed;
            int turn = GalaxyManager.Instance?.GeneratedGalaxy?.CurrentTurn ?? 0;
            EconomicLog.Shop(turn, EconomicLog.Safe(site.Name), "REFRESH",
                $"ptu={ptu} eco={settlement.EconomyType} gov={settlement.Government} " +
                $"added={added} removed={removed} total={settlement.EquipmentShop.Items.Count}");
        }

        /// <summary>Держит стеллаж микромодулей в магазине: 0..2 штук, отобранных по расе/стороне
        /// планеты и её ПТУ (см. <see cref="ItemShopSource"/>).</summary>
        private static void RefreshMicroModules(ILandingSite site, GalaxyGenerationContext ctx, int ptu)
        {
            var itemsConfig = ctx?.ItemsConfig;
            if (itemsConfig == null) return;

            var shop = site.Settlement.EquipmentShop;
            // Считаем сколько уже лежит.
            int current = 0;
            foreach (var kv in shop.Items)
                if (kv.Value != null && kv.Value.Category == MicroModuleFactory.CategoryKey) current++;

            int target = Mathf.Clamp(Random.Range(0, 3), 0, 2);
            int diff = target - current;
            if (diff <= 0) return;

            // Пул кандидатов, подходящих цели посадки.
            List<string> pool = null;
            foreach (var kv in itemsConfig.EnumerateByKind(ItemKind.MicroModules))
            {
                var s = kv.Value.Sources?.Shop;
                if (s == null) continue;
                if (s.MinPtu > ptu) continue;

                bool raceOk = s.RaceWhitelist != null && s.RaceWhitelist.Count > 0 &&
                              (s.RaceWhitelist.Contains("*") || s.RaceWhitelist.Contains(site.Race));
                bool sideOk = s.SideWhitelist != null && s.SideWhitelist.Count > 0 &&
                              (s.SideWhitelist.Contains("*") ||
                               (site.Owner != null && s.SideWhitelist.Contains(site.Owner)));
                if (!raceOk && !sideOk) continue;

                (pool ??= new()).Add(kv.Key);
            }
            if (pool == null || pool.Count == 0) return;

            for (int i = 0; i < diff; i++)
            {
                var id = pool[Random.Range(0, pool.Count)];
                var inst = ItemGrantService.CreateMicroModule(id, ctx);
                if (inst != null) shop.Items[inst.Uid] = inst;
            }
        }

        // ──────────────────────────────────────────
        // Расчёт целевого числа
        // ──────────────────────────────────────────

        private static int ComputeTargetCount(string category, EconomyTypeConfig econ, GovernmentTypeConfig gov)
        {
            int baseTarget = Random.Range(0, RandomMaxExclusive);
            int ecoMod = ReadMod(econ?.EquipmentShopMods, category);
            int govMod = ReadMod(gov?.EquipmentShopMods, category);
            return Mathf.Clamp(baseTarget + ecoMod + govMod, MinTargetPerCategory, MaxTargetPerCategory);
        }

        private static int ReadMod(Dictionary<string, int> mods, string category)
        {
            if (mods == null) return 0;
            return mods.TryGetValue(category, out var v) ? v : 0;
        }

        // ──────────────────────────────────────────
        // Подсчёт / добавление / удаление
        // ──────────────────────────────────────────

        private static int CountByCategory(PlanetEquipmentShop shop, string category)
        {
            int count = 0;
            foreach (var kv in shop.Items)
                if (kv.Value != null && kv.Value.Category == category) count++;
            return count;
        }

        // Кэш кандидатов по (equipConfig, category, minTL, maxTL).
        // EnumerateAllItems проходит через _itemsByCat + резолв шаблонов — это не дёшево
        // когда вызывается на каждую планету × категорию каждую неделю.
        private static readonly Dictionary<(ItemsConfig cfg, string cat, int minTL, int maxTL), List<string>> _candidateCache
            = new Dictionary<(ItemsConfig, string, int, int), List<string>>();

        private static List<string> GetCandidates(ItemsConfig equipConfig, string category, int minTL, int maxTL)
        {
            var key = (equipConfig, category, minTL, maxTL);
            if (_candidateCache.TryGetValue(key, out var cached)) return cached;

            var list = new List<string>();
            foreach (var entry in equipConfig.EnumerateAllItems())
            {
                if (entry.category != category) continue;

                if (category == EquipmentCategory.Weapons)
                {
                    list.Add(entry.itemId);
                    continue;
                }

                int start = entry.config.StartGTL > 0 ? entry.config.StartGTL : 1;
                int end   = entry.config.EndGTL   > 0 ? entry.config.EndGTL   : 10;
                if (end < minTL || start > maxTL) continue;
                list.Add(entry.itemId);
            }
            _candidateCache[key] = list;
            return list;
        }

        /// <summary>Сбрасывает кеш кандидатов — нужно, если ResearchState изменил доступность
        /// шаблонов (AddItemTemplate/RemoveItemTemplate).</summary>
        public static void InvalidateCandidateCache() => _candidateCache.Clear();

        private static void AddItems(
            ILandingSite site, GalaxyGenerationContext ctx,
            string category, int minTL, int maxTL, int count)
        {
            // Weapons: gtlOverride задаёт TL → пул один и тот же независимо от TL-окна.
            // Остальные категории: фильтр по пересечению [StartGTL..EndGTL] и [minTL..maxTL].
            var candidates = GetCandidates(ctx.ItemsConfig, category, minTL, maxTL);
            if (candidates.Count == 0) return;

            var research = GalaxyManager.Instance?.GeneratedGalaxy?.ResearchState;

            for (int i = 0; i < count; i++)
            {
                // Делаем до 8 попыток найти кандидата, не запрещённого ResearchState.
                string id = null;
                for (int attempt = 0; attempt < 8 && id == null; attempt++)
                {
                    string pick = candidates[Random.Range(0, candidates.Count)];
                    if (IsAllowedByResearch(research, site, pick, maxTL)) id = pick;
                }
                if (id == null) continue;

                int? gtlOverride = category == EquipmentCategory.Weapons
                    ? Random.Range(minTL, maxTL + 1)
                    : (int?)null;
                var inst = ItemGrantService.CreateEquipment(
                    category, id, ctx, raceOverride: site.Race, gtlOverride: gtlOverride);
                if (inst != null)
                    site.Settlement.EquipmentShop.Items[inst.Uid] = inst;
            }
        }

        private static bool IsAllowedByResearch(GalacticResearchState state, ILandingSite site, string templateId, int siteMaxTL)
        {
            if (state == null) return true;
            if (state.IsTemplateDisabled(templateId)) return false;
            if (state.TemplateGates != null && state.TemplateGates.TryGetValue(templateId, out var gate))
            {
                if (gate.MinTL > siteMaxTL) return false;
                if (!string.IsNullOrEmpty(gate.OwnerRaceFilter)
                    && !string.Equals(gate.OwnerRaceFilter, site.Race, System.StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private static void RemoveItems(ILandingSite site, string category, int count)
        {
            // Собираем список uid'ов нужной категории, перемешиваем и удаляем первые N.
            var uids = new List<string>();
            foreach (var kv in site.Settlement.EquipmentShop.Items)
                if (kv.Value != null && kv.Value.Category == category) uids.Add(kv.Key);

            // Перемешивание (Fisher-Yates) — небольшая выборка, OK.
            for (int i = uids.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (uids[i], uids[j]) = (uids[j], uids[i]);
            }

            int toRemove = Mathf.Min(count, uids.Count);
            for (int i = 0; i < toRemove; i++)
                site.Settlement.EquipmentShop.Items.Remove(uids[i]);
        }

        // ──────────────────────────────────────────
        // Общее
        // ──────────────────────────────────────────

        private static bool IsInhabited(PlanetData planet)
        {
            if (planet == null) return false;
            if (string.IsNullOrEmpty(planet.Race)) return false;
            if (string.Equals(planet.Race, GalaxyConstants.RACE_NONE_KEY, System.StringComparison.OrdinalIgnoreCase)) return false;
            return planet.Settlement.Population > 0;
        }
    }
}
