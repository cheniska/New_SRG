using System.Collections.Generic;
using Newtonsoft.Json;
using SRG.Combat;

namespace SRG.Equipment
{
    /// <summary>
    /// Общий «полезный груз» бонусов: набор аддитивных модификаторов, слотовых бонусов, эффектов
    /// и флагов оружия. Один и тот же payload используют:
    ///   • <see cref="ItemConfig.IntrinsicEmbed"/> — свойства, вшитые в конфиг предмета;
    ///   • <see cref="ItemConfig.SlotCode"/> — эффект артефакта, пока стоит в слоте;
    ///   • <see cref="RuntimeBonus.Bonus"/> — бонус, наложенный сценарием через BonusService;
    ///   • базовый класс <see cref="EmbedConfig"/> для встраиваемых предметов (микромодули), где
    ///     к payload'у добавляются Compat/Removable/Tier/Priority.
    /// Применение — <see cref="EmbedService.ApplyBonusSource"/>.
    /// </summary>
    public class EffectConfig
    {
        [JsonProperty("CarrierMods")] public EmbedCarrierMods CarrierMods { get; set; } = new();

        /// <summary>Ключ: "&lt;Category&gt;.&lt;ParamKey&gt;"; значение — аддитивная дельта к
        /// <see cref="ItemInstance.Params"/> носителя. Бонус применяется только если носитель
        /// принадлежит указанной категории.</summary>
        [JsonProperty("Bonuses")] public Dictionary<string, float> Bonuses { get; set; } = new();

        /// <summary>Активатор слотов корпуса: "Weapons": +2 и т.п. Учитывается в
        /// <see cref="EquipmentSystem.GetHullSlotPlan"/> для корпуса-носителя.</summary>
        [JsonProperty("SlotBonuses")] public Dictionary<string, int> SlotBonuses { get; set; } = new();

        /// <summary>Добавляемые эффекты оружия (Slow/ArmorDebuff/…).</summary>
        [JsonProperty("AddedWeaponEffects")] public List<WeaponEffectSpec> AddedWeaponEffects { get; set; } = new();

        /// <summary>Флаги оружия без числовых значений (NoDamageDelta, IgnoreArmorAndShield, …).</summary>
        [JsonProperty("WeaponFlags")] public List<string> WeaponFlags { get; set; } = new();

        /// <summary>Если true — <see cref="Bonuses"/> с ключом <c>&lt;OtherCategory&gt;.&lt;ParamKey&gt;</c>,
        /// где OtherCategory ≠ категория носителя, применяются к предмету в соответствующем слоте
        /// на корабле-хозяине (кросс-слотовый эффект). При false такие бонусы игнорируются
        /// (обычные бонусы <c>&lt;carrier.Category&gt;.&lt;Param&gt;</c> действуют всегда).
        /// Обрабатывается в <see cref="ShipBonusService"/>.</summary>
        [JsonProperty("ShipScope")] public bool ShipScope { get; set; } = false;

        /// <summary>Список условных эффектов: срабатывают на событиях (Hit/Fire/MissileFire/Wear/
        /// TurnStart/TurnEnd/Kill/TakeDamage/Move). Универсальный движок TriggerBus обходит все
        /// источники бонусов и матчит фильтры. Используется артефактами вместо C#-ветвлений
        /// («шанс сбить энергоурон», «каждый N-й выстрел усилен», «двойной ракетный залп»).</summary>
        [JsonProperty("Triggers")] public List<TriggerSpec> Triggers { get; set; } = new();
    }

    /// <summary>Один триггер SlotCode/IntrinsicEmbed: описание события, условий срабатывания
    /// и списка операций-эффектов. Все поля опциональны, кроме <see cref="On"/>.</summary>
    public class TriggerSpec
    {
        /// <summary>Имя события (см. <see cref="TriggerEvent"/>): Fire/Hit/MissileFire/Wear/...</summary>
        [JsonProperty("On")] public string On { get; set; }

        /// <summary>Фильтры контекста события. Известные ключи:
        ///   • DamageType — Kinetic/Explosive/Energy;
        ///   • Slot — категория слота (Engine/Shield/...);
        ///   • Cause — метка причины (для Wear: Move/Hit/Shot/Turn/Forsage).
        /// Неизвестные ключи игнорируются (не мешают модам).</summary>
        [JsonProperty("Filter")] public Dictionary<string, string> Filter { get; set; }

