using System.Collections.Generic;
using SRG.Config;
using SRG.Galaxy;

namespace SRG.Equipment
{
    /// <summary>
    /// Сверка классификационных флагов предметов сохранённого мира с текущим конфигом.
    /// Флаг <see cref="ItemInstance.IsEquipment"/> копируется в экземпляр при создании и
    /// сохраняется в сейв; если конфиг его исправил (рукописное оружие/артефакты раньше
    /// создавались с false), старые экземпляры получают верное значение при загрузке.
    /// Обходит корабли (слоты, трюм, магазины станций), магазины планет и встроенные модули.
    /// </summary>
    public static class ItemFlagsReconciler
    {
        /// <summary>Возвращает число исправленных экземпляров.</summary>
        public static int Apply(GalaxyData galaxy, ItemsConfig config)
        {
            if (galaxy == null || config == null) return 0;
            int fixedCount = 0;
            foreach (var star in galaxy.StarsMap.Values)
            {
                foreach (var planet in star.Planets)
                    fixedCount += FixAll(planet?.Settlement?.EquipmentShop?.Items?.Values, config);
                foreach (var ship in star.Ships)
                {
                    if (ship == null) continue;
                    fixedCount += FixAll(ship.AllItems?.Values, config);
                    fixedCount += FixAll(ship.Inventory?.Items, config);
                    fixedCount += FixAll(ship.Settlement?.EquipmentShop?.Items?.Values, config);
                }
            }
            return fixedCount;
        }

        private static int FixAll(IEnumerable<ItemInstance> items, ItemsConfig config)
        {
            if (items == null) return 0;
            int n = 0;
            foreach (var item in items)
                n += Fix(item, config);
            return n;
        }

        private static int Fix(ItemInstance item, ItemsConfig config)
        {
            if (item == null) return 0;
            int n = FixAll(item.EmbedItems?.Values, config);
            if (!item.IsEquipment && config.GetItem(item.Category, item.ItemId)?.IsEquipment == true)
            {
                item.IsEquipment = true;
                n++;
            }
            return n;
        }
    }
}
