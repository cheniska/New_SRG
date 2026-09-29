using System.Collections.Generic;
using SRG.Galaxy;

namespace SRG.Equipment
{
    /// <summary>
    /// Пересчёт кросс-слотовых бонусов на корабле. Обрабатывает
    /// <see cref="EffectConfig.ShipScope"/> = true: бонусы источника с ключом
    /// <c>&lt;OtherCategory&gt;.&lt;Param&gt;</c>, где OtherCategory ≠ категория источника,
    /// накладываются как рантайм-бонус на предмет в соответствующем слоте корабля.
    ///
    /// Пример: пушка с <c>IntrinsicEmbed: { Bonuses: { "Engine.Speed": 5 }, ShipScope: true }</c> —
    /// пока пушка стоит в слоте, движок получает +5 к скорости через runtime-бонус
    /// с id вида <c>__ship_cross:&lt;sourceUid&gt;:Engine.Speed</c>.
    ///
    /// Вызывать после любого изменения снаряжения: equip/uninstall/embed install/extract/
    /// runtime bonus apply/remove. Идемпотентно.
    /// </summary>
    public static class ShipBonusService
    {
        /// <summary>Префикс id рантайм-бонусов, сгенерированных этим сервисом. Используется
        /// для чистки перед пересчётом (см. <see cref="BonusService.RemoveRuntimeByPrefix"/>).</summary>
        public const string CrossSlotPrefix = "__ship_cross:";

        /// <summary>Префикс id для бонусов от удалённо-работающих артефактов (через Резонансный хаб):
        /// SlotCode совместимого артефакта из трюма отражается как рантайм-бонусы с этим префиксом.</summary>
        public const string RemoteArtefactPrefix = "__ship_remote:";

        /// <summary>Полный пересчёт: очищает старые cross-slot бонусы, аккумулирует новые,
        /// накладывает на цели и вызывает <see cref="EmbedService.RecomputeCarrier"/> для всех
        /// затронутых предметов.</summary>
        public static void RecomputeShipEquipment(ShipData ship)
        {
            if (ship?.Equipment == null) return;

            // 1. Собираем все установленные предметы (со ссылкой на инстанс).
            var installed = new List<ItemInstance>();
            foreach (var kv in ship.Equipment.Slots)
            {
                if (kv.Value == null) continue;
                if (!ship.AllItems.TryGetValue(kv.Value, out var it) || it == null) continue;
                installed.Add(it);
            }
            if (installed.Count == 0) return;

            // 2. Снимаем прошлые ship-cross и ship-remote runtime-бонусы со всех установленных предметов.
            // (Обходим напрямую RuntimeBonuses, чтобы не вызвать recompute каждого предмета
            // отдельно — сделаем один общий recompute в конце.)
            foreach (var it in installed)
            {
                if (it.RuntimeBonuses == null) continue;
                List<string> toRemove = null;
                foreach (var kv in it.RuntimeBonuses)
                    if (kv.Key.StartsWith(CrossSlotPrefix, System.StringComparison.Ordinal)
                     || kv.Key.StartsWith(RemoteArtefactPrefix, System.StringComparison.Ordinal))
                        (toRemove ??= new List<string>()).Add(kv.Key);
                if (toRemove != null)
                    foreach (var k in toRemove) it.RuntimeBonuses.Remove(k);
            }

            // 3. Индекс: категория → все предметы в слотах этой категории (были нужны все,
            // чтобы, например, +EnergyDamageMult от арта попадал на все установленные пушки).
            var byCategory = new Dictionary<string, List<ItemInstance>>();
            foreach (var it in installed)
            {
                if (!byCategory.TryGetValue(it.Category, out var list))
                    byCategory[it.Category] = list = new List<ItemInstance>();
                list.Add(it);
            }

            // 4. Обходим все источники бонусов у каждого установленного, собираем cross-slot.
            foreach (var source in installed)
            {
                foreach (var e in EmbedService.EnumerateBonusSources(source))
                {
                    if (e == null || !e.ShipScope || e.Bonuses == null) continue;
                    foreach (var kv in e.Bonuses)
                    {
                        int dot = kv.Key.IndexOf('.');
                        if (dot <= 0) continue;
                        string cat = kv.Key.Substring(0, dot);
                        if (cat == source.Category) continue; // это self-бонус, обрабатывает RecomputeCarrier
                        if (!byCategory.TryGetValue(cat, out var targets)) continue;
                        string paramKey = kv.Key.Substring(dot + 1);

                        foreach (var target in targets)
                        {
                            // Накладываем как отдельный runtime-бонус с id вида
                            // "__ship_cross:<sourceUid>:<targetUid>:<Cat>.<Param>". Payload — одиночный
                            // Bonus с ключом "<target.Category>.<param>", чтобы RecomputeCarrier его увидел.
                            string id = CrossSlotPrefix + source.Uid + ":" + target.Uid + ":" + kv.Key;
                            var payload = new EffectConfig();
                            payload.Bonuses[target.Category + "." + paramKey] = kv.Value;
                            target.RuntimeBonuses ??= new Dictionary<string, RuntimeBonus>();
                            target.RuntimeBonuses[id] = new RuntimeBonus
                            {
                                Bonus = payload,
                                Hidden = false,
                                Source = "ship-scope:" + (source.Name ?? source.ItemId),
                            };
                        }
                    }
                }
            }

            // 5. Резонансный хаб: если у установленного артефакта конфиг содержит
            // ScriptParams.RemoteArtefactor { MaxCount, Scale }, то первые MaxCount совместимых
            // предметов из трюма (Params["RemoteCompatible"]=1 или ScriptParams.RemoteCompatible=true)
            // отдают свой SlotCode в бонусы кораблю, все числовые значения масштабируются на Scale.
            var equipConfig = SRG.Core.GalaxyManager.Instance?.Context?.ItemsConfig;
            if (equipConfig != null && ship.Inventory != null && ship.Inventory.Items != null)
            {
                foreach (var hub in installed)
                {
                    var hubCfg = equipConfig.GetItem(hub.Category, hub.ItemId);
                    var sp = hubCfg?.ScriptParams;
                    var rem = sp?["RemoteArtefactor"] as Newtonsoft.Json.Linq.JObject;
                    if (rem == null) continue;
                    int maxCount = rem.Value<int?>("MaxCount") ?? 3;
                    float scale  = rem.Value<float?>("Scale")   ?? 0.5f;
                    if (maxCount <= 0 || scale <= 0f) continue;

                    int used = 0;
                    foreach (var invItem in ship.Inventory.Items)
                    {
                        if (used >= maxCount) break;
                        if (invItem == null) continue;
                        if (!IsRemoteCompatible(invItem, equipConfig)) continue;
                        ApplyRemoteBonuses(ship, hub, invItem, scale, byCategory, equipConfig);
                        used++;
                    }
                }
            }

            // 6. Один общий recompute — по каждому установленному предмету.
            foreach (var it in installed)
                EmbedService.RecomputeCarrier(it);
        }

