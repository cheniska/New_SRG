using System;
using System.Collections.Generic;
using SRG.Config;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.Ships.Services
{
    /// <summary>
    /// Автоматика загрузки NPC-корабля: надевание подобранного оборудования и сброс груза при перегрузе.
    ///
    /// Надевание (<see cref="TryAutoEquipUpgrades"/>):
    ///   • запускается на ход, СЛЕДУЮЩИЙ за подбором (<see cref="ShipData.PendingAutoEquipTurn"/>,
    ///     выставляет <see cref="SRG.Combat.PickupSystem"/> в момент разгрузки контейнера);
    ///   • кандидат — работающий (не сломанный) предмет из трюма, для которого на корпусе есть слот:
    ///     пустой, либо занятый более слабым/сломанным предметом — тогда своп, старое уходит в трюм;
    ///   • «лучше» = выше TechLevel, при равенстве — выше Price; сломанный текущий проигрывает любому рабочему;
    ///   • корпуса (Hull) автоматически не переставляются (смена корпуса пересобирает слоты и HP);
    ///   • вес отдельно не проверяется: предмет уже на борту, и трюм, и слоты входят в одну и ту же
    ///     нагрузку корпуса — установка не меняет суммарный вес корабля.
    ///
    /// Перегруз (<see cref="ResolveOverload"/>): пока вес (оборудование + трюм) превышает вместимость
    /// корпуса, выкидывает лишнее в космос контейнерами по приоритету:
    ///   1) стакабельное (Stacks) — частично, ровно на величину перегруза, дешёвое за единицу первым;
    ///   2) предметы в трюме (оборудование и прочее) — дешёвые первыми;
    ///   3) установленное оборудование — только если больше нечего: дешёвое некритичное первым,
    ///      Engine/FuelTank в последнюю очередь, Hull не снимается никогда.
    /// </summary>
    public static class ShipLoadoutService
    {
        /// <summary>Перегружен ли корабль собственным весом. Та же формула, что в
        /// EquipmentSystem.CalculateOwnEngineSpeed (перегруз → скорость 0).</summary>
        public static bool IsOverloaded(ShipData ship)
        {
            if (ship == null) return false;
            int cap = EquipmentSystem.GetHullCapacity(ship);
            return cap > 0 && CurrentWeight(ship) > cap;
        }

        /// <summary>
        /// Ежеходовой тик (вызов из NpcSystem до Brain.Tick): отложенное надевание подобранного
        /// оборудования + сброс груза при перегрузе по любой причине. На планете не работает —
        /// там корабль разбирается с грузом через порт, а контейнеры в космос с поверхности не летят.
        /// </summary>
        public static void TickTurn(ShipData ship, StarData star, int turn, ItemsConfig equipConfig)
        {
            if (ship == null || ship.IsItem || ship.CurrentHull <= 0) return;
            if (!string.IsNullOrEmpty(ship.LandedPlanetUid)) return;

            // PendingAutoEquipTurn = ход подбора. NpcSystem тикает ПОСЛЕ симуляции этого хода,
            // на стадии планирования следующего — установка здесь означает «предмет работает
            // со следующего хода», в сам ход подбора он ничего не даёт.
            if (ship.PendingAutoEquipTurn >= 0 && turn >= ship.PendingAutoEquipTurn)
            {
                ship.PendingAutoEquipTurn = -1;
                // AllowArrange=false — лидер запретил подчинённому переставлять оборудование
                // (подменю «Настроить поведение»): подобранное остаётся лежать в трюме.
                // Разбор перегруза ниже флагом не гейтится — иначе корабль встанет намертво.
                if (ship.AllowArrange)
                {
                    TryAutoEquipUpgrades(ship, equipConfig);
                    TryAutoEmbedFromInventory(ship);
                }
            }

            if (IsOverloaded(ship))
                ResolveOverload(ship, star);
        }

        // ── Авто-экипировка ─────────────────────────────────────────────────────

        /// <summary>Надеть из трюма всё, что лучше текущего. Возвращает число установленных предметов.</summary>
        public static int TryAutoEquipUpgrades(ShipData ship, ItemsConfig equipConfig)
        {
            if (ship?.Inventory?.Items == null || ship.Equipment == null) return 0;

            var candidates = new List<ItemInstance>();
            foreach (var it in ship.Inventory.Items)
            {
                if (it == null || !it.IsWorking) continue;
                if (it.Uid != null && it.Uid.StartsWith(ShipInventory.StackItemUidPrefix, StringComparison.Ordinal))
                    continue; // теневой ItemInstance стека — не оборудование
                if (it.Category == EquipmentCategory.Hull) continue;
                // Микромодуль не является оборудованием и не встаёт в слот — им займётся
                // TryAutoEmbedFromInventory на следующем шаге.
                if (!it.IsEquipment) continue;
                candidates.Add(it);
            }
            if (candidates.Count == 0) return 0;

            // Лучшие первыми — при нескольких кандидатах на один слот встанет сильнейший.
            candidates.Sort((a, b) => a.TechLevel != b.TechLevel
                ? b.TechLevel.CompareTo(a.TechLevel)
                : b.Price.CompareTo(a.Price));

            int equipped = 0;
            foreach (var item in candidates)
            {
                string slotKey = FindSlotForUpgrade(ship, item);
                if (slotKey == null) continue;
                var res = EquipmentSystem.Install(ship, slotKey, item.Uid, equipConfig, allowInSpace: true);
                if (!res.Success) continue;
                equipped++;
                GameLog.Add($"[Экипировка] {ship.Name} устанавливает {item.Name}.");
            }
            return equipped;
        }

        /// <summary>
        /// Пробует встроить каждый встраиваемый предмет из трюма в подходящее установленное
        /// оборудование. Возвращает число установок. Правила выбора носителя:
        ///   • берём только установленное оборудование (не смотрим трюм-в-трюм);
        ///   • носитель должен иметь свободный слот встраивания (EmbedService.EffectiveMaxEmbeds);
        ///   • среди подходящих — предпочитаем более дорогое (обычно это лучший предмет);
        ///   • ММ у NPC пока ограничиваем «один встроенный на предмет» (лимит из §12 диздока):
        ///     если у носителя уже стоит хотя бы один ММ — пропускаем его.
        /// </summary>
        public static int TryAutoEmbedFromInventory(ShipData ship)
        {
            if (ship?.Inventory?.Items == null || ship.Equipment == null) return 0;

            // Собираем встраиваемые из трюма (микромодули + встраиваемые артефакты).
            var embeds = new List<ItemInstance>();
            foreach (var it in ship.Inventory.Items)
            {
                if (it == null || !it.IsEmbeddable || it.Embed == null) continue;
                if (it.Uid != null && it.Uid.StartsWith(ShipInventory.StackItemUidPrefix, StringComparison.Ordinal))
                    continue;
                embeds.Add(it);
            }
            if (embeds.Count == 0) return 0;

            // Установленные потенциальные носители.
            var carriers = new List<ItemInstance>();
            foreach (var kv in ship.Equipment.Slots)
            {
                if (kv.Value == null) continue;
                if (!ship.AllItems.TryGetValue(kv.Value, out var carrier) || carrier == null) continue;
                if (!carrier.AllowEmbeds) continue;
                carriers.Add(carrier);
            }
            if (carriers.Count == 0) return 0;

            // Сортируем носителей: более дорогие — приоритетнее (лучший карман для ММ).
            carriers.Sort((a, b) => b.Price.CompareTo(a.Price));

            int installed = 0;
            foreach (var embed in embeds)
            {
                // Лимит §12: NPC держит не более одного встроенного на предмет.
                ItemInstance target = null;
                foreach (var carrier in carriers)
                {
                    if (carrier.Embeds != null && carrier.Embeds.Count > 0) continue;
                    if (carrier.Embeds != null && carrier.Embeds.Count >= EmbedService.EffectiveMaxEmbeds(carrier)) continue;
                    if (!EmbedService.CanInstall(carrier, embed).Ok) continue;
                    target = carrier;
                    break;
                }
                if (target == null) continue;
                if (!EmbedService.Install(target, embed)) continue;

                // Убираем встраиваемое из трюма — оно переехало внутрь носителя.
                ship.Inventory.Remove(embed.Uid);
                ship.AllItems.Remove(embed.Uid);
                installed++;
                GameLog.Add($"[Встраивание] {ship.Name} встраивает {embed.Name} в {target.Name}.");
            }
            if (installed > 0) ShipBonusService.RecomputeShipEquipment(ship);
            return installed;
        }

        /// <summary>Слот, куда стоит поставить item: первый пустой подходящий, иначе слот с самым
        /// слабым текущим предметом, если item его лучше. null — ставить некуда/незачем.</summary>
        private static string FindSlotForUpgrade(ShipData ship, ItemInstance item)
        {
            string weakestKey = null;
            ItemInstance weakest = null;
            foreach (var kv in ship.Equipment.Slots)
            {
                string cat = EquipmentSystem.SlotCategory(kv.Key);
                if (cat == EquipmentCategory.Hull) continue;
                if (!item.CanFitInSlotCategory(cat)) continue;
                if (kv.Value == null) return kv.Key;
                if (!ship.AllItems.TryGetValue(kv.Value, out var cur) || cur == null) return kv.Key;
                if (weakest == null || IsBetter(weakest, cur))
                {
                    weakest = cur;
                    weakestKey = kv.Key;
                }
            }
            return weakestKey != null && IsBetter(item, weakest) ? weakestKey : null;
        }

        /// <summary>Сравнение «a лучше b»: рабочий бьёт сломанного, дальше TechLevel, потом Price.</summary>
        private static bool IsBetter(ItemInstance a, ItemInstance b)
        {
            if (b == null) return true;
            if (a.IsWorking != b.IsWorking) return a.IsWorking;
            if (a.TechLevel != b.TechLevel) return a.TechLevel > b.TechLevel;
            return a.Price > b.Price;
        }

        // ── Сброс груза при перегрузе ───────────────────────────────────────────

        /// <summary>
        /// Выкидывает груз контейнерами в космос, пока корабль перегружен (приоритет — см. шапку класса).
        /// Возвращает число выброшенных контейнеров. star можно не передавать — возьмётся по CurrentStarUid.
        /// </summary>
        public static int ResolveOverload(ShipData ship, StarData star = null)
        {
            if (ship == null || ship.Inventory == null) return 0;
            int cap = EquipmentSystem.GetHullCapacity(ship);
            if (cap <= 0 || CurrentWeight(ship) <= cap) return 0;

            star ??= ship.CurrentStar ?? FindStar(ship.CurrentStarUid);
            if (star == null) return 0;

            int dropped = 0;

            // 1) Стаки — частично, ровно на величину перегруза; дешёвые за единицу первыми.
            if (ship.Inventory.Stacks.Count > 0)
            {
                var stacks = new List<ItemStack>(ship.Inventory.Stacks.Values);
                stacks.Sort((a, b) => a.BasePrice.CompareTo(b.BasePrice));
                foreach (var s in stacks)
                {
                    int over = CurrentWeight(ship) - cap;
                    if (over <= 0) return dropped;
                    var taken = ship.Inventory.TakeStack(s.ItemId, over);
                    if (taken == null || taken.TotalWeight <= 0) continue;
                    ContainerFactory.SpawnContainerWithStack(ship, taken, star);
                    dropped++;
                    GameLog.Add(
                        $"[Перегруз] {ship.Name} выбрасывает {taken.Name ?? taken.ItemId} ({taken.TotalWeight} ед.).");
                }
            }

            // 2) Предметы трюма (оборудование и прочее, кроме теневых стеков) — дешёвые первыми.
            if (CurrentWeight(ship) > cap)
            {
                var items = new List<ItemInstance>();
                foreach (var it in ship.Inventory.Items)
                {
                    if (it == null) continue;
                    if (it.Uid != null && it.Uid.StartsWith(ShipInventory.StackItemUidPrefix, StringComparison.Ordinal))
                        continue;
                    items.Add(it);
                }
                items.Sort((a, b) => a.Price.CompareTo(b.Price));
                foreach (var it in items)
                {
                    if (CurrentWeight(ship) <= cap) return dropped;
                    InventoryService.RemoveCompletely(ship, it);
                    ContainerFactory.SpawnContainerWithItem(ship, it, star);
                    dropped++;
                    GameLog.Add($"[Перегруз] {ship.Name} выбрасывает {it.Name}.");
                }
            }

            // 3) Установленное оборудование — только если больше нечего выбрасывать.
            //    Дешёвое некритичное первым; Engine/FuelTank в конец (без них корабль встанет навсегда),
            //    Hull не снимается никогда.
            if (CurrentWeight(ship) > cap && ship.Equipment != null)
            {
                var slots = new List<(string key, ItemInstance item, bool critical)>();
                foreach (var kv in ship.Equipment.Slots)
                {
                    if (kv.Value == null) continue;
                    string cat = EquipmentSystem.SlotCategory(kv.Key);
                    if (cat == EquipmentCategory.Hull) continue;
                    if (!ship.AllItems.TryGetValue(kv.Value, out var it) || it == null) continue;
                    bool critical = cat == EquipmentCategory.Engine || cat == EquipmentCategory.FuelTank;
                    slots.Add((kv.Key, it, critical));
                }
                slots.Sort((a, b) => a.critical != b.critical
                    ? a.critical.CompareTo(b.critical)
                    : a.item.Price.CompareTo(b.item.Price));
                foreach (var (key, _, _) in slots)
                {
                    if (CurrentWeight(ship) <= cap) break;
                    var removed = InventoryService.UnequipFromSlot(ship, key);
                    if (removed == null) continue;
                    ContainerFactory.SpawnContainerWithItem(ship, removed, star);
                    dropped++;
                    GameLog.Add($"[Перегруз] {ship.Name} снимает и выбрасывает {removed.Name}.");
                }
            }
            return dropped;
        }

        private static int CurrentWeight(ShipData ship)
            => EquipmentSystem.GetEquippedWeight(ship) + ship.Inventory.TotalWeight();

        private static StarData FindStar(string starUid)
        {
            if (string.IsNullOrEmpty(starUid)) return null;
            var map = GameWorld.GeneratedGalaxy?.StarsMap;
            return map != null && map.TryGetValue(starUid, out var star) ? star : null;
        }
    }
}
