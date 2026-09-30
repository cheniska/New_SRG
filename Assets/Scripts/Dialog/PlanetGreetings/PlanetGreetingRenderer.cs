using System.Collections.Generic;
using System.Text.RegularExpressions;
using SRG.Config;
using SRG.Economy;
using SRG.Galaxy;
using SRG.Ships;

namespace SRG.Dialog.PlanetGreetings
{
    /// <summary>
    /// Тонкая обёртка над <see cref="PlanetGreetingSelector"/>: выбирает правило под контекст
    /// и подставляет <c>&lt;Player&gt;</c>/<c>&lt;CurPlanet&gt;</c>/<c>&lt;ToPlanet&gt;</c>/… в тексте.
    /// Используется тегом <c>{planet_greeting}</c> (см. <see cref="DialogActions"/>).
    /// </summary>
    public static class PlanetGreetingRenderer
    {
        // Плейсхолдеры формата <Name>. Не найденное в таблице оставляется как есть — поэтому
        // таблица обязана покрывать ВСЕ плейсхолдеры, встречающиеся в текстах приветствий:
        // приветствие показывается при каждом открытии диалога, и пропуск сразу видно игроку.
        // Страховка от пропусков — Seed() ниже: заполняет каждый известный ключ нейтральным
        // значением, а конкретные данные его перекрывают.
        private static readonly Regex _placeholder = new(@"<([A-Za-z][A-Za-z0-9_]*)>", RegexOptions.Compiled);

        /// <summary>Карта подстановок, засеянная нейтральными значениями всех известных
        /// плейсхолдеров (<c>TextsConfig.Dialog.GreetingDefaults</c>). Ключ, для которого
        /// в конкретном контексте нет данных (правило не задало ToPlanet, у NPC нет агрессора,
        /// у правила нет Goods), отрендерится этим значением, а не сырым <c>&lt;Tag&gt;</c>.</summary>
        private static Dictionary<string, string> Seed()
        {
            var map = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var kv in DialogTexts.GreetingDefaultsMap) map[kv.Key] = kv.Value ?? "";
            return map;
        }

        /// <summary>Собирает готовый текст приветствия правительства планеты.</summary>
        public static string Render(ShipData player, PlanetData planet,
                                    GalaxyConfig cfg, GalaxyData galaxy,
                                    System.Random rng = null)
        {
            var pick = PlanetGreetingSelector.Choose(player, planet, cfg, galaxy, rng);
            if (pick?.Rule?.Texts == null || pick.Rule.Texts.Count == 0) return string.Empty;
            return Join(pick.Rule.Texts, BuildPlanetSubstitutions(player, planet, pick, cfg, galaxy));
        }

        /// <summary>Собирает готовый текст приветствия капитана корабля.</summary>
        public static string RenderShip(ShipData player, ShipData ship,
                                        GalaxyConfig cfg, GalaxyData galaxy,
                                        System.Random rng = null)
        {
            var pick = ShipGreetingSelector.Choose(player, ship, cfg, galaxy, rng);
            if (pick?.Rule?.Texts == null || pick.Rule.Texts.Count == 0) return string.Empty;
            return Join(pick.Rule.Texts, BuildShipSubstitutions(player, ship, pick, cfg, galaxy));
        }

