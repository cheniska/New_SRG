using System.Collections.Generic;
using SRG.Galaxy;

namespace SRG.Equipment
{
    /// <summary>
    /// Универсальные геттеры/сеттеры статов оборудования. Единая точка доступа для скриптов
    /// артефактов (TurnCode/UseCode), квестов и внешних систем — избавляет от прямого возни
    /// с Params, RuntimeBonuses и ручным пересчётом.
    ///
    /// Читатели (Get*) возвращают итоговое значение с учётом всех источников бонусов
    /// (IntrinsicEmbed + SlotCode + Embeds + RuntimeBonuses).
    ///
    /// Модификаторы (Add/Multiply/Clear) работают через <see cref="BonusService"/> —
    /// стабильный <c>sourceId</c> позволяет наложить и снять бонус позднее (например,
    /// временный дебафф от эффекта). Идемпотентно: повторное Add с тем же sourceId
    /// перезаписывает предыдущее значение.
    ///
    /// Формат ключей — <c>&lt;Category&gt;.&lt;ParamKey&gt;</c>, где ParamKey должен быть
    /// зарегистрирован в <see cref="EmbedRegistry"/>. Для мультипликативных эффектов
    /// используется отдельный ParamKey (например, <c>Engine.SpeedMult</c>) — сам StatBus
    /// не различает add/mul, читатель применяет модель сам.
    /// </summary>
    public static class StatBus
    {
        // ── Чтение ───────────────────────────────────────────────────────────

        /// <summary>Итоговое значение параметра предмета (Params/DisplayParams содержат
        /// уже применённые бонусы, если <see cref="EmbedService.RecomputeCarrier"/> вызывался).</summary>
        public static float GetItem(ItemInstance item, string paramKey, float def = 0f)
        {
            if (item == null || string.IsNullOrEmpty(paramKey)) return def;
            return item.Params != null && item.Params.TryGetValue(paramKey, out var v) ? v : def;
        }

        /// <summary>Итог параметра в конкретном слоте корабля.</summary>
        public static float GetSlot(ShipData ship, string slotKey, string paramKey, float def = 0f)
        {
            if (ship == null || string.IsNullOrEmpty(slotKey)) return def;
            var item = EquipmentSystem.GetEquipped(ship, slotKey);
            return GetItem(item, paramKey, def);
        }

        /// <summary>Сумма параметра по всем установленным предметам указанной категории.
        /// Полезно для собирающих эффектов ("сколько всего Armor даёт весь стек оборудования").
        /// Не путать с cross-slot бонусами — они уже применены к целевому слоту через ShipBonusService.</summary>
        public static float SumShipCategory(ShipData ship, string category, string paramKey)
        {
            if (ship?.Equipment == null || string.IsNullOrEmpty(category)) return 0f;
            float total = 0f;
            foreach (var slotKey in ship.Equipment.GetOccupiedSlotsOfCategory(category))
            {
                string uid = ship.Equipment.GetItemUid(slotKey);
                if (uid != null && ship.AllItems.TryGetValue(uid, out var item))
                    total += GetItem(item, paramKey, 0f);
            }
            return total;
        }

        // ── Модификация ──────────────────────────────────────────────────────

        /// <summary>Наложить аддитивный бонус <paramref name="delta"/> на параметр предмета.
        /// <paramref name="sourceId"/> — стабильный ключ (например "quest_boss:speedbuff");
        /// повторный вызов с тем же id перезаписывает значение. Используется артефактными скриптами.
        /// key формата "&lt;Category&gt;.&lt;ParamKey&gt;" (self-scope, категория совпадает с носителем).</summary>
        public static void Add(ItemInstance target, string key, string sourceId, float delta,
                               bool hidden = false, string sourceLabel = null)
        {
            if (target == null || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(sourceId)) return;
            var payload = new EffectConfig();
            payload.Bonuses[key] = delta;
            BonusService.ApplyRuntime(target, sourceId, payload, hidden, sourceLabel);
        }

        /// <summary>Мультипликативный эффект: наложить бонус в отдельный ParamKey, интерпретируемый
        /// системой как множитель (delta от 1.0, например 0.2 → +20%). Хранится по тому же
        /// каналу RuntimeBonuses; читатель должен использовать соответствующий ParamKey.</summary>
        public static void Multiply(ItemInstance target, string key, string sourceId, float multDelta,
                                    bool hidden = false, string sourceLabel = null)
            => Add(target, key, sourceId, multDelta, hidden, sourceLabel);

        /// <summary>Снять ранее наложенный бонус с этим <paramref name="sourceId"/>.
        /// Возвращает true, если бонус был.</summary>
        public static bool Clear(ItemInstance target, string sourceId)
            => BonusService.RemoveRuntime(target, sourceId);

        /// <summary>Наложить бонус в один вызов сразу на все установленные предметы категории
        /// (например, «все двигатели корабля получают +20% скорости на 3 хода»).</summary>
        public static int AddShipCategory(ShipData ship, string category, string key, string sourceId,
                                          float delta, bool hidden = false, string sourceLabel = null)
        {
            if (ship?.Equipment == null || string.IsNullOrEmpty(category) ||
                string.IsNullOrEmpty(key) || string.IsNullOrEmpty(sourceId)) return 0;
            var payload = new EffectConfig();
            payload.Bonuses[key] = delta;
            return BonusService.ApplyRuntimeShipCategory(ship, category, sourceId, payload, hidden, sourceLabel);
        }

        /// <summary>Полный recompute носителя. Обычно не нужен — BonusService/EmbedService сами
        /// вызывают recompute. Публикуется на случай ручной сборки состояния в скрипте.</summary>
        public static void Recompute(ItemInstance target, SRG.Config.ItemsConfig equipConfig = null)
            => EmbedService.RecomputeCarrier(target, equipConfig);
    }
}
