using System.Collections.Generic;
using UnityEngine;
using SRG.Combat;
using SRG.Config;
using SRG.Galaxy;
using SRG.Simulation;

namespace SRG.Equipment
{
    /// <summary>
    /// Установка/извлечение встраиваемых предметов (микромодулей и встраиваемых артефактов)
    /// в предмет-носитель. См. docs/modules/micromodules_design.md.
    /// </summary>
    public static class EmbedService
    {
        public struct InstallCheckResult
        {
            public bool Ok;
            public string Reason;

            public static InstallCheckResult Success => new() { Ok = true };
            public static InstallCheckResult Fail(string reason) => new() { Ok = false, Reason = reason };
        }

        // ── Совместимость ─────────────────────────────────────────────────────

        /// <summary>Ok, если встраиваемый <paramref name="embed"/> подходит носителю
        /// <paramref name="carrier"/> по формальным правилам совместимости.</summary>
        public static InstallCheckResult CanInstall(ItemInstance carrier, ItemInstance embed)
        {
            if (carrier == null) return InstallCheckResult.Fail("Носитель не задан.");
            if (embed == null || embed.Embed == null || !embed.IsEmbeddable)
                return InstallCheckResult.Fail("Предмет не является встраиваемым.");
            if (!carrier.AllowEmbeds)
                return InstallCheckResult.Fail("В этот предмет нельзя встраивать.");
            int max = EffectiveMaxEmbeds(carrier);
            if (carrier.Embeds != null && carrier.Embeds.Count >= max)
                return InstallCheckResult.Fail($"Достигнут лимит встроенных ({max}).");

            var compat = embed.Embed.Compat ?? new EmbedCompat();

            if (compat.CarrierCategories == null || compat.CarrierCategories.Count == 0)
                return InstallCheckResult.Fail("Список совместимых категорий пуст.");
            if (!MatchListOrWildcard(compat.CarrierCategories, carrier.Category))
                return InstallCheckResult.Fail("Категория носителя не подходит.");

            if (compat.CarrierRaces == null || compat.CarrierRaces.Count == 0)
                return InstallCheckResult.Fail("Список совместимых рас пуст.");
            if (!MatchListOrWildcard(compat.CarrierRaces, carrier.ManufacturerRace ?? "None"))
                return InstallCheckResult.Fail("Раса-производитель носителя не подходит.");

            if (compat.CarrierSides != null && compat.CarrierSides.Count > 0)
            {
                if (!MatchListOrWildcard(compat.CarrierSides, carrier.ManufacturerSide ?? "None"))
                    return InstallCheckResult.Fail("Сторона-производитель носителя не подходит.");
            }

            if (compat.CarrierTechLevelMin > 0 && carrier.TechLevel < compat.CarrierTechLevelMin)
                return InstallCheckResult.Fail("Слишком низкий техуровень носителя.");
            if (compat.CarrierTechLevelMax > 0 && carrier.TechLevel > compat.CarrierTechLevelMax)
                return InstallCheckResult.Fail("Слишком высокий техуровень носителя.");

            if (compat.CarrierSizeMin > 0 && carrier.Weight < compat.CarrierSizeMin)
                return InstallCheckResult.Fail("Размер носителя мал.");
            if (compat.CarrierSizeMax > 0 && carrier.Weight > compat.CarrierSizeMax)
                return InstallCheckResult.Fail("Размер носителя велик.");

            // Конфликты и требования — по существующим встроенным
            foreach (var existingUid in carrier.Embeds)
            {
                if (!carrier.EmbedItems.TryGetValue(existingUid, out var existing)) continue;
                var existingCompat = existing.Embed?.Compat;
                // Симметрично: новый конфликтует со старым или старый — с новым.
                if (compat.ConflictsWith != null && MatchesIdOrTag(compat.ConflictsWith, existing))
                    return InstallCheckResult.Fail($"Конфликт с уже встроенным '{existing.Name ?? existing.ItemId}'.");
                if (existingCompat?.ConflictsWith != null && MatchesIdOrTag(existingCompat.ConflictsWith, embed))
                    return InstallCheckResult.Fail($"Уже встроенный '{existing.Name ?? existing.ItemId}' не терпит соседа.");
            }

            if (compat.Requires != null && compat.Requires.Count > 0)
            {
                foreach (var req in compat.Requires)
                {
                    bool satisfied = false;
                    foreach (var existingUid in carrier.Embeds)
                    {
                        if (!carrier.EmbedItems.TryGetValue(existingUid, out var existing)) continue;
                        if (existing.ItemId == req) { satisfied = true; break; }
                        var tags = existing.Embed?.Compat?.Tags;
                        if (tags != null && tags.Contains(req)) { satisfied = true; break; }
                    }
                    if (!satisfied)
                        return InstallCheckResult.Fail($"Требуется предварительно установить '{req}'.");
                }
            }

            return InstallCheckResult.Success;
        }

