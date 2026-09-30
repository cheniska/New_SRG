using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using SRG.Config;
using SRG.Galaxy.Generation;
using SRG.Galaxy;
using SRG.Simulation;
using SRG.UI.Logic;

namespace SRG.Tests
{
    public class UiPresenterTests
    {
        [TearDown]
        public void TearDown() => GameWorld.ResetForTests();

        private static PlanetData PlanetWithGood(string goodId, int stock, int buy, int sell)
        {
            var planet = new PlanetData { Name = "Тест" };
            planet.Settlement.Shop.Goods[goodId] = new ShopGoodEntry { Stock = stock, BuyPrice = buy, SellPrice = sell };
            return planet;
        }

        // ── Магазин товаров ────────────────────────────────────────────────

        [Test]
        public void GoodsShop_Rows_ReflectShopEntries()
        {
            var rows = new GoodsShopPresenter(PlanetWithGood("Food", 40, 12, 8), cfg: null).BuildRows();
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("Food", rows[0].GoodId);
            Assert.AreEqual(12, rows[0].BuyPrice);
            Assert.AreEqual(8, rows[0].SellPrice);
            Assert.AreEqual(40, rows[0].Stock);
            Assert.AreEqual("Food", rows[0].Label);
        }

        [Test]
        public void GoodsShop_IllegalGood_IsMarkedRed()
        {
            var row = new GoodsRowModel { DisplayName = "Наркотики", IsIllegal = true };
            Assert.AreEqual("<color=#E05050>Наркотики*</color>", row.Label);
        }

        [Test]
        public void GoodsShop_PrepareBuy_LimitedByStockAndMoney()
        {
            var presenter = new GoodsShopPresenter(PlanetWithGood("Food", 40, 10, 5), cfg: null);

            var rich = presenter.PrepareBuy(new ShipData { Money = 10_000 }, "Food", out var e1);
            Assert.IsNull(e1);
            Assert.AreEqual(40, rich.Max, "ограничено стоком");

            var poor = presenter.PrepareBuy(new ShipData { Money = 55 }, "Food", out _);
            Assert.AreEqual(5, poor.Max, "ограничено деньгами");
            Assert.AreEqual("3 / 5   (30 кр.)", poor.FormatAmount(3));

            var broke = presenter.PrepareBuy(new ShipData { Money = 5 }, "Food", out var e3);
            Assert.IsNull(broke);
            Assert.AreEqual("[Магазин] Недостаточно кредитов.", e3);
        }

        [Test]
        public void GoodsShop_PrepareBuy_OutOfStock()
        {
            var presenter = new GoodsShopPresenter(PlanetWithGood("Food", 0, 10, 5), cfg: null);
            Assert.IsNull(presenter.PrepareBuy(new ShipData { Money = 100 }, "Food", out var error));
            Assert.AreEqual("[Магазин] Товара нет в наличии.", error);
        }

        [Test]
        public void GoodsShop_PrepareSell_RequiresCargo()
        {
            var presenter = new GoodsShopPresenter(PlanetWithGood("Food", 10, 10, 7), cfg: null);
            Assert.IsNull(presenter.PrepareSell(new ShipData(), "Food", out var error));
            Assert.AreEqual("[Магазин] Нет такого товара в трюме.", error);

            var ship = new ShipData();
            ship.Inventory.Stacks["Food"] = new ItemStack { ItemId = "Food", Name = "Еда", TotalWeight = 12 };
            var prompt = presenter.PrepareSell(ship, "Food", out _);
            Assert.AreEqual(12, prompt.Max);
            Assert.AreEqual(7, prompt.UnitPrice);
        }

        // ── Магазин оборудования ──────────────────────────────────────────

        private static ItemInstance Item(string name, int price) =>
            new ItemInstance { Name = name, Category = "Engine", Price = price, TechLevel = 3 };

        [Test]
        public void EquipmentShop_Buy_NotEnoughMoney()
        {
            var planet = new PlanetData();
            var item = Item("Двигатель", 500);
            planet.Settlement.EquipmentShop.Items[item.Uid] = item;
            var ship = new ShipData { Money = 100 };

            var result = new EquipmentShopPresenter(planet).Buy(ship, item.Uid);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("[Магазин] Недостаточно кредитов.", result.Messages[0]);
            Assert.AreEqual(100, ship.Money);
            Assert.IsTrue(planet.Settlement.EquipmentShop.Items.ContainsKey(item.Uid));
        }

        [Test]
        public void EquipmentShop_Buy_TakesMoneyAndItem()
        {
            var planet = new PlanetData();
            var item = Item("Двигатель", 500);
            planet.Settlement.EquipmentShop.Items[item.Uid] = item;
            var ship = new ShipData { Money = 800 };

            var result = new EquipmentShopPresenter(planet).Buy(ship, item.Uid);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(300, ship.Money);
            Assert.IsFalse(planet.Settlement.EquipmentShop.Items.ContainsKey(item.Uid));
        }

        [Test]
        public void EquipmentShop_Sell_HalfPrice_OnlyUnequipped()
        {
            var planet = new PlanetData();
            var ship = new ShipData { Money = 0 };
            var loose = Item("Запасной", 301);
            var installed = Item("Установленный", 1000);
            ship.AllItems[loose.Uid] = loose;
            ship.AllItems[installed.Uid] = installed;
            ship.Equipment.Slots["Engine_0"] = installed.Uid;
            var presenter = new EquipmentShopPresenter(planet);

            var sellRows = presenter.BuildSellRows(ship);
            Assert.AreEqual(1, sellRows.Count);
            Assert.AreEqual(150, sellRows[0].Price); // round(301 × 0.5) — банковское округление, как Mathf.RoundToInt

            Assert.IsFalse(presenter.Sell(ship, installed.Uid).Success);
            Assert.IsTrue(presenter.Sell(ship, loose.Uid).Success);
            Assert.AreEqual(150, ship.Money);
            Assert.IsTrue(planet.Settlement.EquipmentShop.Items.ContainsKey(loose.Uid));
            Assert.IsFalse(ship.AllItems.ContainsKey(loose.Uid));
        }

