using SRG.Config;
using SRG.Core;
using SRG.Galaxy;
using SRG.Ships.Services;

namespace SRG.Dialog.PlanetGreetings
{
    /// <summary>
    /// Производные признаки игрока для матчинга реплик: статус, ранг, рейтинг.
    /// Все пороги и лестницы вынесены в <see cref="DialogTuning"/> (Assets/Config/DialogsConfig.json),
    /// поэтому балансится без пересборки. Если tuning-конфиг не задан — используются значения по умолчанию.
    /// </summary>
    public static class PlayerGreetingProfile
    {
        // Дефолтные лестницы — совместимы с историческим хардкодом; используются, если в
        // DialogsConfig.Tuning не заполнены RankLadder / RatingLadder.
        private static readonly (string Rank, int MinKills)[] DefaultRankLadder =
        {
            ("Admiral",   200),
            ("Commander", 100),
            ("Ace",        50),
            ("Leader",     25),
            ("Wingman",    12),
            ("Pilot",       5),
            ("Cadet",       1),
            ("Rookie",      0),
        };

        private static readonly (string Level, float MinScore)[] DefaultRatingLadder =
        {
            ("Huge",    300f),
            ("Big",     100f),
            ("Average",  30f),
            ("Small",    10f),
            ("Mini",      0f),
        };

        public static string ResolveStatus(ShipData ship) => ResolveStatus(ship, GetTuning());

        public static string ResolveStatus(ShipData ship, DialogTuning t)
        {
            if (ship == null) return "Ranger";
            string pirateOwner = t?.PirateOwner ?? "Pirates";
            float pirateCrimeMin = t?.PirateCrimeMin ?? 50f;
            int traderMin = t?.TraderProfitMin ?? 5000;
            int traderPerKill = t?.TraderProfitPerKill ?? 500;
            int warriorMin = t?.WarriorKillsMin ?? 5;

            if (string.Equals(ship.Owner, pirateOwner, System.StringComparison.OrdinalIgnoreCase)
                || ship.CrimeRating >= pirateCrimeMin) return "Pirate";
            int kills = ship.KillsDominator + ship.KillsPirate;
            if (ship.TradeProfit > traderMin && ship.TradeProfit > kills * traderPerKill) return "Trader";
            if (kills >= warriorMin) return "Warrior";
            return "Ranger";
        }

        public static string ResolveRank(ShipData ship) => ResolveRank(ship, GetTuning());

        public static string ResolveRank(ShipData ship, DialogTuning t)
        {
            if (ship == null) return "Rookie";
            int kills = ship.KillsDominator + ship.KillsPirate;
            var ladder = t?.RankLadder;
            if (ladder != null && ladder.Count > 0)
            {
                string best = null; int bestMin = int.MinValue;
                foreach (var step in ladder)
                {
                    if (step == null || string.IsNullOrEmpty(step.Rank)) continue;
                    if (kills >= step.MinKills && step.MinKills >= bestMin)
                    {
                        best = step.Rank; bestMin = step.MinKills;
                    }
                }
                if (best != null) return best;
            }
            foreach (var (rank, min) in DefaultRankLadder)
                if (kills >= min) return rank;
            return "Rookie";
        }

        public static string ResolveRating(ShipData ship, GalaxyConfig cfg)
        {
            if (ship == null) return "Mini";
            var ratingCfg = cfg != null ? ShipRatingService.FindRating(cfg, "Rangers") : null;
            float score = ratingCfg != null ? ShipRatingService.GetScore(ship, ratingCfg)
                                            : (ship.KillsDominator * 3f + ship.KillsPirate);

            var ladder = cfg?.Dialogs?.Tuning?.RatingLadder;
            if (ladder != null && ladder.Count > 0)
            {
                string best = null; float bestMin = float.NegativeInfinity;
                foreach (var step in ladder)
                {
                    if (step == null || string.IsNullOrEmpty(step.Level)) continue;
                    if (score >= step.MinScore && step.MinScore >= bestMin)
                    {
                        best = step.Level; bestMin = step.MinScore;
                    }
                }
                if (best != null) return best;
            }
            foreach (var (level, min) in DefaultRatingLadder)
                if (score >= min) return level;
            return "Mini";
        }

        private static DialogTuning GetTuning()
            => GalaxyManager.Instance?.Context?.Config?.Dialogs?.Tuning;
    }
}
