using SRG.Combat;
using SRG.Config;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Ships;
using SRG.Utils;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SRG.Simulation;

namespace SRG.UI.Logic
{
    /// <summary>
    /// Текст описания предмета (всплывающая подсказка формы корабля, инфо-панель объекта):
    /// характеристики, производитель, встроенные бонусы, микромодули. Чистое форматирование —
    /// без UI, покрыто тестами.
    /// </summary>
    public static class ItemDescription
    {
        // ── Описание элемента ─────────────────────────────────────────────────────

        public static string Build(ItemInstance item)
        {
            var sb = new StringBuilder(384);
            sb.Append(item.Name).Append("  |  ТУ").Append(item.TechLevel);
            sb.Append('\n');
            sb.Append("Тип: ").Append(LocalizeCategory(item.Category));
            sb.Append("    Вес: ").Append(item.Weight);
            sb.Append('\n');
            sb.Append("Цена: ").Append(item.Price).Append(" кр.");
            sb.Append('\n');
            if (item.NoWear)
                sb.Append("Прочность: без износа");
            else
                sb.Append("Прочность: ").Append(item.Durability).Append('/').Append(item.MaxDurability);

            string manufacturer = ResolveManufacturerLabel(item);
            if (!string.IsNullOrEmpty(manufacturer))
                sb.Append('\n').Append("Производитель: ").Append(manufacturer);

            string extra = ExtraStatLine(item);
            if (!string.IsNullOrEmpty(extra))
                sb.Append('\n').Append(extra);

            // Пометка об SB-апгрейде (значения улучшенных статов уже подсвечены зелёным в FormatStat).
            if (item.ImprovedAttributes != null && item.ImprovedAttributes.Count > 0)
            {
                sb.Append('\n').Append("<color=#4DDA73>Улучшено на SB");
                if (!string.IsNullOrEmpty(item.ImprovementTier)) sb.Append(" (").Append(item.ImprovementTier).Append(")");
                sb.Append("</color>");
            }

            AppendEmbedInfo(sb, item);
            AppendIntrinsicBonusInfo(sb, item);

            if (!string.IsNullOrEmpty(item.Description))
                sb.Append('\n').Append('\n').Append(item.Description);

            return sb.ToString();
        }

        /// <summary>Показать блок «встроенных» бонусов из конфига предмета: IntrinsicEmbed
        /// (безусловные) и SlotCode (пока предмет стоит в слоте). Не путать со встроенными ММ.</summary>
        private static void AppendIntrinsicBonusInfo(StringBuilder sb, ItemInstance item)
        {
            var eq = GameWorld.Context?.ItemsConfig;
            var cfg = eq?.GetItem(item.Category, item.ItemId);
            if (cfg == null) return;
            bool wroteHeader = false;
            AppendEffectBlock(sb, cfg.IntrinsicEmbed, ref wroteHeader);
            AppendEffectBlock(sb, cfg.SlotCode, ref wroteHeader);
        }

        private static void AppendEffectBlock(StringBuilder sb, EffectConfig e, ref bool wroteHeader)
        {
            if (e == null) return;
            if (!wroteHeader) { sb.Append('\n').Append("Особенности:"); wroteHeader = true; }
            if (e.Bonuses != null)
                foreach (var kv in e.Bonuses)
                    AppendBonusLine(sb, kv.Key, kv.Value);
            if (e.AddedWeaponEffects != null)
                foreach (var eff in e.AddedWeaponEffects)
                    if (eff != null && !string.IsNullOrEmpty(eff.Type))
                        sb.Append('\n').Append("Эффект: ").Append(WeaponEffectLabel(eff));
            if (e.WeaponFlags != null)
                foreach (var flag in e.WeaponFlags)
                    sb.Append('\n').Append(WeaponFlagLabel(flag));
            if (e.SlotBonuses != null)
                foreach (var kv in e.SlotBonuses)
                    if (kv.Value != 0)
                        sb.Append('\n').Append(SlotBonusLabel(kv.Key)).Append(": ")
                          .Append(kv.Value > 0 ? "+" : "").Append(kv.Value);
        }

