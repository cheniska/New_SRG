using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Galaxy;

namespace SRG.Equipment
{
    /// <summary>
    /// Улучшение оборудования (научная база). Диздок: docs/modules/equipment_improvement.md.
    ///
    /// Общие правила:
    ///   • Один предмет — один апгрейд (обычный ИЛИ продвинутый). После успешного апгрейда
    ///     <see cref="ItemInstance.IsImprovable"/> = false навсегда.
    ///   • Предмет с встроенным микромодулем/встраиваемым артефактом улучшить нельзя
    ///     (флаг сбрасывается <see cref="EmbedService"/> при установке).
    ///   • Weight/MaxDurability меняются ТОЛЬКО в продвинутом режиме, и только по явному выбору
    ///     атрибута. В обычном апгрейде — только технические характеристики + Price.
    ///   • Прирост Sign × max(current × Pct × roll, MinStep); Sign — у атрибута или категории
    ///     (масса оборудования при улучшении уменьшается).
    ///   • Detail-режим: игрок выбирает основной атрибут — остальные атрибуты этой категории
    ///     тоже растут, но с коэф. <see cref="ImprovementCategoryDef.SecondaryGrowthFactor"/>.
    /// </summary>
    public static class ImprovementService
    {
        public enum Mode
        {
            Standard,   // Min/Avg/Max тир, категорийные Attributes
            Advanced,   // Один конкретный атрибут из AdvancedTargets (или из Attributes)
        }

        /// <summary>Владелец предмета (для UI и корректного recompute после апгрейда).</summary>
        public enum ItemLocation
        {
            EquippedOnPlayer,
            InPlayerInventory,
            PartnerEquipped,
            PartnerInventory,
        }

        public struct EligibleItem
        {
            public ItemInstance Item;
            public ShipData Owner;
            public ItemLocation Location;
            public string OwnerLabel; // для UI ("Партнёр «Имя»")
        }

        public struct Quote
        {
            public int Money;
            public int Nodes;         // 0 если нейроядра не требуются
            public bool NeedsNodes;
            public string Reason;     // не null, если апгрейд невозможен
            public bool Ok => string.IsNullOrEmpty(Reason);
        }

        public struct ApplyResult
        {
            public bool Ok;
            public string Message;
            public List<(string Key, float Delta)> Changes;
        }

        // ── Публичный API ──────────────────────────────────────────────────────

        /// <summary>Единый список eligible-предметов игрока: установленное, трюм, партнёры и их трюм.</summary>
        public static List<EligibleItem> CollectEligible(ShipData player)
        {
            var result = new List<EligibleItem>();
            if (player == null) return result;

            // 1) Установленное на игроке
            AddEquipped(player, ItemLocation.EquippedOnPlayer, ownerLabel: null, result);
            // 2) Трюм игрока
            AddInventory(player, ItemLocation.InPlayerInventory, ownerLabel: null, result);
            // 3) Партнёры
            if (player.PartnerFollowerUids != null)
            {
                foreach (var uid in player.PartnerFollowerUids)
                {
                    var partner = SRG.Ships.Services.PartnerService.FindShipInGalaxy(uid);
                    if (partner == null) continue;
                    string label = partner.Name ?? "Партнёр";
                    AddEquipped(partner, ItemLocation.PartnerEquipped, label, result);
                    AddInventory(partner, ItemLocation.PartnerInventory, label, result);
                }
            }
            return result;
        }

        public static bool CanImprove(ItemInstance item, out string reason)
        {
            reason = null;
            if (item == null) { reason = "Нет предмета."; return false; }
            if (!item.IsEquipment) { reason = "Не оборудование."; return false; }
            if (!item.IsImprovable) { reason = "Уже улучшено или установлен модуль."; return false; }
            if (item.MaxDurability > 0 && item.Durability <= 0) { reason = "Оборудование сломано."; return false; }
            if (item.Embeds != null && item.Embeds.Count > 0) { reason = "Извлеките встроенный модуль."; return false; }
            return true;
        }