        // ── Установка ─────────────────────────────────────────────────────────

        /// <summary>Устанавливает встраиваемый в носителя. Возвращает false, если проверка не прошла.
        /// Инстанс встраиваемого перемещается внутрь <see cref="ItemInstance.EmbedItems"/> носителя.</summary>
        public static bool Install(ItemInstance carrier, ItemInstance embed)
        {
            var check = CanInstall(carrier, embed);
            if (!check.Ok)
            {
                Debug.LogWarning($"[EmbedService] Install refused: {check.Reason}");
                return false;
            }

            carrier.EmbedItems[embed.Uid] = embed;
            carrier.Embeds.Add(embed.Uid);
            // После встраивания предмет нельзя улучшить на SB (см. docs/design/SB_Equipment_Improvement.txt).
            // Извлечение модуля флаг не возвращает — «улучшить можно только один раз в жизни» соблюдается.
            carrier.IsImprovable = false;
            RecomputeCarrier(carrier);
            return true;
        }

        // ── Извлечение ────────────────────────────────────────────────────────

        /// <summary>Извлекает встроенный. Возвращает инстанс, освобождённый из носителя (для передачи
        /// в общий инвентарь) или null, если извлечение запрещено или предмет не найден.</summary>
        public static ItemInstance Extract(ItemInstance carrier, string embedUid, bool force = false)
        {
            if (carrier == null || string.IsNullOrEmpty(embedUid)) return null;
            if (!carrier.EmbedItems.TryGetValue(embedUid, out var embed)) return null;
            if (!force && (embed.Embed == null || embed.Embed.Removable != 1))
            {
                Debug.LogWarning($"[EmbedService] Extract refused: '{embed.Name ?? embed.ItemId}' не извлекается.");
                return null;
            }

            carrier.EmbedItems.Remove(embedUid);
            carrier.Embeds.Remove(embedUid);
            RecomputeCarrier(carrier);
            return embed;
        }

        /// <summary>Прямой спавн микромодуля из скрипта (квесты). Обходит правила источников (§9),
        /// но получает валидный <see cref="ItemInstance"/> с непустым <see cref="ItemInstance.Embed"/>.</summary>
        public static ItemInstance SpawnMicroModule(string id, ItemsConfig itemsConfig) =>
            MicroModuleFactory.Create(id, itemsConfig);

        // ── Пересчёт статов носителя ──────────────────────────────────────────

