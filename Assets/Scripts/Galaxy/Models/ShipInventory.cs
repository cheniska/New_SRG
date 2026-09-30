using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System;
using SRG.Combat;
using SRG.Config;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy.Generation;
using SRG.Galaxy.Politics;
using SRG.Galaxy.Simulation;
using SRG.NpcAI;
using SRG.NpcAI.Actions;
using SRG.NpcAI.Spawning;
using SRG.Science;
using SRG.Ships;
using SRG.Ships.Movement;
using SRG.Ships.Services;
using SRG.Simulation;

namespace SRG.Galaxy
{
    [Serializable]
    public class ShipInventory
    {
        public List<ItemInstance> Items { get; set; } = new();


        public Dictionary<string, ItemStack> Stacks { get; set; } = new();

        [JsonIgnore]
        private readonly Dictionary<string, ItemInstance> _index = new();
        public ItemInstance GetByUid(string uid) =>
            _index.TryGetValue(uid, out var item) ? item : null;

        public bool Contains(string uid) => _index.ContainsKey(uid);
        public int Count => Items.Count;
        public int TotalWeight()
        {
            // Стеки зеркалятся в Items как ItemInstance с тем же Weight, чтобы UI трюма видел груз
            // как обычные предметы. TotalWeight считаем только по Items — иначе будет двойной учёт.
            int w = 0;
            foreach (var item in Items) w += item.Weight;
            return w;
        }

        /// <summary>Сумма веса всех стеков (товары/минералы). Совпадает с суммой веса парных
        /// ItemInstance в <see cref="Items"/>; оставлен для совместимости с диагностикой.</summary>
        public int TotalStacksWeight()
        {
            int w = 0;
            foreach (var s in Stacks.Values) w += s.TotalWeight;
            return w;
        }

        public void Add(ItemInstance item)
        {
            if (item == null) return;
            Items.Add(item);
            _index[item.Uid] = item;
        }

        public bool Remove(string uid)
        {
            var item = GetByUid(uid);
            if (item == null) return false;
            Items.Remove(item);
            _index.Remove(uid);
            return true;
        }

        public ItemInstance TakeByUid(string uid)
        {
            var item = GetByUid(uid);
            if (item != null) Remove(uid);
            return item;
        }

        public void AddStack(ItemStack stack)
        {
            if (stack == null || string.IsNullOrEmpty(stack.ItemId)) return;

            if (Stacks.TryGetValue(stack.ItemId, out var existing))
            {
                existing.TotalWeight += stack.TotalWeight;
            }
            else
            {
                Stacks[stack.ItemId] = new ItemStack
                {
                    ItemId = stack.ItemId,
                    Category = stack.Category,
                    Name = stack.Name,
                    TotalWeight = stack.TotalWeight,
                    BasePrice = stack.BasePrice,
                    IsGoods = stack.IsGoods
                };
            }
            SyncStackItem(Stacks[stack.ItemId]);
        }

        public ItemStack TakeStack(string itemId, int amount)
        {
            if (!Stacks.TryGetValue(itemId, out var stack)) return null;

            int taken = Mathf.Min(amount, stack.TotalWeight);
            stack.TotalWeight -= taken;

            string snapId = stack.ItemId;
            string snapCategory = stack.Category;
            string snapName = stack.Name;
            int snapPrice = stack.BasePrice;
            bool snapIsGoods = stack.IsGoods;

            if (stack.TotalWeight <= 0)
            {
                Stacks.Remove(itemId);
                RemoveStackItem(itemId);
            }
            else
            {
                SyncStackItem(stack);
            }

            return new ItemStack
            {
                ItemId = snapId,
                Category = snapCategory,
                Name = snapName,
                TotalWeight = taken,
                BasePrice = snapPrice,
                IsGoods = snapIsGoods
            };
        }

        /// <summary>Префикс Uid для «теневых» ItemInstance, отражающих стеки в <see cref="Items"/>.
        /// Стабильный по ItemId — стек одного товара всегда соответствует одному ItemInstance.</summary>
        public const string StackItemUidPrefix = "stack:";

        /// <summary>Создать/обновить ItemInstance, отображающий стек в инвентаре как предмет.
        /// Weight = TotalWeight, Price = TotalWeight × BasePrice (полная стоимость кучи).</summary>
        private void SyncStackItem(ItemStack stack)
        {
            if (stack == null || string.IsNullOrEmpty(stack.ItemId)) return;
            string uid = StackItemUidPrefix + stack.ItemId;
            if (!_index.TryGetValue(uid, out var inst) || inst == null)
            {
                inst = new ItemInstance
                {
                    Uid = uid,
                    Category = stack.Category ?? "Goods",
                    ItemId = stack.ItemId,
                    Name = stack.Name ?? stack.ItemId,
                    NoWear = true,
                    Durability = 1,
                    MaxDurability = 1,
                    AllowModules = false,
                    TechLevel = 0,
                };
                Items.Add(inst);
                _index[uid] = inst;
            }
            inst.Weight = stack.TotalWeight;
            inst.Price = stack.TotalWeight * stack.BasePrice;
            inst.Name = stack.Name ?? stack.ItemId;
            // Иконка может зависеть от количества (GraphicSteps в ItemsConfig) —
            // пересчитываем при каждом изменении стека.
            inst.GraphicPath = StackGraphics.ResolveIcon(stack.ItemId, stack.TotalWeight);
        }

        private void RemoveStackItem(string itemId)
        {
            string uid = StackItemUidPrefix + itemId;
            if (!_index.TryGetValue(uid, out var inst) || inst == null) return;
            Items.Remove(inst);
            _index.Remove(uid);
        }
        public void RebuildIndex()
        {
            _index.Clear();
            foreach (var item in Items)
                if (item != null) _index[item.Uid] = item;

            // Реконсиляция Stacks → Items для старых сейвов: у каждого стека должен быть
            // теневой ItemInstance с Uid = "stack:<itemId>", чтобы UI трюма видел груз.
            if (Stacks != null)
                foreach (var s in Stacks.Values)
                    if (s != null && s.TotalWeight > 0) SyncStackItem(s);
        }
    }
}