        /// <summary>Стоимость апгрейда конкретным тиром/режимом.</summary>
        public static Quote GetQuote(ItemInstance item, string tierKey, Mode mode, ImprovementConfig cfg)
        {
            if (!CanImprove(item, out var reason)) return new Quote { Reason = reason };
            if (cfg == null) return new Quote { Reason = "Нет конфига улучшений." };

            string moneyTier = mode == Mode.Advanced ? cfg.AdvancedBaseTier : tierKey;
            if (!cfg.CostShare.TryGetValue(moneyTier, out var share))
                return new Quote { Reason = $"Неизвестный тир: {moneyTier}." };

            float raw = Mathf.Max(0, item.Price) * share;
            if (mode == Mode.Advanced) raw *= Mathf.Max(1f, cfg.AdvancedCostMultiplier);
            int money = Mathf.Max(10, Mathf.RoundToInt(raw / 10f) * 10);

            int nodes = 0;
            bool needsNodes = item.RequiresNodesToImprove;
            if (needsNodes) nodes = Mathf.Max(1, Mathf.CeilToInt(money / (float)Mathf.Max(1, cfg.CreditsPerNode)));

            return new Quote { Money = money, Nodes = nodes, NeedsNodes = needsNodes };
        }

        /// <summary>Список атрибутов, доступных для Detail-режима стандартного апгрейда.</summary>
        public static IReadOnlyDictionary<string, ImprovementAttributeDef> ListStandardAttributes(
            ItemInstance item, ImprovementConfig cfg)
        {
            var cat = cfg?.GetCategory(item?.Category);
            return cat?.Attributes;
        }

        /// <summary>Список атрибутов, доступных для продвинутого апгрейда (включает Weight/MaxDurability).</summary>
        public static IReadOnlyDictionary<string, ImprovementAttributeDef> ListAdvancedAttributes(
            ItemInstance item, ImprovementConfig cfg)
        {
            var cat = cfg?.GetCategory(item?.Category);
            if (cat == null) return null;
            var merged = new Dictionary<string, ImprovementAttributeDef>();
            if (cat.Attributes != null) foreach (var kv in cat.Attributes) merged[kv.Key] = kv.Value;
            if (cat.AdvancedTargets != null) foreach (var kv in cat.AdvancedTargets) merged[kv.Key] = kv.Value;
            return merged;
        }

        /// <summary>Применить улучшение. Списывает деньги/нейроядра, мутирует предмет, вызывает recompute.
        /// selectedAttribute:
        ///   • Mode.Standard: null — все атрибуты категории по полному, иначе выбранный по полному
        ///     + остальные ×SecondaryGrowthFactor;
        ///   • Mode.Advanced: ОБЯЗАН быть указан.
        /// </summary>
        public static ApplyResult Apply(
            EligibleItem eligible,
            ShipData payer,
            string tierKey,
            Mode mode,
            string selectedAttribute,
            ImprovementConfig cfg)
        {
            var item = eligible.Item;
            if (!CanImprove(item, out var reason)) return Fail(reason);
            if (payer == null) return Fail("Нет плательщика.");
            var quote = GetQuote(item, tierKey, mode, cfg);
            if (!quote.Ok) return Fail(quote.Reason);
            if (payer.Money < quote.Money) return Fail("Недостаточно средств.");
            if (quote.NeedsNodes && !CanPayNodes(payer, quote.Nodes))
                return Fail($"Недостаточно нейроядер (требуется {quote.Nodes}).");
            if (mode == Mode.Advanced && string.IsNullOrEmpty(selectedAttribute))
                return Fail("Продвинутый апгрейд требует выбора конкретной характеристики.");

            var cat = cfg.GetCategory(item.Category);
            if (cat == null) return Fail($"Категория '{item.Category}' не поддерживает улучшение.");

            string effectiveTier = mode == Mode.Advanced ? "Advanced" : tierKey;
            var changes = new List<(string, float)>();

            if (mode == Mode.Standard)
            {
                if (cat.Attributes == null || cat.Attributes.Count == 0)
                    return Fail("У категории нет атрибутов для стандартного апгрейда.");
                foreach (var kv in cat.Attributes)
                {
                    bool isPrimary = string.IsNullOrEmpty(selectedAttribute) || kv.Key == selectedAttribute;
                    float factor = isPrimary ? 1f : Mathf.Max(0f, cat.SecondaryGrowthFactor);
                    if (factor <= 0f) continue;
                    if (!kv.Value.Tiers.TryGetValue(effectiveTier, out var t)) continue;
                    int sign = kv.Value.ResolveSign(cat);
                    float delta = ComputeDelta(item, kv.Key, t, factor);
                    ApplyDelta(item, kv.Key, delta, sign, kv.Value.Integer);
                    if (Mathf.Abs(delta) > 0.0001f)
                    {
                        changes.Add((kv.Key, delta * sign));
                        if (!item.ImprovedAttributes.Contains(kv.Key))
                            item.ImprovedAttributes.Add(kv.Key);
                    }
                }
            }
            else // Advanced
            {
                ImprovementAttributeDef def = null;
                if (cat.AdvancedTargets != null && cat.AdvancedTargets.TryGetValue(selectedAttribute, out var a1)) def = a1;
                else if (cat.Attributes != null && cat.Attributes.TryGetValue(selectedAttribute, out var a2)) def = a2;
                if (def == null) return Fail($"Атрибут '{selectedAttribute}' не поддерживается.");
                if (!def.Tiers.TryGetValue(effectiveTier, out var t) && !def.Tiers.TryGetValue(cfg.AdvancedBaseTier, out t))
                    return Fail($"У атрибута '{selectedAttribute}' нет данных для тира '{effectiveTier}'.");
                int sign = def.ResolveSign(cat);
                float delta = ComputeDelta(item, selectedAttribute, t, 1f);
                ApplyDelta(item, selectedAttribute, delta, sign, def.Integer);
                if (Mathf.Abs(delta) > 0.0001f)
                {
                    changes.Add((selectedAttribute, delta * sign));
                    if (!item.ImprovedAttributes.Contains(selectedAttribute))
                        item.ImprovedAttributes.Add(selectedAttribute);
                }
            }

            // Списание
            payer.Money -= quote.Money;
            if (quote.NeedsNodes) PayNodes(payer, quote.Nodes);

            // Вложенное в улучшение частично переходит в стоимость предмета.
            item.Price += Mathf.Max(1, Mathf.RoundToInt(quote.Money * Mathf.Clamp01(cfg.ValueGainShare)));

            // Флаги
            item.IsImprovable = false;
            item.ImprovementTier = mode == Mode.Advanced ? "Advanced" : tierKey;

            // Пересчёт бонусов у владельца (влияет на скорость/дальность/HP-максимум)
            if (eligible.Owner != null)
                SRG.Equipment.ShipBonusService.RecomputeShipEquipment(eligible.Owner);

            return new ApplyResult { Ok = true, Changes = changes, Message = BuildMessage(item, mode, changes) };
        }

