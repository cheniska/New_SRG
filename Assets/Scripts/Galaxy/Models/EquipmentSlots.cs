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
    public class EquipmentSlots
    {
        public Dictionary<string, string> Slots { get; set; } = new();

        public string GetItemUid(string slotKey) =>
            Slots.TryGetValue(slotKey, out var uid) ? uid : null;

        public bool IsOccupied(string slotKey) =>
            Slots.TryGetValue(slotKey, out var uid) && uid != null;

        public List<string> GetOccupiedSlotsOfCategory(string category)
        {
            var result = new List<string>();
            foreach (var kv in Slots)
                if (kv.Value != null && kv.Key.StartsWith(category + "_", StringComparison.Ordinal))
                    result.Add(kv.Key);
            return result;
        }

        public IEnumerable<string> GetAllSlotsOfCategory(string category)
        {
            foreach (var kv in Slots)
                if (kv.Key.StartsWith(category + "_", StringComparison.Ordinal))
                    yield return kv.Key;
        }

        public string Set(string slotKey, string itemUid)
        {
            Slots.TryGetValue(slotKey, out var prev);
            Slots[slotKey] = itemUid;
            return prev;
        }

        public string Clear(string slotKey)
        {
            Slots.TryGetValue(slotKey, out var prev);
            Slots[slotKey] = null;
            return prev;
        }

        /// <summary>
        /// Перестраивает слоты под новый корпус. <paramref name="slotPlan"/> — итоговое число слотов
        /// по категориям (HullSlotsHullType + HullSlotsRace, либо HullSeries.Slots перебивает).
        /// Возвращает uid'ы предметов, вытесненных из исчезнувших слотов.
        /// </summary>
        public List<string> RebuildForHullSlots(Dictionary<string, int> slotPlan, ItemsConfig equipConfig)
        {
            var evicted = new List<string>();
            var newSlots = new Dictionary<string, string>();

            if (Slots.TryGetValue(SlotKeys.Hull, out var hullUid))
                newSlots[SlotKeys.Hull] = hullUid;

            if (slotPlan != null)
            {
                foreach (var kv in slotPlan)
                {
                    if (kv.Value <= 0) continue;
                    int maxGlobal = equipConfig?.GetCategoryCommon(kv.Key)?.MaxSlots ?? 1;
                    int count = Mathf.Min(kv.Value, maxGlobal);
                    for (int i = 0; i < count; i++)
                    {
                        string key = $"{kv.Key}_{i}";
                        newSlots[key] = Slots.TryGetValue(key, out var uid) ? uid : null;
                    }
                }
            }

            foreach (var kv in Slots)
                if (kv.Value != null && !newSlots.ContainsKey(kv.Key))
                    evicted.Add(kv.Value);

            Slots = newSlots;
            return evicted;
        }

        public void InitForHullSlots(Dictionary<string, int> slotPlan, ItemsConfig equipConfig)
        {
            Slots.Clear();
            if (slotPlan == null) return;

            foreach (var kv in slotPlan)
            {
                if (kv.Value <= 0) continue;
                int maxGlobal = equipConfig?.GetCategoryCommon(kv.Key)?.MaxSlots ?? 1;
                int count = Mathf.Min(kv.Value, maxGlobal);
                for (int i = 0; i < count; i++)
                    Slots[$"{kv.Key}_{i}"] = null;
            }
        }
    }
}
