using UnityEngine;
using SRG.Config;
using SRG.Core;
using SRG.Economy;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.NpcAI.Orders;
using SRG.Presentation.World;
using SRG.Ships.Movement;
using SRG.Ships.Services;
using SRG.Utils;

namespace SRG.NpcAI.Actions
{
    /// <summary>
    /// Транспорт-торговец: ищет выгодный рейс на ближайших системах, везёт реальный груз.
    /// Состояние хранится в ShipData.Trader* полях — переживает сейв-лоад. См. TraderAI.
    /// </summary>
    public class ActionGoodsTrader : NpcAction
    {
        private NpcAction _jumpAction;   // multi-hop через HyperNavigation.ActionToward
        private OrderLand _landOrder;
        // Idle-таймаут берётся из TraderAI.IdleWaitTurnsConst (единая точка для торговых модулей).

        public override bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
            var cfg = ctx?.Config;
            if (galaxy == null || cfg == null) return false;

            // 0. Гиперпрыжок в активной фазе — ничего не делаем, ждём окончания.
            if (ship.HyperjumpPhase != HyperjumpPhase.None)
                return false;

            // 1. Корабль приземлён.
            if (!string.IsNullOrEmpty(ship.LandedPlanetUid))
            {
                _landOrder = null;
                HandleLanded(ship, star, galaxy, cfg, ctx);
                return false;
            }

            // 2. В полёте: либо ещё в неверной системе, либо в целевой → лететь к планете.
            if (string.IsNullOrEmpty(ship.TraderTargetPlanetUid))
            {
                // Ситуация «летим без цели» — присаживаемся к ближайшей обитаемой планете, чтобы оттуда планировать.
                var nearest = FindNearestInhabitedPlanet(ship, star);
                if (nearest != null) ship.TraderTargetPlanetUid = nearest.Uid;
                ship.TraderTargetStarUid = star.Uid;
                ship.NextPlanetUid = ship.TraderTargetPlanetUid;
                ship.NextStarUid   = ship.TraderTargetStarUid;
            }

            if (!string.IsNullOrEmpty(ship.TraderTargetStarUid) && ship.TraderTargetStarUid != ship.CurrentStarUid)
            {
                // Multi-hop через HyperNavigation.ActionToward: прямой прыжок если долетаем,
                // иначе промежуточная система. При смене фазы (прибыли в промежуточную) — на
                // следующем Tick пересоздадим action.
                if (_jumpAction == null || _jumpAction.IsCompleted)
                    _jumpAction = HyperNavigation.ActionToward(ship, star, ship.TraderTargetStarUid);
                if (_jumpAction == null)
                {
                    // Долететь невозможно (нет двигателя/топлива/пути). Сбрасываем рейс и
                    // берём паузу: TraderAI.PlanRoute переоценит на следующей приземлении.
                    ship.TraderTargetPlanetUid = null;
                    ship.TraderTargetStarUid = null;
                    ship.NextPlanetUid = null;
                    ship.NextStarUid = null;
                    ship.TraderIdleTurnsLeft = TraderAI.IdleWaitTurnsConst;
                    return false;
                }
                _jumpAction.Tick(ship, star, ctx);
                return false;
            }

            // Если мы только что прибыли в целевую систему, сбрасываем jumpAction для следующего рейса.
            _jumpAction = null;

            // 3. Цель пропала из системы (захватили / уничтожили) — обнулим рейс и попробуем заново на следующем тике.
            var targetPlanet = FindPlanetInStar(star, ship.TraderTargetPlanetUid);
            if (targetPlanet == null)
            {
                ship.TraderTargetPlanetUid = null;
                ship.TraderTargetStarUid   = null;
                ship.NextPlanetUid         = null;
                ship.NextStarUid           = null;
                ship.LandingPlanetUid      = null;
                ship.LandingPhase          = LandingPhase.None;
                _landOrder                 = null;
                return false;
            }

            // 4. Мы в нужной системе — посадка через атомарный OrderLand
            //    (фаза подлёта + двухфазная финализация с fade-out, см. PlanetGeometry/ShipVisualController).
            //    Если цель сменилась между ходами — пересоздаём ордер.
            if (_landOrder == null || _landOrder.PlanetUid != ship.TraderTargetPlanetUid)
                _landOrder = new OrderLand(ship.TraderTargetPlanetUid);
            _landOrder.Execute(ship, star, ctx);
            if (_landOrder.IsCompleted(ship, star))
            {
                // Момент посадки: ordering уже проставил LandedPlanetUid. Логируем «LAND»
                // отсюда — order не знает про trader-специфику (cargo, money).
                var planet = FindPlanetInStar(star, ship.LandedPlanetUid);
                if (planet != null)
                {
                    int turn = galaxy.CurrentTurn;
                    EconomicLog.Trade(turn, EconomicLog.Safe(ship.Name), "LAND",
                        $"planet={EconomicLog.Safe(planet.Name)} cargo={(ship.TraderCargoGoodId ?? "-")} " +
                        $"cargo_weight={ship.TraderCargoWeight} money={ship.Money}");
                }
                _landOrder = null;
            }
            return false;
        }