        private static string Join(List<string> texts, Dictionary<string, string> subs)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < texts.Count; i++)
            {
                if (i > 0) sb.Append("\n\n");
                sb.Append(Substitute(texts[i], subs));
            }
            return sb.ToString();
        }

        private static Dictionary<string, string> BuildPlanetSubstitutions(
            ShipData player, PlanetData cur, PlanetGreetingSelector.Pick pick,
            GalaxyConfig cfg, GalaxyData galaxy)
        {
            var map = Seed();
            map["Player"]              = SafeName(player?.Name, "Player");
            map["PlayerRace"]          = SafeName(player?.Race, "PlayerRace");
            map["PlayerRank"]          = SafeName(PlayerGreetingProfile.ResolveRank(player), "PlayerRank");
            // <Ranger> — обращение к игроку, поэтому подставляется его имя, а не «вольный пилот».
            map["Ranger"]              = map["Player"];
            map["CurPlanet"]           = SafeName(cur?.Name, "CurPlanet");
            map["CurPlanetRace"]       = SafeName(cur?.Race, "CurPlanetRace");
            map["CurPlanetGovernment"] = SafeName(cur?.Settlement?.Government, "CurPlanetGovernment");
            map["CurPlanetEconomy"]    = SafeName(cur?.Settlement?.EconomyType, "CurPlanetEconomy");
            map["CurStar"]             = SafeName(cur?.ParentStar?.Name, "CurStar");
            map["Star"]                = map["CurStar"];

            if (pick?.ToPlanet != null)
            {
                map["ToPlanet"]           = SafeName(pick.ToPlanet.Name, "ToPlanet");
                map["ToPlanetRace"]       = SafeName(pick.ToPlanet.Race, "ToPlanetRace");
                map["ToPlanetGovernment"] = SafeName(pick.ToPlanet.Settlement?.Government, "ToPlanetGovernment");
                map["ToPlanetEconomy"]    = SafeName(pick.ToPlanet.Settlement?.EconomyType, "ToPlanetEconomy");
            }
            if (pick?.ToStar != null)
                map["ToStar"] = SafeName(pick.ToStar.Name, "ToStar");

            FillGoods(map, pick?.Rule?.Goods, cur, cfg);

            // <FactionName> — первая разгромленная фракция. Использует ключ Owner (Coalition/Pirates/…),
            // сюда же попадают сюжетные обозначения из GalaxyData.DefeatedFactions.
            if (galaxy?.DefeatedFactions != null)
                foreach (var f in galaxy.DefeatedFactions) { map["FactionName"] = f; break; }

            return map;
        }

        /// <summary>Семейство <c>&lt;*Goods*&gt;</c> и <c>&lt;Item&gt;</c>. Товар берётся из
        /// <see cref="GreetingRule.Goods"/> — правило само задаёт, о чём речь.
        /// <c>&lt;CurPlanetGoodsSale/Buy&gt;</c> — цены в лавке планеты (числа, реплики их
        /// подставляют как «всего по … cr»), <c>&lt;CurPlanetGoodsCnt&gt;</c> — сток.
        /// Планеты может не быть (корабельные приветствия) — тогда остаются нейтральные
        /// значения из <see cref="Seed"/>.</summary>
        private static void FillGoods(Dictionary<string, string> map, string goodId,
                                      PlanetData planet, GalaxyConfig cfg)
        {
            if (string.IsNullOrEmpty(goodId) || cfg == null) return;
            string display = TradeSystem.GetDisplayName(goodId, cfg);
            map["Goods"] = display;
            map["Item"] = display;
            map["CurPlanetGoodsBuy"] = display;
            map["CurPlanetGoodsSale"] = display;
            map["LastPlanetGoodsBuy"] = display;
            map["LastPlanetGoodsSale"] = display;
            map["ToPlanetGoodsBuy"] = display;
            map["ToPlanetGoodsSale"] = display;

            if (planet?.Settlement?.Shop?.Goods != null
                && planet.Settlement.Shop.Goods.TryGetValue(goodId, out var entry))
            {
                map["CurPlanetGoodsBuy"] = entry.BuyPrice.ToString();
                map["CurPlanetGoodsSale"] = entry.SellPrice.ToString();
                map["CurPlanetGoodsCnt"] = entry.Stock.ToString();
            }
        }

        private static Dictionary<string, string> BuildShipSubstitutions(
            ShipData player, ShipData ship, ShipGreetingSelector.Pick pick,
            GalaxyConfig cfg, GalaxyData galaxy)
        {
            var lastPlanet = LookupPlanet(ship?.LastPlanetUid, galaxy);
            var homePlanet = LookupPlanet(ship?.HomePlanetUid, galaxy);

            string shipTypeDisplay = LookupShipTypeDisplayName(ship?.ShipTypeId, cfg);
            string playerName      = SafeName(player?.Name, "Player");
            string curStarName     = SafeName(LookupStar(ship?.CurrentStarUid, galaxy)?.Name, "CurStar");
            string shipShort       = SafeName(ship?.Name, "Ship");
            string shipFull        = string.IsNullOrEmpty(ship?.Name)
                ? SafeName(shipTypeDisplay, "Ship")
                : (string.IsNullOrEmpty(shipTypeDisplay) ? ship.Name : FullShipFormat(shipTypeDisplay, ship.Name));

            var map = Seed();
            map["Player"]         = playerName;
            map["PlayerRace"]     = SafeName(player?.Race, "PlayerRace");
            map["PlayerRank"]     = SafeName(PlayerGreetingProfile.ResolveRank(player), "PlayerRank");
            map["Ranger"]         = playerName;                 // алиас — «вольный пилот <Player>» → просто <Player>
            map["Ship"]           = shipShort;
            map["ShipName"]       = shipShort;                  // алиас <Ship>
            map["FullShip"]       = shipFull;                   // «Транспорт «Заря»»
            map["ShipRace"]       = SafeName(ship?.Race, "ShipRace");
            map["ShipType"]       = SafeName(shipTypeDisplay ?? ship?.ShipTypeId, "ShipType");
            map["CurStar"]        = curStarName;
            map["Star"]           = curStarName;                // алиас <CurStar>
            map["LastPlanet"]     = SafeName(lastPlanet?.Name, "LastPlanet");
            map["LastPlanetStar"] = SafeName(lastPlanet?.ParentStar?.Name, "LastPlanetStar");
            map["HomePlanet"]     = SafeName(homePlanet?.Name, "HomePlanet");
            map["HomePlanetStar"] = SafeName(homePlanet?.ParentStar?.Name, "HomePlanetStar");

            // Третьи стороны. <ShipBad> — тот, кто гонится за NPC («на моём хвосте сидит…»):
            // это его последний агрессор. <ShipGood> — тот, кого NPC защищает/считает другом
            // («…мой друг?»): его текущая боевая цель, если NPC уже в бою. Оба имеют полный
            // вариант <Full…> с типом корабля впереди.
            var aggressor = LookupShipInStar(ship, ship?.LastAttackerUid);
            if (aggressor != null)
            {
                map["ShipBad"]     = SafeName(aggressor.Name, "ShipBad");
                map["FullShipBad"] = FullName(aggressor, cfg);
            }
            var ally = LookupShipInStar(ship, ship?.Brain?.GetCombatTargetUid());
            if (ally != null)
            {
                map["ShipGood"]     = SafeName(ally.Name, "ShipGood");
                map["FullShipGood"] = FullName(ally, cfg);
                map["ToShip"]       = SafeName(ally.Name, "ToShip");
            }
            if (pick?.ToPlanet != null)
            {
                map["ToPlanet"]           = SafeName(pick.ToPlanet.Name, "ToPlanet");
                map["ToPlanetRace"]       = SafeName(pick.ToPlanet.Race, "ToPlanetRace");
                map["ToPlanetGovernment"] = SafeName(pick.ToPlanet.Settlement?.Government, "ToPlanetGovernment");
                map["ToPlanetEconomy"]    = SafeName(pick.ToPlanet.Settlement?.EconomyType, "ToPlanetEconomy");
            }
            if (pick?.ToStar != null)
                map["ToStar"] = SafeName(pick.ToStar.Name, "ToStar");

            // Семейство <*Goods*> и <Item> — все ссылаются на один товар из rule.Goods
            // (в исходнике это был «самый выгодный товар на планете такой-то»; у нас без глубокой
            // выкладки экономики берём display-name товара из контекста правила). Цены/сток —
            // с планеты, на которую NPC летит либо с которой шёл.
            FillGoods(map, pick?.Rule?.Goods, pick?.ToPlanet ?? lastPlanet, cfg);

            if (galaxy?.DefeatedFactions != null)
                foreach (var f in galaxy.DefeatedFactions) { map["FactionName"] = f; break; }
            return map;
        }

        private static string Substitute(string src, Dictionary<string, string> map)
        {
            if (string.IsNullOrEmpty(src) || map == null) return src;
            return _placeholder.Replace(src, m =>
                map.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
        }

        /// <summary>Значение плейсхолдера: данные из мира, а при их отсутствии — нейтральная фраза
        /// из <c>TextsConfig.Dialog.GreetingDefaults</c> по имени самого плейсхолдера.</summary>
        /// <param name="placeholderKey">Имя плейсхолдера без угловых скобок («Player», «CurStar»).</param>
        private static string SafeName(string src, string placeholderKey)
            => string.IsNullOrEmpty(src) ? DialogTexts.GreetingDefault(placeholderKey) : src;

        /// <summary>«Транспорт «Заря»» — склейка типа и имени. Формат (в т.ч. вид кавычек) живёт
        /// в текст-конфиге: <c>Phrases["greeting_full_ship"]</c>, где <c>{0}</c> — тип, <c>{1}</c> — имя.</summary>
        private static string FullShipFormat(string type, string name)
        {
            string fmt = DialogTexts.Phrase("greeting_full_ship");
            return string.IsNullOrEmpty(fmt) ? $"{type} {name}" : string.Format(fmt, type, name);
        }

        /// <summary>Корабль по UID в звезде, где сейчас находится <paramref name="near"/>.
        /// Приветствия говорят только о том, что происходит рядом, поэтому галактику не обходим.</summary>
        private static ShipData LookupShipInStar(ShipData near, string uid)
            => string.IsNullOrEmpty(uid) ? null : near?.CurrentStar?.FindShip(uid);

        /// <summary>«Транспорт «Заря»» — тип корабля + имя. Для <c>&lt;Full…&gt;</c>-плейсхолдеров.</summary>
        private static string FullName(ShipData ship, GalaxyConfig cfg)
        {
            if (ship == null) return null;
            string type = LookupShipTypeDisplayName(ship.ShipTypeId, cfg);
            if (string.IsNullOrEmpty(ship.Name)) return SafeName(type, "Ship");
            return string.IsNullOrEmpty(type) ? ship.Name : FullShipFormat(type, ship.Name);
        }

        private static PlanetData LookupPlanet(string uid, GalaxyData galaxy)
            => galaxy?.PlanetsMap != null && !string.IsNullOrEmpty(uid) &&
               galaxy.PlanetsMap.TryGetValue(uid, out var p) ? p : null;

        private static StarData LookupStar(string uid, GalaxyData galaxy)
            => galaxy?.StarsMap != null && !string.IsNullOrEmpty(uid) &&
               galaxy.StarsMap.TryGetValue(uid, out var s) ? s : null;

        private static string LookupShipTypeDisplayName(string shipTypeId, GalaxyConfig cfg)
        {
            if (string.IsNullOrEmpty(shipTypeId)) return null;
            var types = cfg?.Ships?.ShipTypes;
            if (types == null || !types.TryGetValue(shipTypeId, out var t) || t == null) return null;
            return string.IsNullOrEmpty(t.DisplayName) ? null : t.DisplayName;
        }
    }
}