        private static void AppendEmbedInfo(StringBuilder sb, ItemInstance item)
        {
            // Микромодуль/встраиваемый артефакт: SR-стиль описания действий
            if (item.IsEmbeddable && item.Embed != null)
            {
                var e = item.Embed;
                var compat = e.Compat;

                // Общая строка: тип, совместимость по категории носителя, ограничение по расе
                var header = new StringBuilder(128);
                header.Append("Встраиваемый");
                if (!string.IsNullOrEmpty(e.Tier)) header.Append(" [").Append(e.Tier).Append(']');
                if (compat?.CarrierCategories != null && compat.CarrierCategories.Count > 0)
                    header.Append(". Носитель: ").Append(FormatCarrierCategories(compat.CarrierCategories));
                if (compat != null && !ContainsWildcard(compat.CarrierRaces))
                    header.Append(". Только раса ").Append(string.Join("/", compat.CarrierRaces));
                if (compat != null && compat.CarrierSides != null && compat.CarrierSides.Count > 0
                    && !ContainsWildcard(compat.CarrierSides))
                    header.Append(". Сторона ").Append(string.Join("/", compat.CarrierSides));
                if (e.Removable == 0) header.Append(". Извлечение невозможно.");
                sb.Append('\n').Append(header);

                // Построчно — что даёт
                if (e.Bonuses != null)
                    foreach (var kv in e.Bonuses)
                        AppendBonusLine(sb, kv.Key, kv.Value);
                if (e.SlotBonuses != null)
                    foreach (var kv in e.SlotBonuses)
                        if (kv.Value != 0)
                            sb.Append('\n').Append(SlotBonusLabel(kv.Key)).Append(": ")
                              .Append(kv.Value > 0 ? "+" : "").Append(kv.Value);
                if (e.CarrierMods != null)
                {
                    AppendMulLine(sb, "Стоимость носителя", e.CarrierMods.PriceMul);
                    AppendMulLine(sb, "Вес носителя",       e.CarrierMods.SizeMul);
                    AppendMulLine(sb, "Прочность носителя", e.CarrierMods.DurabilityMul);
                }
                if (e.AddedWeaponEffects != null)
                    foreach (var eff in e.AddedWeaponEffects)
                        if (eff != null && !string.IsNullOrEmpty(eff.Type))
                            sb.Append('\n').Append("Эффект: ").Append(WeaponEffectLabel(eff));
                if (e.WeaponFlags != null)
                    foreach (var flag in e.WeaponFlags)
                        sb.Append('\n').Append(WeaponFlagLabel(flag));
            }

            // Носитель со встроенными: перечисляем содержимое
            if (item.Embeds != null && item.Embeds.Count > 0 && item.EmbedItems != null)
            {
                sb.Append('\n').Append("Встроено: ");
                int printed = 0;
                foreach (var uid in item.Embeds)
                {
                    if (!item.EmbedItems.TryGetValue(uid, out var em)) continue;
                    if (printed++ > 0) sb.Append(", ");
                    sb.Append(em.Name ?? em.ItemId);
                }
            }
        }

        // ── Форматирование бонусов для описания ММ ────────────────────────────

        private static void AppendBonusLine(StringBuilder sb, string key, float value)
        {
            if (value == 0f) return;
            string label = BonusLabel(key);
            if (label == null) return;
            sb.Append('\n').Append(label).Append(": ")
              .Append(value > 0 ? "+" : "").Append(value.ToString("0.##"));
            string unit = BonusUnit(key);
            if (unit != null) sb.Append(' ').Append(unit);
        }

        private static void AppendMulLine(StringBuilder sb, string label, float mul)
        {
            if (Mathf.Approximately(mul, 1f)) return;
            int pct = Mathf.RoundToInt((mul - 1f) * 100f);
            sb.Append('\n').Append(label).Append(": ").Append(pct > 0 ? "+" : "").Append(pct).Append('%');
        }

        private static bool ContainsWildcard(List<string> list) =>
            list != null && list.Contains("*");

        private static string FormatCarrierCategories(List<string> cats)
        {
            var parts = new List<string>(cats.Count);
            foreach (var c in cats) parts.Add(LocalizeCategory(c));
            return string.Join(", ", parts);
        }

        // ключ "<Category>.<ParamKey>" → человеческое имя
        private static string BonusLabel(string key)
        {
            switch (key)
            {
                case "Engine.Speed":              return "Скорость двигателя";
                case "Engine.JumpRange":          return "Дальность прыжка";
                case "FuelTank.Capacity":         return "Ёмкость бака";
                case "Radar.Range":               return "Дальность радара";
                case "Scanner.Power":             return "Мощность сканера";
                case "Droid.Efficiency":          return "Эффективность дроида";
                case "Droid.HealPerTurn":         return "Лечение дроида";
                case "Droid.Damage":              return "Урон дроида";
                case "CargoGrabber.Power":        return "Мощность захвата";
                case "CargoGrabber.Range":        return "Дальность захвата";
                case "Shield.BlockPercent":       return "Блок щита";
                case "Forsage.SpeedMul":          return "Множитель форсажа";
                case "Hull.Armor":                return "Броня корпуса";
                case "Hull.HP":                   return "HP корпуса";
                case "Weapons.MinDmg":            return "Минимальный урон";
                case "Weapons.MaxDmg":            return "Максимальный урон";
                case "Weapons.Range":             return "Дальность оружия";
                case "Weapons.ArmorPenetration":  return "Пробитие брони";
                case "Weapons.ShieldPenetration": return "Пробитие щита";
                case "Weapons.EquipmentHitChance":return "Шанс попасть по оборудованию";
                case "Weapons.EquipmentDamage":   return "Урон по оборудованию";
                case "Weapons.MaxAmmo":           return "Ёмкость боезапаса";
            }
            if (key.StartsWith("Hull.Vulnerability.", System.StringComparison.Ordinal))
                return "Уязвимость к " + key.Substring("Hull.Vulnerability.".Length);
            return key;
        }

