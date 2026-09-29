using UnityEngine;
using SRG.Config;
using SRG.Galaxy;

namespace SRG.Equipment
{
    /// <summary>
    /// Собирает <see cref="ItemInstance"/> из записи <see cref="ItemConfig"/>.
    /// Микромодуль — не оборудование: IsEquipment=false, IsGoods=false, Category="MicroModule",
    /// IsEmbeddable=true. Устанавливается через <see cref="EmbedService"/> в носителя,
    /// у которого AllowEmbeds=true.
    /// </summary>
    public static class MicroModuleFactory
    {
        /// <summary>Категория экземпляра микромодуля в <see cref="ItemInstance.Category"/>.
        /// Не входит в <see cref="EquipmentCategory.DisplayOrder"/> — UI показывает
        /// микромодули отдельным разделом (аналогично находкам).</summary>
        public const string CategoryKey = "MicroModule";

        public static ItemInstance Create(string id, ItemsConfig itemsConfig)
        {
            if (itemsConfig == null || string.IsNullOrEmpty(id))
            {
                Debug.LogError($"[MicroModuleFactory] Bad args: itemsConfig={itemsConfig}, id={id}");
                return null;
            }
            var cfg = itemsConfig.GetMicroModule(id);
            if (cfg == null)
            {
                Debug.LogError($"[MicroModuleFactory] MicroModule '{id}' not found in ItemsConfig.");
                return null;
            }

            var inst = new ItemInstance
            {
                Category     = CategoryKey,
                ItemId       = id,
                Name         = cfg.Name ?? id,
                Description  = cfg.Description,
                Weight       = cfg.Weight,
                Price        = cfg.BasePrice,
                GraphicPath  = cfg.Icon,
                IsEquipment  = false,
                IsEmbeddable = true,
                Embed        = cfg.Embed ?? new EmbedConfig(),
                MaxEmbeds    = 0,
                AllowEmbeds  = false,
                MaxDurability = 1,
                Durability   = 1,
                NoWear       = true,
                TechLevel    = 1,
            };
            return inst;
        }
    }
}
