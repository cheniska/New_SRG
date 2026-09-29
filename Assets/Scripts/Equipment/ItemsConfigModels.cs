using System.Collections.Generic;
using Newtonsoft.Json;

namespace SRG.Equipment
{
    /// <summary>
    /// Правила появления встраиваемого/находимого предмета в мире. См.
    /// docs/modules/micromodules_design.md §9. Используется для микромодулей и артефактов —
    /// любых предметов, у которых есть <see cref="SRG.Config.ItemConfig.Sources"/>.
    /// </summary>
    public class ItemSources
    {
        [JsonProperty("NpcDrop")] public ItemNpcDrop NpcDrop { get; set; }
        [JsonProperty("Shop")]    public ItemShopSource Shop { get; set; }
        /// <summary>Появляется ли в очереди центра рейнджеров. По умолчанию true.</summary>
        [JsonProperty("RangerCenter")] public bool RangerCenter { get; set; } = true;
    }

    public class ItemNpcDrop
    {
        /// <summary>Базовый шанс дропа при уничтожении корабля указанной стороны.
        /// Ключ — Owner/Side убитого NPC (Coalition/Dominators/Pirates/…).</summary>
        [JsonProperty("SideChance")] public Dictionary<string, float> SideChance { get; set; } = new();
        /// <summary>Базовый шанс дропа при уничтожении корабля указанной расы.
        /// Ключ — Race убитого NPC.</summary>
        [JsonProperty("RaceChance")] public Dictionary<string, float> RaceChance { get; set; } = new();
        /// <summary>Разрешённое окно ГТУ [min, max]. 0/0 — без ограничений.</summary>
        [JsonProperty("GtlWindow")]  public int[] GtlWindow { get; set; }
    }

    public class ItemShopSource
    {
        [JsonProperty("RaceWhitelist")] public List<string> RaceWhitelist { get; set; } = new();
        [JsonProperty("SideWhitelist")] public List<string> SideWhitelist { get; set; } = new();
        [JsonProperty("MinPtu")]        public int          MinPtu        { get; set; } = 1;
    }

    /// <summary>
    /// Ступень графики стакабельного предмета: начиная с <see cref="MinAmount"/> единиц стек
    /// показывается с этой инвентарной иконкой (<see cref="Icon"/>) и/или этим космическим
    /// спрайтшитом (<see cref="Sprite"/>). Общая механика «картинка зависит от количества»:
    /// применяется и к инвентарю (теневой ItemInstance стека), и к дропу в космосе.
    /// Резолв — <see cref="StackGraphics"/>.
    ///
    /// <see cref="NaturalSprite"/> — альтернативный космический спрайт для «природного» дропа:
    /// стек создан естественным источником (астероид → минерал/ноды), а не выброшен из трюма.
    /// Признак стека — <see cref="ItemStack.NaturalOrigin"/>. Если поле пустое — используется
    /// обычный <see cref="Sprite"/>.
    /// </summary>
    public class StackGraphicStep
    {
        [JsonProperty("MinAmount")] public int MinAmount { get; set; }
        [JsonProperty("Icon")] public string Icon { get; set; }
        [JsonProperty("Sprite")] public string Sprite { get; set; }
        [JsonProperty("NaturalSprite")] public string NaturalSprite { get; set; }
    }
}
