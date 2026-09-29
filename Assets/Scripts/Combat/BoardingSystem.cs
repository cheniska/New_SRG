using UnityEngine;
using SRG.Config;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Utils;

namespace SRG.Combat
{
    /// <summary>
    /// Механика абордажа. Атакующий через абордажный крюк (категория <c>BoardingHook</c>) фиксирует
    /// корабль-цель сбоку: цель не двигается самостоятельно, её Position каждый кадр выставляется
    /// рядом с атакующим по локальному вектору <c>HullTypeDef.BoardAnchor</c>. Открыть форму цели
    /// (обмен оборудованием в обе стороны) — внешним UI.
    /// </summary>
    public static class BoardingSystem
    {
        /// <summary>Найти на корабле работающий абордажный крюк (<see cref="EquipmentCategory.BoardingHook"/>).</summary>
        public static ItemInstance FindGrapplingHook(ShipData ship)
            => EquipmentSystem.FindEquipmentByCategory(ship, EquipmentCategory.BoardingHook);

        public static bool CanBoard(ShipData attacker, ShipData target, ItemsConfig equipConfig, out string reason)
        {
            reason = null;
            if (attacker == null || target == null) { reason = "Нет атакующего или цели."; return false; }
            if (target.IsItem) { reason = "Абордаж предметов невозможен."; return false; }
            if (target.IsStation) { reason = "Станции не поддаются абордажу."; return false; }
            if (target.CurrentHull <= 0) { reason = "Цель уничтожена."; return false; }
            if (!string.IsNullOrEmpty(target.BoardedByUid)) { reason = "Цель уже абордирована."; return false; }
            if (!string.IsNullOrEmpty(target.TowedByUid)) { reason = "Цель уже на буксире."; return false; }
            if (!string.IsNullOrEmpty(attacker.BoardingTargetUid)) { reason = "Атакующий уже абордирует другой корабль."; return false; }
            var hook = FindGrapplingHook(attacker);
            if (hook == null) { reason = "Нет работающего абордажного крюка."; return false; }
            float pullR = SRUnits.ToWorld(hook.GetParam("PullRadius", 0f));
            float distSq = (target.Position - attacker.Position).sqrMagnitude;
            if (distSq > pullR * pullR) { reason = "Цель вне радиуса крюка."; return false; }
            if (HullAnchorMath.GetHullTypeDef(attacker, equipConfig)?.BoardAnchor == null)
            { reason = "Корпус атакующего не задаёт точку крепления для абордажа."; return false; }
            return true;
        }

        public static bool TryBoard(ShipData attacker, ShipData target, ItemsConfig equipConfig)
        {
            if (!CanBoard(attacker, target, equipConfig, out _)) return false;
            attacker.BoardingTargetUid = target.Uid;
            target.BoardedByUid = attacker.Uid;
            // Цель не двигается самостоятельно — очищаем её маршрут.
            target.FreezeRoute();
            SnapBoardedTarget(attacker, target, equipConfig);
            return true;
        }

        public static void EndBoarding(ShipData attacker, ShipData target)
        {
            if (attacker != null && attacker.BoardingTargetUid == target?.Uid)
                attacker.BoardingTargetUid = null;
            if (target != null && target.BoardedByUid == attacker?.Uid)
                target.BoardedByUid = null;
        }

        /// <summary>
        /// Передвинуть абордированную цель в точку крепления атакующего. BoardAnchor задаёт ТОЛЬКО
        /// направление от центра атакующего; расстояние = сумме половин спрайтов (бок-о-бок без перекрытия).
        /// </summary>
        public static void SnapBoardedTarget(ShipData attacker, ShipData target, ItemsConfig equipConfig)
        {
            if (attacker == null || target == null) return;
            var def = HullAnchorMath.GetHullTypeDef(attacker, equipConfig);
            Vector2 worldPos;
            if (def?.BoardAnchor != null && def.BoardAnchor.Length >= 2)
            {
                // Локальное направление крепления (нормализуем; модуль из JSON игнорируем — он использовался
                // как фиксированный пиксельный отступ, теперь дистанция считается из спрайтов).
                Vector2 localDir = new Vector2(def.BoardAnchor[0], def.BoardAnchor[1]);
                if (localDir.sqrMagnitude < 1e-6f) localDir = Vector2.right;
                localDir.Normalize();
                float ang = float.IsNaN(attacker.CurrentHeading) ? 0f : attacker.CurrentHeading - Mathf.PI * 0.5f;
                float cos = Mathf.Cos(ang), sin = Mathf.Sin(ang);
                Vector2 worldDir = new Vector2(cos * localDir.x - sin * localDir.y,
                                                sin * localDir.x + cos * localDir.y);
                float gap = 0.5f * Mathf.Max(0f, attacker.SpriteWorldSize)
                          + 0.5f * Mathf.Max(0f, target.SpriteWorldSize);
                worldPos = attacker.Position + worldDir * gap;
            }
            else worldPos = attacker.Position;

            target.PreviousPosition = target.Position;
            target.Position = worldPos;
            // Цель ориентирована так же, как атакующий — иначе спрайт будет «висеть» под углом.
            target.CurrentHeading = attacker.CurrentHeading;
        }

        /// <summary>
        /// Перенос предмета из инвентаря одного корабля в инвентарь другого. Используется UI абордажа.
        /// Не накладывает дополнительных проверок (вес/совместимость со слотом) — это делает UI.
        /// </summary>
        public static bool TransferItem(ShipData from, ShipData to, string itemUid)
        {
            if (from == null || to == null || string.IsNullOrEmpty(itemUid)) return false;
            var item = from.Inventory.TakeByUid(itemUid);
            if (item == null) return false;
            to.Inventory.Add(item);
            return true;
        }
    }
}
