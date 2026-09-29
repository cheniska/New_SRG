using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Combat;
using SRG.Config;
using SRG.Galaxy;

namespace SRG.Equipment
{
    public static partial class EquipmentSystem
    {
        /// <summary>
        /// Списывает WearOnTurn у каждого слота: только пока корабль НЕ в доке.
        /// Уважает item.NoWear (per-item override отключает любой износ).
        /// </summary>
        public static void ApplyTurnWear(ShipData ship, ItemsConfig equipConfig)
        {
            if (ship.LandedPlanetUid != null) return;
            if (!string.IsNullOrEmpty(ship.LandedOnShipUid)) return; // пристыкован к носителю = в доке

            foreach (var kv in ship.Equipment.Slots)
            {
                if (kv.Value == null) continue;
                if (!ship.AllItems.TryGetValue(kv.Value, out var item)) continue;
                if (item.NoWear || item.MaxDurability <= 0) continue;

                string category = SlotCategory(kv.Key);
                var typeCfg = equipConfig.GetCategoryCommon(category);
                if (typeCfg == null || typeCfg.WearOnTurn <= 0) continue;

                item.Durability = Mathf.Max(0, item.Durability - typeCfg.WearOnTurn);
            }
        }

        public static void ApplyMovementWear(ShipData ship, ItemsConfig equipConfig)
        {
            float forsageMult = 1f;
            if (ship.ForsageActive)
            {
                var forsage = GetEquipped(ship, SlotKeys.Forsage);
                if (forsage != null && forsage.IsWorking)
                    forsageMult = forsage.GetParam("EngineDurabilityBurnMult", 2f);
            }

            foreach (var kv in ship.Equipment.Slots)
            {
                if (kv.Value == null) continue;
                if (!ship.AllItems.TryGetValue(kv.Value, out var item)) continue;
                if (item.NoWear || item.MaxDurability <= 0) continue;

                string category = SlotCategory(kv.Key);
                var typeCfg = equipConfig.GetCategoryCommon(category);
                if (typeCfg == null || typeCfg.WearOnMove <= 0) continue;

                float wear = typeCfg.WearOnMove;
                bool forsageOnEngine = category == EquipmentCategory.Engine && ship.ForsageActive;
                if (forsageOnEngine)
                    wear *= forsageMult;

                // Триггеры Wear (Обливионный коннектор — «износ двигателя при форсаже ×0.5»).
                // Причина Forsage выставляется только для износа двигателя при активном форсаже.
                var trigCtx = new TriggerContext
                {
                    Event = TriggerEvent.Wear,
                    Slot  = category,
                    Cause = forsageOnEngine ? "Forsage" : "Move",
                };
                TriggerBus.Fire(ship, trigCtx);
                if (!Mathf.Approximately(trigCtx.WearMult, 1f))
                    wear *= Mathf.Max(0f, trigCtx.WearMult);

                item.Durability = Mathf.Max(0, item.Durability - Mathf.CeilToInt(wear));
            }
        }

        public static void ApplyShieldHitWear(ShipData ship, ItemsConfig equipConfig)
        {
            var shield = GetEquipped(ship, SlotKeys.Shield);
            if (shield == null || !shield.IsWorking) return;

            var typeCfg = equipConfig.GetCategoryCommon(EquipmentCategory.Shield);
            if (typeCfg == null || typeCfg.WearOnHit <= 0) return;

            shield.Durability = Mathf.Max(0, shield.Durability - typeCfg.WearOnHit);
        }

        public static void ApplyWeaponShotWear(ShipData ship, string slotKey, ItemsConfig equipConfig)
        {
            string uid = ship.Equipment.GetItemUid(slotKey);
            if (uid == null || !ship.AllItems.TryGetValue(uid, out var weapon)) return;
            if (!weapon.IsWorking) return;

            var typeCfg = equipConfig.GetCategoryCommon(EquipmentCategory.Weapons);
            if (typeCfg == null || typeCfg.WearOnShot <= 0) return;

            weapon.Durability = Mathf.Max(0, weapon.Durability - typeCfg.WearOnShot);
        }

        public static void AccumulateDroidHeal(ItemInstance droid, float healAmount)
        {
            if (droid == null || !droid.IsWorking) return;
            float threshold = droid.GetParam("WearThreshold", 30f);
            if (threshold <= 0f) return;

            droid.HealAccumulator += healAmount;
            int wear = Mathf.FloorToInt(droid.HealAccumulator / threshold);
            if (wear <= 0) return;

            droid.HealAccumulator -= wear * threshold;
            droid.Durability = Mathf.Max(0, droid.Durability - wear);
        }

        public static void AccumulateCargoGrab(ItemInstance grabber, float cargoWeight)
        {
            if (grabber == null || !grabber.IsWorking) return;
            float threshold = grabber.GetParam("WearThreshold", 5000f);
            if (threshold <= 0f) return;

            grabber.CargoAccumulator += cargoWeight;
            int wear = Mathf.FloorToInt(grabber.CargoAccumulator / threshold);
            if (wear <= 0) return;

            grabber.CargoAccumulator -= wear * threshold;
            grabber.Durability = Mathf.Max(0, grabber.Durability - wear);
        }