        /// <summary>Полный пересчёт Params/DisplayParams/Price/Weight/MaxDurability по
        /// <see cref="ItemInstance.BaseParams"/> и всем источникам бонусов:
        ///   1. <see cref="ItemConfig.IntrinsicEmbed"/> — из конфига предмета (по (Category, ItemId));
        ///   2. <see cref="ItemConfig.SlotCode"/> — если предмет является артефактом
        ///      и стоит в слоте (RecomputeCarrier сам этого не знает, поэтому SlotCode добавляется
        ///      в источники безусловно; если IsWorking=false, применение подавляется);
        ///   3. <see cref="ItemInstance.Embeds"/> — установленные ММ/встраиваемые артефакты;
        ///   4. <see cref="ItemInstance.RuntimeBonuses"/> — рантайм-бонусы (BonusService).
        /// <see cref="EffectConfig.ShipScope"/>-кросс-категории пропускаются здесь — их
        /// накладывает <see cref="ShipBonusService.RecomputeShipEquipment"/> как runtime-бонус.</summary>
        public static void RecomputeCarrier(ItemInstance carrier, ItemsConfig equipConfig = null)
        {
            if (carrier == null) return;

            // Первая установка: снимок Params → BaseParams.
            if ((carrier.BaseParams == null || carrier.BaseParams.Count == 0) && carrier.Params != null)
            {
                carrier.BaseParams = new Dictionary<string, float>(carrier.Params);
            }
            if (carrier.BaseParams != null && !carrier.BaseParams.ContainsKey("__BasePrice"))
            {
                carrier.BaseParams["__BasePrice"] = carrier.Price;
                carrier.BaseParams["__BaseWeight"] = carrier.Weight;
                carrier.BaseParams["__BaseMaxDurability"] = carrier.MaxDurability;
            }

            // Params (все) и DisplayParams (без скрытых) — стартуют с базы.
            var paramsAll = new Dictionary<string, float>();
            var paramsVisible = new Dictionary<string, float>();
            if (carrier.BaseParams != null)
                foreach (var kv in carrier.BaseParams)
                {
                    if (kv.Key.StartsWith("__", System.StringComparison.Ordinal)) continue;
                    paramsAll[kv.Key] = kv.Value;
                    paramsVisible[kv.Key] = kv.Value;
                }

            float priceMul = 1f, sizeMul = 1f, durMul = 1f;
            float priceMulVis = 1f, sizeMulVis = 1f, durMulVis = 1f;

            // 1. IntrinsicEmbed — из конфига (equipConfig может быть null в изолированных вызовах).
            equipConfig ??= GameWorld.Context?.ItemsConfig;
            var cfg = equipConfig?.GetItem(carrier.Category, carrier.ItemId);
            ApplyBonusSource(carrier.Category, cfg?.IntrinsicEmbed, paramsAll, paramsVisible,
                ref priceMul, ref sizeMul, ref durMul,
                ref priceMulVis, ref sizeMulVis, ref durMulVis, hidden: false);

            // 1b. SlotCode — эффект артефакта/оборудования, стоящего в слоте (пропускается,
            // если предмет сломан). Смотри также EnumerateBonusSources.
            if (cfg?.SlotCode != null && carrier.IsWorking)
                ApplyBonusSource(carrier.Category, cfg.SlotCode, paramsAll, paramsVisible,
                    ref priceMul, ref sizeMul, ref durMul,
                    ref priceMulVis, ref sizeMulVis, ref durMulVis, hidden: false);

            // 2. Embeds
            if (carrier.Embeds != null)
                foreach (var uid in carrier.Embeds)
                {
                    if (!carrier.EmbedItems.TryGetValue(uid, out var embed)) continue;
                    ApplyBonusSource(carrier.Category, embed.Embed, paramsAll, paramsVisible,
                        ref priceMul, ref sizeMul, ref durMul,
                        ref priceMulVis, ref sizeMulVis, ref durMulVis, hidden: false);
                }

            // 3. RuntimeBonuses (в т.ч. cross-slot от ShipBonusService и скрытые).
            if (carrier.RuntimeBonuses != null)
                foreach (var kv in carrier.RuntimeBonuses)
                {
                    var rb = kv.Value;
                    if (rb == null || rb.Bonus == null) continue;
                    ApplyBonusSource(carrier.Category, rb.Bonus, paramsAll, paramsVisible,
                        ref priceMul, ref sizeMul, ref durMul,
                        ref priceMulVis, ref sizeMulVis, ref durMulVis, hidden: rb.Hidden);
                }

            carrier.Params = paramsAll;
            carrier.DisplayParams = paramsVisible;

            float baseP = carrier.BaseParams != null && carrier.BaseParams.TryGetValue("__BasePrice", out var bp) ? bp : carrier.Price;
            float baseW = carrier.BaseParams != null && carrier.BaseParams.TryGetValue("__BaseWeight", out var bw) ? bw : carrier.Weight;
            float baseD = carrier.BaseParams != null && carrier.BaseParams.TryGetValue("__BaseMaxDurability", out var bd) ? bd : carrier.MaxDurability;
            carrier.Price = Mathf.RoundToInt(baseP * priceMul);
            carrier.Weight = Mathf.Max(0, Mathf.RoundToInt(baseW * sizeMul));
            int newMaxDur = Mathf.Max(1, Mathf.RoundToInt(baseD * durMul));
            float durRatio = carrier.MaxDurability > 0 ? (float)carrier.Durability / carrier.MaxDurability : 1f;
            carrier.MaxDurability = newMaxDur;
            carrier.Durability = Mathf.Clamp(Mathf.RoundToInt(newMaxDur * durRatio), 0, newMaxDur);
        }

