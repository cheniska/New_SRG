using UnityEngine;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.NpcAI.Orders;
using SRG.Ships.Movement;
using SRG.Ships.Services;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.NpcAI.Actions
{
    /// <summary>
    /// Действие «сопровождать лидера партнёрства». Оборачивает готовую логику ActionEscort
    /// в собственной звезде и добавляет автогиперпрыжок вслед за лидером в другую звезду.
    ///
    /// Личностные вариации:
    ///  * Высокая Aggression (>70) + высокая Discipline (>60) → перенимает CombatTarget лидера
    ///    даже если рядом нет прямой угрозы (берсерк-режим — атакует того, кого бьёт босс).
    ///  * Низкая Discipline (&lt;25) + низкий HullPct (&lt;FleeHullPercent) → дезертирует (Flee).
    ///    Следующий loyalty-check в NpcSystem.TickStar уронит attitude и уже он разорвёт связь.
    ///  * Все остальные случаи — обычный Escort (follow + threat-response) или HyperJump за лидером.
    ///
    /// Дрон-специфика: если ship.ShipTypeId помечен как IsDrone и он рядом с хозяином, действие
    /// перед вызовом sub-action выполняет side-effect'ы: перелив топлива и попытку упаковки
    /// при активном DroneReturnPending. Это заменяет отдельный DroneAutoServiceSystem.
    /// </summary>
    public class ActionPartnerAttend : NpcAction
    {
        private readonly string _leaderUid;
        private NpcAction _subAction;
        private string _subActionKind;

        // Параметры дрон-сервиса. Вынести в NpcBalance при необходимости.
        private const float DroneServiceRangeSq = 2.5f * 2.5f;
        private const int   DroneFuelTransferPerTurn = 5;

        public string LeaderUid => _leaderUid;
        public override string CombatTargetUid => _subAction?.CombatTargetUid;

        public ActionPartnerAttend(string leaderUid) => _leaderUid = leaderUid;

        public override bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            // Лидер потерян (умер/удалён) — завершаем: следующий тик Brain выберет дефолт.
            ShipData leader = PartnerService.FindShipInGalaxy(_leaderUid);
            if (leader == null || leader.CurrentHull <= 0) { IsCompleted = true; return true; }

            // Синхронизация проверяется каждый ход: контракта нет — выходим.
            if (ship.PartnerLeaderUid != _leaderUid) { IsCompleted = true; return true; }

            // Дрон рядом с хозяином — автообслуживание. Если удалось упаковаться, ship удалён
            // из звезды, sub-action тикать не нужно.
            if (IsDrone(ship) && HandleDroneServicing(ship, leader))
            {
                IsCompleted = true;
                return true;
            }

            string desired = DecideKind(ship, leader);

            if (_subAction == null || _subAction.IsCompleted || desired != _subActionKind)
            {
                _subAction = BuildSub(desired, ship, leader);
                _subActionKind = desired;
            }

            _subAction?.Tick(ship, star, ctx);
            return false;
        }

        /// <summary>Проверяет, нужно ли сделать pack (возврат) и подкачать топливо. Возвращает true,
        /// если дрон только что запакован — тогда живого корабля больше нет.</summary>
        private static bool HandleDroneServicing(ShipData drone, ShipData host)
        {
            if (drone.CurrentStarUid != host.CurrentStarUid) return false;
            if ((drone.Position - host.Position).sqrMagnitude > DroneServiceRangeSq) return false;

            // Возврат — упаковка при контакте.
            if (DroneService.TryFinishReturn(drone)) return true;

            // Перелив топлива.
            var droneTank = EquipmentSystem.GetEquipped(drone, SlotKeys.FuelTank);
            var hostTank  = EquipmentSystem.GetEquipped(host,  SlotKeys.FuelTank);
            if (droneTank != null && hostTank != null && hostTank.CurrentFuel > 0)
            {
                int cap = (int)droneTank.GetParam("Capacity", droneTank.CurrentFuel);
                int need = cap - droneTank.CurrentFuel;
                if (need > 0)
                {
                    int transfer = UnityEngine.Mathf.Min(DroneFuelTransferPerTurn, UnityEngine.Mathf.Min(need, hostTank.CurrentFuel));
                    if (transfer > 0)
                    {
                        hostTank.CurrentFuel -= transfer;
                        droneTank.CurrentFuel += transfer;
                    }
                }
            }
            return false;
        }

        private static bool IsDrone(ShipData ship)
        {
            var types = GameWorld.Context?.Config?.Ships?.ShipTypes;
            return types != null
                && !string.IsNullOrEmpty(ship.ShipTypeId)
                && types.TryGetValue(ship.ShipTypeId, out var t)
                && t.IsDrone;
        }

        private string DecideKind(ShipData ship, ShipData leader)
        {
            bool crossStar = ship.CurrentStarUid != leader.CurrentStarUid;

            // 0. Явный приказ лидера имеет приоритет над всем. FlyToMe при cross-star
            // означает «прыгнуть за лидером» — иначе Escort мгновенно завершится
            // (ActionEscort ищет цель только в текущей звезде).
            switch (ship.PartnerOrder)
            {
                case PartnerOrderKind.Attack    when !string.IsNullOrEmpty(ship.PartnerOrderTargetUid): return "OrderAttack";
                case PartnerOrderKind.LandOn    when !string.IsNullOrEmpty(ship.PartnerOrderTargetUid): return "OrderLand";
                case PartnerOrderKind.FlyToStar when !string.IsNullOrEmpty(ship.PartnerOrderTargetUid): return "OrderStar";
                case PartnerOrderKind.FlyToMe: return crossStar ? "Jump" : "Escort";
            }

            // 1. В разных звёздах — прыгаем к лидеру.
            if (crossStar) return "Jump";

            var p = ship.Personality;
            if (p != null)
            {
                // 2. Дезертирство: низкая Discipline + низкое HP + рядом враг.
                float hullPct = ship.MaxHull > 0 ? (float)ship.CurrentHull / ship.MaxHull : 1f;
                if (p.Discipline < 25f && hullPct < p.FleeHullPercent) return "Desert";

                // 3. Берсерк: высокая Aggression+Discipline и лидер в бою → перенимаем его цель.
                if (p.Aggression > 70f && p.Discipline > 60f)
                {
                    string leaderTarget = leader.Brain?.GetCombatTargetUid();
                    if (!string.IsNullOrEmpty(leaderTarget)) return "Berserk";
                }
            }

            // 4. Дефолт: Escort (follow + защита от локальных угроз).
            return "Escort";
        }

        private NpcAction BuildSub(string kind, ShipData ship, ShipData leader)
        {
            switch (kind)
            {
                case "OrderAttack":
                    // Дисциплина ниже 30 — часть приказов игнорируется (шанс = discipline/100).
                    if (RollObeyOrder(ship))
                        return new ActionPursueAndAttack(ship.PartnerOrderTargetUid);
                    ClearOrder(ship);
                    return new ActionEscort(leader.Uid);

                case "OrderLand":
                    // OrderLand: сейчас поддерживаем только Land на планету через LandingPlanetUid.
                    // Реализация приказа = проставить LandingPlanetUid и передать управление обычному
                    // конвейеру посадки. По сути, ставим "Land" в поле и завершаем приказ.
                    // AllowLand=false — лидер сам же запретил посадки: приказ отбрасываем,
                    // чтобы запрет нельзя было обойти собственным приказом.
                    if (ship.AllowLand && RollObeyOrder(ship))
                    {
                        ship.LandingPlanetUid = ship.PartnerOrderTargetUid;
                        ClearOrder(ship);
                        return new ActionEscort(leader.Uid);
                    }
                    ClearOrder(ship);
                    return new ActionEscort(leader.Uid);

                case "OrderStar":
                    if (RollObeyOrder(ship))
                        return HyperNavigation.ActionToward(ship, ship.CurrentStar, ship.PartnerOrderTargetUid)
                               ?? (NpcAction)new ActionHyperJumpTo(ship.PartnerOrderTargetUid);
                    ClearOrder(ship);
                    return new ActionEscort(leader.Uid);

                case "Jump":
                    // multi-hop к лидеру: если долетаем — прямой прыжок, иначе промежуточный хоп.
                    // Fallback на прямой ActionHyperJumpTo, если ActionToward не найдёт путь
                    // (сохраняет старое поведение).
                    return HyperNavigation.ActionToward(ship, ship.CurrentStar, leader.CurrentStarUid)
                           ?? (NpcAction)new ActionHyperJumpTo(leader.CurrentStarUid);

                case "Desert":
                {
                    // Ищем ближайшего явно враждебного корабля как цель Flee.
                    // Если такого нет — обычный Escort (нечего убегать).
                    string threat = NpcTargeting.FindNearestHostileUid(ship.CurrentStar, ship, ship.Position);
                    return threat != null ? new ActionFlee(threat) : new ActionEscort(leader.Uid);
                }

                case "Berserk":
                {
                    string leaderTarget = leader.Brain?.GetCombatTargetUid();
                    return !string.IsNullOrEmpty(leaderTarget)
                        ? new ActionPursueAndAttack(leaderTarget)
                        : new ActionEscort(leader.Uid);
                }

                default: return new ActionEscort(leader.Uid);
            }
        }

        private static bool RollObeyOrder(ShipData ship)
            => DisciplineCheck.ShouldObey(ship?.Personality, DisciplineCheck.OrderKind.PartnerOrder);

        private static void ClearOrder(ShipData ship)
        {
            // Для дрона дефолт — FlyToMe (машина всегда обязана следовать за хостом);
            // для живых партнёров — None (тогда включается Desert/Berserk-личность).
            ship.PartnerOrder = IsDrone(ship) ? PartnerOrderKind.FlyToMe : PartnerOrderKind.None;
            ship.PartnerOrderTargetUid = null;
        }

        public override string DebugName => $"PartnerAttend({SpriteUtility.ShortId(_leaderUid)}/{_subActionKind})";
    }
}
