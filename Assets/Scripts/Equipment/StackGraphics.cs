using System.Collections.Generic;
using SRG.Simulation;

namespace SRG.Equipment
{
    /// <summary>
    /// Резолвер графики стакабельных предметов (товары, нейроядра, находки) по количеству в стеке.
    ///
    /// Источник — <see cref="ItemsConfig.GetItem(string)"/>: у записи может быть задан список
    /// <see cref="StackGraphicStep"/> («ступени»): берётся ступень с наибольшим MinAmount ≤ количеству.
    /// Если у выбранной ступени нужное поле (Icon/Sprite) пустое — ищется ниже по ступеням.
    /// Без ступеней: иконка — одиночное поле Icon, космический спрайт — null (легаси-схема
    /// {SpritePath}/Tier_{N} остаётся на вызывающей стороне, см. DroppedItemVisualController).
    ///
    /// Использование:
    ///   • ShipInventory.SyncStackItem — инвентарная иконка теневого ItemInstance стека
    ///     (обновляется при каждом изменении количества);
    ///   • DroppedItemVisualController — спрайт дропа в космосе.
    /// Выброс стека из трюма сюда НЕ ходит — контейнер сознательно сохраняет графику контейнера
    /// (ContainerFactory.SpawnContainerWithStack).
    /// </summary>
    public static class StackGraphics
    {
        /// <summary>Инвентарная иконка стека itemId при количестве amount. null — иконки нет.</summary>
        public static string ResolveIcon(string itemId, int amount)
        {
            var (steps, singleIcon) = FindConfig(itemId);
            string fromSteps = PickFromSteps(steps, amount, s => s.Icon);
            return fromSteps ?? singleIcon;
        }

        /// <summary>Космический спрайтшит стека itemId при количестве amount.
        /// null — ступени не заданы, использовать легаси-схему тиров.
        /// <paramref name="naturalOrigin"/>=true — приоритет у поля NaturalSprite, при его отсутствии
        /// откат на обычный Sprite.</summary>
        public static string ResolveSprite(string itemId, int amount, bool naturalOrigin = false)
        {
            var (steps, _) = FindConfig(itemId);
            if (naturalOrigin)
            {
                string natural = PickFromSteps(steps, amount, s => s.NaturalSprite);
                if (!string.IsNullOrEmpty(natural)) return natural;
            }
            return PickFromSteps(steps, amount, s => s.Sprite);
        }

        private static (List<StackGraphicStep> steps, string icon) FindConfig(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return (null, null);
            var cfg = GameWorld.Context?.ItemsConfig;
            if (cfg == null) return (null, null);

            var item = cfg.GetItem(itemId);
            if (item != null) return (item.GraphicSteps, item.Icon);
            return (null, null);
        }

        /// <summary>Ступень с наибольшим MinAmount ≤ amount; если у неё поле пустое — спуск ниже.
        /// Список в конфиге ожидается отсортированным по MinAmount, но на всякий случай ищем честно.</summary>
        private static string PickFromSteps(
            List<StackGraphicStep> steps, int amount, System.Func<StackGraphicStep, string> field)
        {
            if (steps == null || steps.Count == 0) return null;

            StackGraphicStep best = null;
            foreach (var s in steps)
            {
                if (s == null || s.MinAmount > amount) continue;
                if (best == null || s.MinAmount > best.MinAmount) best = s;
            }
            best ??= steps[0]; // количество ниже первой ступени — показываем минимальную

            // Поле пустое у выбранной ступени → берём ближайшую снизу с заполненным полем.
            string value = field(best);
            if (!string.IsNullOrEmpty(value)) return value;
            StackGraphicStep fallback = null;
            foreach (var s in steps)
            {
                if (s == null || s.MinAmount > best.MinAmount || string.IsNullOrEmpty(field(s))) continue;
                if (fallback == null || s.MinAmount > fallback.MinAmount) fallback = s;
            }
            return fallback != null ? field(fallback) : null;
        }
    }
}
