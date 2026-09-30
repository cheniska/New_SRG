using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Config;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Simulation;
using SRG.NpcAI.Spawning;
using SRG.Ships;
using SRG.Controllers;
using SRG.Ships.Services;
using SRG.Scripting;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.UI.Screens
{
    public partial class GameConsoleController
    {
        // ── skills (debug) ────────────────────────────────────────────────────────

        void ShowSkills()
        {
            var ps = PlayerShip.Instance;
            if (ps?.ShipData?.Skills == null) { Log("No PlayerShip."); return; }
            var s = ps.ShipData.Skills;
            var cfg = GalaxyManager.Instance?.Context?.Config?.Skills;

            Log($"=== Skills (Points={s.Points} Free={s.FreePoints}) ===");
            foreach (var sk in SkillTypeExtensions.All)
            {
                int b = s.GetBase(sk);
                int e = s.GetEffective(sk, cfg);
                int cost = s.GetUpgradeCost(sk, cfg);
                string costStr = cost == int.MaxValue ? "max" : cost.ToString();
                string deltaStr = (b == e) ? "" : $" ({(e - b >= 0 ? "+" : "")}{e - b})";
                Log($"  {sk.DisplayNameRu()} ({sk}): base={b} eff={e}{deltaStr} nextCost={costStr}");
            }
            if (s.ExpByCategory.Count > 0)
            {
                Log("  ExpByCategory:");
                foreach (var kv in s.ExpByCategory) Log($"    {kv.Key}: {kv.Value:F1}");
            }
            if (s.TempModifiers.Count > 0)
            {
                int curTurn = GalaxyManager.Instance?.GeneratedGalaxy?.CurrentTurn ?? 0;
                Log($"  TempModifiers ({s.TempModifiers.Count}):");
                foreach (var kv in s.TempModifiers)
                    Log($"    [{kv.Key}] {FormatModEntry(kv.Value, curTurn)}");
            }
        }

        void AddExp()
        {
            var ps = PlayerShip.Instance;
            if (ps?.ShipData?.Skills == null) { Log("No PlayerShip."); return; }
            var parts = _lastArg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || !int.TryParse(parts[0], out int amount) || amount <= 0)
            { Log("Usage: addexp <N> [category]"); return; }

            var cat = parts.Length > 1 ? ParseCategory(parts[1]) : ExpCategory.None;
            int pBefore = ps.ShipData.Skills.Points;
            ps.IncPoints(amount, cat);
            int delta = ps.ShipData.Skills.Points - pBefore;
            Log($"+{delta} exp (запрошено {amount}, cat={cat}). Points={ps.ShipData.Skills.Points} Free={ps.ShipData.Skills.FreePoints}");
        }

        void UpSkill()
        {
            var ps = PlayerShip.Instance;
            if (ps?.ShipData?.Skills == null) { Log("No PlayerShip."); return; }
            var sk = ParseSkill(_lastArg);
            if (!sk.HasValue) { Log($"Unknown skill '{_lastArg}'. Use: acc|mob|tech|trade|charm|lead"); return; }

            var cfg = GalaxyManager.Instance?.Context?.Config?.Skills;
            var s = ps.ShipData.Skills;
            int cost = s.GetUpgradeCost(sk.Value, cfg);
            if (s.TryUpgrade(sk.Value, cfg))
                Log($"Прокачан {sk.Value} → {s.GetBase(sk.Value)} (потрачено {cost}, Free={s.FreePoints})");
            else
            {
                string why = cost == int.MaxValue ? "уже cap" : $"не хватает: нужно {cost}, есть {s.FreePoints}";
                Log($"Не могу прокачать {sk.Value}: {why}");
            }
        }

        void SetSkill()
        {
            var ps = PlayerShip.Instance;
            if (ps?.ShipData?.Skills == null) { Log("No PlayerShip."); return; }
            var parts = _lastArg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !int.TryParse(parts[1], out int n))
            { Log("Usage: setskill <type> <N>"); return; }

            var sk = ParseSkill(parts[0]);
            if (!sk.HasValue) { Log($"Unknown skill '{parts[0]}'"); return; }

            // Через SetBase, чтобы сработало OnChanged и UI перерисовался.
            // Передаём null cfg, чтобы НЕ клампить (для debug-команды — никаких границ).
            ps.ShipData.Skills.SetBase(sk.Value, n, null);
            Log($"Set {sk.Value} base={n} (eff={ps.GetEffectiveSkill(sk.Value)})");
        }

        void AddBonus()   => AddBonusOrPenalty(isBonus: true);
        void AddPenalty() => AddBonusOrPenalty(isBonus: false);

        void AddBonusOrPenalty(bool isBonus)
        {
            var ps = PlayerShip.Instance;
            if (ps?.ShipData?.Skills == null) { Log("No PlayerShip."); return; }

            var parts = _lastArg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string cmd = isBonus ? "addbonus" : "addpenalty";
            if (parts.Length < 2)
            {
                Log($"Usage: {cmd} <skill> <amount> [turns]");
                Log("  amount: положительное целое; для penalty применится как -amount");
                Log("  turns: 0 или опущено = действует пока не снимут");
                return;
            }

            var sk = ParseSkill(parts[0]);
            if (!sk.HasValue) { Log($"Unknown skill '{parts[0]}'. Use acc|mob|tech|trade|charm|lead"); return; }
            if (!int.TryParse(parts[1], out int amount) || amount <= 0)
            { Log("amount должен быть положительным целым"); return; }

            int turns = 0;
            if (parts.Length >= 3 && !int.TryParse(parts[2], out turns))
            { Log("turns должен быть целым числом"); return; }

            var deltas = new int[SkillTypeExtensions.SkillCount];
            deltas[(int)sk.Value] = isBonus ? amount : -amount;

            int curTurn = GalaxyManager.Instance?.GeneratedGalaxy?.CurrentTurn ?? 0;
            int endTurn = turns <= 0 ? int.MaxValue : curTurn + turns - 1;

            string id = NextModId(ps.ShipData.Skills);
            string label = $"{(isBonus ? "bonus" : "penalty")} {sk.Value.DisplayNameRu()} {(isBonus ? "+" : "-")}{amount}";
            ps.ShipData.Skills.SetTempModifier(id, deltas, endTurn, label);

            string durStr = turns <= 0 ? "пока не снимут" : $"{turns} ход(ов) (до хода {endTurn})";
            Log($"+[{id}] {label}, {durStr}");
        }

        static string NextModId(ShipSkills skills)
        {
            int n = 1;
            while (skills.TempModifiers.ContainsKey(n.ToString())) n++;
            return n.ToString();
        }

        void ClearMod()
        {
            var ps = PlayerShip.Instance;
            if (ps?.ShipData?.Skills == null) { Log("No PlayerShip."); return; }
            if (string.IsNullOrEmpty(_lastArg)) { Log("Usage: clearmod <id>"); return; }
            var skills = ps.ShipData.Skills;
            bool had = skills.TempModifiers.ContainsKey(_lastArg);
            skills.ClearTempModifier(_lastArg);
            Log(had ? $"-mod '{_lastArg}' снят" : $"mod '{_lastArg}' не найден");
        }

        void ClearSkillMods()
        {
            var ps = PlayerShip.Instance;
            if (ps?.ShipData?.Skills == null) { Log("No PlayerShip."); return; }
            var sk = ParseSkill(_lastArg);
            if (!sk.HasValue) { Log("Usage: clearskill <skill> (acc|mob|tech|trade|charm|lead)"); return; }
            int n = ps.ShipData.Skills.ClearModifiersForSkill(sk.Value);
            Log($"Снято модификаторов на {sk.Value.DisplayNameRu()}: {n}");
        }

        void ClearAllMods()
        {
            var ps = PlayerShip.Instance;
            if (ps?.ShipData?.Skills == null) { Log("No PlayerShip."); return; }
            int n = ps.ShipData.Skills.TempModifiers.Count;
            ps.ShipData.Skills.ClearAllTempModifiers();
            Log($"Снято модификаторов: {n}");
        }

        void ListMods()
        {
            var ps = PlayerShip.Instance;
            if (ps?.ShipData?.Skills == null) { Log("No PlayerShip."); return; }
            var mods = ps.ShipData.Skills.TempModifiers;
            if (mods.Count == 0) { Log("Активных модификаторов нет."); return; }
            int curTurn = GalaxyManager.Instance?.GeneratedGalaxy?.CurrentTurn ?? 0;
            Log($"=== Modifiers ({mods.Count}) ===");
            foreach (var kv in mods)
                Log($"  [{kv.Key}] {FormatModEntry(kv.Value, curTurn)}");
        }

        static string FormatModEntry(TempSkillModifier m, int curTurn)
        {
            string label = string.IsNullOrEmpty(m.Source) ? FormatDeltas(m.Deltas) : m.Source;
            string dur = m.EndTurn == int.MaxValue
                ? "бессрочно"
                : $"{Mathf.Max(0, m.EndTurn - curTurn + 1)} ход(ов)";
            return $"{label}  ({dur})";
        }

        static string FormatDeltas(int[] deltas)
        {
            if (deltas == null) return "(пусто)";
            var sb = new System.Text.StringBuilder(48);
            for (int i = 0; i < deltas.Length && i < SkillTypeExtensions.SkillCount; i++)
            {
                int d = deltas[i];
                if (d == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(((SkillType)i).ToString().Substring(0, 3));
                if (d > 0) sb.Append('+');
                sb.Append(d);
            }
            return sb.Length == 0 ? "(пусто)" : sb.ToString();
        }

        static SkillType? ParseSkill(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            return s.Trim().ToLowerInvariant() switch
            {
                "acc" or "accuracy"     => SkillType.Accuracy,
                "mob" or "mobility"     => SkillType.Mobility,
                "tech" or "technical"   => SkillType.Technical,
                "trade" or "trader"     => SkillType.Trader,
                "charm"                 => SkillType.Charm,
                "lead" or "leadership"  => SkillType.Leadership,
                _                       => null,
            };
        }

        static ExpCategory ParseCategory(string s)
        {
            if (string.IsNullOrEmpty(s)) return ExpCategory.None;
            return s.Trim().ToLowerInvariant() switch
            {
                "dom" or "dominator" or "dominatorkill"  => ExpCategory.DominatorKill,
                "pirate" or "piratekill"                 => ExpCategory.PirateKill,
                "good" or "goodship" or "goodshipkill"   => ExpCategory.GoodShipKill,
                "trade"                                  => ExpCategory.Trade,
                _                                        => ExpCategory.None,
            };
        }
    }
}
