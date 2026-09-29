using UnityEngine;
using SRG.Config;
using SRG.Core;
using SRG.Economy;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.Galaxy.Simulation;
using SRG.Presentation.World;
using SRG.Ships.Movement;

namespace SRG.Ships.Services
{
    /// <summary>
    /// Стыковка со «носителем» — общий термин: и станции (<see cref="ShipData.IsStation"/>),
    /// и обычные корабли-носители (<see cref="ShipTypeConfig.CanBeLandedOn"/>, по конфигу
    /// только линкоры). Пристыкованный корабль скрыт, следует за носителем (перенос позиции
    /// в <see cref="StarSimulator"/>), не участвует в бою и не изнашивается. При потере
    /// носителя (смерть/уход из системы) корабль расстыковывается на месте.
    ///
    /// Планетный UI открывается на общем <see cref="SettlementData"/> носителя через
    /// транзитное <see cref="PlanetData"/> (<see cref="PlanetData.BackingShip"/>). Разница
    /// «станция vs линкор» решается по <see cref="ShipData.IsStation"/>, а не отдельным
    /// флагом на view.
    /// </summary>
    public static class ShipDockingService
    {
        /// <summary>Радиус стыковки: подлёт ближе этого расстояния к центру носителя = посадка.</summary>
        public static float GetDockRadius(ShipData carrier)
            => Mathf.Max(0.25f, (carrier?.SpriteWorldSize ?? 0f) * 0.75f);

        /// <summary>Разрешена ли посадка на корабль. Станции dockable всегда (это их назначение);
        /// у обычных кораблей проверяется <see cref="ShipTypeConfig.CanBeLandedOn"/> в конфиге типа.</summary>
        public static bool TypeAllowsLanding(ShipData carrier)
        {
            if (carrier == null) return false;
            if (carrier.IsStation) return true;
            if (string.IsNullOrEmpty(carrier.ShipTypeId)) return false;
            var types = GalaxyManager.Instance?.Context?.AvailableShipTypes;
            return types != null
                && types.TryGetValue(carrier.ShipTypeId, out var cfg)
                && cfg != null && cfg.CanBeLandedOn;
        }

        /// <summary>Разрешена ли посадка ship на carrier: флаг типа, живость носителя,
        /// не-враждебность, носитель сам не посажен/пристыкован и не уходит в гипер.</summary>
        public static bool CanLandOn(ShipData ship, ShipData carrier, out string reason)
        {
            reason = null;
            if (ship == null || carrier == null || carrier == ship)
            { reason = "нет цели"; return false; }
            if (carrier.IsItem || carrier.CurrentHull <= 0)
            { reason = "цель не действующий корабль"; return false; }
            if (!TypeAllowsLanding(carrier))
            { reason = "на этот тип корабля нельзя садиться"; return false; }
            if (!string.IsNullOrEmpty(carrier.LandedOnShipUid) || !string.IsNullOrEmpty(carrier.LandedPlanetUid))
            { reason = "носитель сам пристыкован/посажен"; return false; }
            if (carrier.HyperjumpPhase != HyperjumpPhase.None)
            { reason = "носитель в гиперпереходе"; return false; }
            var relations = OwnerRaceRelationsManager.Instance;
            if (relations != null && relations.AreHostile(ship, carrier))
            { reason = "носитель враждебен"; return false; }
            return true;
        }

        /// <summary>Пристыковать ship к carrier: позиция = носитель, маршрут заморожен,
        /// гипер/форсаж отменены. Видимость гасит вызывающий код (ShipVisualController).</summary>
        public static void Land(ShipData ship, ShipData carrier)
        {
            HyperjumpController.CancelOnRouteChange(ship);
            ship.LandedOnShipUid   = carrier.Uid;
            ship.Position          = carrier.Position;
            ship.LandingPlanetUid  = null;
            ship.LandingCarrierUid = null;
            ship.LandingPhase      = LandingPhase.None;
            ship.ForsageActive     = false;
            ship.FreezeRoute();
        }

        /// <summary>Расстыковка: корабль остаётся в текущей точке носителя (или своей, если
        /// носитель уже потерян) и снова становится самостоятельным.</summary>
        public static void Undock(ShipData ship, ShipData carrier)
        {
            if (carrier != null && carrier.CurrentHull > 0)
                ship.Position = carrier.Position;
            ship.LandedOnShipUid = null;
            ship.FreezeRoute();
        }

        /// <summary>Подготовить носитель как посадочную цель: создать <see cref="SettlementData"/>
        /// если ещё нет (у линкоров он лениво создаётся при первой стыковке), у станций — ленивое
        /// наполнение магазина оборудования. Никакого транзитного PlanetData не строим — сам
        /// <see cref="ShipData"/> уже реализует <see cref="ILandingSite"/>.</summary>
        public static ILandingSite AsLandingSite(ShipData carrier)
        {
            if (carrier == null) return null;
            carrier.Settlement ??= new SettlementData();

            if (carrier.IsStation)
            {
                var ctx = GalaxyManager.Instance?.Context;
                var shop = carrier.Settlement.EquipmentShop;
                if (ctx != null && (shop == null || shop.Items.Count == 0))
                    EquipmentShopSystem.InitialFill(carrier, ctx);
            }
            return carrier;
        }
    }
}
