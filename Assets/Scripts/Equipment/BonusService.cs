using System.Collections.Generic;
using UnityEngine;
using SRG.Galaxy;

namespace SRG.Equipment
{
    /// <summary>
    /// Публичный API рантайм-бонусов. Позволяет любому коду (квесты, скрипты, эффекты)
    /// наложить или снять «микромодулеподобный» бонус на конкретный <see cref="ItemInstance"/>
    /// по стабильному <c>id</c>. Скрытые бонусы (<c>hidden=true</c>) действуют, но не отображаются
    /// в UI-описании (см. <see cref="ItemInstance.DisplayParams"/>).
    ///
    /// Расширяемость: ключи Bonuses / WeaponFlags / AddedWeaponEffects.Type проверяются
    /// валидатором через <see cref="EmbedRegistry"/>. Новые значения регистрируются
    /// вызовом <c>EmbedRegistry.Register*</c> при старте мода/системы.
    ///
    /// Идемпотентность: <see cref="ApplyRuntime"/> с тем же <c>id</c> перезаписывает
    /// предыдущее значение и пересчитывает носителя. <see cref="RemoveRuntime"/> — no-op,
    /// если id не найден.
    /// </summary>
    public static class BonusService
    {
        /// <summary>Наложить рантайм-бонус на предмет. Если <c>id</c> уже занят — перезаписать.</summary>
        /// <param name="target">Целевой предмет (любое оборудование или встраиваемый).</param>
        /// <param name="id">Стабильный ключ (например "quest_boss_buff"). Использовать префиксы
        /// с двумя подчёркиваниями (__*) — зарезервированы для внутренних систем (ShipBonusService).</param>
        /// <param name="bonus">Полезная нагрузка: Bonuses / CarrierMods / AddedWeaponEffects / WeaponFlags / SlotBonuses.
        /// Тип — <see cref="EffectConfig"/> (можно передать и <see cref="EmbedConfig"/> — Compat/Tier
        /// проигнорируются, они не имеют смысла в рантайм-бонусе).</param>
        /// <param name="hidden">Действует, но не отображается в UI.</param>
        /// <param name="sourceLabel">Опциональная строковая метка источника (для логов/дебага).</param>
        public static void ApplyRuntime(ItemInstance target, string id, EffectConfig bonus,
                                        bool hidden = false, string sourceLabel = null)
        {
            if (target == null || string.IsNullOrEmpty(id) || bonus == null) return;
            target.RuntimeBonuses ??= new Dictionary<string, RuntimeBonus>();
            target.RuntimeBonuses[id] = new RuntimeBonus
            {
                Bonus = bonus,
                Hidden = hidden,
                Source = sourceLabel,
            };
            EmbedService.RecomputeCarrier(target);
        }

        /// <summary>Снять рантайм-бонус по <c>id</c>. Возвращает true, если бонус был.</summary>
        public static bool RemoveRuntime(ItemInstance target, string id)
        {
            if (target?.RuntimeBonuses == null || string.IsNullOrEmpty(id)) return false;
            if (!target.RuntimeBonuses.Remove(id)) return false;
            EmbedService.RecomputeCarrier(target);
            return true;
        }

        /// <summary>Есть ли рантайм-бонус с таким id.</summary>
        public static bool HasRuntime(ItemInstance target, string id) =>
            target?.RuntimeBonuses != null && !string.IsNullOrEmpty(id) &&
            target.RuntimeBonuses.ContainsKey(id);

        /// <summary>Получить payload рантайм-бонуса по id (или null).</summary>
        public static RuntimeBonus GetRuntime(ItemInstance target, string id)
        {
            if (target?.RuntimeBonuses == null || string.IsNullOrEmpty(id)) return null;
            return target.RuntimeBonuses.TryGetValue(id, out var rb) ? rb : null;
        }

        /// <summary>Снять все рантайм-бонусы, id которых начинается с указанного префикса.
        /// Возвращает число снятых. Внутренний ShipBonusService использует это для чистки
        /// своих сгенерированных бонусов перед пересчётом.</summary>
        public static int RemoveRuntimeByPrefix(ItemInstance target, string prefix)
        {
            if (target?.RuntimeBonuses == null || string.IsNullOrEmpty(prefix)) return 0;
            List<string> toRemove = null;
            foreach (var kv in target.RuntimeBonuses)
                if (kv.Key.StartsWith(prefix, System.StringComparison.Ordinal))
                    (toRemove ??= new List<string>()).Add(kv.Key);
            if (toRemove == null) return 0;
            foreach (var k in toRemove) target.RuntimeBonuses.Remove(k);
            EmbedService.RecomputeCarrier(target);
            return toRemove.Count;
        }

        /// <summary>Наложить рантайм-бонус на всё установленное оборудование корабля с указанной категорией.
        /// Аналог «баффа щита корабля» — one call, one id per target. Пересчёт корабля выполняется однократно.</summary>
        public static int ApplyRuntimeShipCategory(ShipData ship, string category, string id,
                                                   EffectConfig bonus, bool hidden = false, string sourceLabel = null)
        {
            if (ship?.Equipment == null || string.IsNullOrEmpty(category) || bonus == null) return 0;
            int applied = 0;
            foreach (var slotKey in ship.Equipment.GetOccupiedSlotsOfCategory(category))
            {
                string uid = ship.Equipment.GetItemUid(slotKey);
                if (uid == null || !ship.AllItems.TryGetValue(uid, out var it)) continue;
                ApplyRuntime(it, id, bonus, hidden, sourceLabel);
                applied++;
            }
            return applied;
        }
    }
}
