using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Galaxy;

namespace SRG.Equipment
{
    /// <summary>
    /// Контракт «код на сабтёрн» для артефакта или иного оборудования. Скрипт получает корабль,
    /// сам предмет (доступ к <c>ItemInstance.Params</c>, Durability и т.п.), общий конфиг и
    /// номер сабтёрна (1..10). Все условия срабатывания и расход ресурса — внутри скрипта.
    /// Регистрируется в <see cref="ArtefactTurnRegistry"/> под именем, которое кладётся в
    /// <see cref="ItemConfig.TurnCode"/>.
    /// </summary>
    public interface IArtefactTurnScript
    {
        void OnTurn(ShipData ship, ItemInstance item, ItemsConfig equipConfig, int subTurn);
    }

    /// <summary>
    /// Реестр TurnCode-скриптов. Один скрипт может обслуживать несколько артефактов
    /// (например, «HealSelected» и «RegenFuel» имеют одинаковую структуру, но разные Params).
    /// Скрипт срабатывает на сабтёрнах из <see cref="ItemConfig.Subturns"/> (по умолчанию [1] —
    /// первый сабтёрн, «раз в ход в начале»).
    /// </summary>
    public static class ArtefactTurnRegistry
    {
        private static readonly Dictionary<string, IArtefactTurnScript> _scripts = new();

        public static void Register(string id, IArtefactTurnScript script)
        {
            if (string.IsNullOrEmpty(id) || script == null) return;
            _scripts[id] = script;
        }

        public static IArtefactTurnScript Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _scripts.TryGetValue(id, out var s) ? s : null;
        }

        public static bool HasAny => _scripts.Count > 0;

        /// <summary>Проходит по всем предметам корабля (и в слотах, и в трюме), у чьего конфига
        /// задан <see cref="ItemConfig.TurnCode"/> и <see cref="ItemConfig.RunsOnSubturn"/>
        /// (subTurn) вернул true. Перед каждым вызовом обновляет
        /// <see cref="ItemInstance.IsEquipped"/> — скрипт сам решает, тикать ли и как в
        /// зависимости от размещения предмета. Пропускает сломанные предметы. Отсутствующий id
        /// в реестре — тихий no-op (валидацию делает EmbedConfigValidator при загрузке).</summary>
        public static void RunAll(ShipData ship, ItemsConfig equipConfig, int subTurn)
        {
            if (ship?.Equipment == null || equipConfig == null || ship.AllItems == null) return;

            var equipped = _uidBuf;
            equipped.Clear();
            foreach (var kv in ship.Equipment.Slots)
                if (kv.Value != null) equipped.Add(kv.Value);

            foreach (var kv in ship.AllItems)
            {
                var item = kv.Value;
                if (item == null || !item.IsWorking) continue;

                var cfg = equipConfig.GetItem(item.Category, item.ItemId);
                if (cfg == null || string.IsNullOrEmpty(cfg.TurnCode)) continue;
                if (!cfg.RunsOnSubturn(subTurn)) continue;

                // Синхронизируем флаг перед вызовом скрипта — Lua-код читает item.IsEquipped.
                item.IsEquipped = equipped.Contains(kv.Key);

                var script = Get(cfg.TurnCode);
                if (script == null)
                {
                    Debug.LogWarning($"[ArtefactTurnRegistry] TurnCode '{cfg.TurnCode}' у '{item.Name ?? item.ItemId}' не зарегистрирован.");
                    continue;
                }

                try { script.OnTurn(ship, item, equipConfig, subTurn); }
                catch (System.Exception e)
                {
                    Debug.LogError($"[ArtefactTurnRegistry] Ошибка TurnCode '{cfg.TurnCode}' у '{item.Name ?? item.ItemId}': {e.Message}");
                }
            }
        }

        /// <summary>Обход всех кораблей звезды. Вызов из <c>StarSimulator.SimulateTurn</c>
        /// в цикле сабтёрнов.</summary>
        public static void RunForStar(StarData star, ItemsConfig equipConfig, int subTurn)
        {
            if (star?.Ships == null || equipConfig == null || _scripts.Count == 0) return;
            for (int i = 0; i < star.Ships.Count; i++)
            {
                var ship = star.Ships[i];
                if (ship == null || ship.CurrentHull <= 0) continue;
                if (!string.IsNullOrEmpty(ship.LandedPlanetUid)) continue;
                if (!string.IsNullOrEmpty(ship.LandedOnShipUid)) continue;
                RunAll(ship, equipConfig, subTurn);
            }
        }

        // Переиспользуемый буфер uids экипированных предметов — избегаем аллокации HashSet
        // на каждый ход и на каждый корабль.
        private static readonly HashSet<string> _uidBuf = new();
    }
}
