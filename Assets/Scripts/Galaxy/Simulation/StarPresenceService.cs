using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Galaxy.Politics;

namespace SRG.Galaxy.Simulation
{
    /// <summary>
    /// Периодический анализ «плотности сущностей» в звёздных системах: пираты, транспорты, вольные пилоты.
    /// Раз в StarPresenceStrideTurns ходов пробегает по каждой звезде, где есть игрок ИЛИ где живут
    /// населённые планеты (чтобы шум не забивал буфер новостей), и публикует:
    ///
    ///   - Star.Pirates.None/Some/Many — по числу пиратов;
    ///   - Star.Rangers.ManyWarrior/Trader/Pirate — по числу вольных пилотов профиля Warrior/Trader/Pirate;
    ///   - Star.Transport.Many — при перегруженности транспортниками.
    ///
    /// Пороги — компромиссные константы; при необходимости выносить в GameSettingsConfig.
    /// «Профиль вольного пилота» определяется по метрикам: Kills*/TradeProfit/CrimeRating.
    /// </summary>
    public static class StarPresenceService
    {
        private const int StrideTurns   = 30;
        private const int PirateNone    = 0;
        private const int PirateSome    = 1;
        private const int PirateMany    = 4;
        private const int TransportMany = 6;
        private const int RangersMany   = 3;

        // История прошлых объявлений — чтобы не постить одно и то же каждый тик.
        // Ключ = starUid | категория (например, "abc123|pirates").
        private static readonly Dictionary<string, string> _lastPost = new();

        // Chunked анализ: вместо всплеска раз в 30 ходов раскладываем по ~stars/30 звёзд
        // каждый тик. Каждая звезда всё равно попадает в анализ ~раз в 30 ходов, семантика
        // PostIfChanged не меняется. Убирает 200-мс лаг на 30-х ходах.
        private static readonly List<StarData> _starsCache = new();
        private static int _cursor;

        public static void TickIfDue(GalaxyData galaxy)
        {
            if (galaxy == null || galaxy.CurrentTurn <= 0) return;

            if (_starsCache.Count != galaxy.StarsMap.Count)
            {
                _starsCache.Clear();
                foreach (var s in galaxy.StarsMap.Values) _starsCache.Add(s);
                _cursor = 0;
            }
            if (_starsCache.Count == 0) return;

            int chunk = Mathf.Max(1, Mathf.CeilToInt(_starsCache.Count / (float)StrideTurns));
            for (int i = 0; i < chunk; i++)
            {
                var star = _starsCache[_cursor % _starsCache.Count];
                _cursor++;
                if (IsInteresting(star)) AnalyzeStar(star);
            }
        }

        private static bool IsInteresting(StarData star)
        {
            if (star == null) return false;
            // Достаточно наличия обитаемых планет — за них люди «слышат» новости.
            for (int i = 0; i < star.Planets.Count; i++)
                if (star.Planets[i].Settlement.Population > 0) return true;
            return false;
        }

