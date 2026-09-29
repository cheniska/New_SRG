using System.Collections.Generic;
using UnityEngine;
using SRG.Combat;
using SRG.Config;

namespace SRG.Equipment
{
    /// <summary>
    /// Валидатор блоков <see cref="EffectConfig"/> / <see cref="EmbedConfig"/> (микромодули,
    /// SlotCode/IntrinsicEmbed оборудования и артефактов). Проверяет:
    ///   • <c>CarrierCategories</c> — существуют в <see cref="EquipmentCategory.DisplayOrder"/> или в шаблонах;
    ///   • <c>CarrierRaces</c> — существуют в <see cref="GalaxyConfig.Races"/> (плюс "*", "None");
    ///   • <c>CarrierSides</c> — существуют в <see cref="ItemsConfig.EnumerateSides"/> (плюс "*");
    ///   • <c>AddedWeaponEffects[i].Type</c> — валидное значение <see cref="CombatEffectType"/>;
    ///   • <c>WeaponFlags</c> — из реестра <see cref="EmbedWeaponFlags"/>;
    ///   • ключи <c>Bonuses</c> — формат <c>&lt;Category&gt;.&lt;ParamKey&gt;</c>, категория известна,
    ///     а ParamKey — из таблицы допустимых бонусов (см. docs/modules/micromodules_design.md §6.2);
    ///   • ключи <c>SlotBonuses</c> — категория из <see cref="EquipmentCategory.DisplayOrder"/> либо "Embeds".
    ///
    /// Каждое нарушение — <c>Debug.LogError</c> с указанием источника (id ММ/артефакта). Загрузка
    /// не блокируется, но ошибки видны в консоли и логах — считаем это «ошибкой конфига».
    /// </summary>
    public static class EmbedConfigValidator
    {
        // Whitelist'ы вынесены в <see cref="EmbedRegistry"/> — расширяемый реестр.

        // ── API ────────────────────────────────────────────────────────────────

        public static void Validate(ItemsConfig itemsConfig, GalaxyConfig galaxyConfig)
        {
            if (itemsConfig == null) return;

            var races = CollectRaces(galaxyConfig);
            var sides = CollectSides(itemsConfig);
            var categories = CollectCategories(itemsConfig);

            int errors = 0;

            foreach (var kv in itemsConfig.EnumerateByKind(ItemKind.MicroModules))
            {
                errors += ValidateEmbed($"MicroModule '{kv.Key}'", kv.Value?.Embed,
                                        races, sides, categories);
            }

            // Встраиваемые артефакты + intrinsic-бонусы у любого оборудования.
            foreach (var (cat, id, cfg) in itemsConfig.EnumerateAllItems())
            {
                if (cfg == null) continue;
                if (cfg.IsEmbeddable)
                    errors += ValidateEmbed($"{cat} '{id}' (Embed)", cfg.Embed,
                                            races, sides, categories);
                if (cfg.IntrinsicEmbed != null)
                    errors += ValidateEmbed($"{cat} '{id}' (IntrinsicEmbed)", cfg.IntrinsicEmbed,
                                            races, sides, categories, allowNoCompat: true);
                if (cfg.SlotCode != null)
                    errors += ValidateEmbed($"{cat} '{id}' (SlotCode)", cfg.SlotCode,
                                            races, sides, categories, allowNoCompat: true);
            }

            // EmbedTiers: непересекающиеся диапазоны — не критично, но проверим на дубли Id.
            if (itemsConfig.EmbedTiers != null)
            {
                var ids = new HashSet<string>();
                foreach (var t in itemsConfig.EmbedTiers)
                {
                    if (t == null || string.IsNullOrEmpty(t.Id)) continue;
                    if (!ids.Add(t.Id))
                        errors += LogError($"EmbedTiers: дубликат Id='{t.Id}'.");
                }
            }

            if (errors > 0)
                Debug.LogError($"[EmbedConfigValidator] Найдено ошибок: {errors}. См. предыдущие сообщения.");
        }

        // ── Внутренняя валидация ─────────────────────────────────────────────