        /// <summary>Шанс срабатывания (0..1). 0 или отсутствует — считается 1.</summary>
        [JsonProperty("Chance")] public float Chance { get; set; } = 1f;

        /// <summary>Если &gt; 0 — триггер срабатывает только на каждом N-м событии, соответствующем
        /// фильтру. Счётчик хранится на самом предмете-источнике (<see cref="ItemInstance.TriggerCounters"/>).
        /// Используется Импульсным конденсатором (каждый 3-й энерговыстрел усилен) и т.п.</summary>
        [JsonProperty("EveryN")] public int EveryN { get; set; } = 0;

        /// <summary>Опциональный стабильный id — если задан, используется как ключ счётчика EveryN
        /// вместо (source.Uid + On). Позволяет двум разным триггерам одного предмета иметь
        /// раздельные счётчики.</summary>
        [JsonProperty("Id")] public string Id { get; set; }

        /// <summary>Операции, применяемые при срабатывании. Порядок сохраняется.</summary>
        [JsonProperty("Effects")] public List<TriggerOp> Effects { get; set; } = new();
    }

    /// <summary>Одна операция триггера. <see cref="Op"/> — имя из реестра (см. TriggerOps в
    /// TriggerBus). <see cref="Value"/> — числовой аргумент (множ, дельта). Extras хранит остальные
    /// поля (например, Type=«HeatDamage» для AddWeaponEffect) в JToken.</summary>
    public class TriggerOp
    {
        [JsonProperty("Op")]    public string Op    { get; set; }
        [JsonProperty("Value")] public float  Value { get; set; }

        [Newtonsoft.Json.JsonExtensionData]
        public Dictionary<string, Newtonsoft.Json.Linq.JToken> Extras { get; set; }

        public string GetString(string key, string def = null)
        {
            if (Extras != null && Extras.TryGetValue(key, out var t))
                try { return t.ToObject<string>(); } catch { }
            return def;
        }

        public float GetFloat(string key, float def = 0f)
        {
            if (Extras != null && Extras.TryGetValue(key, out var t))
                try { return t.ToObject<float>(); } catch { }
            return def;
        }

        public int GetInt(string key, int def = 0)
        {
            if (Extras != null && Extras.TryGetValue(key, out var t))
                try { return t.ToObject<int>(); } catch { }
            return def;
        }
    }

    /// <summary>Имена событий, доступных для <see cref="TriggerSpec.On"/>. Расширяются
    /// регистрацией в <see cref="EmbedRegistry"/> (RegisterTriggerEvent).</summary>
    public static class TriggerEvent
    {
        public const string Fire        = "Fire";         // выстрел оружия
        public const string Hit         = "Hit";          // попадание по кораблю-носителю
        public const string TakeDamage  = "TakeDamage";   // урон принят (после расчёта брони/щита)
        public const string Kill        = "Kill";         // корабль уничтожил цель
        public const string MissileFire = "MissileFire";  // запуск ракеты
        public const string Wear        = "Wear";         // износ оборудования
        public const string TurnStart   = "TurnStart";
        public const string TurnEnd     = "TurnEnd";
        public const string Move        = "Move";
    }

    /// <summary>
    /// Блок «встраиваемости» предмета. Присутствует только у микромодулей (см.
    /// docs/modules/micromodules_design.md). Расширяет <see cref="EffectConfig"/> полями
    /// правил установки: Compat/Removable/Tier/Priority.
    /// Артефакты НЕ используют этот тип — их эффект в слоте описывается через
    /// <see cref="ItemConfig.SlotCode"/> (<see cref="EffectConfig"/>).
    /// </summary>
    public class EmbedConfig : EffectConfig
    {
        /// <summary>Ключ уровня редкости — T1/T2/T3/... Соответствует одной из записей
        /// <see cref="EmbedTierConfig"/>. Не путать с TechLevel оборудования.</summary>
        [JsonProperty("Tier")] public string Tier { get; set; }

        /// <summary>Приоритет 0..100. Используется формулой подбора по ГТУ и таблицами дропа.</summary>
        [JsonProperty("Priority")] public int Priority { get; set; }

        [JsonProperty("Compat")] public EmbedCompat Compat { get; set; } = new();

        /// <summary>0 = извлечь нельзя никогда; 1 = можно (магазин/сервис/скрипт).</summary>
        [JsonProperty("Removable")] public int Removable { get; set; } = 0;
    }