        private void HandleLanded(ShipData ship, StarData star, GalaxyData galaxy, GalaxyConfig cfg, GalaxyGenerationContext ctx)
        {
            var planet = FindPlanetInStar(star, ship.LandedPlanetUid);
            if (planet == null) { ship.LandedPlanetUid = null; return; }

            // Каждый ход «приклеиваем» корабль к текущей позиции планеты — иначе планета
            // улетит по орбите, а транспорт останется висеть в старой точке.
            Vector2 planetPos = OrbitMath.GetPlanetWorldPosition(planet);
            ship.Position = planetPos;
            ship.PreviousPosition = planetPos;
            ship.TargetPosition = planetPos;

            // 1) Есть груз — выгружаем, потом думаем о следующем рейсе.
            if (!string.IsNullOrEmpty(ship.TraderCargoGoodId))
            {
                TraderAI.SellCargo(ship, planet, cfg);
                // После продажи — сразу планируем новый рейс из этой же планеты.
            }

            // 1.5) Общий landed-пайплайн: продажа побочек (не-трейдер goods, useless,
            //      снятое оборудование) → заправка → ремонт корпуса и оборудования → один апгрейд.
            //      Делегация в ActionLandResupply (вариант B из дизайна): pipeline единый для всех NPC.
            ActionLandResupply.RunLandedPipeline(ship, planet, ctx);

            // 2) Ожидание после неудачного поиска: тикаем счётчик.
            if (ship.TraderIdleTurnsLeft > 0)
            {
                ship.TraderIdleTurnsLeft--;
                return;
            }

            // 3) Ищем выгодный рейс из этой планеты (включая варианты «слетать за покупкой»).
            int turn = galaxy.CurrentTurn;
            var route = TraderAI.PlanRoute(ship, star, galaxy, cfg);
            if (!route.Valid)
            {
                ship.TraderIdleTurnsLeft = TraderAI.IdleWaitTurnsConst;
                EconomicLog.Trade(turn, EconomicLog.Safe(ship.Name), "IDLE",
                    $"planet={EconomicLog.Safe(planet.Name)} reason=no_profit money={ship.Money} wait={TraderAI.IdleWaitTurnsConst}");
                return;
            }

            bool buyHere = route.BuyPlanetUid == planet.Uid;
            string buyPlanetName = ResolvePlanetNameByUid(galaxy, route.BuyPlanetUid);
            string sellPlanetName = ResolvePlanetNameByUid(galaxy, route.SellPlanetUid);
            EconomicLog.Trade(turn, EconomicLog.Safe(ship.Name), "PLAN",
                $"good={route.GoodId} amount={route.Amount} " +
                $"buy_at={EconomicLog.Safe(buyPlanetName)}@{route.BuyPrice} sell_at={EconomicLog.Safe(sellPlanetName)}@{route.SellPrice} " +
                $"fuel_leg1={route.FuelLeg1} fuel_leg2={route.FuelLeg2} profit={route.ProfitTotal} buy_here={buyHere}");

            if (buyHere)
            {
                // Купить тут и лететь продавать
                if (!TraderAI.BuyCargo(ship, planet, route.GoodId, route.Amount, cfg))
                {
                    ship.TraderIdleTurnsLeft = TraderAI.IdleWaitTurnsConst;
                    EconomicLog.Trade(turn, EconomicLog.Safe(ship.Name), "IDLE",
                        $"planet={EconomicLog.Safe(planet.Name)} reason=buy_failed wait={TraderAI.IdleWaitTurnsConst}");
                    return;
                }
                ship.TraderTargetPlanetUid = route.SellPlanetUid;
                ship.TraderTargetStarUid   = route.SellStarUid;
            }
            else
            {
                // Лететь за покупкой в другую систему/планету (не покупаем здесь).
                ship.TraderTargetPlanetUid = route.BuyPlanetUid;
                ship.TraderTargetStarUid   = route.BuyStarUid;
            }
            _jumpAction = null;

            // Взлёт через общий LaunchService.Depart (RecordLastVisitAndLaunch + outward course).
            // Зеркалим trader-цели в общие поля «следующий визит» для тегов диалога — ДО Depart,
            // чтобы UI подхватил.
            ship.NextPlanetUid = ship.TraderTargetPlanetUid;
            ship.NextStarUid   = ship.TraderTargetStarUid;
            LaunchService.Depart(ship);

            string nextTargetName = ResolvePlanetNameByUid(galaxy, ship.TraderTargetPlanetUid);
            EconomicLog.Trade(turn, EconomicLog.Safe(ship.Name), "LAUNCH",
                $"from={EconomicLog.Safe(planet.Name)} to={EconomicLog.Safe(nextTargetName)} " +
                $"cargo={(ship.TraderCargoGoodId ?? "-")} cargo_weight={ship.TraderCargoWeight}");
        }

        private static string ResolvePlanetNameByUid(GalaxyData galaxy, string uid)
        {
            if (galaxy == null || string.IsNullOrEmpty(uid)) return "-";
            return galaxy.PlanetsMap.TryGetValue(uid, out var p) ? (p.Name ?? uid) : uid;
        }

        private static PlanetData FindPlanetInStar(StarData star, string uid)
        {
            if (star?.Planets == null || string.IsNullOrEmpty(uid)) return null;
            foreach (var p in star.Planets) if (p.Uid == uid) return p;
            return null;
        }

        private static PlanetData FindNearestInhabitedPlanet(ShipData ship, StarData star)
        {
            if (star?.Planets == null) return null;
            PlanetData best = null;
            float bestDist = float.MaxValue;
            foreach (var p in star.Planets)
            {
                if (string.IsNullOrEmpty(p.Race) || p.Race == GalaxyConstants.RACE_NONE_KEY) continue;
                if (p.Settlement.Population <= 0) continue;
                Vector2 pos = OrbitMath.GetPlanetWorldPosition(p);
                float d = (pos - ship.Position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = p; }
            }
            return best;
        }

        public override string DebugName => "GoodsTrade";
    }
}