        // ── Ангар ──────────────────────────────────────────────────────────

        [Test]
        public void Hangar_ButtonLabels_WithoutShip()
        {
            var labels = HangarPresenter.ButtonLabels(null);
            Assert.AreEqual("Дозаправиться\n(полный)", labels.Refuel);
            Assert.AreEqual("Зарядить оружие\n(нет ракет)", labels.Reload);
        }

        [Test]
        public void Hangar_DockedShips_OnlyAliveForeignShipsOnThisSite()
        {
            var planet = new PlanetData();
            var star = new StarData();
            star.Ships.Add(new ShipData { Name = "Гость", ShipTypeId = "Transport", LandedPlanetUid = planet.Uid, CurrentHull = 10 });
            star.Ships.Add(new ShipData { Name = "Мимо", LandedPlanetUid = "другая", CurrentHull = 10 });
            star.Ships.Add(new ShipData { Name = "Обломки", LandedPlanetUid = planet.Uid, CurrentHull = 0 });

            var docked = new HangarPresenter(planet).DockedShips(star);
            Assert.AreEqual(1, docked.Count);
            Assert.AreEqual("[Transport] Гость", docked[0].Label);
            Assert.AreEqual("—", docked[0].Race);
            Assert.AreEqual("Корабли на объекте: нет", HangarPresenter.DockedHeader(0));
        }

        [Test]
        public void Hangar_Summary_WithoutShip() =>
            Assert.AreEqual("Нет данных о корабле.", HangarPresenter.ShipSummary(null));

        // ── Описание предмета ─────────────────────────────────────────────

        [Test]
        public void ItemDescription_ContainsCoreStats()
        {
            var item = new ItemInstance { Name = "Ионный радар", Category = "Radar", TechLevel = 2, Price = 2440, Weight = 24, NoWear = true };
            string text = ItemDescription.Build(item);
            StringAssert.StartsWith("Ионный радар  |  ТУ2", text);
            StringAssert.Contains("Вес: 24", text);
            StringAssert.Contains("Цена: 2440 кр.", text);
            StringAssert.Contains("Прочность: без износа", text);
        }

        // ── Карта галактики ───────────────────────────────────────────────

        /// <summary>Контекст с настоящими конфигами, у расы Peleng цвет подменён на красный.</summary>
        private static GalaxyGenerationContext ContextWithRaces()
        {
            var ctx = SimulationSetup.CreateContext(TestWorld.LoadConfigs(), settings: null);
            ctx.Config.Races["Peleng"] = new RaceConfig { Color = "255,0,0" };
            return ctx;
        }

        [Test]
        public void GalaxyMap_ParseColor_RgbCsvAndHtml()
        {
            Assert.AreEqual(new Color(1f, 0f, 0f), GalaxyMapPresenter.ParseColor("255, 0, 0"));
            Assert.AreEqual(Color.white, GalaxyMapPresenter.ParseColor("не цвет"));
        }

        [Test]
        public void GalaxyMap_StarColor_UnknownIsWhite()
        {
            Assert.AreEqual(Color.white, GalaxyMapPresenter.StarColor(null));
            Assert.AreNotEqual(Color.white, GalaxyMapPresenter.StarColor("RED"));
        }

        [Test]
        public void GalaxyMap_SectorFill_ByRaceOrNeutral()
        {
            var ctx = ContextWithRaces();
            var sector = new SectorData { Race = "Peleng" };
            Assert.AreEqual(new Color(1f, 0f, 0f), GalaxyMapPresenter.SectorFillColor(sector, ctx, byOwner: false));
            Assert.AreEqual(GalaxyMapPresenter.NeutralFill, GalaxyMapPresenter.SectorFillColor(sector, ctx, byOwner: true),
                "режим «по владельцу» без владельца с цветом — нейтральная заливка");
            Assert.AreEqual(GalaxyMapPresenter.NeutralFill, GalaxyMapPresenter.SectorFillColor(null, ctx, byOwner: false));
        }

        [Test]
        public void GalaxyMap_PlanetNameColor()
        {
            var ctx = ContextWithRaces();
            Assert.AreEqual("FF0000", GalaxyMapPresenter.PlanetNameColorHex(new PlanetData { Race = "Peleng" }, ctx));
            Assert.AreEqual("AAAAAA", GalaxyMapPresenter.PlanetNameColorHex(new PlanetData { Race = "Unknown" }, ctx));
            Assert.AreEqual("7B7B7B", GalaxyMapPresenter.PlanetNameColorHex(new PlanetData(), ctx));
        }

        [Test]
        public void GalaxyMap_ProlongerInfo_HiddenWithoutArtefact()
        {
            var star = new StarData();
            star.Ships.Add(new ShipData { Owner = "Pirates", ShipTypeId = "Raider", CurrentHull = 10 });
            var sb = new StringBuilder();
            Assert.AreEqual(0, GalaxyMapPresenter.AppendProlongerShipsInfo(sb, star, player: null, galaxy: null));
            Assert.AreEqual(0, GalaxyMapPresenter.AppendProlongerShipsInfo(sb, star, new ShipData(), galaxy: null));
            Assert.AreEqual(0, sb.Length);
        }
    }
}
