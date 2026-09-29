using System.Collections.Generic;
using UnityEngine;
using SRG.Combat;
using SRG.Config;
using SRG.Core;
using SRG.Galaxy;

namespace SRG.Equipment
{
    /// <summary>
    /// «Что делает предмет, когда контейнер, в котором он лежит, был уничтожен в космосе».
    /// Позволяет описать поведение «выбрось в космос и подорви» (Кварковая бомба и т.п.) —
    /// без отдельного слота активации: игрок дропает предмет обычным способом, стреляет
    /// в контейнер, скрипт исполняется на его смерть.
    /// </summary>
    public interface IContainerHitScript
    {
        void OnContainerHit(ShipData container, ItemInstance item, ShipData killer,
                            SRG.Config.ItemsConfig equipConfig);
    }

    public static class CargoHitRegistry
    {
        private static readonly Dictionary<string, IContainerHitScript> _scripts = new();
        private static bool _hooked;

        public static void Register(string id, IContainerHitScript script)
        {
            if (string.IsNullOrEmpty(id) || script == null) return;
            _scripts[id] = script;
        }

        public static IContainerHitScript Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _scripts.TryGetValue(id, out var s) ? s : null;
        }

        /// <summary>Подписаться на <see cref="ShipDeathBus"/>. Идемпотентно. Вызывать при старте
        /// игры (например, из GalaxyManager.Awake после регистрации встроенных скриптов).</summary>
        public static void EnsureHooked()
        {
            if (_hooked) return;
            _hooked = true;
            ShipDeathBus.OnShipDestroyed += OnShipDestroyed;
        }

        private static void OnShipDestroyed(ShipData victim, ShipData killer, string cause)
        {
            if (victim == null || !victim.IsItem) return;
            if (victim.Inventory?.Items == null || victim.Inventory.Items.Count == 0) return;

            var eq = GalaxyManager.Instance?.Context?.ItemsConfig;
            if (eq == null) return;

            for (int i = 0; i < victim.Inventory.Items.Count; i++)
            {
                var item = victim.Inventory.Items[i];
                if (item == null) continue;
                string id = ResolveScriptId(item, eq);
                if (string.IsNullOrEmpty(id)) continue;
                var script = Get(id);
                if (script == null)
                {
                    Debug.LogWarning($"[CargoHitRegistry] OnContainerHit '{id}' у '{item.Name ?? item.ItemId}' не зарегистрирован.");
                    continue;
                }
                try { script.OnContainerHit(victim, item, killer, eq); }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CargoHitRegistry] Ошибка '{id}' у '{item.Name ?? item.ItemId}': {e.Message}");
                }
            }
        }

        private static string ResolveScriptId(ItemInstance item, ItemsConfig eq)
        {
            // Приоритет: Params["OnContainerHit"] (можно менять рантаймом), затем ScriptParams.OnContainerHit.
            var s = item.GetParamString("OnContainerHit");
            if (!string.IsNullOrEmpty(s)) return s;
            var cfg = eq.GetItem(item.Category, item.ItemId);
            var tok = cfg?.ScriptParams?["OnContainerHit"];
            return tok?.ToObject<string>();
        }
    }
}
