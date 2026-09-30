using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Economy;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.Galaxy.Politics
{
    /// <summary>
    /// Смена правительства на планете. Используется через EventActionConfig типа "ChangeGovernment".
    /// Поддерживает режимы "Random" (любой другой строй) и "Weighted" (взвешенно по Weights).
    /// </summary>
    public static class GovernmentChangeService
    {
        public static bool ChangeGovernment(PlanetData planet, GalaxyConfig cfg, EventActionConfig action, int turn)
        {
            if (planet == null || cfg?.Planets?.GovernmentTypes == null || action == null) return false;
            var govs = cfg.Planets.GovernmentTypes;
            if (govs.Count == 0) return false;

            string oldGov = planet.Settlement.Government ?? "";
            string newGov = null;

            bool weighted = string.Equals(action.Mode, "Weighted", System.StringComparison.OrdinalIgnoreCase)
                            && action.Weights != null && action.Weights.Count > 0;

            if (weighted)
            {
                newGov = WeightedPick(action.Weights, oldGov);
            }
            else
            {
                // Random: любой строй кроме текущего.
                var keys = new List<string>(govs.Keys);
                keys.Remove(oldGov);
                if (keys.Count == 0) return false;
                newGov = keys[GameRng.Range(0, keys.Count)];
            }

            if (string.IsNullOrEmpty(newGov) || newGov == oldGov) return false;
            if (!govs.ContainsKey(newGov)) return false;

            planet.Settlement.Government = newGov;

            EconomicLog.Custom(turn, "EVENT", EconomicLog.Safe(planet.Name), "GOVERNMENT_CHANGED",
                $"from={oldGov} to={newGov} mode={(weighted ? "Weighted" : "Random")}");
            GameLog.Add($"[Власть] {planet.Name}: новый строй — {newGov} (был {oldGov}).");

            string starName = planet.ParentStar?.Name ?? "?";
            string ctrl = OccupationService.GetControllingOwner(planet);
            GalaxyNewsService.PostForSide(GalaxyNewsService.CAT_GOVERNMENT,
                BuildRevolutionText(planet.Name, starName, newGov, oldGov),
                ctrl);
            return true;
        }

        // Разные варианты «риторики». Ключи:
        //   Anarchy/Monarchy/Republic/Democracy — если такие ключи используются в GovernmentTypes;
        //   любые другие проваливаются в дефолтный шаблон.
        private static string BuildRevolutionText(string planetName, string starName, string newGov, string oldGov)
        {
            string keyNorm = (newGov ?? "").ToLowerInvariant();
            string tplKey = keyNorm.Contains("anarch") ? "government.anarchy"
                          : keyNorm.Contains("monarch") ? "government.monarchy"
                          : keyNorm.Contains("repub")   ? "government.republic"
                          : keyNorm.Contains("democ")   ? "government.democracy"
                          : "government.default";
            string text = NewsTexts.Format(tplKey,
                ("planetName", planetName), ("starName", starName),
                ("newGov", newGov), ("oldGov", oldGov));
            if (string.IsNullOrEmpty(text))
                text = $"Политическая обстановка на планете {planetName} (система {starName}) изменилась: " +
                       $"к власти пришли {newGov} (было — {oldGov}).";
            return text;
        }

        private static string WeightedPick(Dictionary<string, float> weights, string exclude)
        {
            float total = 0f;
            foreach (var kv in weights)
            {
                if (kv.Key == exclude) continue;
                if (kv.Value > 0f) total += kv.Value;
            }
            if (total <= 0f) return null;

            float roll = GameRng.Value * total;
            float cum = 0f;
            foreach (var kv in weights)
            {
                if (kv.Key == exclude) continue;
                if (kv.Value <= 0f) continue;
                cum += kv.Value;
                if (roll <= cum) return kv.Key;
            }
            return null;
        }
    }
}
