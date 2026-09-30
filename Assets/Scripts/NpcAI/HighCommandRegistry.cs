using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Simulation;

namespace SRG.NpcAI
{
    /// <summary>
    /// Реестр генштабов фракций. Один MonoBehaviour-синглтон, тикающий все
    /// <see cref="FactionHighCommand"/>. Синхронизируется с <see cref="GalaxyData.HighCommandStates"/>
    /// (сохранение/загрузка). Строит ГШ на новой галактике или восстанавливает из сейва.
    /// </summary>
    public class HighCommandRegistry : MonoBehaviour
    {
        public static HighCommandRegistry Instance { get; private set; }

        private readonly Dictionary<string, FactionHighCommand> _commands = new();
        private GalaxyData _indexedGalaxy;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>Вызывается из DirectiveManager после AutoIssueReactiveDirectives.</summary>
        public void Tick(GalaxyData galaxy)
        {
            if (galaxy == null) return;
            EnsureIndexedFor(galaxy);
            foreach (var cmd in _commands.Values) cmd.Tick(galaxy);
            SyncToGalaxy(galaxy);
            // Сбрасываем буфер каждый ход: сообщений мало (обычно 0-1 на фракцию раз в несколько
            // ходов), FlushThreshold=50 достигается редко — без этого лог остаётся пустым до
            // выхода из игры или сейва.
            HighCommandLog.Flush();
        }

        /// <summary>Читает <see cref="FactionHighCommand"/> по ключу (Owner или Owner|Race).
        /// Возвращает null, если ГШ нет.</summary>
        public FactionHighCommand Get(string ownerId, string raceId = null)
        {
            string key = string.IsNullOrEmpty(raceId) ? ownerId : $"{ownerId}|{raceId}";
            return _commands.TryGetValue(key, out var cmd) ? cmd : null;
        }

        public IEnumerable<FactionHighCommand> All => _commands.Values;

        private void EnsureIndexedFor(GalaxyData galaxy)
        {
            if (_indexedGalaxy == galaxy) return;
            _commands.Clear();
            int turn = galaxy.CurrentTurn;
            if (galaxy.HighCommandStates != null && galaxy.HighCommandStates.Count > 0)
            {
                // Восстановление из сейва.
                foreach (var kv in galaxy.HighCommandStates)
                {
                    var cmd = FactionHighCommand.FromState(kv.Value);
                    _commands[kv.Key] = cmd;
                    HighCommandLog.Init(turn, cmd.Key, "RESTORE",
                        $"strategy={cmd.Strategy} next_eval={cmd.NextEvaluationTurn}");
                }
                Debug.Log($"[HighCommandRegistry] Restored {_commands.Count} ГШ из сейва.");
            }
            else
            {
                // Инициализация с нуля: пробегаем населённые планеты и создаём ГШ на каждую нужную
                // (Owner|Race?) пару.
                BootstrapFromGalaxy(galaxy);
                Debug.Log($"[HighCommandRegistry] Bootstrapped {_commands.Count} ГШ для новой галактики.");
            }
            _indexedGalaxy = galaxy;
        }

        private void BootstrapFromGalaxy(GalaxyData galaxy)
        {
            var ctx = GameWorld.Context;
            if (ctx?.AvailableOwners == null) return;

            // Собираем множество (Owner, Race?) присутствующих в галактике.
            var seen = new HashSet<string>();
            foreach (var star in galaxy.StarsMap.Values)
            {
                for (int i = 0; i < star.Planets.Count; i++)
                {
                    var p = star.Planets[i];
                    if (p.Settlement.Population <= 0) continue;
                    if (string.IsNullOrEmpty(p.Owner) || p.Owner == GalaxyConstants.OWNER_NONE_KEY) continue;
                    if (!ctx.AvailableOwners.TryGetValue(p.Owner, out var oc)) continue;
                    // Пираты не воюют за территорию — свой AI.
                    if (p.Owner == "Pirates") continue;

                    bool perRace = oc.SeparatePerRaceHighCommand;
                    string key = perRace ? $"{p.Owner}|{p.Race}" : p.Owner;
                    if (!seen.Add(key)) continue;

                    var strategy = ResolveStrategy(ctx, p.Owner, perRace ? p.Race : null, oc);
                    int nextEval = galaxy.CurrentTurn + GameRng.Range(
                        FactionHighCommand.EvaluationIntervalMin,
                        FactionHighCommand.EvaluationIntervalMax + 1);
                    var cmd = new FactionHighCommand(p.Owner, perRace ? p.Race : null, strategy, nextEval);
                    _commands[cmd.Key] = cmd;
                    HighCommandLog.Init(galaxy.CurrentTurn, cmd.Key, "INIT",
                        $"strategy={strategy} next_eval={nextEval} per_race={perRace}");
                }
            }
        }

        private static HighCommandStrategy ResolveStrategy(GalaxyGenerationContext ctx, string ownerId, string raceId, OwnerConfig oc)
        {
            // Приоритет: RaceConfig.HighCommandStrategy (если per-race) → OwnerConfig.HighCommandStrategy → WeakestNearby.
            string s = null;
            if (!string.IsNullOrEmpty(raceId) && ctx.Config?.Races != null
                && ctx.Config.Races.TryGetValue(raceId, out var rc))
                s = rc.HighCommandStrategy;
            if (string.IsNullOrEmpty(s)) s = oc?.HighCommandStrategy;

            if (!string.IsNullOrEmpty(s) && System.Enum.TryParse<HighCommandStrategy>(s, ignoreCase: true, out var parsed))
                return parsed;
            return HighCommandStrategy.WeakestNearby;
        }

        private void SyncToGalaxy(GalaxyData galaxy)
        {
            galaxy.HighCommandStates ??= new Dictionary<string, HighCommandStateData>();
            galaxy.HighCommandStates.Clear();
            foreach (var kv in _commands)
                galaxy.HighCommandStates[kv.Key] = kv.Value.ToState();
        }
    }
}