        private static int ValidateEmbed(string ownerLabel, EffectConfig e,
            HashSet<string> races, HashSet<string> sides, HashSet<string> categories,
            bool allowNoCompat = false)
        {
            if (e == null) return 0;
            int errors = 0;
            // Compat присутствует только у EmbedConfig (микромодули). У SlotCode/IntrinsicEmbed
            // тип — EffectConfig, Compat нет.
            var c = (e as EmbedConfig)?.Compat ?? new EmbedCompat();

            // CarrierCategories/Races/Sides — обязательны только у встраиваемых предметов,
            // у IntrinsicEmbed/SlotCode их можно опустить (нет установщика — некому проверять).
            if (!allowNoCompat)
            {
                if (c.CarrierCategories == null || c.CarrierCategories.Count == 0)
                    errors += LogError($"{ownerLabel}: Compat.CarrierCategories пуст — предмет неустановим.");
                else
                    foreach (var cat in c.CarrierCategories)
                    {
                        if (cat == "*") continue;
                        if (!categories.Contains(cat))
                            errors += LogError($"{ownerLabel}: неизвестная CarrierCategory='{cat}'.");
                    }

                if (c.CarrierRaces == null || c.CarrierRaces.Count == 0)
                    errors += LogError($"{ownerLabel}: Compat.CarrierRaces пуст — предмет неустановим.");
                else
                    foreach (var race in c.CarrierRaces)
                    {
                        if (race == "*" || race == "None") continue;
                        if (!races.Contains(race))
                            errors += LogError($"{ownerLabel}: неизвестная CarrierRace='{race}'.");
                    }
            }

            if (c.CarrierSides != null)
                foreach (var side in c.CarrierSides)
                {
                    if (side == "*" || side == "None") continue;
                    if (!sides.Contains(side))
                        errors += LogError($"{ownerLabel}: неизвестная CarrierSide='{side}'.");
                }

            // Bonuses — валидируем через EmbedRegistry.
            if (e.Bonuses != null)
                foreach (var kv in e.Bonuses)
                {
                    int dot = kv.Key.IndexOf('.');
                    if (dot <= 0)
                    {
                        errors += LogError($"{ownerLabel}: ключ Bonuses '{kv.Key}' — ожидается '<Category>.<ParamKey>'.");
                        continue;
                    }
                    string cat = kv.Key.Substring(0, dot);
                    string param = kv.Key.Substring(dot + 1);
                    if (!categories.Contains(cat) && !EmbedRegistry.HasCategory(cat))
                    {
                        errors += LogError($"{ownerLabel}: Bonuses '{kv.Key}' — неизвестная категория '{cat}'.");
                        continue;
                    }
                    if (!EmbedRegistry.IsKnownBonus(cat, param))
                        errors += LogError($"{ownerLabel}: Bonuses '{kv.Key}' — неизвестный ParamKey '{param}' для категории '{cat}' (зарегистрируй в EmbedRegistry.RegisterBonus).");
                }

            // SlotBonuses
            if (e.SlotBonuses != null)
                foreach (var kv in e.SlotBonuses)
                {
                    if (kv.Key == "Embeds") continue;
                    if (!categories.Contains(kv.Key))
                        errors += LogError($"{ownerLabel}: SlotBonuses ключ '{kv.Key}' — неизвестная категория (или 'Embeds').");
                }

            // AddedWeaponEffects — валидируем через EmbedRegistry.
            if (e.AddedWeaponEffects != null)
                foreach (var spec in e.AddedWeaponEffects)
                {
                    if (spec == null || string.IsNullOrEmpty(spec.Type)) continue;
                    if (!EmbedRegistry.IsKnownWeaponEffectType(spec.Type))
                        errors += LogError($"{ownerLabel}: AddedWeaponEffects.Type '{spec.Type}' — не зарегистрирован (EmbedRegistry.RegisterWeaponEffectType).");
                }

            // WeaponFlags — через EmbedRegistry.
            if (e.WeaponFlags != null)
                foreach (var flag in e.WeaponFlags)
                {
                    if (string.IsNullOrEmpty(flag)) continue;
                    if (!EmbedRegistry.IsKnownWeaponFlag(flag))
                        errors += LogError($"{ownerLabel}: WeaponFlag '{flag}' — не зарегистрирован (EmbedRegistry.RegisterWeaponFlag).");
                }

            // Triggers — событие и все операции по реестру.
            if (e.Triggers != null)
                for (int i = 0; i < e.Triggers.Count; i++)
                {
                    var t = e.Triggers[i];
                    if (t == null) continue;
                    if (string.IsNullOrEmpty(t.On))
                    {
                        errors += LogError($"{ownerLabel}: Triggers[{i}].On пуст.");
                        continue;
                    }
                    if (!EmbedRegistry.IsKnownTriggerEvent(t.On))
                        errors += LogError($"{ownerLabel}: Triggers[{i}].On '{t.On}' — неизвестное событие (EmbedRegistry.RegisterTriggerEvent).");
                    if (t.Effects != null)
                        foreach (var op in t.Effects)
                        {
                            if (op == null || string.IsNullOrEmpty(op.Op)) continue;
                            if (!EmbedRegistry.IsKnownTriggerOp(op.Op))
                                errors += LogError($"{ownerLabel}: Triggers[{i}].Effects[Op='{op.Op}'] — неизвестная операция (EmbedRegistry.RegisterTriggerOp).");
                        }
                }

            return errors;
        }

        // ── Сбор допустимых значений из других конфигов ──────────────────────

        private static HashSet<string> CollectRaces(GalaxyConfig cfg)
        {
            var s = new HashSet<string>();
            if (cfg?.Races != null)
                foreach (var kv in cfg.Races) s.Add(kv.Key);
            return s;
        }

        private static HashSet<string> CollectSides(ItemsConfig cfg)
        {
            var s = new HashSet<string>();
            if (cfg != null)
                foreach (var side in cfg.EnumerateSides()) s.Add(side);
            return s;
        }

        private static HashSet<string> CollectCategories(ItemsConfig cfg)
        {
            var s = new HashSet<string>();
            foreach (var c in EquipmentCategory.DisplayOrder) s.Add(c);
            // Плюс всё, что есть в EquipmentTemplates (шаблоны могут добавить кастомные категории).
            if (cfg != null)
                foreach (var (cat, _, _) in cfg.EnumerateAllItems()) s.Add(cat);
            return s;
        }

        private static int LogError(string msg)
        {
            Debug.LogError("[EmbedConfigValidator] " + msg);
            return 1;
        }
    }
}