        /// <summary>Применить NPC-апгрейд (без денег, без нейроядер, без выбора атрибута — всё сразу).
        /// Возвращает true, если что-то улучшили.</summary>
        public static bool TryApplyNpcImprovement(ItemInstance item, ShipData owner, ImprovementConfig cfg, System.Random rng)
        {
            if (!CanImprove(item, out _)) return false;
            if (cfg == null || cfg.Categories == null) return false;
            var cat = cfg.GetCategory(item.Category);
            if (cat == null || cat.Attributes == null || cat.Attributes.Count == 0) return false;

            string tier = PickWeighted(cfg.NpcTierWeights, rng);
            if (tier == null) return false;

            bool any = false;
            foreach (var kv in cat.Attributes)
            {
                if (!kv.Value.Tiers.TryGetValue(tier, out var t)) continue;
                float delta = ComputeDelta(item, kv.Key, t, 1f);
                ApplyDelta(item, kv.Key, delta, kv.Value.ResolveSign(cat), kv.Value.Integer);
                if (Mathf.Abs(delta) > 0.0001f)
                {
                    if (!item.ImprovedAttributes.Contains(kv.Key))
                        item.ImprovedAttributes.Add(kv.Key);
                    any = true;
                }
            }

            if (any)
            {
                item.IsImprovable = false;
                item.ImprovementTier = tier;
                if (owner != null)
                    SRG.Equipment.ShipBonusService.RecomputeShipEquipment(owner);
            }
            return any;
        }

        // ── Внутренние помощники ───────────────────────────────────────────────

        private static void AddEquipped(ShipData ship, ItemLocation loc, string ownerLabel, List<EligibleItem> result)
        {
            if (ship?.Equipment?.Slots == null) return;
            foreach (var kv in ship.Equipment.Slots)
            {
                if (kv.Value == null) continue;
                if (!ship.AllItems.TryGetValue(kv.Value, out var it)) continue;
                if (!CanImprove(it, out _)) continue;
                result.Add(new EligibleItem { Item = it, Owner = ship, Location = loc, OwnerLabel = ownerLabel });
            }
        }

