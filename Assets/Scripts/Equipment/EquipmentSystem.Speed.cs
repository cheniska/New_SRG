using UnityEngine;
using SRG.Combat;
using SRG.Galaxy;

namespace SRG.Equipment
{
    public static partial class EquipmentSystem
    {
        /// <summary>Фиксированная скорость для сломанного двигателя (без веса/форсажа/эффектов).</summary>
        private const float BrokenEngineSpeed = 100f;

        /// <summary>
        /// Скорость корабля с учётом буксира/абордажа: если корабль на буксире (TowedByUid) или
        /// абордирован (BoardedByUid) — двигается со скоростью носителя.
        /// </summary>
        public static float CalculateSpeed(ShipData ship)
        {
            string linkedUid = !string.IsNullOrEmpty(ship.BoardedByUid) ? ship.BoardedByUid
                             : !string.IsNullOrEmpty(ship.TowedByUid)   ? ship.TowedByUid
                             : null;
            if (linkedUid != null)
            {
                var carrier = FindShipByUid(linkedUid, ship.CurrentStarUid);
                if (carrier != null && carrier != ship)
                    return CalculateSpeed(carrier);
            }
            return CalculateOwnEngineSpeed(ship);
        }

        /// <summary>
        /// Скорость по собственному двигателю, без учёта буксира/абордажа.
        /// Нужна для cap'а «settle» (плавный подход к якорю) и фазы Pull — там кораблю
        /// важна его собственная подвижность, а не унаследованная от носителя.
        /// </summary>
        public static float CalculateOwnEngineSpeed(ShipData ship)
        {
            // Базовая скорость двигателя. Возвращаемые ветви:
            //   <= 0          — корабль вообще не летит (нет engine/fuel slot/Speed param/etc.) → 0.
            //   == BrokenEngineSpeed (=100) — сломанный двигатель, финальная аварийная скорость.
            //   > 0           — нормальный engineSpeed (уже с форсажем, если активен).
            float engineSpeed = ComputeEngineBaseSpeed(ship);
            if (engineSpeed <= 0f) return 0f;
            if (Mathf.Approximately(engineSpeed, BrokenEngineSpeed)) return BrokenEngineSpeed;

            int ownWeight = GetEquippedWeight(ship) + ship.Inventory.TotalWeight();
            int hullCapacity = GetHullCapacity(ship);

            // Собственный перегруз — корабль не летит вообще.
            if (hullCapacity > 0 && ownWeight > hullCapacity) return 0f;

            int totalWeight = ownWeight + SumTowedSubtreeWeightIfRoot(ship);
            // Антигравитатор и другие корпусные модификаторы массы. Bonus "Hull.MassMult"
            // — дельта от 1.0 (например -0.25 → эффективная масса 75%).
            float massMult = 1f + GetHullParam(ship, "MassMult");
            if (massMult < 0.05f) massMult = 0.05f;
            if (!Mathf.Approximately(massMult, 1f))
                totalWeight = Mathf.RoundToInt(totalWeight * massMult);
            float pct = ComputeLoadFactor(totalWeight, hullCapacity);

            float speed = engineSpeed * pct;
            speed *= CollectSpeedMult(ship);
            speed *= WeaponSystem.GetSpeedMultiplierFromEffects(ship);
            return Mathf.Max(0f, speed);
        }

        /// <summary>Базовая скорость двигателя с учётом наличия слотов, сломанности и форсажа.</summary>
        private static float ComputeEngineBaseSpeed(ShipData ship)
        {
            if (ship.Equipment.Slots.ContainsKey(SlotKeys.Engine) && GetEquipped(ship, SlotKeys.Engine) == null)
                return 0f;
            if (ship.Equipment.Slots.ContainsKey(SlotKeys.FuelTank) && GetEquipped(ship, SlotKeys.FuelTank) == null)
                return 0f;

            var engine = GetEquipped(ship, SlotKeys.Engine);
            if (engine == null) return 0f;
            if (!engine.IsWorking) return BrokenEngineSpeed;

            float engineSpeed = engine.GetParam("Speed", 0f);
            if (engineSpeed <= 0f)
            {
                Debug.LogWarning($"[EquipmentSystem] Ship '{ship.Name}': engine '{engine.Name}' has no Speed param. Speed=0.");
                return 0f;
            }

            if (ship.ForsageActive)
            {
                var forsage = GetEquipped(ship, SlotKeys.Forsage);
                if (forsage != null && forsage.IsWorking)
                    engineSpeed *= forsage.GetParam("SpeedMult", 2f);
            }
            return engineSpeed;
        }

        /// <summary>Суммарный вес буксируемых, если этот корабль — корень дерева буксира.
        /// Если он сам кого-то буксирует — 0 (вес считается на корне дерева).</summary>
        private static int SumTowedSubtreeWeightIfRoot(ShipData ship)
        {
            if (ship.TowedObjectUids == null || ship.TowedObjectUids.Count == 0) return 0;
            if (!string.IsNullOrEmpty(ship.TowedByUid)) return 0;     // сам на буксире — не корень
            return SumTowedSubtreeWeight(ship);
        }

        /// <summary>Коэффициент скорости от загрузки. С capacity — линейный lerp от 1.0 (пусто) до
        /// 0.33 (полный объём), клампится в [0.1, 1.0]. Без capacity — формула «без корпуса»
        /// (max 0.1, 1.22333 - 0.00045 * totalWeight). Магические числа сохранены без изменений.</summary>
        private static float ComputeLoadFactor(int totalWeight, int hullCapacity)
        {
            if (hullCapacity > 0)
                return Mathf.Clamp(Mathf.Lerp(1.0f, 0.33f, (float)totalWeight / hullCapacity), 0.1f, 1.0f);
            return Mathf.Max(0.1f, 1.22333f - 0.00045f * totalWeight);
        }

        private static float CollectSpeedMult(ShipData ship)
        {
            float mult = 1f;
            foreach (var kv in ship.Equipment.GetOccupiedSlotsOfCategory(EquipmentCategory.Artefacts))
            {
                string uid = ship.Equipment.GetItemUid(kv);
                if (uid != null && ship.AllItems.TryGetValue(uid, out var art) && art.IsWorking)
                    mult *= art.GetParam("SpeedMult", 1f);
            }
            return mult;
        }
    }
}
