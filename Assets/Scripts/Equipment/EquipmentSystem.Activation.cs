using UnityEngine;
using SRG.Galaxy;
using SRG.Ships.Services;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.Equipment
{
    public static partial class EquipmentSystem
    {
        /// <summary>
        /// Активация предмета: вызывается, когда игрок положил активируемый предмет в псевдо-слот
        /// активации (см. ShipFormView). Сейчас обрабатываются категории встроенно (форсаж включает
        /// режим форсажа корабля). В будущем сюда будет приходить произвольный скрипт предмета
        /// (через ItemInstance.ActivateScript / Params["ActivateScript"]).
        /// </summary>
        public static bool ActivateItem(ShipData ship, ItemInstance item)
        {
            if (ship == null || item == null || !item.Activatable) return false;
            if (!item.IsWorking)
            {
                GameLog.Add($"[Активация] {item.Name}: предмет повреждён, активация невозможна.");
                return false;
            }

            // Сначала — UseCode из конфига. Если задан и обработчик зарегистрирован — его результат
            // определяет исход активации; consume удаляет предмет из инвентаря, wear списывает
            // прочность. Легаси-switch срабатывает только если UseCode не задан.
            var equipCfg = GameWorld.Context?.ItemsConfig;
            var itemCfg = equipCfg?.GetItem(item.Category, item.ItemId);
            if (itemCfg != null && !string.IsNullOrEmpty(itemCfg.UseCode))
            {
                var script = ArtefactActionRegistry.Get(itemCfg.UseCode);
                if (script != null)
                {
                    var res = script.TryUse(ship, item, equipCfg);
                    if (!string.IsNullOrEmpty(res.Message))
                        GameLog.Add(res.Message);
                    if (!res.Success) return false;
                    if (res.WearApplied > 0)
                        item.Durability = Mathf.Max(0, item.Durability - res.WearApplied);
                    if (res.Consume)
                    {
                        ship.Inventory?.Remove(item.Uid);
                        ship.AllItems?.Remove(item.Uid);
                    }
                    return true;
                }
                GameLog.Add($"[Активация] {item.Name}: UseCode '{itemCfg.UseCode}' не зарегистрирован.");
                return false;
            }

            switch (item.Category)
            {
                case EquipmentCategory.Forsage:
                    if (!CanActivateForsage(ship, out string reason))
                    {
                        GameLog.Add($"[Форсаж] Активация невозможна: {reason}.");
                        return false;
                    }
                    ship.ForsageActive = true;
                    GameLog.Add($"[Активация] Форсаж включён: {item.Name}.");
                    return true;

                case EquipmentCategory.CompanionDrone:
                {
                    var drone = DroneService.Deploy(ship, item);
                    if (drone == null)
                    {
                        GameLog.Add($"[Дрон] Развёртывание {item.Name} не удалось.");
                        return false;
                    }
                    GameLog.Add($"[Дрон] Развёрнут: {drone.Name}.");
                    return true;
                }

                default:
                    GameLog.Add($"[Активация] {item.Name}: активация для этой категории ещё не реализована.");
                    return false;
            }
        }

        /// <summary>
        /// Проверка условий, при которых форсаж можно включить. Возвращает false только
        /// при сильном износе двигателя (< 40%): риск окончательно сжечь двигатель.
        /// Подлёт к посадке обрабатывается отдельно — авто-выключением форсажа на 6-м
        /// субходе хода посадки (см. ShipData.ShipNextDay), а не блокировкой активации.
        /// </summary>
        public static bool CanActivateForsage(ShipData ship, out string reason)
        {
            reason = null;
            if (ship == null) { reason = "корабль не задан"; return false; }

            var engine = GetEquipped(ship, SlotKeys.Engine);
            if (engine != null && engine.MaxDurability > 0
                && (float)engine.Durability / engine.MaxDurability < 0.4f)
            {
                reason = "двигатель изношен (< 40%)";
                return false;
            }

            return true;
        }

        /// <summary>
        /// На текущем ходу корабль уже выйдет в радиус посадки выбранной планеты. Используется
        /// для авто-отключения форсажа на 6-м субходе хода посадки — чтобы корабль не проскочил
        /// планету. Берётся фактическая скорость хода (cached, с учётом форсажа) — если её хватит,
        /// чтобы дойти до планеты за этот ход, ход считается «последним перед посадкой».
        /// </summary>
        public static bool IsLandingThisTurn(ShipData ship)
        {
            if (ship == null || string.IsNullOrEmpty(ship.LandingPlanetUid)) return false;
            var star = GameWorld.CurrentStar;
            var planet = star?.Planets?.Find(p => p.Uid == ship.LandingPlanetUid);
            if (planet == null) return false;

            float speedPerTurn = SRUnits.ToWorld(ship._turnCachedSpeed >= 0f ? ship._turnCachedSpeed : ship.ActualSpeed);
            if (speedPerTurn <= 0f) return false;

            Vector2 planetPos = OrbitMath.GetPlanetWorldPosition(planet);
            float dist = Vector2.Distance(ship.Position, planetPos);

            float landingRange = PlanetGeometry.GetLandingRadius(planet);
            return dist <= speedPerTurn + landingRange;
        }
    }
}
