using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Economy;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.UI.Screens;

namespace SRG.Science
{
    /// <summary>
    /// Применение эффектов изобретений к галактическому состоянию (диздок §10).
    ///
    /// Все эффекты — данные, описанные в Science.Inventions[id].Effects. Этот сервис
    /// диспетчеризует Type → конкретное изменение state/cfg/UI. Если для изобретения
    /// есть расовый фильтр, OwnerRaceFilter автоматически наследуется в эффектах,
    /// где он не задан явно.
    /// </summary>
    public static class UnlockEffectsService
    {
        public static void ApplyAll(GalaxyData galaxy, GalaxyGenerationContext ctx,
            string invId, InventionNodeConfig invCfg, InventionProgress progress)
        {
            var state = galaxy.ResearchState;
            if (state == null) return;

            // Дефолтная новость о завершении — если у узла нет своих TriggerNews-эффектов.
            bool hasExplicitNews = false;
            if (invCfg.Effects != null)
            {
                for (int i = 0; i < invCfg.Effects.Count; i++)
                {
                    var e = invCfg.Effects[i];
                    if (e?.Type == "TriggerNews") { hasExplicitNews = true; break; }
                }
            }
            if (!hasExplicitNews)
                AnnounceInvention(galaxy, invCfg, progress);

            if (invCfg.Effects == null) return;
            foreach (var effect in invCfg.Effects)
            {
                if (effect == null || string.IsNullOrEmpty(effect.Type)) continue;
                ApplyOne(galaxy, ctx, state, invId, invCfg, progress, effect);
            }

            EconomicLog.Custom(galaxy.CurrentTurn, "SCIENCE", "Galaxy", "INVENTION_UNLOCKED",
                $"id={invId} tier={invCfg.Tier} race={invCfg.Race ?? "-"} effects={invCfg.Effects?.Count ?? 0}");
        }

        private static void ApplyOne(GalaxyData galaxy, GalaxyGenerationContext ctx, GalacticResearchState state,
            string invId, InventionNodeConfig invCfg, InventionProgress progress, UnlockEffectConfig effect)
        {
            switch (effect.Type)
            {
                case "TriggerNews":
                    NewsEffect(galaxy, invCfg, progress, effect);
                    break;

                case "AddGood":
                    AddGood(ctx, state, effect);
                    break;

                case "RemoveGood":
                    RemoveGood(state, effect);
                    break;

                case "ModifyGoodPrice":
                    ModifyGoodPrice(state, effect);
                    break;

                case "ModifyEventChance":
                    ModifyEventChance(state, effect);
                    break;

                case "AddItemTemplate":
                    AddItemTemplate(state, invCfg, effect);
                    break;

                case "RemoveItemTemplate":
                    RemoveItemTemplate(state, effect);
                    break;

                default:
                    Debug.LogWarning($"[UnlockEffectsService] Unknown effect Type='{effect.Type}' on invention '{invId}'.");
                    break;
            }
        }

        // ──────────────────────────────────────────
        // Конкретные эффекты
        // ──────────────────────────────────────────

        private static void AnnounceInvention(GalaxyData galaxy, InventionNodeConfig invCfg, InventionProgress progress)
        {
            string contributor = FindMainContributorName(galaxy, invCfg, progress);
            string display = invCfg.DisplayName ?? "Новая технология";
            string msg = string.IsNullOrEmpty(contributor)
                ? $"[Наука] Завершено изобретение: {display}."
                : $"[Наука] {contributor}: завершено изобретение {display}.";
            GameConsoleController.AddEntry(msg);
        }

        private static void NewsEffect(GalaxyData galaxy, InventionNodeConfig invCfg, InventionProgress progress, UnlockEffectConfig effect)
        {
            string contributor = FindMainContributorName(galaxy, invCfg, progress);
            string display = invCfg.DisplayName ?? effect.NewsKey ?? "Технология";
            string template = effect.Message
                ?? (string.IsNullOrEmpty(contributor)
                    ? $"[Наука] Завершено изобретение: {display}."
                    : $"[Наука] {contributor}: завершено изобретение {display}.");
            template = template.Replace("{planet}", contributor ?? "-").Replace("{name}", display);
            GameConsoleController.AddEntry(template);
        }