        public static void ApplyTurnEffects(ShipData ship, ItemsConfig equipConfig, bool moved)
        {
            if (equipConfig != null)
                ApplyTurnWear(ship, equipConfig);

            if (moved && equipConfig != null)
                ApplyMovementWear(ship, equipConfig);

            foreach (var slotKey in ship.Equipment.GetOccupiedSlotsOfCategory(EquipmentCategory.Droid))
            {
                string uid = ship.Equipment.GetItemUid(slotKey);
                if (uid == null || !ship.AllItems.TryGetValue(uid, out var droid)) continue;
                if (!droid.IsWorking) continue;

                float heal = droid.GetParam("HealPerTurn", 0f);
                if (heal > 0)
                {
                    int before = ship.CurrentHull;
                    ship.CurrentHull = Mathf.Min(ship.MaxHull, ship.CurrentHull + Mathf.RoundToInt(heal));
                    float actualHeal = ship.CurrentHull - before;
                    if (actualHeal > 0)
                        AccumulateDroidHeal(droid, actualHeal);
                }
            }

            foreach (var slotKey in ship.Equipment.GetOccupiedSlotsOfCategory(EquipmentCategory.Artefacts))
            {
                string uid = ship.Equipment.GetItemUid(slotKey);
                if (uid == null || !ship.AllItems.TryGetValue(uid, out var art)) continue;
                if (!art.IsWorking) continue;

                float heal = art.GetParam("HealPerTurn", 0f);
                if (heal > 0)
                    ship.CurrentHull = Mathf.Min(ship.MaxHull, ship.CurrentHull + Mathf.RoundToInt(heal));
            }

            // TurnCode/TurnScript больше НЕ вызываются здесь: они переехали в цикл сабтёрнов
            // StarSimulator.SimulateTurn (см. ArtefactTurnRegistry.RunForStar). Дефолтный
            // сабтёрн срабатывания — 1-й (начало хода).

            WeaponSystem.RegenerateShield(ship);
            WeaponSystem.TickEffects(ship);
            ship.ForsageActive = false;
        }

        public static void ApplyDamage(
            ShipData ship,
            int rawDamage,
            string damageType,
            int armorPierce,
            float equipHitChance,
            int equipDamage,
            ItemsConfig equipConfig)
        {
            float vuln = GetHullVulnerability(ship, damageType);
            float effective = rawDamage * vuln;

            float afterShield = effective;
            var shield = GetEquipped(ship, SlotKeys.Shield);
            if (shield != null && shield.IsWorking)
            {
                float block = shield.GetParam("BlockPercent", 0f) / 100f;
                afterShield = effective * (1f - block);
                if (equipConfig != null)
                    ApplyShieldHitWear(ship, equipConfig);
            }

            int armor = Mathf.RoundToInt(GetHullParam(ship, "Armor"));
            float afterArmor = Mathf.Max(0f, afterShield - Mathf.Max(0f, armor - armorPierce));

            ship.CurrentHull = Mathf.Max(0, ship.CurrentHull - Mathf.RoundToInt(afterArmor));

            if (equipDamage > 0 && Random.value < equipHitChance)
            {
                var picked = GetRandomDamageableSlot(ship, equipConfig);
                if (picked.item != null)
                    picked.item.Durability = Mathf.Max(0, picked.item.Durability - equipDamage);
            }
        }

        private static readonly List<(ItemInstance item, string slotKey)> _damageableCandidates = new();

        /// <summary>
        /// Случайный рабочий предмет на корабле из категории, помеченной как CanBeDamagedByWeapon.
        /// Возвращает (item, slotKey) либо (null, null). Единая точка для урона по оборудованию —
        /// используется в EquipmentSystem.ApplyDamage и WeaponSystem.ProcessShot/ApplyMissileImpact.
        /// Внутренний буфер переиспользуется между вызовами (single-threaded Unity).
        /// </summary>
        internal static (ItemInstance item, string slotKey) GetRandomDamageableSlot(ShipData ship, ItemsConfig equipConfig)
        {
            _damageableCandidates.Clear();
            if (ship?.Equipment == null) return (null, null);

            foreach (var kv in ship.Equipment.Slots)
            {
                if (kv.Value == null) continue;
                if (!ship.AllItems.TryGetValue(kv.Value, out var item)) continue;
                if (!item.IsWorking) continue;

                string cat = SlotCategory(kv.Key);
                var typeCfg = equipConfig?.GetCategoryCommon(cat);
                if (typeCfg != null && typeCfg.CanBeDamagedByWeapon)
                    _damageableCandidates.Add((item, kv.Key));
            }

            if (_damageableCandidates.Count == 0) return (null, null);
            return _damageableCandidates[Random.Range(0, _damageableCandidates.Count)];
        }
    }
}