        private static bool IsRemoteCompatible(ItemInstance item, SRG.Config.ItemsConfig equipConfig)
        {
            // Флаг в Params.RemoteCompatible (жёсткий 0/1) — самый простой сигнал.
            if (item.Params != null && item.Params.TryGetValue("RemoteCompatible", out var p) && p > 0f) return true;
            // Резерв — ScriptParams.RemoteCompatible=true в конфиге.
            var cfg = equipConfig.GetItem(item.Category, item.ItemId);
            var sp = cfg?.ScriptParams;
            if (sp == null) return false;
            var tok = sp["RemoteCompatible"];
            return tok != null && tok.Type == Newtonsoft.Json.Linq.JTokenType.Boolean && tok.ToObject<bool>();
        }

        private static void ApplyRemoteBonuses(
            ShipData ship, ItemInstance hub, ItemInstance remoteItem, float scale,
            Dictionary<string, List<ItemInstance>> byCategory, SRG.Config.ItemsConfig equipConfig)
        {
            var srcCfg = equipConfig.GetItem(remoteItem.Category, remoteItem.ItemId);
            var slotCode = srcCfg?.SlotCode;
            if (slotCode?.Bonuses == null) return;

            foreach (var kv in slotCode.Bonuses)
            {
                int dot = kv.Key.IndexOf('.');
                if (dot <= 0) continue;
                string cat = kv.Key.Substring(0, dot);
                string paramKey = kv.Key.Substring(dot + 1);
                if (!byCategory.TryGetValue(cat, out var targets)) continue;
                float scaled = kv.Value * scale;
                foreach (var target in targets)
                {
                    string id = RemoteArtefactPrefix + hub.Uid + ":" + remoteItem.Uid + ":" + target.Uid + ":" + kv.Key;
                    var payload = new EffectConfig();
                    payload.Bonuses[target.Category + "." + paramKey] = scaled;
                    target.RuntimeBonuses ??= new Dictionary<string, RuntimeBonus>();
                    target.RuntimeBonuses[id] = new RuntimeBonus
                    {
                        Bonus = payload,
                        Hidden = false,
                        Source = "remote:" + (remoteItem.Name ?? remoteItem.ItemId),
                    };
                }
            }
        }
    }
}