        /// <summary>Применяет один источник бонусов к аккумуляторам Params/DisplayParams и множителям.
        /// Cross-slot (<see cref="EffectConfig.ShipScope"/>) бонусы <c>&lt;OtherCategory&gt;.&lt;Param&gt;</c>
        /// здесь пропускаются — их накладывает ShipBonusService как runtime-бонус на другой предмет.</summary>
        private static void ApplyBonusSource(string carrierCategory, EffectConfig e,
            Dictionary<string, float> paramsAll, Dictionary<string, float> paramsVisible,
            ref float priceMul, ref float sizeMul, ref float durMul,
            ref float priceMulVis, ref float sizeMulVis, ref float durMulVis, bool hidden)
        {
            if (e == null) return;

            if (e.CarrierMods != null)
            {
                priceMul *= e.CarrierMods.PriceMul;
                sizeMul  *= e.CarrierMods.SizeMul;
                durMul   *= e.CarrierMods.DurabilityMul;
                if (!hidden)
                {
                    priceMulVis *= e.CarrierMods.PriceMul;
                    sizeMulVis  *= e.CarrierMods.SizeMul;
                    durMulVis   *= e.CarrierMods.DurabilityMul;
                }
            }
            if (e.Bonuses == null) return;
            foreach (var kv in e.Bonuses)
            {
                int dot = kv.Key.IndexOf('.');
                if (dot <= 0) continue;
                string cat = kv.Key.Substring(0, dot);
                if (cat != carrierCategory) continue; // cross-slot — не здесь
                string paramKey = kv.Key.Substring(dot + 1);
                paramsAll.TryGetValue(paramKey, out var curAll);
                paramsAll[paramKey] = curAll + kv.Value;
                if (!hidden)
                {
                    paramsVisible.TryGetValue(paramKey, out var curVis);
                    paramsVisible[paramKey] = curVis + kv.Value;
                }
            }
        }

        // ── Хелперы ───────────────────────────────────────────────────────────

        /// <summary>Максимум встроенных с учётом бонусных слотов от уже встроенных встраиваемых
        /// (например, артефакт-«хаб» даёт +N через <see cref="EmbedConfig.SlotBonuses"/>["Embeds"]).</summary>
        public static int EffectiveMaxEmbeds(ItemInstance carrier)
        {
            if (carrier == null) return 0;
            int max = carrier.MaxEmbeds;
            if (max <= 0 && carrier.AllowEmbeds) max = 1;
            if (carrier.Embeds != null)
            {
                foreach (var uid in carrier.Embeds)
                {
                    if (!carrier.EmbedItems.TryGetValue(uid, out var e)) continue;
                    if (e.Embed?.SlotBonuses != null && e.Embed.SlotBonuses.TryGetValue("Embeds", out var b))
                        max += b;
                }
            }
            return Mathf.Max(0, max);
        }

