using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Core;
using SRG.Dialog;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.NpcAI.Orders;

namespace SRG.Ships.Services
{
    /// <summary>Причина отказа/успеха при попытке ограбить груз.</summary>
    public enum CargoRobRefusalReason
    {
        Accepted = 0,
        LongDistance,   // угроза издалека — не сработает
        NoCargo,        // в трюме пусто (ни Items, ни Stacks)
        ShipRefuses,    // Warrior / Ranger / Pirate / Dominator — груз не отдаёт
        WeAlreadyHavePact, // мирное соглашение
    }

    /// <summary>Результат Preview/TryRob.
    /// Accepted → Weight = суммарный вес сброшенного груза; Containers = сколько контейнеров создано;
    /// ContainerUids — UID созданных контейнеров (для последующего Pickup-этапа AI).</summary>
    public struct CargoRobResult
    {
        public CargoRobRefusalReason Reason;
        public int Weight;
        public int Containers;
        public List<string> ContainerUids;
        public bool Accepted => Reason == CargoRobRefusalReason.Accepted;

        public static CargoRobResult Ok(int weight, int containers, List<string> uids) => new()
        { Reason = CargoRobRefusalReason.Accepted, Weight = weight, Containers = containers, ContainerUids = uids };
        public static CargoRobResult Refuse(CargoRobRefusalReason r) => new() { Reason = r };
    }

    /// <summary>
    /// Грабёж трюма NPC-корабля: цель сбрасывает часть предметов (стеки товаров + единичные
    /// ItemInstance — оборудование, юзлес, микромодули) в виде контейнеров рядом с собой.
    /// Грабитель (игрок или NPC-пират) собирает контейнеры через <see cref="SRG.Combat.PickupSystem"/>.
    ///
    /// Формула: доля <see cref="DialogTuning.CargoRobFraction"/> от суммарного веса КАЖДОГО стека
    /// уходит в контейнер (округление вверх, минимум 1). Из <c>Items</c> изымается та же доля от
    /// количества (округлённо), контейнер на каждый предмет. Оставшееся у цели.
    /// Побочные эффекты (если хоть что-то удалось изъять): отношение цели → грабителю Bad,
    /// +CrimeRating грабителю. При отказе — вызов стороны решает как реагировать (обычно
    /// LowerToLevel(Hostile) + PursueAndAttack, см. ActionRob).
    /// </summary>
    public static class CargoRobberyService
    {
        public static CargoRobResult Preview(ShipData target, ShipData robber)
        {
            if (target == null || robber == null) return CargoRobResult.Refuse(CargoRobRefusalReason.ShipRefuses);
            var tuning = DialogTuning.Current;

            if (ShipUtils.IsHardTargetForCriminalAct(target)) return CargoRobResult.Refuse(CargoRobRefusalReason.ShipRefuses);

            int rel = Relations.Get(target, robber);
            if (rel >= OwnerRaceRelationsManager.NORMAL_MAX + 1)
                return CargoRobResult.Refuse(CargoRobRefusalReason.WeAlreadyHavePact);

            float distSq = (target.Position - robber.Position).sqrMagnitude;
            float longD = Mathf.Max(1f, tuning?.RobberyLongDistance ?? 400f);
            if (distSq > longD * longD)
                return CargoRobResult.Refuse(CargoRobRefusalReason.LongDistance);

            // Пусто, если нет НИ стеков, НИ единичных предметов, кроме теневых из стеков.
            if (!HasAnyRobbableCargo(target))
                return CargoRobResult.Refuse(CargoRobRefusalReason.NoCargo);

            return CargoRobResult.Ok(0, 0, null); // фактические числа/UID считаются в TryRob.
        }