        private static string BonusUnit(string key)
        {
            switch (key)
            {
                case "Shield.BlockPercent":
                case "Weapons.ShieldPenetration":
                case "Weapons.EquipmentHitChance":
                    return "%";
                case "Radar.Range":
                case "Engine.Speed":
                case "CargoGrabber.Range":
                case "Weapons.Range":
                    return "ед.";
                default: return null;
            }
        }

        private static string SlotBonusLabel(string cat) =>
            cat == "Embeds" ? "Слоты встраивания" : "Слоты " + LocalizeCategory(cat);

        private static string WeaponEffectLabel(WeaponEffectSpec e)
        {
            string type = e.Type switch
            {
                "Slow"          => "замедление",
                "Shutdown"      => "отключение (ЭМИ)",
                "ArmorDebuff"   => "снятие брони",
                "Jamming"       => "помеха",
                "EngineDisable" => "поломка двигателя",
                "Drain"         => "вампиризм",
                "BlockWeapon"   => "блок оружия",
                "BlockDroid"    => "блок дроида",
                "ExecuteBonus"  => "добивание",
                "LootFocus"     => "фокус трофеев",
                _               => e.Type,
            };
            var s = new StringBuilder(48);
            s.Append(type);
            if (e.Duration > 0) s.Append(", ").Append(e.Duration).Append(" хода");
            if (e.Magnitude != 0f) s.Append(", сила ").Append(e.Magnitude.ToString("0.##"));
            if (e.Chance > 0f && e.Chance < 1f) s.Append(", шанс ").Append(Mathf.RoundToInt(e.Chance * 100f)).Append('%');
            return s.ToString();
        }

        private static string WeaponFlagLabel(string flag) => flag switch
        {
            EmbedWeaponFlags.NoDamageDelta        => "Максимальный урон стабилен (без разброса)",
            EmbedWeaponFlags.IgnoreArmorAndShield => "Игнорирует броню и щит",
            EmbedWeaponFlags.NonLethal            => "Не может добить цель",
            EmbedWeaponFlags.AmmoFree             => "Не тратит боезапас",
            _                                     => flag,
        };

        /// <summary>Форматирует значение стата с бонусом от источников: "База (+бонус)" или просто число.
        /// Читает <see cref="ItemInstance.DisplayParams"/> (без скрытых бонусов); если DisplayParams
        /// пуст — фолбэк на Params. Дельта показывается только если она не нулевая и есть BaseParams.
        /// Если стат был поднят SB-апгрейдом (<see cref="ItemInstance.ImprovedAttributes"/>),
        /// значение окрашивается в зелёный (rich text — Unity UI Text это поддерживает).</summary>
        private static string FormatStat(ItemInstance item, string paramKey, string format = "F0", float defaultValue = 0f)
        {
            float cur = defaultValue;
            if (item.DisplayParams != null && item.DisplayParams.TryGetValue(paramKey, out var vd))
                cur = vd;
            else if (item.Params != null && item.Params.TryGetValue(paramKey, out var vp))
                cur = vp;

            string body;
            if (item.BaseParams != null && item.BaseParams.TryGetValue(paramKey, out var baseVal) && baseVal != cur)
            {
                float delta = cur - baseVal;
                string sign = delta > 0 ? "+" : "";
                body = $"{baseVal.ToString(format)} ({sign}{delta.ToString(format)})";
            }
            else body = cur.ToString(format);

            if (item.ImprovedAttributes != null && item.ImprovedAttributes.Contains(paramKey))
                return $"<color=#4DDA73>{body}</color>";
            return body;
        }

