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
        // ── Описание элемента ─────────────────────────────────────────────────────

        public static string BuildItemDescription(ItemInstance item)
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
            var eq = GalaxyManager.Instance?.Context?.ItemsConfig;
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
    }
}