        private static void AddInventory(ShipData ship, ItemLocation loc, string ownerLabel, List<EligibleItem> result)
        {
            if (ship?.Inventory?.Items == null) return;
            foreach (var it in ship.Inventory.Items)
            {
                if (!CanImprove(it, out _)) continue;
                result.Add(new EligibleItem { Item = it, Owner = ship, Location = loc, OwnerLabel = ownerLabel });
            }
        }

        private static float ComputeDelta(ItemInstance item, string paramKey, float[] tierArr, float factor)
        {
            if (tierArr == null || tierArr.Length < 2) return 0f;
            float pct     = tierArr[0];
            float minStep = tierArr[1];

            float current;
            if (paramKey == "Weight") current = item.Weight;
            else if (paramKey == "MaxDurability") current = item.MaxDurability;
            else current = item.GetParam(paramKey, 0f);

            // Среднее двух равномерных — «колокол» с пиком в 1: крайние результаты редки.
            float roll = 0.75f + 0.25f * (Random.value + Random.value);
            float delta = Mathf.Max(Mathf.Abs(current) * pct * roll, minStep);
            return delta * factor;
        }

        private static void ApplyDelta(ItemInstance item, string paramKey, float delta, int sign, bool asInt)
        {
            if (Mathf.Abs(delta) < 0.0001f) return;
            float signed = delta * (sign >= 0 ? 1f : -1f);

            if (paramKey == "Weight")
            {
                item.Weight = Mathf.Max(1, item.Weight + Mathf.RoundToInt(signed));
                return;
            }
            if (paramKey == "MaxDurability")
            {
                int add = Mathf.RoundToInt(signed);
                item.MaxDurability = Mathf.Max(1, item.MaxDurability + add);
                // прочность тоже растёт вместе с максимумом (иначе апгрейд бесполезен для целого предмета)
                item.Durability = Mathf.Min(item.MaxDurability, item.Durability + Mathf.Max(0, add));
                return;
            }

            float cur = item.GetParam(paramKey, 0f);
            float updated = cur + signed;
            if (asInt) updated = Mathf.Round(updated);
            item.Params[paramKey] = updated;
        }

        private static bool CanPayNodes(ShipData payer, int required)
        {
            int fromStack = 0;
            if (payer.Inventory != null && payer.Inventory.Stacks != null &&
                payer.Inventory.Stacks.TryGetValue("Nod", out var nodStack))
                fromStack = nodStack.TotalWeight;
            return fromStack + Mathf.Max(0, payer.NodeAccount) >= required;
        }

        private static void PayNodes(ShipData payer, int required)
        {
            int left = required;
            if (payer.Inventory != null && payer.Inventory.Stacks != null &&
                payer.Inventory.Stacks.TryGetValue("Nod", out var _))
            {
                int taken = 0;
                var taken_stack = payer.Inventory.TakeStack("Nod", left);
                if (taken_stack != null) taken = taken_stack.TotalWeight;
                left -= taken;
            }
            if (left > 0) payer.NodeAccount = Mathf.Max(0, payer.NodeAccount - left);
        }

        private static string PickWeighted(Dictionary<string, float> weights, System.Random rng)
        {
            if (weights == null || weights.Count == 0) return null;
            float total = 0f;
            foreach (var kv in weights) total += Mathf.Max(0f, kv.Value);
            if (total <= 0f) return null;
            float roll = (float)rng.NextDouble() * total;
            float acc = 0f;
            foreach (var kv in weights)
            {
                acc += Mathf.Max(0f, kv.Value);
                if (roll <= acc) return kv.Key;
            }
            return null;
        }

        private static ApplyResult Fail(string reason) =>
            new() { Ok = false, Message = reason, Changes = new List<(string, float)>() };

        private static string BuildMessage(ItemInstance item, Mode mode, List<(string Key, float Delta)> changes)
        {
            if (changes == null || changes.Count == 0) return $"{item.Name}: улучшение применено.";
            var sb = new System.Text.StringBuilder();
            sb.Append(item.Name).Append(mode == Mode.Advanced ? " (Продвинутое)" : "").Append(": ");
            for (int i = 0; i < changes.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var c = changes[i];
                sb.Append(c.Key).Append(' ').Append(c.Delta >= 0 ? "+" : "").Append(c.Delta.ToString("0.##"));
            }
            return sb.ToString();
        }
    }
}
