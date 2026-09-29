using UnityEngine;
using SRG.Core;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy;

namespace SRG.Ships.Services
{
    /// <summary>
    /// Дозарядка ракетного оружия (ракеты/торпеды) у планеты. Энергооружие имеет
    /// бесконечный боезапас (Ammo = -1) и здесь игнорируется. Максимум боезапаса —
    /// это <c>Params["MaxAmmo"]</c>, проставляемый при создании предмета из шаблона
    /// (<see cref="ItemInstance.FromConfig"/>). Цена — за один снаряд, масштабируется
    /// тиром орудия по той же формуле, что и ремонт (<see cref="RepairService.CostPerPoint"/>).
    /// </summary>
    public static class AmmoService
    {
        /// <summary>Базовая цена одного снаряда (кредитов) на ГТУ/тире 1.</summary>
        public const int CostPerRoundBase = 20;

        /// <summary>Множитель цены за тир: cost = ceil(base × (1 + TL × mult)).</summary>
        public const float TLMultiplier = 0.25f;

        public static int CostPerRound(int techLevel)
        {
            int tl = Mathf.Max(1, techLevel);
            return Mathf.Max(1, Mathf.CeilToInt(CostPerRoundBase * (1f + tl * TLMultiplier)));
        }

        /// <summary>Сколько снарядов не хватает конкретному орудию до полного боезапаса.</summary>
        public static int MissingAmmo(ItemInstance weapon)
        {
            if (weapon == null || weapon.Category != EquipmentCategory.Weapons) return 0;
            if (!weapon.Params.TryGetValue("MaxAmmo", out var max) || max <= 0f) return 0;
            float cur = weapon.GetParam("Ammo", max);
            int missing = Mathf.RoundToInt(max - cur);
            return Mathf.Max(0, missing);
        }

        /// <summary>Полная стоимость дозарядки всех ракетных орудий корабля до максимума.</summary>
        public static int EstimateReloadCost(ShipData ship)
        {
            if (ship?.Equipment?.Slots == null) return 0;
            int total = 0;
            foreach (var kv in ship.Equipment.Slots)
            {
                if (kv.Value == null || !ship.AllItems.TryGetValue(kv.Value, out var w)) continue;
                int missing = MissingAmmo(w);
                if (missing <= 0) continue;
                total += missing * CostPerRound(w.TechLevel);
            }
            return total;
        }

        /// <summary>true — на корабле есть ракетное оружие с конечным боезапасом.</summary>
        public static bool HasReloadableWeapons(ShipData ship)
        {
            if (ship?.Equipment?.Slots == null) return false;
            foreach (var kv in ship.Equipment.Slots)
            {
                if (kv.Value == null || !ship.AllItems.TryGetValue(kv.Value, out var w)) continue;
                if (w.Category == EquipmentCategory.Weapons
                    && w.Params.TryGetValue("MaxAmmo", out var max) && max > 0f)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Дозарядить всё ракетное оружие в пределах имеющихся кредитов. Списывает деньги,
        /// пишет в EconomicLog при загрузке &gt;0. Возвращает число заряженных снарядов.
        /// </summary>
        public static int ReloadAllAmmo(ShipData ship, ILandingSite site)
        {
            if (ship?.Equipment?.Slots == null || site == null) return 0;

            int budget = Mathf.Max(0, ship.Money);
            int loaded = 0;
            int spent = 0;

            foreach (var kv in ship.Equipment.Slots)
            {
                if (budget <= 0) break;
                if (kv.Value == null || !ship.AllItems.TryGetValue(kv.Value, out var w)) continue;
                int missing = MissingAmmo(w);
                if (missing <= 0) continue;

                int perRound = CostPerRound(w.TechLevel);
                int affordable = perRound > 0 ? budget / perRound : 0;
                int restore = Mathf.Min(missing, affordable);
                if (restore <= 0) continue;

                float cur = w.GetParam("Ammo", 0f);
                w.Params["Ammo"] = cur + restore;
                int cost = restore * perRound;
                budget -= cost;
                spent += cost;
                loaded += restore;
            }

            if (loaded <= 0) return 0;

            ship.Money -= spent;
            int turn = GalaxyManager.Instance?.GeneratedGalaxy?.CurrentTurn ?? 0;
            EconomicLog.Trade(turn, EconomicLog.Safe(ship.Name), "REARM",
                $"site={EconomicLog.Safe(site.Name)} rounds={loaded} spent={spent} money_after={ship.Money}");
            return loaded;
        }
    }
}