        /// <summary>Сумма <see cref="EmbedConfig.SlotBonuses"/> всех источников бонусов носителя
        /// по указанной категории (Weapons, Artefacts, Radar, …). См. GetHullSlotPlan.</summary>
        public static int GetCarrierSlotBonus(ItemInstance carrier, string category)
        {
            if (carrier == null || string.IsNullOrEmpty(category)) return 0;
            int sum = 0;
            foreach (var src in EnumerateBonusSources(carrier))
                if (src?.SlotBonuses != null && src.SlotBonuses.TryGetValue(category, out var b))
                    sum += b;
            return sum;
        }

        /// <summary>Собирает <see cref="WeaponEffect"/> из всех источников бонусов оружия:
        /// IntrinsicEmbed конфига + встроенных ММ/артефактов + рантайм-бонусов.</summary>
        public static void AppendWeaponEffects(ItemInstance weapon, List<WeaponEffect> effects)
        {
            if (weapon == null || effects == null) return;
            foreach (var src in EnumerateBonusSources(weapon))
            {
                if (src?.AddedWeaponEffects == null) continue;
                foreach (var spec in src.AddedWeaponEffects)
                {
                    if (!System.Enum.TryParse<CombatEffectType>(spec.Type, out var type)) continue;
                    effects.Add(new WeaponEffect
                    {
                        Type = type,
                        DurationTurns = spec.Duration,
                        Magnitude = spec.Magnitude,
                        Chance = spec.Chance
                    });
                }
            }
        }

        /// <summary>true, если у оружия есть указанный флаг — из встроенных, intrinsic или runtime.</summary>
        public static bool HasWeaponFlag(ItemInstance weapon, string flag)
        {
            if (weapon == null || string.IsNullOrEmpty(flag)) return false;
            foreach (var src in EnumerateBonusSources(weapon))
            {
                var flags = src?.WeaponFlags;
                if (flags == null) continue;
                for (int i = 0; i < flags.Count; i++)
                    if (flags[i] == flag) return true;
            }
            return false;
        }

        /// <summary>Все <see cref="EffectConfig"/>-источники, действующие на предмет:
        /// IntrinsicEmbed (из конфига) + SlotCode (у артефактов) + Embeds + RuntimeBonuses.
        /// SlotCode применяется, только если предмет — рабочий (IsWorking=true). Порядок стабильный.</summary>
        public static IEnumerable<EffectConfig> EnumerateBonusSources(ItemInstance item)
        {
            if (item == null) yield break;
            var eq = GameWorld.Context?.ItemsConfig;
            var cfg = eq?.GetItem(item.Category, item.ItemId);

            if (cfg?.IntrinsicEmbed != null) yield return cfg.IntrinsicEmbed;
            if (cfg?.SlotCode != null && item.IsWorking) yield return cfg.SlotCode;

            if (item.Embeds != null)
                foreach (var uid in item.Embeds)
                    if (item.EmbedItems != null && item.EmbedItems.TryGetValue(uid, out var em) && em?.Embed != null)
                        yield return em.Embed;

            if (item.RuntimeBonuses != null)
                foreach (var kv in item.RuntimeBonuses)
                    if (kv.Value?.Bonus != null) yield return kv.Value.Bonus;
        }

        // ── Внутренние проверки ───────────────────────────────────────────────

        private static bool MatchListOrWildcard(List<string> list, string value)
        {
            if (list == null || list.Count == 0) return false;
            if (list.Contains("*")) return !string.IsNullOrEmpty(value);
            return value != null && list.Contains(value);
        }

        private static bool MatchesIdOrTag(List<string> patterns, ItemInstance item)
        {
            if (patterns == null || item == null) return false;
            foreach (var p in patterns)
            {
                if (item.ItemId == p) return true;
                var tags = item.Embed?.Compat?.Tags;
                if (tags != null && tags.Contains(p)) return true;
            }
            return false;
        }
    }
}