        private static void AddGood(GalaxyGenerationContext ctx, GalacticResearchState state, UnlockEffectConfig effect)
        {
            if (string.IsNullOrEmpty(effect.GoodId)) return;
            var cfg = ctx?.Config;
            if (cfg == null) return;

            var entry = new GalaxyGoodConfig
            {
                DisplayName = effect.DisplayName ?? effect.GoodId,
                BasePrice = effect.BasePrice > 0 ? effect.BasePrice : 50
            };
            state.AddedGoods ??= new Dictionary<string, GalaxyGoodConfig>();
            state.AddedGoods[effect.GoodId] = entry;
            // Также вливаем в общий каталог, чтобы TradeSystem мог его обработать единообразно.
            cfg.Goods ??= new Dictionary<string, GalaxyGoodConfig>();
            cfg.Goods[effect.GoodId] = entry;
            state.RemovedGoods?.Remove(effect.GoodId);
        }

        private static void RemoveGood(GalacticResearchState state, UnlockEffectConfig effect)
        {
            if (string.IsNullOrEmpty(effect.GoodId)) return;
            state.RemovedGoods ??= new HashSet<string>();
            state.RemovedGoods.Add(effect.GoodId);
        }

        private static void ModifyGoodPrice(GalacticResearchState state, UnlockEffectConfig effect)
        {
            if (string.IsNullOrEmpty(effect.GoodId) || effect.Multiplier <= 0f) return;
            state.GoodPriceMultipliers ??= new Dictionary<string, float>();
            state.GoodPriceMultipliers.TryGetValue(effect.GoodId, out var prev);
            if (prev <= 0f) prev = 1f;
            state.GoodPriceMultipliers[effect.GoodId] = prev * effect.Multiplier;
        }

        private static void ModifyEventChance(GalacticResearchState state, UnlockEffectConfig effect)
        {
            if (string.IsNullOrEmpty(effect.EventId) || effect.Multiplier < 0f) return;
            state.EventChanceMultipliers ??= new Dictionary<string, float>();
            state.EventChanceMultipliers.TryGetValue(effect.EventId, out var prev);
            if (prev <= 0f) prev = 1f;
            state.EventChanceMultipliers[effect.EventId] = prev * effect.Multiplier;
        }

        private static void AddItemTemplate(GalacticResearchState state, InventionNodeConfig invCfg, UnlockEffectConfig effect)
        {
            if (string.IsNullOrEmpty(effect.TemplateId)) return;
            state.DisabledTemplates?.Remove(effect.TemplateId);
            state.TemplateGates ??= new Dictionary<string, TemplateGate>();
            state.TemplateGates[effect.TemplateId] = new TemplateGate
            {
                MinTL = Mathf.Max(1, effect.MinTL),
                MaxTL = effect.MaxTL <= 0 ? 10 : effect.MaxTL,
                OwnerRaceFilter = effect.OwnerRaceFilter ?? invCfg.Race
            };
            EquipmentShopSystem.InvalidateCandidateCache();
        }

        private static void RemoveItemTemplate(GalacticResearchState state, UnlockEffectConfig effect)
        {
            if (string.IsNullOrEmpty(effect.TemplateId)) return;
            state.DisabledTemplates ??= new HashSet<string>();
            state.DisabledTemplates.Add(effect.TemplateId);
            EquipmentShopSystem.InvalidateCandidateCache();
        }

        // ──────────────────────────────────────────
        // Утилиты
        // ──────────────────────────────────────────

        private static string FindMainContributorName(GalaxyData galaxy, InventionNodeConfig invCfg, InventionProgress progress)
        {
            if (galaxy == null || progress?.Contributors == null) return null;
            string bestUid = null;
            float bestPts = 0f;
            foreach (var kv in progress.Contributors)
            {
                if (kv.Value <= bestPts) continue;
                bestPts = kv.Value;
                bestUid = kv.Key;
            }
            if (bestUid == null) return null;
            return galaxy.PlanetsMap.TryGetValue(bestUid, out var p) ? p.Name : null;
        }
    }
}