        /// <summary>Человекочитаемое имя производителя предмета: «<i>RaceDisplay</i> [Side]».
        /// null/пусто — если у предмета не задан ни <see cref="ItemInstance.ManufacturerRace"/>, ни
        /// <see cref="ItemInstance.ManufacturerSide"/>. Race резолвится через <c>TextsConfig.RaceNames</c>.</summary>
        private static string ResolveManufacturerLabel(ItemInstance item)
        {
            if (item == null) return null;
            string race = item.ManufacturerRace;
            string side = item.ManufacturerSide;
            if (string.IsNullOrEmpty(race) && string.IsNullOrEmpty(side)) return null;

            string raceLabel = race;
            var textCfg = GameWorld.Context?.TextConfig;
            if (!string.IsNullOrEmpty(race) && textCfg?.RaceNames != null
                && textCfg.RaceNames.TryGetValue(race, out var displayName)
                && !string.IsNullOrEmpty(displayName))
                raceLabel = displayName;

            if (!string.IsNullOrEmpty(raceLabel) && !string.IsNullOrEmpty(side))
                return $"{raceLabel} [{side}]";
            return raceLabel ?? side;
        }

        private static string ExtraStatLine(ItemInstance item)
        {
            switch (item.Category)
            {
                case EquipmentCategory.Engine:
                    return $"Скорость: {FormatStat(item, "Speed")}   Прыжок: {FormatStat(item, "JumpRange")}";
                case EquipmentCategory.FuelTank:
                    return $"Объём: {FormatStat(item, "Capacity")}";
                case EquipmentCategory.Shield:
                    return $"Блок: {FormatStat(item, "BlockPercent")}%";
                case EquipmentCategory.Radar:
                    return $"Радиус: {FormatStat(item, "Range")}";
                case EquipmentCategory.Hull:
                    return $"HP: {FormatStat(item, "HP")}   Броня: {FormatStat(item, "Armor")}   [{item.GetParamString("HullType", "?")}]";
                case EquipmentCategory.Droid:
                    {
                        float h = item.GetParam("HealPerTurn");
                        float d = item.GetParam("Damage");
                        var sb = new StringBuilder(64);
                        if (h > 0f) sb.Append($"Лечение: +{FormatStat(item, "HealPerTurn")}/ход   ");
                        if (d > 0f) sb.Append($"Урон: {FormatStat(item, "Damage")}");
                        return sb.ToString();
                    }
                case EquipmentCategory.CargoGrabber:
                    return $"Мощн: {FormatStat(item, "Power")}   Дальн: {FormatStat(item, "Range")}";
                case EquipmentCategory.Forsage:
                    return $"Множитель износа двигателя: x{item.GetParam("EngineDurabilityBurnMult", 1f):F2}";
                case EquipmentCategory.Weapons:
                    {
                        var sb = new StringBuilder(96);
                        sb.Append($"Урон: {FormatStat(item, "MinDmg")}-{FormatStat(item, "MaxDmg")} ({item.GetParamString("DamageType", "?")})");
                        sb.Append($"   Дальн: {FormatStat(item, "Range")}");
                        float ap = item.GetParam("ArmorPenetration", 0f);
                        float sp = item.GetParam("ShieldPenetration", 0f);
                        if (ap > 0f) sb.Append($"   Пробитие: {FormatStat(item, "ArmorPenetration")}");
                        if (sp > 0f) sb.Append($"   Щит-проб: {FormatStat(item, "ShieldPenetration")}%");
                        return sb.ToString();
                    }
                case EquipmentCategory.Artefacts:
                    {
                        float sm = item.GetParam("SpeedMult", 1f);
                        float h = item.GetParam("HealPerTurn");
                        var sb = new StringBuilder(64);
                        if (sm != 1f) sb.Append($"Скор x{sm:F2}   ");
                        if (h > 0f) sb.Append($"Рем +{h:F0}/ход");
                        return sb.ToString();
                    }
                default: return "";
            }
        }

        private static string LocalizeCategory(string cat) => cat switch
        {
            EquipmentCategory.Hull => "Корпус",
            EquipmentCategory.Engine => "Двигатель",
            EquipmentCategory.FuelTank => "Топливный бак",
            EquipmentCategory.Forsage => "Форсаж",
            EquipmentCategory.Shield => "Генератор щита",
            EquipmentCategory.Radar => "Радар",
            EquipmentCategory.Scanner => "Сканер",
            EquipmentCategory.Droid => "Дроид",
            EquipmentCategory.CargoGrabber => "Грузовой захват",
            EquipmentCategory.Weapons => "Оружие",
            EquipmentCategory.Artefacts => "Артефакт",
            "Goods"   => "Товар",
            "Mineral" => "Минерал",
            _ => cat
        };
    }
}
