using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SRG.Combat;
using SRG.Config;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Presentation.Common;
using SRG.Ships;
using SRG.Controllers;
using SRG.UI.Common;
using SRG.UI.HUD;
using SRG.Utils;

namespace SRG.UI.Screens
{
    public partial class ShipFormView
    {
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
            var textCfg = GalaxyManager.Instance?.Context?.TextConfig;
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
