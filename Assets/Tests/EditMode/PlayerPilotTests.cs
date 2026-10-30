using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Ships.Player;
using SRG.Simulation;
using UnityEngine;

namespace SRG.Tests
{
    /// <summary>
    /// Команды игрока (<see cref="PlayerPilot"/>) без сцены: маршрут, следование, выстрел,
    /// конец маршрута. Пилотом управляется обычный корабль сгенерированного мира.
    /// </summary>
    public class PlayerPilotTests
    {
        private sealed class FakeView : IPlayerPilotView
        {
            public int Draws, Clears;
            public List<Vector2> LastChain;
            public void DrawRoute(List<Vector2> chain, float dayStep, Vector2? endMarker) { Draws++; LastChain = chain; }
            public void ClearRoute() => Clears++;
            public void OnLanded(PlanetData planet) { }
            public void OnDockedOnShip() { }
            public void OnTakeoff() { }
            public void OnLandingCancelled() { }
            public void ShowMessage(string text) { }
        }

        private TestWorld _world;
        private StarData _star;
        private ShipData _ship;
        private FakeView _view;
        private PlayerPilot _pilot;

        [SetUp]
        public void SetUp()
        {
            _world = TestWorld.Generate(31337);
            // Звезда, где есть корабль со свободным оружейным слотом и ещё хотя бы один живой корабль.
            foreach (var star in _world.Session.ActiveGalaxy.StarsMap.Values)
            {
                var ship = star.Ships.FirstOrDefault(s => !s.IsItem && !s.IsStation && s.CurrentHull > 0
                                                         && FreeWeaponSlot(s) != null);
                if (ship == null || star.Ships.Count(s => !s.IsItem && s.CurrentHull > 0) < 2) continue;
                _star = star;
                _ship = ship;
                break;
            }
            Assert.IsNotNull(_ship, "В мире нет звезды с подходящим кораблём и соседом");

            // Пушку ставим явно: проверяется пилот, а не стартовая комплектация NPC.
            var ctx = _world.Session.Context;
            var cannon = ItemFactory.Create("Weapons", "W_Cannon", ctx.ItemsConfig, ctx.Config);
            InventoryService.EquipToSlot(_ship, FreeWeaponSlot(_ship), cannon);
            _world.Host.CurrentStar = _star;
            _view = new FakeView();
            _pilot = new PlayerPilot(_ship, settings: null, view: _view);
        }

        [TearDown]
        public void TearDown() => _world?.Dispose();

        private static string FreeWeaponSlot(ShipData ship)
            => ship.Equipment.Slots.Where(kv => kv.Key.StartsWith("Weapons_") && kv.Value == null)
                   .Select(kv => kv.Key).FirstOrDefault();

        private ShipData Other() => _star.Ships.First(s => s != _ship && !s.IsItem && s.CurrentHull > 0);

        [Test]
        public void FlyTo_ReplacesOrAppendsRoute_AndDrawsIt()
        {
            var a = _ship.Position + new Vector2(5f, 0f);
            var b = _ship.Position + new Vector2(5f, 5f);

            _pilot.FlyTo(a, append: false);
            _pilot.FlyTo(b, append: true);
            Assert.AreEqual(a, _ship.TargetPosition);
            CollectionAssert.AreEqual(new[] { b }, _ship.TargetQueue.ToArray());
            Assert.IsTrue(_pilot.HasPlannedRoute());
            Assert.Greater(_view.Draws, 0);
            Assert.AreEqual(_ship.Position, _view.LastChain[0]);

            _pilot.FlyTo(b, append: false);
            Assert.AreEqual(b, _ship.TargetPosition);
            Assert.IsEmpty(_ship.TargetQueue);
        }

        [Test]
        public void ClearRoute_StopsShip()
        {
            _pilot.FlyTo(_ship.Position + new Vector2(5f, 0f), append: false);
            _pilot.ClearRoute();
            Assert.IsFalse(_pilot.HasPlannedRoute());
            Assert.AreEqual(_ship.Position, _ship.TargetPosition);
            Assert.Greater(_view.Clears, 0);
        }

        [Test]
        public void RouteEnd_RequestsPlanningPause()
        {
            var dest = _ship.Position + new Vector2(3f, 0f);
            _pilot.FlyTo(dest, append: false);
            _ship.Position = dest; // как будто долетели за ход

            _pilot.OnTurnCompleted();

            Assert.IsTrue(_world.Host.PlanningRequests.Contains(PlanningReason.RouteCompleted));
            Assert.IsFalse(_pilot.HasPlannedRoute());
        }

        [Test]
        public void FollowOrCycle_StartsPursuit_RepeatClickSwitchesMode()
        {
            var target = Other();
            _pilot.FollowOrCycle(target);
            Assert.AreEqual(target.Uid, _pilot.FollowShipUid);
            Assert.AreEqual(PlayerFollowMode.PursueAtMaxRange, _pilot.CurrentFollowMode);

            _pilot.FollowOrCycle(target);
            Assert.AreEqual(PlayerFollowMode.AttackWithLongRange, _pilot.CurrentFollowMode);

            // Атакующий режим взводит автоогонь по цели.
            _pilot.UpdateBetweenTurns();
            Assert.AreEqual(target.Uid, _ship.ManualShootTargetUid);
            Assert.IsTrue(_ship.ManualShootIsAutoFollow);
        }

        [Test]
        public void FollowPoint_NeverMergesWithTarget()
        {
            var target = Other();
            _pilot.SetFollow(target, PlayerFollowMode.FollowClose);
            float minStop = Mathf.Max(_ship.SpriteWorldSize, target.SpriteWorldSize) * 1.5f;
            float dist = (target.Position - _pilot.ComputeFollowPoint(target)).magnitude;
            Assert.AreEqual(minStop, dist, 1e-3f);
        }

        [Test]
        public void LostFollowTarget_ClearsFollowAndAutoFire()
        {
            var target = Other();
            _pilot.SetFollow(target, PlayerFollowMode.AttackWithAllGuns);
            _pilot.UpdateBetweenTurns();
            Assert.IsTrue(_ship.ManualShootIsAutoFollow);

            _star.Ships.Remove(target);
            _pilot.UpdateBetweenTurns();

            Assert.IsFalse(_pilot.IsFollowingShip);
            Assert.IsFalse(_ship.ManualShootIsAutoFollow);
            Assert.IsNull(_ship.ManualShootTargetUid);
        }

        [Test]
        public void SelectShotTarget_RespectsWeaponRange()
        {
            var target = Other();
            float range = _pilot.GetMaxWeaponRange();

            target.Position = _ship.Position + new Vector2(range * 2f, 0f);
            Assert.IsFalse(_pilot.SelectShotTarget(target, null));
            Assert.AreNotEqual(target.Uid, _ship.ManualShootTargetUid);

            target.Position = _ship.Position + new Vector2(range * 0.5f, 0f);
            Assert.IsTrue(_pilot.SelectShotTarget(target, null));
            Assert.AreEqual(target.Uid, _ship.ManualShootTargetUid);
            Assert.IsFalse(_ship.ManualShootTargetIsAsteroid);
        }
    }
}