    public class EmbedCompat
    {
        [JsonProperty("CarrierCategories")] public List<string> CarrierCategories { get; set; } = new();
        [JsonProperty("CarrierRaces")]      public List<string> CarrierRaces      { get; set; } = new();
        [JsonProperty("CarrierSides")]      public List<string> CarrierSides      { get; set; } = new();
        [JsonProperty("CarrierTechLevelMin")] public int CarrierTechLevelMin { get; set; } = 0;
        [JsonProperty("CarrierTechLevelMax")] public int CarrierTechLevelMax { get; set; } = 0;
        [JsonProperty("CarrierSizeMin")]    public int CarrierSizeMin { get; set; } = 0;
        [JsonProperty("CarrierSizeMax")]    public int CarrierSizeMax { get; set; } = 0;
        [JsonProperty("Tags")]              public List<string> Tags          { get; set; } = new();
        [JsonProperty("ConflictsWith")]     public List<string> ConflictsWith { get; set; } = new();
        [JsonProperty("Requires")]          public List<string> Requires      { get; set; } = new();
    }

    public class EmbedCarrierMods
    {
        [JsonProperty("PriceMul")]      public float PriceMul      { get; set; } = 1f;
        [JsonProperty("SizeMul")]       public float SizeMul       { get; set; } = 1f;
        [JsonProperty("DurabilityMul")] public float DurabilityMul { get; set; } = 1f;
    }

    /// <summary>Декларативная запись боевого эффекта из EmbedConfig.AddedWeaponEffects.
    /// Преобразуется в <see cref="WeaponEffect"/> при сборке WeaponShotParams.</summary>
    public class WeaponEffectSpec
    {
        [JsonProperty("Type")]      public string Type      { get; set; }
        [JsonProperty("Duration")]  public int    Duration  { get; set; } = 1;
        [JsonProperty("Magnitude")] public float  Magnitude { get; set; } = 0f;
        [JsonProperty("Chance")]    public float  Chance    { get; set; } = 1f;
    }

    /// <summary>Описание уровня редкости встраиваемых. Задаётся в
    /// <see cref="MicroModulesConfig.EmbedTiers"/>; количество и границы свободные.</summary>
    public class EmbedTierConfig
    {
        [JsonProperty("Id")]          public string Id          { get; set; }
        [JsonProperty("DisplayName")] public string DisplayName { get; set; }
        [JsonProperty("Color")]       public string Color       { get; set; }
        [JsonProperty("PriorityMin")] public int    PriorityMin { get; set; }
        [JsonProperty("PriorityMax")] public int    PriorityMax { get; set; }
        [JsonProperty("ExcludeFromRandomDrop")] public bool ExcludeFromRandomDrop { get; set; } = false;
    }

    /// <summary>Запись рантайм-бонуса на <see cref="ItemInstance"/>. Накладывается/снимается
    /// через <see cref="BonusService"/>. Хранится в <c>ItemInstance.RuntimeBonuses</c>.</summary>
    [System.Serializable]
    public class RuntimeBonus
    {
        /// <summary>Payload бонусов — общий тип с IntrinsicEmbed / SlotCode / ММ.
        /// Может содержать Bonuses / CarrierMods / AddedWeaponEffects / WeaponFlags / SlotBonuses.
        /// Compat/Removable/Tier/Priority здесь не имеют смысла, поэтому тип — <see cref="EffectConfig"/>,
        /// а не <see cref="EmbedConfig"/>.</summary>
        [JsonProperty("Bonus")] public EffectConfig Bonus { get; set; }

        /// <summary>Если true — бонус действует, но не отображается в описании (popup/tooltip).</summary>
        [JsonProperty("Hidden")] public bool Hidden { get; set; }

        /// <summary>Опциональная метка источника — для дебага/логов. Не влияет на механику.</summary>
        [JsonProperty("Source")] public string Source { get; set; }
    }

    /// <summary>Универсальные имена флагов оружия, распознаваемые встраиваемыми предметами.
    /// Дополнения из модов регистрируются в <c>EmbedRegistry.RegisterWeaponFlag</c>.</summary>
    public static class EmbedWeaponFlags
    {
        public const string NoDamageDelta        = "NoDamageDelta";
        public const string IgnoreArmorAndShield = "IgnoreArmorAndShield";
        public const string NonLethal            = "NonLethal";
        public const string AmmoFree             = "AmmoFree";
    }
}