        /// <summary>Сбрасывает часть трюма цели контейнерами рядом с ней. Обрабатывает и Items
        /// (единичные предметы), и Stacks (товары). Возвращает список UID созданных контейнеров.
        /// Исполнение — через <see cref="OrderTransfer"/> в режиме SpawnContainers: сам сброс
        /// живёт в Order (жертва «выкидывает» груз), сервис только считает вес/эффекты.</summary>
        public static CargoRobResult TryRob(ShipData target, ShipData robber)
        {
            var preview = Preview(target, robber);
            if (!preview.Accepted) return preview;

            var tuning = DialogTuning.Current;
            float frac = Mathf.Clamp(tuning?.CargoRobFraction ?? 0.5f, 0.05f, 1f);
            var star = target.CurrentStar
                       ?? GalaxyManager.Instance?.GeneratedGalaxy?.StarsMap[target.CurrentStarUid];
            if (star == null) return CargoRobResult.Refuse(CargoRobRefusalReason.NoCargo);

            // Собираем списки, которые жертва «выкинет»: единичные предметы и id стеков.
            var itemUids = new List<string>();
            if (target.Inventory?.Items != null && target.Inventory.Items.Count > 0)
            {
                var allReal = new List<string>();
                foreach (var it in target.Inventory.Items)
                {
                    if (it == null || string.IsNullOrEmpty(it.Uid)) continue;
                    if (it.Uid.StartsWith(ShipInventory.StackItemUidPrefix, System.StringComparison.Ordinal)) continue;
                    allReal.Add(it.Uid);
                }
                int takeCount = Mathf.Max(1, Mathf.RoundToInt(allReal.Count * frac));
                takeCount = Mathf.Min(takeCount, allReal.Count);
                for (int i = 0; i < takeCount; i++) itemUids.Add(allReal[i]);
            }

            var stackIds = new List<string>();
            if (target.Inventory?.Stacks != null)
                foreach (var kv in target.Inventory.Stacks)
                    if (kv.Value != null && kv.Value.TotalWeight > 0) stackIds.Add(kv.Key);

            // Вес до сброса — чтобы посчитать сколько ушло (для CrimeRating и Result.Weight).
            int weightBefore = (target.Inventory?.TotalWeight() ?? 0)
                             + (target.Inventory?.TotalStacksWeight() ?? 0);

            // Один тик: жертва скидывает.
            var order = new OrderTransfer(robber.Uid, itemUids, stackIds, frac);
            order.Execute(target, star, GalaxyManager.Instance?.Context);
            var containerUids = order.LastSpawnedContainerUids ?? new List<string>();

            if (containerUids.Count == 0)
                return CargoRobResult.Refuse(CargoRobRefusalReason.NoCargo);

            int weightAfter = (target.Inventory?.TotalWeight() ?? 0)
                            + (target.Inventory?.TotalStacksWeight() ?? 0);
            int droppedWeight = Mathf.Max(0, weightBefore - weightAfter);

            Relations.LowerToLevel(target, robber, RelationLevel.Bad);

            float crimeGain = Mathf.Max(0f, tuning?.ExtortionCrimePerHundred ?? 0.5f);
            robber.CrimeRating += crimeGain * (droppedWeight / 10f); // 10 ед. груза ~ 100 кр. по «духу».

            return CargoRobResult.Ok(droppedWeight, containerUids.Count, containerUids);
        }

        /// <summary>Есть ли в трюме что-то, что можно ограбить (стеки ИЛИ реальные Items —
        /// теневые из стеков не считаются).</summary>
        public static bool HasAnyRobbableCargo(ShipData target)
        {
            if (target?.Inventory == null) return false;
            if (target.Inventory.Stacks != null)
                foreach (var s in target.Inventory.Stacks.Values)
                    if (s != null && s.TotalWeight > 0) return true;
            if (target.Inventory.Items != null)
                foreach (var it in target.Inventory.Items)
                {
                    if (it == null || string.IsNullOrEmpty(it.Uid)) continue;
                    if (it.Uid.StartsWith(ShipInventory.StackItemUidPrefix, System.StringComparison.Ordinal)) continue;
                    return true;
                }
            return false;
        }
    }
}
