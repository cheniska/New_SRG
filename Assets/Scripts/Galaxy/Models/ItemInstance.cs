using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System;
using SRG.Combat;
using SRG.Config;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy.Generation;
using SRG.Galaxy.Politics;
using SRG.Galaxy.Simulation;
using SRG.NpcAI;
using SRG.NpcAI.Actions;
using SRG.NpcAI.Spawning;
using SRG.Science;
using SRG.Ships;
using SRG.Ships.Movement;
using SRG.Ships.Services;
using SRG.Simulation;

namespace SRG.Galaxy
{
    [Serializable] public class ItemStack
    {
        public string ItemId { get; set; }
        public string Category { get; set; }
        public string Name { get; set; }
        public int TotalWeight { get; set; }
        public int BasePrice { get; set; }
        /// <summary>Классификация: стек — торговый товар (участвует в TraderAI/Shop/Inflation).
        /// Задаётся при создании стека из <see cref="ItemConfig.IsGoods"/>. Стек без флага —
        /// «useless» (стакабельные находки, минералы, квестовые предметы).</summary>
        public bool IsGoods { get; set; }
        /// <summary>Стек создан «естественным» источником (астероид → минерал/ноды), а не выброшен
        /// из трюма. Влияет только на графику дропа в космосе: если у ступени задан
        /// <see cref="StackGraphicStep.NaturalSprite"/>, он используется вместо обычного Sprite.</summary>
        public bool NaturalOrigin { get; set; }
        [JsonIgnore] public int TotalPrice => TotalWeight * BasePrice;
        /// <summary>«Не товар» — считается useless (см. <see cref="IsGoods"/>).</summary>
        [JsonIgnore] public bool IsUseless => !IsGoods;
    }

    [Serializable]
    public class ItemInstance
    {
        public string Uid { get; set; } = GameRng.NewUid();
        public string Category { get; set; }
        public string ItemId { get; set; }

        public string Name { get; set; }
        public string Description { get; set; }
        public int Weight { get; set; }
        public int Price { get; set; }
        public int TechLevel { get; set; }
        public bool NoWear { get; set; }
        /// <summary>Предмет-«носитель» разрешает встраивать в себя микромодули/встраиваемые артефакты.
        /// Раньше называлось AllowModules — сохранена совместимость через <see cref="AllowModules"/>.</summary>
        public bool AllowEmbeds { get; set; }
        /// <summary>Устаревшее имя <see cref="AllowEmbeds"/>. Сеттер обновляет новое поле, чтобы старые
        /// сейвы (до июля 2026) продолжали читаться. Не сериализуется.</summary>
        [JsonProperty("AllowModules")]
        public bool AllowModules { get => AllowEmbeds; set => AllowEmbeds = value; }
        public bool ShouldSerializeAllowModules() => false;
        /// <summary>Предмет можно «активировать» (drop в слот активации справа от радара).
        /// Сейчас активация форсажа включает ship.ForsageActive; для остальных типов
        /// предусмотрен расширяемый хук (см. EquipmentSystem.ActivateItem).</summary>
        public bool Activatable { get; set; }
        /// <summary>Классификация: экземпляр — оборудование (можно установить в слот, ремонтировать,
        /// апгрейдить). Задаётся из <see cref="ItemConfig.IsEquipment"/>, унаследованного
        /// от <c>EquipmentTemplate.Defaults["IsEquipment"]=true</c>.</summary>
        public bool IsEquipment { get; set; }
        /// <summary>Классификация: предмет является встраиваемым (микромодуль или встраиваемый артефакт).
        /// При установке в предмет-носителя (<see cref="AllowEmbeds"/>=true) применяет
        /// <see cref="Embed"/>.CarrierMods/Bonuses. См. docs/modules/micromodules_design.md.</summary>
        public bool IsEmbeddable { get; set; }
        /// <summary>Блок встраиваемости. null у обычных предметов.</summary>
        public EmbedConfig Embed { get; set; }
        /// <summary>Максимальное число встроенных предметов у носителя. У встраиваемых обычно 0.</summary>
        public int MaxEmbeds { get; set; } = 0;
        /// <summary>Классификация: экземпляр не является ни оборудованием, ни товаром — «useless»
        /// (находки, статуэтки, квесты). Определяется через отсутствие флага <see cref="IsEquipment"/>.
        /// Товары живут отдельно как <see cref="ItemStack"/>, поэтому в контексте <c>ItemInstance</c>
        /// «не оборудование» ≡ «useless».</summary>
        [JsonIgnore] public bool IsUseless => !IsEquipment;
        /// <summary>Установлен ли предмет сейчас в слот корабля. Не сериализуется — пересчитывается
        /// на каждом сабтёрне в <see cref="SRG.Equipment.ArtefactTurnRegistry.RunAll"/>
        /// перед вызовом скриптов.
        /// Доступно из Lua как <c>item.IsEquipped</c> — скрипт сам решает, тикать ли и как.</summary>
        [JsonIgnore] public bool IsEquipped { get; set; }
        public string ManufacturerRace { get; set; }
        /// <summary>Сторона-производитель (Coalition/Dominators/Pirates/...). Раньше называлось Owner.</summary>
        public string ManufacturerSide { get; set; }
        /// <summary>Путь к иконке предмета (в инвентаре/магазине).</summary>
        public string GraphicPath { get; set; }
        /// <summary>Базовый путь к графике корабля в космосе (для корпусов).</summary>
        public string BodyGraphicPath { get; set; }
        /// <summary>Только для корпусов. true → SpritesheetPath резолвится по
        /// <see cref="ManufacturerSide"/>/<see cref="ManufacturerRace"/>, а не по Owner/Race корабля.</summary>
        public bool GraphicByManufacturer { get; set; }
        /// <summary>Только для корпусов станций. true → графика тела не поворачивается по курсу
        /// (переносится в <see cref="ShipData.SpriteFixedRotation"/> при RefreshSpritesheetPath).</summary>
        public bool FixedRotation { get; set; }

