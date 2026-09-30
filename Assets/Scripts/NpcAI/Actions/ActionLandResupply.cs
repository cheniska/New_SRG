using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Economy;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Galaxy.Politics;
using SRG.NpcAI.Orders;
using SRG.Ships.Services;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.NpcAI.Actions
{
    /// <summary>
    /// Универсальная процедура «сесть на планету и восстановить боеспособность»:
    /// продать лишнее → дозаправка → ремонт (корпус + оборудование) → одна попытка апгрейда → взлёт.
    ///
    /// Используется двумя способами:
    ///   • Как самостоятельная активность NpcBrain: триггерится composite pressure-score
    ///     (см. <see cref="NpcBalance.Resupply_PressureThreshold"/>).
    ///   • Как делегат внутри других действий (например, <see cref="ActionGoodsTrader"/>
    ///     переключается сюда на посадке — вариант B из дизайна).
    ///
    /// «Продать лишнее» покрывает три категории:
    ///   1. Товары (ItemStack.IsGoods) — через ShopService.TrySell по цене SellPrice планеты.
    ///      КРОМЕ активного торгового груза (<see cref="ShipData.TraderCargoGoodId"/>) — его
    ///      планирует продать сам TraderAI по своему маршруту.
    ///   2. Useless-стеки — 50% BasePrice за единицу, деньги «на планету» (utilities-эквивалент
    ///      pawn-shop). Для найденных статуэток/минералов.
    ///   3. Useless-предметы и снятое оборудование — 50% Price за штуку.
    /// </summary>
    public class ActionLandResupply : NpcAction
    {
        private enum Phase { FlyToPlanet, Landed, Depart, Done }
        private Phase _phase = Phase.FlyToPlanet;
        private OrderLand _landOrder;
        private string _targetPlanetUid;
        private readonly bool _departOnly;

        public string TargetPlanetUid => _targetPlanetUid;

        public override string DebugName => _departOnly
            ? $"Depart(phase={_phase})"
            : $"LandResupply(phase={_phase})";

        /// <summary>Стандартный конструктор — полный цикл (Sell/Repair/Fuel/Upgrade + взлёт).</summary>
        public ActionLandResupply() { _departOnly = false; }

        /// <summary>DepartOnly-режим: пропускает <see cref="RunLandedPipeline"/> в фазе Landed,
        /// сразу переходит в TickDepart. Используется <see cref="NpcBrain.ChooseDefaultActivity"/>
        /// для «просто взлететь с планеты и выбрать классовый action», без магазинных операций.</summary>
        public static ActionLandResupply DepartOnly() => new ActionLandResupply(departOnly: true);
        private ActionLandResupply(bool departOnly) { _departOnly = departOnly; }

        public override bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            if (ship == null || star == null || ctx == null) { IsCompleted = true; return true; }

            // Если уже приземлились (например, действие подхватило корабль в доке — трейдер) — сразу выполнить.
            if (_phase == Phase.FlyToPlanet && !string.IsNullOrEmpty(ship.LandedPlanetUid))
            {
                _targetPlanetUid = ship.LandedPlanetUid;
                _phase = Phase.Landed;
            }

            switch (_phase)
            {
                case Phase.FlyToPlanet: return TickFly(ship, star, ctx);
                case Phase.Landed:      return TickLanded(ship, star, ctx);
                case Phase.Depart:      return TickDepart(ship, star, ctx);
                default:                IsCompleted = true; return true;
            }
        }

        private bool TickFly(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            if (string.IsNullOrEmpty(_targetPlanetUid))
            {
                var planet = FindLandingPlanet(ship, star);
                if (planet == null) { IsCompleted = true; return true; }
                _targetPlanetUid = planet.Uid;
            }

            _landOrder ??= new OrderLand(_targetPlanetUid);
            _landOrder.Execute(ship, star, ctx);
            if (_landOrder.IsCompleted(ship, star))
            {
                _landOrder = null;
                _phase = Phase.Landed;
            }
            return false;
        }

        private bool TickLanded(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            var planet = star?.FindPlanet(ship.LandedPlanetUid ?? _targetPlanetUid);
            if (planet == null) { IsCompleted = true; return true; }
            if (!_departOnly) RunLandedPipeline(ship, planet, ctx);
            _phase = Phase.Depart;
            return false;
        }

        /// <summary>
        /// Общий «landed»-пайплайн: продажа лишнего → дозаправка → ремонт → апгрейд.
        /// Без взлёта и полёта — используется внешними действиями (например, ActionGoodsTrader
        /// после SellCargo), которые сами управляют посадкой/взлётом. Идемпотентен: повторный
        /// вызов на той же планете без изменений просто ничего не сделает.
        /// </summary>
        public static void RunLandedPipeline(ShipData ship, PlanetData planet, GalaxyGenerationContext ctx)
        {
            if (ship == null || planet == null || ctx == null) return;
            SellSurplus(ship, planet, ctx);
            FuelService.RefillToFull(ship, planet);
            RepairService.RepairShipAtPlanet(ship, planet);
            NpcOutfitter.TryUpgrade(ship, planet, ctx);
        }

        private bool TickDepart(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            // Взлёт через общий LaunchService.Depart (RecordLastVisitAndLaunch + outward course + clear queue).
            SRG.Ships.Services.LaunchService.Depart(ship);
            _phase = Phase.Done;
            IsCompleted = true;
            return true;
        }

        // ── Продажа излишков ─────────────────────────────────────────────────────

        /// <summary>
        /// Продаёт всё, что нет смысла держать: товары (кроме активного трейдер-груза),
        /// useless-стеки, useless-предметы, снятое оборудование (не в слоте).
        /// Публичный — используется трейдером напрямую в HandleLanded для сдачи «побочек».
        /// </summary>
        // Переиспользуемые буферы для SellSurplus. Разгружает GC на ходах с множественными
        // trader-посадками (было new List<string>(N) на каждую посадку × два раза).
        private static readonly List<string> _sellStackIdsBuffer = new();
        private static readonly List<string> _sellItemUidsBuffer = new();

        public static int SellSurplus(ShipData ship, PlanetData planet, GalaxyGenerationContext ctx)
        {
            if (ship == null || planet == null) return 0;
            int totalIncome = 0;

            var cfg = ctx?.Config;

            // 1. Стеки: товары через ShopService, useless — прямой pawn (0.5 × BasePrice).
            var stackIds = _sellStackIdsBuffer;
            stackIds.Clear();
            stackIds.AddRange(ship.Inventory.Stacks.Keys);
            foreach (var goodId in stackIds)
            {
                if (!ship.Inventory.Stacks.TryGetValue(goodId, out var stack)) continue;
                if (stack.TotalWeight <= 0) continue;

                if (stack.IsGoods)
                {
                    if (goodId == ship.TraderCargoGoodId) continue; // трейдер сам решит когда продавать
                    if (cfg != null)
                        totalIncome += SellGoodsStack(ship, planet, goodId, stack.TotalWeight, cfg);
                }
                else
                {
                    totalIncome += PawnUselessStack(ship, goodId, stack.TotalWeight);
                }
            }

            // 2. ItemInstance: неэкипированные предметы. Оборудование — 0.5 × Price в магазин;
            //    useless-предметы — тот же коэффициент, но деньги «в никуда» (нет отдельного pawn-shop).
            var itemUids = _sellItemUidsBuffer;
            itemUids.Clear();
            itemUids.AddRange(ship.AllItems.Keys);
            foreach (var uid in itemUids)
            {
                if (!ship.AllItems.TryGetValue(uid, out var item)) continue;
                if (IsEquipped(ship, uid)) continue;

                if (item.IsEquipment)
                {
                    totalIncome += NpcOutfitter.SellItemToShop(ship, planet, item);
                }
                else if (item.IsUseless)
                {
                    int price = Mathf.RoundToInt(item.Price * 0.5f);
                    ship.AllItems.Remove(uid);
                    ship.Inventory.Remove(uid);
                    ship.Money += price;
                    totalIncome += price;
                }
            }

            if (totalIncome > 0)
            {
                int turn = GameWorld.GeneratedGalaxy?.CurrentTurn ?? 0;
                EconomicLog.Trade(turn, EconomicLog.Safe(ship.Name), "SELL_SURPLUS",
                    $"planet={EconomicLog.Safe(planet.Name)} total={totalIncome} money_after={ship.Money}");
            }
            return totalIncome;
        }

        private static int SellGoodsStack(ShipData ship, PlanetData planet, string goodId, int amount, GalaxyConfig cfg)
        {
            ShopService.TrySell(ship, planet, goodId, amount, cfg, out int income);
            return income;
        }

        private static int PawnUselessStack(ShipData ship, string itemId, int amount)
        {
            if (!ship.Inventory.Stacks.TryGetValue(itemId, out var stack)) return 0;
            int unit = Mathf.Max(1, Mathf.RoundToInt(stack.BasePrice * 0.5f));
            int payout = unit * amount;
            var taken = ship.Inventory.TakeStack(itemId, amount);
            if (taken == null || taken.TotalWeight == 0) return 0;
            ship.Money += payout;
            return payout;
        }

        private static bool IsEquipped(ShipData ship, string itemUid)
        {
            foreach (var slotUid in ship.Equipment.Slots.Values)
                if (slotUid == itemUid) return true;
            return false;
        }

        // ── Выбор посадочной планеты ─────────────────────────────────────────────

        private static PlanetData FindLandingPlanet(ShipData ship, StarData star)
        {
            if (star?.Planets == null) return null;
            var relations = OwnerRaceRelationsManager.Instance;
            PlanetData best = null;
            float bestDistSq = float.MaxValue;

            foreach (var p in star.Planets)
            {
                if (string.IsNullOrEmpty(p.Race) || p.Race == GalaxyConstants.RACE_NONE_KEY) continue;
                if (p.Settlement.Population <= 0) continue;
                bool hostile = relations != null && relations.AreHostile(ship, p);
                if (hostile) continue;
                // Блокировка посадки: не садимся, если не член контроллёра планеты.
                if (p.Settlement.LandingBlocked && OccupationService.GetControllingOwner(p) != ship.Owner) continue;

                Vector2 pos = OrbitMath.GetPlanetPositionSimple(p);
                float d = (pos - ship.Position).sqrMagnitude;
                if (d < bestDistSq) { bestDistSq = d; best = p; }
            }
            return best;
        }

    }
}
