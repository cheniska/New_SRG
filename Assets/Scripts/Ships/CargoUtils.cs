using System.Collections.Generic;
using SRG.Galaxy;
using SRG.NpcAI;
using SRG.NpcAI.Actions;

namespace SRG.Ships
{
    /// <summary>
    /// Утилиты работы с грузом (товарами, категория «Goods») в трюме корабля.
    /// Используются и AI (<see cref="TraderAI"/>, <c>ActionGoodsTrader</c>), и системой диалогов
    /// (тег-резолверы <c>{cargo_*}</c>, условия <c>has_cargo</c>/<c>has_cargo:goodId</c>).
    /// Единственный источник правды о наличии/количестве груза — <see cref="ShipInventory.Stacks"/>.
    /// </summary>
    public static class CargoUtils
    {
        public const string CargoCategory = "Goods";

        /// <summary>Есть ли в трюме корабля вообще какой-либо товар.</summary>
        public static bool HasAnyCargo(ShipData ship)
        {
            var stacks = ship?.Inventory?.Stacks;
            if (stacks == null || stacks.Count == 0) return false;
            foreach (var s in stacks.Values)
                if (s != null && s.TotalWeight > 0 && IsGoods(s.Category)) return true;
            return false;
        }

        /// <summary>Есть ли в трюме корабля товар с указанным id (например, "grain"). Пустой id → false.</summary>
        public static bool HasCargoOfType(ShipData ship, string goodId)
        {
            if (string.IsNullOrEmpty(goodId)) return false;
            return GetCargoAmount(ship, goodId) > 0;
        }

        /// <summary>Сколько единиц товара указанного типа в трюме. 0 — нет товара / нет корабля.</summary>
        public static int GetCargoAmount(ShipData ship, string goodId)
        {
            if (string.IsNullOrEmpty(goodId)) return 0;
            var stacks = ship?.Inventory?.Stacks;
            if (stacks == null) return 0;
            if (!stacks.TryGetValue(goodId, out var s) || s == null) return 0;
            if (!IsGoods(s.Category)) return 0;
            return s.TotalWeight > 0 ? s.TotalWeight : 0;
        }

        /// <summary>Перечисляет все стеки товаров корабля. Возвращает пустой список, если трюм пуст.</summary>
        public static List<(string goodId, int amount)> GetAllCargo(ShipData ship)
        {
            var result = new List<(string, int)>();
            var stacks = ship?.Inventory?.Stacks;
            if (stacks == null) return result;
            foreach (var s in stacks.Values)
            {
                if (s == null || s.TotalWeight <= 0) continue;
                if (!IsGoods(s.Category)) continue;
                result.Add((s.ItemId, s.TotalWeight));
            }
            return result;
        }

        /// <summary>Самый объёмный стек товара (по TotalWeight). Используется тегом <c>{cargo_id}</c>/<c>{cargo_amount}</c>
        /// для краткого «лечу с тем-то». При пустом трюме возвращает (null, 0).</summary>
        public static (string goodId, int amount) GetLargestCargo(ShipData ship)
        {
            string bestId = null;
            int bestAmount = 0;
            var stacks = ship?.Inventory?.Stacks;
            if (stacks == null) return (null, 0);
            foreach (var s in stacks.Values)
            {
                if (s == null || s.TotalWeight <= 0) continue;
                if (!IsGoods(s.Category)) continue;
                if (s.TotalWeight > bestAmount)
                {
                    bestAmount = s.TotalWeight;
                    bestId = s.ItemId;
                }
            }
            return (bestId, bestAmount);
        }

        private static bool IsGoods(string category) =>
            string.IsNullOrEmpty(category) || category == CargoCategory;
    }
}