        public int Durability { get; set; }
        public int MaxDurability { get; set; }
        public int CurrentFuel { get; set; }
        public Dictionary<string, float> Params { get; set; } = new();
        public Dictionary<string, string> ParamStrings { get; set; } = new();
        public float HealAccumulator { get; set; } = 0f;
        public float CargoAccumulator { get; set; } = 0f;
        public string Modification { get; set; }
        /// <summary>Uid встроенных предметов (микромодули + встраиваемые артефакты). Порядок
        /// сохраняется — от него зависит порядок применения бонусов. Раньше называлось Modules.</summary>
        public List<string> Embeds { get; set; } = new();
        /// <summary>Устаревшее имя <see cref="Embeds"/>. Сеттер переносит данные из старых сейвов
        /// в новое поле. Не сериализуется.</summary>
        [JsonProperty("Modules")]
        public List<string> Modules
        {
            get => null;
            set { if (value != null && value.Count > 0) Embeds = value; }
        }
        public bool ShouldSerializeModules() => false;
        /// <summary>Контейнер экземпляров встроенных предметов (Uid → инстанс). Живут внутри носителя,
        /// в общий инвентарь/торговлю не попадают.</summary>
        public Dictionary<string, ItemInstance> EmbedItems { get; set; } = new();
        /// <summary>Оригинальные значения <see cref="Params"/>, снятые перед первой установкой
        /// встроенного предмета. При извлечении бонусы откатываются относительно этой базы.</summary>
        public Dictionary<string, float> BaseParams { get; set; } = new();

        /// <summary>Скрытые/рантайм-бонусы, наложенные через <see cref="SRG.Equipment.BonusService"/>.
        /// Ключ — стабильный id (например "quest_boss_buff"). Действуют, но скрытые не отображаются в UI.
        /// </summary>
        public Dictionary<string, RuntimeBonus> RuntimeBonuses { get; set; } = new();

        /// <summary>Счётчики срабатываний триггеров (<see cref="TriggerSpec.EveryN"/>). Ключ —
        /// стабильный id триггера (см. TriggerSpec.Id или sourceUid+On), значение — сколько
        /// событий этого типа уже произошло. Инкрементируется TriggerBus. Не читается напрямую.</summary>
        public Dictionary<string, int> TriggerCounters { get; set; } = new();