        private static void AnalyzeStar(StarData star)
        {
            int pirates = 0, transports = 0;
            int rangersWar = 0, rangersTrader = 0, rangersPirate = 0;
            // Топ-N имён вольных пилотов каждого профиля — для использования в шаблоне <Names>.
            var warriorNames = new List<string>();
            var traderNames  = new List<string>();
            var pirateRangerNames = new List<string>();

            var ships = star.Ships;
            for (int i = 0; i < ships.Count; i++)
            {
                var s = ships[i];
                if (s == null || s.CurrentHull <= 0) continue;

                if (SRG.Ships.ShipUtils.IsPirate(s)) pirates++;
                if (SRG.Ships.ShipUtils.IsTransport(s)) transports++;

                if (s.ShipTypeId == "Ranger")
                {
                    RangerProfile prof = ClassifyRanger(s);
                    if (prof == RangerProfile.Warrior)      { rangersWar++;    if (warriorNames.Count < 3) warriorNames.Add(s.Name); }
                    else if (prof == RangerProfile.Trader)  { rangersTrader++; if (traderNames.Count < 3)  traderNames.Add(s.Name); }
                    else if (prof == RangerProfile.Pirate)  { rangersPirate++; if (pirateRangerNames.Count < 3) pirateRangerNames.Add(s.Name); }
                }
            }

            // Пираты
            string pirateBucket = pirates <= PirateNone ? "none"
                                : pirates >= PirateMany ? "many"
                                : "some";
            PostIfChanged(star, "pirates", pirateBucket, BuildPirateNews(star, pirates, pirateBucket));

            // Транспорты — только при превышении
            if (transports >= TransportMany)
                PostIfChanged(star, "transport", "many",
                    $"Таможенные службы системы {star.Name} не справляются с потоком транспортов " +
                    $"({transports} судов). Возможны заторы и рост криминальной активности.");

            // Вольные пилоты
            if (rangersWar >= RangersMany)
                PostIfChanged(star, "rangers_war", "many",
                    $"В системе {star.Name} собралась группа вольных пилотов-воинов: {Join(warriorNames)}. " +
                    $"Возможно, готовится удар по врагам Содружества.");
            if (rangersTrader >= RangersMany)
                PostIfChanged(star, "rangers_trader", "many",
                    $"Аналитики отмечают выгодные условия торговли в системе {star.Name}: " +
                    $"туда слетелись вольные пилоты-торговцы — {Join(traderNames)}.");
            if (rangersPirate >= RangersMany)
                PostIfChanged(star, "rangers_pirate", "many",
                    $"В системе {star.Name} промышляют вольные пилоты с подмоченной репутацией: {Join(pirateRangerNames)}. " +
                    $"Мирным судам рекомендуется облетать её стороной.");
        }

        private static string BuildPirateNews(StarData star, int pirates, string bucket)
        {
            return bucket switch
            {
                "none" => $"Система {star.Name} объявлена зоной безопасной торговли: пиратов не зафиксировано.",
                "many" => $"Пиратский беспредел в системе {star.Name} набирает обороты — {pirates} пиратских кораблей активны. " +
                          $"Транспортникам рекомендуется прокладывать пути через другие системы.",
                _      => $"Возобновилась активность пиратов в системе {star.Name} ({pirates} корабль/-я). " +
                          $"Патрули усилены."
            };
        }

        private static void PostIfChanged(StarData star, string key, string bucket, string text)
        {
            string id = $"{star.Uid}|{key}";
            if (_lastPost.TryGetValue(id, out var prev) && prev == bucket) return;
            _lastPost[id] = bucket;
            if (!string.IsNullOrEmpty(text))
            {
                string cat = key.StartsWith("rangers") ? GalaxyNewsService.CAT_RANGERS
                                                       : GalaxyNewsService.CAT_SYSTEM;
                GalaxyNewsService.PostForSide(cat, text, star?.CurrentSystemController);
            }
        }

        private static string Join(List<string> names)
        {
            if (names == null || names.Count == 0) return "—";
            return string.Join(", ", names);
        }

        private enum RangerProfile { Neutral, Warrior, Trader, Pirate }

        private static RangerProfile ClassifyRanger(ShipData s)
        {
            // Простая эвристика — приоритетно смотрим CrimeRating (Pirate), затем сравниваем
            // Kills* vs TradeProfit. Пороги подобраны так, чтобы 0-й ход давал Neutral.
            if (s.CrimeRating >= 40f) return RangerProfile.Pirate;
            int fightScore = s.KillsDominator * 3 + s.KillsPirate;
            int tradeScore = s.TradeProfit / 500; // грубая нормализация
            if (fightScore >= 5 && fightScore > tradeScore) return RangerProfile.Warrior;
            if (tradeScore >= 5 && tradeScore > fightScore) return RangerProfile.Trader;
            return RangerProfile.Neutral;
        }
    }
}
