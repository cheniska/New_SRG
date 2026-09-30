using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SRG.Combat;
using SRG.Dialog;
using SRG.Dialog.PlanetGreetings;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.NpcAI;
using SRG.NpcAI.Spawning;
using SRG.Science;
using SRG.Ships;
using SRG.Ships.Services;
using SRG.Simulation;

namespace SRG.Config
{
    public class ShipConfigSection
    {
        [JsonProperty("Owners")] public Dictionary<string, OwnerConfig> Owners { get; set; }

        /// <summary>Именованные профили характера NPC: имя → диапазоны 6 черт
        /// (см. <see cref="ShipPersonality"/>). На профиль ссылается <see cref="ShipTypeConfig.Personality"/>.
        /// Профиль "Default" применяется к типам без явной ссылки (и при неизвестном имени).</summary>
        [JsonProperty("PersonalityProfiles")] public Dictionary<string, PersonalityProfileConfig> PersonalityProfiles { get; set; }

        [JsonProperty("ShipTypes")] public Dictionary<string, ShipTypeConfig> ShipTypes { get; set; }
    }

    /// <summary>Профиль характера NPC в конфиге. Каждая черта — массив [min, max] в шкале 0..100;
    /// отсутствующая черта берёт нейтральный дефолт (см. PersonalityRange.Fallback).</summary>
    public class PersonalityProfileConfig
    {
        [JsonProperty("Aggression")] public float[] Aggression { get; set; }
        [JsonProperty("Caution")] public float[] Caution { get; set; }
        [JsonProperty("Greed")] public float[] Greed { get; set; }
        [JsonProperty("Discipline")] public float[] Discipline { get; set; }
        [JsonProperty("Tribalism")] public float[] Tribalism { get; set; }
        [JsonProperty("Vendetta")] public float[] Vendetta { get; set; }
    }

    public class ShipTypeConfig
    {
        /// <summary>Русское display-имя типа для UI и подстановок в диалогах (Транспорт/Лайнер/…).
        /// Используется рендерером приветствий: <c>&lt;FullShip&gt;</c> → «{DisplayName} «{Name}»».</summary>
        [JsonProperty("DisplayName")] public string DisplayName { get; set; }
        [JsonProperty("MinimapIconPath")] public string MinimapIconPath { get; set; }
        [JsonProperty("Animations")] public Dictionary<string, ShipAnimationClip> Animations { get; set; }
        [JsonProperty("Thrusters")] public List<ShipThrusterConfig> Thrusters { get; set; }
        [JsonProperty("StarterKit")] public Dictionary<string, string> StarterKit { get; set; }
        [JsonProperty("StarterWeapons")] public List<string> StarterWeapons { get; set; }

        /// <summary>Боевой класс типа: строковое имя <see cref="global::CombatClass"/>
        /// (Civilian/Mercenary/Military/Pirate), регистронезависимо. Определяет ветку AI-логики,
        /// участие в директивах ГШ, учёт в боевой силе фракций и оккупации.
        /// Пусто/неизвестное имя → Mercenary.</summary>
        [JsonProperty("CombatClass")] public string CombatClass { get; set; }

        /// <summary>Имя профиля характера из <see cref="ShipConfigSection.PersonalityProfiles"/>
        /// (Military/Pirate/Trader/…): диапазоны 6 черт для генерации <see cref="ShipPersonality"/>.
        /// Пусто или неизвестное имя → профиль "Default".</summary>
        [JsonProperty("Personality")] public string Personality { get; set; }

        /// <summary>Стартовые значения 6 скилов в порядке SkillType:
        /// [Accuracy, Mobility, Technical, Trader, Charm, Leadership]. Используется ShipFactory
        /// при создании ShipData этого типа. По умолчанию все скилы = 2.</summary>
        [JsonProperty("BaseSkills")] public int[] BaseSkills { get; set; }

        /// <summary>Флаг «этот тип — дрон». Дрон живёт в двух формах (корабль/предмет), не имеет
        /// самостоятельной AI-жизни (только PartnerAttend), не считается в MaxPartners по умолчанию.
        /// Через отдельный ShipType можно легко добавить Drone_Heavy, Drone_Scout и т.п.</summary>
        [JsonProperty("IsDrone")] public bool IsDrone { get; set; }

        /// <summary>Веса приоритетов апгрейда по категориям оборудования для NpcOutfitter.
        /// Ключ = <see cref="EquipmentCategory"/> (Weapons/Hull/Shield/Engine/FuelTank/Radar/Scanner/...).
        /// Значение = int 0..10 (0 = не апгрейдим). Используется как множитель к базовому скору
        /// предмета при выборе, что покупать: score(item) × EquipmentPriority[category].
        /// Также определяет очередь ремонта в RepairService: категории с большим весом чинятся первыми.
        /// Отсутствие ключа = вес 1 (нейтрально).</summary>
        [JsonProperty("EquipmentPriority")] public Dictionary<string, int> EquipmentPriority { get; set; }

        /// <summary>Флаг «этот тип не садится ради заправки/ремонта/апгрейда».
        /// true — корабль игнорирует триггеры LandResupply (fuel/hull/money) и остаётся в бою.
        /// Проектно для доминаторов, которые «не заходят на планеты».
        /// Смертельно опасные ситуации (пустой бак → застревание) конфигуратор должен закладывать сам.</summary>
        [JsonProperty("SkipsResupplyLandings")] public bool SkipsResupplyLandings { get; set; }

        /// <summary>Флаг «на корабль этого типа можно садиться» (стыковка, аналог посадки на планету):
        /// пристыкованный корабль скрыт, следует за носителем, не участвует в бою.
        /// По умолчанию false; в конфиге true только у линкоров. См. <see cref="ShipDockingService"/>.</summary>
        [JsonProperty("CanBeLandedOn")] public bool CanBeLandedOn { get; set; }
    }

    public class ShipThrusterConfig
    {
        [JsonProperty("OffsetX")] public float OffsetX { get; set; }
        [JsonProperty("OffsetY")] public float OffsetY { get; set; }
        [JsonProperty("SpritesheetPath")] public string SpritesheetPath { get; set; }
        [JsonProperty("FPS")] public float FPS { get; set; } = 12f;
    }

    public class EquipmentManufacturingConfig
    {
        [JsonProperty("PriceMult")] public float PriceMult { get; set; } = 1.0f;
        [JsonProperty("DurabilityMult")] public float DurabilityMult { get; set; } = 1.0f;
    }
}