        /// <summary>Отдельная копия <see cref="Params"/> без учёта скрытых рантайм-бонусов.
        /// UI (popup/tooltip) читает отсюда, чтобы не проговариваться о скрытых модификаторах.
        /// Если пусто — фолбэк на <see cref="Params"/>.</summary>
        public Dictionary<string, float> DisplayParams { get; set; } = new();

        public Dictionary<string, float> WeaponVulnerability { get; set; }
        /// <summary>
        /// Категории слотов, в которых предмет может работать. Если пуст — работает
        /// только в слоте своей категории. Например, у абордажного крюка может быть
        /// ["BoardingHook", "CargoGrabber"] — крюк ставится и в "родной" слот, и в
        /// слот грузового захвата (но захват в слот крюка не подойдёт).
        /// </summary>
        public List<string> CompatibleSlots { get; set; } = new();

        // ── Улучшение оборудования (научная база SB, диздок docs/SB_Equipment_Improvement.txt) ──
        /// <summary>Разрешено ли улучшение этого предмета. Ставится в true при генерации
        /// eligible-предметов (см. <see cref="EquipmentTemplate"/>.Defaults["IsImprovable"]).
        /// Сбрасывается в false навсегда, как только предмет был улучшен ЛИБО в него встроили
        /// микромодуль (см. <see cref="SRG.Equipment.EmbedService"/>). Один предмет — один апгрейд.</summary>
        public bool IsImprovable { get; set; }
        /// <summary>Требует расходования Нод при улучшении. Флаг из шаблона (обычно ставится
        /// доминаторскому/трофейному оборудованию). Формула нод — см. <see cref="SRG.Equipment.ImprovementService"/>.</summary>
        public bool RequiresNodesToImprove { get; set; }
        /// <summary>Список ключей <see cref="Params"/>, значения которых были подняты апгрейдом.
        /// Используется UI: значения окрашиваются в зелёный (см. <see cref="SRG.UI.Common.UIColorPalette"/>).
        /// Порядок ключей соответствует порядку применения (в обычном апгрейде — основной атрибут,
        /// затем вторичный).</summary>
        public List<string> ImprovedAttributes { get; set; } = new();
        /// <summary>Тир применённого апгрейда ("Min"/"Avg"/"Max") или "Advanced" для продвинутого.
        /// null — предмет не улучшался.</summary>
        public string ImprovementTier { get; set; }
        [JsonIgnore] public bool IsWorking => Durability > 0;

        /// <summary>Можно ли поставить предмет в слот указанной категории.</summary>
        public bool CanFitInSlotCategory(string slotCategory)
        {
            if (string.IsNullOrEmpty(slotCategory)) return false;
            if (Category == slotCategory) return true;
            if (CompatibleSlots != null)
                foreach (var c in CompatibleSlots)
                    if (c == slotCategory) return true;
            return false;
        }
        public float GetParam(string key, float defaultValue = 0f) =>
            Params.TryGetValue(key, out var v) ? v : defaultValue;
        public string GetParamString(string key, string defaultValue = null) =>
            ParamStrings.TryGetValue(key, out var v) ? v : defaultValue;
        public float GetVulnerability(string damageType) =>
            WeaponVulnerability != null && WeaponVulnerability.TryGetValue(damageType, out var v) ? v : 1.0f;

        public static ItemInstance FromConfig(
            string category,
            string itemId,
            ItemConfig cfg,
            ItemsConfig equipConfig,
            float priceMult = 1f,
            float durabilityMult = 1f,
            int? gtlOverride = null)
        {
            if (cfg == null) return null;

            int techLevel = gtlOverride ?? cfg.TechLevel;
            if (techLevel < 1) techLevel = 1;
            if (techLevel > 10) techLevel = 10;

            var inst = new ItemInstance
            {
                Category = category,
                ItemId = itemId,
                Name = cfg.Name,
                Description = cfg.Description,
                Weight = cfg.Weight,
                TechLevel = techLevel,
                NoWear = cfg.NoWear,
                AllowEmbeds = cfg.AllowEmbeds,
                MaxEmbeds = cfg.AllowEmbeds ? System.Math.Max(1, cfg.MaxEmbeds) : cfg.MaxEmbeds,
                Activatable = cfg.Activatable,
                IsEquipment = cfg.IsEquipment,
                IsEmbeddable = cfg.IsEmbeddable,
                Embed = cfg.Embed,
                ManufacturerRace = cfg.Manufacturer?.Race,
                ManufacturerSide = cfg.Manufacturer?.Side,
                GraphicPath = cfg.GraphicPath,
                BodyGraphicPath = cfg.BodyGraphicPath,
                GraphicByManufacturer = cfg.GraphicByManufacturer,
                FixedRotation = cfg.FixedRotation,

                Price = Mathf.RoundToInt(cfg.Price * priceMult),
                MaxDurability = Mathf.Max(1, Mathf.RoundToInt(cfg.MaxDurability * durabilityMult)),
                IsImprovable = cfg.IsImprovable,
                RequiresNodesToImprove = cfg.RequiresNodesToImprove,
            };
            inst.Durability = inst.MaxDurability;

            float[] gtlDmgMult = null;
            if (cfg.Params != null)
            {
                foreach (var kv in cfg.Params)
                {
                    if (kv.Key == "GTLDamageMultipliers" && kv.Value.Type == Newtonsoft.Json.Linq.JTokenType.Array)
                    {
                        try { gtlDmgMult = kv.Value.ToObject<float[]>(); }
                        catch { }
                        continue;
                    }

                    if (kv.Key == "CompatibleSlots" && kv.Value.Type == Newtonsoft.Json.Linq.JTokenType.Array)
                    {
                        try { inst.CompatibleSlots = new List<string>(kv.Value.ToObject<string[]>()); }
                        catch { }
                        continue;
                    }

                    try { inst.Params[kv.Key] = kv.Value.ToObject<float>(); }
                    catch
                    {
                        try
                        {
                            if (kv.Value.Type == Newtonsoft.Json.Linq.JTokenType.Array)
                                inst.ParamStrings[kv.Key] = string.Join(",", kv.Value.ToObject<string[]>());
                            else
                                inst.ParamStrings[kv.Key] = kv.Value.ToObject<string>();
                        }
                        catch { }
                    }
                }
            }

            // Оружие: BaseMinDmg/BaseMaxDmg → MinDmg/MaxDmg, масштабированное множителем ГТУ.
            // BaseDmg трактуется как урон на ГТУ 1; множитель ГТУ 1 принимается = 1 по умолчанию.
            if (inst.Params.ContainsKey("BaseMinDmg") || inst.Params.ContainsKey("BaseMaxDmg"))
            {
                float mult = 1f;
                if (gtlDmgMult != null && gtlDmgMult.Length > 0)
                {
                    int idx = Mathf.Clamp(techLevel - 1, 0, gtlDmgMult.Length - 1);
                    mult = gtlDmgMult[idx];
                }
                if (inst.Params.TryGetValue("BaseMinDmg", out var bMin))
                    inst.Params["MinDmg"] = bMin * mult;
                if (inst.Params.TryGetValue("BaseMaxDmg", out var bMax))
                    inst.Params["MaxDmg"] = bMax * mult;
            }

            // Ракетное оружие: базовый боезапас запоминаем как максимум (MaxAmmo),
            // чтобы дозаправлять/дозаряжать до полного в ангаре. Энергооружие (Ammo=-1) пропускаем.
            if (inst.Params.TryGetValue("Ammo", out var baseAmmo) && baseAmmo >= 0f)
                inst.Params["MaxAmmo"] = baseAmmo;

            if (category == "FuelTank")
                inst.CurrentFuel = Mathf.RoundToInt(inst.GetParam("Capacity"));
            // Уязвимости к урону — у HullSeries; задаются при установке серии, а не при создании корпуса.
            return inst;
        }
    }
}
