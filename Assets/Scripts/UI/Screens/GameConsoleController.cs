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
    public class GameConsoleController : MonoBehaviour
    {
        public SystemViewManager ViewManager;
        public KeyCode ToggleKey = KeyCode.BackQuote;

        public static GameConsoleController Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._showConsole;
        public static event Action OnTurnPerformed;

        private bool   _showConsole;
        private string _input      = "";
        private int    _caret      = 0;
        private string _lastArg    = "";
        private string _pendingCmd = null;
        private Vector2 _scroll;
        private readonly List<string> _logs = new List<string>();
        private const int MaxLog = 200;

        private int _starIndex = 0;
        private Dictionary<string, (Action act, string desc)> _cmds;

        void Awake()
        {
            Instance = this;
            RegisterCommands();
            GameLog.OnEntry += Log;
        }

        void OnDestroy()
        {
            GameLog.OnEntry -= Log;
            if (Instance == this) Instance = null;
        }

        public static void AddEntry(string msg) => GameLog.Add(msg);

        void Start()
        {
            if (!ViewManager) ViewManager = FindFirstObjectByType<SystemViewManager>();
            StartCoroutine(WaitForInit());
        }

        System.Collections.IEnumerator WaitForInit()
        {
            while (GalaxyManager.Instance?.CurrentStar == null) yield return null;
            var list = GalaxyManager.Instance.GeneratedGalaxy.StarsMap.Values.ToList();
            _starIndex = list.IndexOf(GalaxyManager.Instance.CurrentStar);
            if (_starIndex < 0) _starIndex = 0;
            Log($"Viewing: {GalaxyManager.Instance.CurrentStar.Name}");
        }

        void Update()
        {
            // Переключение консоли
            if (Input.GetKeyDown(ToggleKey))
            {
                _showConsole = !_showConsole;
                if (_showConsole) { _input = ""; _caret = 0; }
                return; // пропускаем ввод в кадре переключения
            }

            if (!_showConsole)
            {
                // Навигация по звёздам клавишами +/-
                if (Input.GetKeyDown(KeyCode.Equals)    || Input.GetKeyDown(KeyCode.KeypadPlus))  ChangeSystem(1);
                if (Input.GetKeyDown(KeyCode.Minus)     || Input.GetKeyDown(KeyCode.KeypadMinus)) ChangeSystem(-1);
                return;
            }

            // Закрытие по Escape
            if (Input.GetKeyDown(KeyCode.Escape)) { _showConsole = false; _input = ""; _caret = 0; return; }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                     || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);

            // Ctrl+C / Ctrl+V / Ctrl+X — работа с системным буфером обмена.
            // Выделения нет, поэтому Ctrl+C копирует всю строку, Ctrl+X — вырезает.
            if (ctrl)
            {
                if (Input.GetKeyDown(KeyCode.C))
                {
                    GUIUtility.systemCopyBuffer = _input;
                }
                else if (Input.GetKeyDown(KeyCode.V))
                {
                    string paste = GUIUtility.systemCopyBuffer ?? string.Empty;
                    if (paste.Length > 0)
                    {
                        // Отфильтруем управляющие символы (кроме табуляции — превратим её в пробел).
                        var sb = new System.Text.StringBuilder(paste.Length);
                        foreach (char c in paste)
                        {
                            if (c == '\r' || c == '\n') continue; // многострочный paste схлопываем
                            if (c == '\t') { sb.Append(' '); continue; }
                            if (c >= ' ') sb.Append(c);
                        }
                        string clean = sb.ToString();
                        _input = _input.Insert(_caret, clean);
                        _caret += clean.Length;
                    }
                }
                else if (Input.GetKeyDown(KeyCode.X))
                {
                    GUIUtility.systemCopyBuffer = _input;
                    _input = string.Empty;
                    _caret = 0;
                }
            }

            // Каретка: стрелки, Home, End, Delete.
            if (Input.GetKeyDown(KeyCode.LeftArrow))  _caret = Mathf.Max(0, _caret - 1);
            if (Input.GetKeyDown(KeyCode.RightArrow)) _caret = Mathf.Min(_input.Length, _caret + 1);
            if (Input.GetKeyDown(KeyCode.Home))       _caret = 0;
            if (Input.GetKeyDown(KeyCode.End))        _caret = _input.Length;
            if (Input.GetKeyDown(KeyCode.Delete) && _caret < _input.Length)
                _input = _input.Remove(_caret, 1);

            // Считываем напечатанные символы (работает независимо от IMGUI-фокуса).
            // При Ctrl-хоткеях inputString не даст осмысленных символов (только SYN/ETX/EOT — они < ' ' и отфильтруются),
            // но на всякий случай подстрахуемся флагом.
            foreach (char c in Input.inputString)
            {
                if (c == '\b')
                {
                    if (_caret > 0)
                    {
                        _input = _input.Remove(_caret - 1, 1);
                        _caret--;
                    }
                }
                else if (c == '\r' || c == '\n')
                {
                    if (_pendingCmd == null) _pendingCmd = _input;
                }
                else if (c >= ' ' && !ctrl)
                {
                    _input = _input.Insert(_caret, c.ToString());
                    _caret++;
                }
            }

            _caret = Mathf.Clamp(_caret, 0, _input.Length);
        }

        void ChangeSystem(int dir)
        {
            var g = GalaxyManager.Instance?.GeneratedGalaxy;
            if (g == null || g.StarsMap.Count == 0) { Log("No Galaxy."); return; }
            var list = g.StarsMap.Values.ToList();
            _starIndex = (_starIndex + dir + list.Count) % list.Count;
            var s = list[_starIndex];
            Log($"Viewing: {s.Name} ({_starIndex})");
            GalaxyManager.Instance.SetCurrentStar(s);
        }

        void RegisterCommands()
        {
            _cmds = new Dictionary<string, (Action, string)>(StringComparer.OrdinalIgnoreCase);

            Reg("save",     "Save game",              () => GalaxyManager.Instance.SaveGame());
            Reg("load",     "Load game",              () => { GalaxyManager.Instance.LoadGame(); OnTurnPerformed?.Invoke(); });
            Reg("regen",    "New galaxy (rand seed)", () => GalaxyManager.Instance.GenerateNewGalaxy((int)DateTime.Now.Ticks));
            Reg("step",     "Start next turn", () =>
            {
                if (GalaxyManager.Instance.Phase == TurnPhase.Simulation) { Log("Simulation already running."); return; }
                Log("Turn started.");
                OnTurnPerformed?.Invoke();
                GalaxyManager.Instance.ClearAllPlanning();
                GalaxyManager.Instance.StartTurn();
            });
            Reg("stop",     "Stop auto mode",  () => { GalaxyManager.Instance.RequestPlanning(PlanningReason.PlayerInput); Log("Auto mode stopped."); });
            Reg("planning", "Show planning requests", () =>
            {
                var gm = GalaxyManager.Instance;
                Log(gm.IsAutoMode ? "Auto mode ON." : $"Phase: {gm.Phase}. Auto mode OFF.");
            });
            Reg("next",  "Next star",  () => ChangeSystem(1));
            Reg("prev",  "Prev star",  () => ChangeSystem(-1));
            Reg("clear", "Clear log",  () => _logs.Clear());
            Reg("quit",  "Exit",       Application.Quit);
            Reg("help",  "Show help",  () => { foreach (var c in _cmds) Log($"  {c.Key}: {c.Value.desc}"); });

            // ── Мультигалактика ─────────────────────────────────────────────────
            Reg("galaxies", "Список галактик (ключ, название, звёзд, активная)", ListGalaxiesCmd);
            Reg("swapgalaxy",
                "swapgalaxy <key> — переключить активную галактику (игрок остаётся в своей — только для просмотра/тестов)",
                SwapGalaxyCmd);
            Reg("teleport",
                "teleport <star-name|uid> [galaxy-key] — переместить игрока к звезде (в т.ч. в другой галактике)",
                TeleportCmd);

            Reg("spawntest",  "spawntest [N] — spawn N test ships evenly across all stars (default 5000)", SpawnTestShips);
            Reg("cleartest",  "Remove all test ships from galaxy",  ClearTestShips);
            Reg("ships",      "Log ship counts across all stars",   LogShips);
            Reg("spawnstation",
                "spawnstation [code] — заспавнить станцию в текущей просматриваемой системе. " +
                "code: BK|CB|MC|PB|RC|RG|WB|SB (коалиция) или Blazer|Keller|Terron (доминаторы). " +
                "Без code — случайный по расе системы.",
                SpawnStationCmd);

            Reg("giveall",    "giveall [cat] — выдать в трюм по одному экземпляру всего оборудования и микромодулей. " +
                              "Фильтр по категории: giveall Artefacts, giveall Weapons, giveall MicroModule. Без аргумента — всё.",
                              GiveAllCmd);
            Reg("give",       "give <id|Cat:Id> [amount] [inv|slot|<slotKey>] — выдать игроку любой предмет " +
                              "(Equipment/Good/Mineral/MicroModule). Без префикса — автопоиск. amount только для стеков.", GiveItemCmd);

            Reg("grabradius", "В очередь захвата все контейнеры в радиусе CargoGrabber",
                () => { var n = PlayerShip.Instance?.EnqueueAllItemsInRadius() ?? 0; Log($"{n} цел(ей)."); });
            Reg("grabsystem", "В очередь захвата все контейнеры в системе (с автоостановкой)",
                () => { var n = PlayerShip.Instance?.EnqueueAllItemsInSystem() ?? 0; Log($"{n} цел(ей)."); });
            Reg("grabclear",  "Очистить очередь захвата",
                () => { PlayerShip.Instance?.ClearPullQueue(); });

            // ── Партнёрство и дроны ─────────────────────────────────────────────
            Reg("spawndrone", "spawndrone [race] — дрон рядом с игроком, партнёром (race по умолчанию Race1)",  SpawnDroneCmd);
            Reg("givedrone",  "givedrone [race] — упакованный дрон-предмет в инвентарь игрока",                 GiveDroneCmd);
            Reg("hire",       "hire — принудительно нанять ближайший hireable-корабль (без проверок цены)",     HireNearestCmd);
            Reg("dismiss",    "dismiss [index] — разорвать контракт с партнёром по индексу (без индекса — со всеми)", DismissCmd);
            Reg("partners",   "Список текущих партнёров игрока с индексами и UID", ListPartnersCmd);

            // ── Червоточины («чёрные дыры») ─────────────────────────────────────
            Reg("wormholes",   "Список активных червоточин: uid, звезда-источник, цель, фаза, ходов до закрытия", ListWormholes);
            Reg("wh_spawn",
                "wh_spawn [key=val ...] — гибкий спавн. keys: src, srcPos, dst, dstPos, ttl, galaxy, opening, cycle, closing, icon. " +
                "Без ключей = случайная. Подробнее — 'wh_spawn ?'",
                SpawnWormholeCmd);
            Reg("wh_spawnnear","wh_spawnnear [radius] — shortcut для 'wh_spawn src=player srcPos=near:<r>' (по умолчанию 5)", SpawnWormholeNearCmd);
            Reg("wh_close",    "wh_close <uid|prefix> — форсированное закрытие (фаза Closing, удалится анимационно)", CloseWormholeCmd);
            Reg("wh_destroy",  "wh_destroy <uid|prefix> — мгновенно удалить червоточину без анимации", DestroyWormholeCmd);

            Reg("showskills", "Вывести скилы пилота + опыт + активные модификаторы", ShowSkills);
            Reg("addexp",     "addexp <N> [cat] — начислить опыт (cat: none|dom|pirate|good|trade)", AddExp);
            Reg("upskill",    "upskill <type> — попытка прокачки (acc|mob|tech|trade|charm|lead)",   UpSkill);
            Reg("setskill",   "setskill <type> <N> — установить базовый уровень напрямую",           SetSkill);
            Reg("addbonus",   "addbonus <skill> <amount> [turns] — +amount к скилу; turns 0/опущено = пока не снимут", AddBonus);
            Reg("addpenalty", "addpenalty <skill> <amount> [turns] — -amount к скилу; turns 0/опущено = пока не снимут", AddPenalty);
            Reg("clearmod",   "clearmod <id> — снять конкретный модификатор по id",                  ClearMod);
            Reg("clearskill", "clearskill <skill> — снять все модификаторы, влияющие на скил",       ClearSkillMods);
            Reg("clearmods",  "Снять все временные модификаторы",                                    ClearAllMods);
            Reg("mods",       "Список активных модификаторов с id, дельтами и оставшимися ходами",   ListMods);
        }

        void Reg(string k, string d, Action a) => _cmds[k] = (a, d);

        void Exec(string line)
        {
            line = line.Trim();
            if (string.IsNullOrEmpty(line)) return;

            // Скрипт-режим: строка похожа на Lua (есть '.', '(', ':' или '=') — отправляем в LuaHost.
            // Примеры: WormholeService.Spawn(Positions.Near(Player(), 100));  x = Player().Money
            if (line.IndexOfAny(new[] { '.', '(', ':', '=' }) >= 0)
            {
                Log($">> {line}");
                try
                {
                    var result = LuaHost.Do(line);
                    Log($"= {LuaHost.Format(result)}");
                }
                catch (MoonSharp.Interpreter.InterpreterException iex)
                {
                    Log($"! {iex.DecoratedMessage ?? iex.Message}");
                }
                catch (Exception ex)
                {
                    Log($"! {ex.GetType().Name}: {ex.Message}");
                }
                return;
            }

            int space  = line.IndexOf(' ');
            string key = space < 0 ? line : line.Substring(0, space);
            _lastArg   = space < 0 ? "" : line.Substring(space + 1).Trim();

            if (_cmds.TryGetValue(key, out var cmd)) { Log($">> {line}"); cmd.act(); }
            else Log($"Unknown: '{key}'. Type 'help'.");
        }

        // ── spawntest ─────────────────────────────────────────────────────────────

        void SpawnTestShips()
        {
            var gm = GalaxyManager.Instance;
            if (gm?.GeneratedGalaxy == null) { Log("No galaxy loaded."); return; }

            var ctx = gm.Context;
            if (ctx == null) { Log("Context not ready."); return; }

            int total = 5000;
            if (!string.IsNullOrEmpty(_lastArg) && int.TryParse(_lastArg, out int n) && n > 0)
                total = n;

            var stars = gm.GeneratedGalaxy.StarsMap.Values.ToList();
            if (stars.Count == 0) { Log("No stars in galaxy."); return; }

            // Собираем все доступные пары (race, shipTypeId) из расовых линеек
            var pairs = new List<(string race, string shipTypeId)>();
            foreach (var kv in ctx.RaceLineups)
                foreach (var typeId in kv.Value)
                    if (ctx.AvailableShipTypes.ContainsKey(typeId))
                        pairs.Add((kv.Key, typeId));

            if (pairs.Count == 0) { Log("No valid race/ship pairs in context."); return; }

            string defaultOwner = ctx.Config?.Settings?.DefaultOwner ?? GalaxyConstants.OWNER_NONE_KEY;
            int perStar = Mathf.Max(1, total / stars.Count);
            int spawned = 0;
            int pairIdx = 0;

            foreach (var star in stars)
            {
                for (int i = 0; i < perStar && spawned < total; i++, pairIdx++)
                {
                    var (race, shipTypeId) = pairs[pairIdx % pairs.Count];
                    string ownerId = defaultOwner;

                    var ship = ShipFactory.BuildShipData(shipTypeId, ownerId, race,
                                                         ctx.AvailableShipTypes, ctx);
                    if (ship == null) continue;

                    ship.Name            = $"TestShip_{spawned}";
                    ship.CurrentStarUid  = star.Uid;
                    ship.PreviousStarUid = star.Uid;
                    ship.CurrentStar     = star;

                    Vector2 pos = new Vector2(GameRng.Range(-5f, 5f),
                                              GameRng.Range(-5f, 5f));
                    ship.Position = ship.PreviousPosition = ship.TargetPosition = pos;

                    if (ctx.AvailableShipTypes.TryGetValue(shipTypeId, out var cfg)
                        && (cfg.StarterKit != null || cfg.StarterWeapons != null)
                        && ctx.ItemsConfig != null)
                        ItemFactory.EquipStarterKit(ship, cfg.StarterKit, ctx.ItemsConfig,
                                                    ctx.Config, cfg.StarterWeapons);
                    else
                    {
                        ship.MaxHull     = 100;
                        ship.CurrentHull = ship.MaxHull;
                    }

                    star.Ships.Add(ship);
                    spawned++;
                }
            }

            Log($"Spawned {spawned} test ships across {stars.Count} stars ({perStar}/star).");
        }

        void ClearTestShips()
        {
            var gm = GalaxyManager.Instance;
            if (gm?.GeneratedGalaxy == null) { Log("No galaxy."); return; }

            int removed = 0;
            foreach (var star in gm.GeneratedGalaxy.StarsMap.Values)
            {
                int before = star.Ships.Count;
                star.Ships.RemoveAll(s => s.Name != null &&
                                          s.Name.StartsWith("TestShip_", StringComparison.Ordinal));
                removed += before - star.Ships.Count;
            }
            Log($"Removed {removed} test ships.");
        }

        // ── ships ─────────────────────────────────────────────────────────────────

        void LogShips()
        {
            var gm = GalaxyManager.Instance;
            if (gm?.GeneratedGalaxy == null) { Log("No galaxy."); return; }

            int totalShips     = 0;
            int starsWithShips = 0;
            int maxInStar      = 0;
            string maxStarName = "";
            var lines = new List<string>();

            foreach (var star in gm.GeneratedGalaxy.StarsMap.Values)
            {
                int cnt = star.Ships.Count;
                if (cnt == 0) continue;
                totalShips += cnt;
                starsWithShips++;
                if (cnt > maxInStar) { maxInStar = cnt; maxStarName = star.Name; }
                lines.Add($"  {star.Name}: {cnt}");
            }

            Log($"=== Ships: {totalShips} total in {starsWithShips}/{gm.GeneratedGalaxy.StarsMap.Count} stars ===");
            Log($"  Most: {maxStarName} ({maxInStar})");
            int shown = Mathf.Min(lines.Count, 30);
            for (int i = 0; i < shown; i++) Log(lines[i]);
            if (lines.Count > shown) Log($"  ...+{lines.Count - shown} more stars");
        }

        void SpawnStationCmd()
        {
            var gm = GalaxyManager.Instance;
            var star = gm?.CurrentStar;
            if (star == null) { Log("Нет текущей звезды."); return; }
            if (gm.Context == null) { Log("GenerationContext не готов."); return; }

            string code = string.IsNullOrWhiteSpace(_lastArg) ? null : _lastArg.Trim();
            var station = SpawnSystem.SpawnStationInStar(star, gm.Context, code);
            if (station == null) { Log("Спавн не удался (см. лог Unity)."); return; }

            SystemViewManager.Instance?.EnsureShipVisual(station);
            Log($"Станция {station.Name} [{station.ShipTypeId}] в '{star.Name}' " +
                $"(pos={station.Position.x:F1},{station.Position.y:F1}) uid={SpriteUtility.ShortId(station.Uid)}");
        }

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

        // ── Партнёрство и дроны ──────────────────────────────────────────────────

        void SpawnDroneCmd()
        {
            var player = PlayerShip.Instance?.ShipData;
            if (player == null) { Log("Player not ready."); return; }
            string race = string.IsNullOrEmpty(_lastArg) ? null : _lastArg;
            var drone = PartnerScriptApi.SpawnDrone(player, race);
            if (drone == null) { Log("Spawn failed (см. лог Unity)."); return; }
            Log($"Дрон {drone.Name} [{race ?? PartnerScriptApi.DefaultDroneRace}] развёрнут, uid={SpriteUtility.ShortId(drone.Uid)}");
        }

        void GiveItemCmd()
        {
            var player = PlayerShip.Instance?.ShipData;
            if (player == null) { Log("Player not ready."); return; }
            if (string.IsNullOrEmpty(_lastArg))
            {
                Log("Usage: give <id|Cat:Id> [amount] [inv|slot|<slotKey>]");
                Log("  Автопоиск: MicroModule → Good → Mineral → Equipment. Форсировать: Weapons:W_Laser, Goods:Alcohol, Mineral:Minerals, MicroModule:...");
                Log("  amount игнорируется для оборудования/ММ. По умолчанию 1.");
                Log("  target: inv (в трюм), slot (авто-слот), Weapons_2 (конкретный слот). По умолчанию — авто.");
                return;
            }

            var parts = _lastArg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string spec  = parts[0];
            int amount   = 1;
            var target   = ItemGrantService.GrantTarget.Auto;
            string slot  = null;

            for (int i = 1; i < parts.Length; i++)
            {
                var p = parts[i];
                if (int.TryParse(p, out var n) && n > 0) { amount = n; continue; }
                if (p.Equals("inv",  StringComparison.OrdinalIgnoreCase)) { target = ItemGrantService.GrantTarget.Inventory; continue; }
                if (p.Equals("slot", StringComparison.OrdinalIgnoreCase)) { target = ItemGrantService.GrantTarget.Auto;      continue; }
                // Иначе трактуем как конкретный slotKey (напр. Weapons_2, Engine_0).
                target = ItemGrantService.GrantTarget.Slot;
                slot   = p;
            }

            // Для стеков (Good/Mineral) amount уходит в сам стек. Для оборудования/ММ — повторяем вызов.
            // Первый вызов сам определит, стек это или предмет; если стек, amount уже применён.
            var first = ItemGrantService.Give(player, spec, amount, target, slot);
            Log(first.Ok ? first.Message : $"give: {first.Message}");
            if (first.Ok && first.Placement != "stack" && amount > 1)
            {
                int extras = 0;
                for (int i = 1; i < amount; i++)
                {
                    var r = ItemGrantService.Give(player, spec, 1, target, slot);
                    if (r.Ok) extras++;
                    else { Log($"  ...прервано на {i + 1}: {r.Message}"); break; }
                }
                if (extras > 0) Log($"  +ещё {extras} шт.");
            }
        }

        void GiveAllCmd()
        {
            var player = PlayerShip.Instance?.ShipData;
            if (player == null) { Log("Player not ready."); return; }
            var ctx = GalaxyManager.Instance?.Context;
            var eq = ctx?.ItemsConfig;
            var items = ctx?.ItemsConfig;
            if (eq == null || items == null) { Log("ItemsConfig / ItemsConfig не загружены."); return; }

            string filter = string.IsNullOrEmpty(_lastArg) ? null : _lastArg.Trim();
            int given = 0, skipped = 0;

            // 1. Оборудование (включая корпуса, оружие, артефакты и т.д.). Каждый предмет — по одному
            // экземпляру. Корпуса пропускаем: несколько корпусов в трюме бесполезны и раздувают вес.
            foreach (var (cat, id, _) in eq.EnumerateAllItems())
            {
                if (!string.IsNullOrEmpty(filter) &&
                    !cat.Equals(filter, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                if (cat == EquipmentCategory.Hull) { skipped++; continue; }
                var res = ItemGrantService.Give(player, cat + ":" + id, 1,
                    ItemGrantService.GrantTarget.Inventory);
                if (res.Ok) given++;
                else skipped++;
            }

            // 2. Микромодули — если фильтра нет или фильтр == "MicroModule".
            if (string.IsNullOrEmpty(filter) ||
                filter.Equals("MicroModule", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var kv in items.EnumerateByKind(ItemKind.MicroModules))
                {
                    var res = ItemGrantService.Give(player, "MicroModule:" + kv.Key, 1,
                        ItemGrantService.GrantTarget.Inventory);
                    if (res.Ok) given++;
                    else skipped++;
                }
            }

            Log($"giveall: выдано {given} предмет(ов), пропущено {skipped}" +
                (filter != null ? $" (фильтр: {filter})" : "") + ".");
        }

        void GiveDroneCmd()
        {
            var player = PlayerShip.Instance?.ShipData;
            if (player == null) { Log("Player not ready."); return; }
            string race = string.IsNullOrEmpty(_lastArg) ? null : _lastArg;
            var item = PartnerScriptApi.GiveDronePackage(player, race);
            if (item == null) { Log("Give failed (см. лог Unity)."); return; }
            Log($"Выдан упакованный дрон [{race ?? PartnerScriptApi.DefaultDroneRace}] в инвентарь, item uid={SpriteUtility.ShortId(item.Uid)}");
        }

        void HireNearestCmd()
        {
            var player = PlayerShip.Instance?.ShipData;
            if (player == null) { Log("Player not ready."); return; }
            var cfg = GalaxyManager.Instance?.Context?.Config?.Partners;
            if (cfg == null) { Log("PartnersConfig не загружен."); return; }

            var target = PartnerScriptApi.FindNearestShip(player, s =>
                string.IsNullOrEmpty(s.PartnerLeaderUid)
                && cfg.PartnerableShipTypes != null
                && cfg.PartnerableShipTypes.ContainsKey(s.ShipTypeId ?? string.Empty));
            if (target == null) { Log("Нет подходящих hireable-кораблей рядом."); return; }

            bool ok = PartnerScriptApi.ForceHire(target, player);
            Log(ok ? $"Найм: {target.Name} ({SpriteUtility.ShortId(target.Uid)}) — партнёр."
                  : "Найм не удался (свита полна или уже занят).");
        }

        void DismissCmd()
        {
            var player = PlayerShip.Instance?.ShipData;
            if (player?.PartnerFollowerUids == null || player.PartnerFollowerUids.Count == 0)
            { Log("Партнёров нет."); return; }

            if (string.IsNullOrEmpty(_lastArg))
            {
                int n = player.PartnerFollowerUids.Count;
                // Копируем, потому что Break модифицирует список.
                var copy = new List<string>(player.PartnerFollowerUids);
                foreach (var uid in copy)
                {
                    var s = PartnerService.FindShipInGalaxy(uid);
                    if (s != null) PartnerScriptApi.BreakContract(s);
                }
                Log($"Разорвано {n} контракт(ов).");
                return;
            }

            if (!int.TryParse(_lastArg, out int idx) || idx < 0 || idx >= player.PartnerFollowerUids.Count)
            { Log($"Индекс вне диапазона (0..{player.PartnerFollowerUids.Count - 1})."); return; }

            var follower = PartnerService.FindShipInGalaxy(player.PartnerFollowerUids[idx]);
            if (follower == null) { Log("Партнёр не найден в галактике."); return; }
            PartnerScriptApi.BreakContract(follower);
            Log($"Контракт с {follower.Name} разорван.");
        }

        void ListPartnersCmd()
        {
            var player = PlayerShip.Instance?.ShipData;
            if (player?.PartnerFollowerUids == null || player.PartnerFollowerUids.Count == 0)
            { Log("Партнёров нет."); return; }

            int max = PartnerService.MaxPartners(player);
            Log($"Партнёры {player.PartnerFollowerUids.Count}/{max}:");
            for (int i = 0; i < player.PartnerFollowerUids.Count; i++)
            {
                var s = PartnerService.FindShipInGalaxy(player.PartnerFollowerUids[i]);
                if (s == null) { Log($"  [{i}] (UID {SpriteUtility.ShortId(player.PartnerFollowerUids[i])}) — не найден"); continue; }
                string state = s.CurrentHull <= 0 ? "мёртв" : $"HP {s.CurrentHull}/{s.MaxHull}";
                Log($"  [{i}] {s.Name} ({s.ShipTypeId}, {state}, uid={SpriteUtility.ShortId(s.Uid)})");
            }
        }

        // ── Мультигалактика ──────────────────────────────────────────────────────

        void ListGalaxiesCmd()
        {
            var gm = GalaxyManager.Instance;
            if (gm == null || gm.Galaxies == null || gm.Galaxies.Count == 0)
            { Log("Галактик нет (Galaxies пуст)."); return; }
            Log($"=== Galaxies ({gm.Galaxies.Count}) ===");
            foreach (var kv in gm.Galaxies)
            {
                string name = gm.Context?.Config?.Galaxies != null
                    && gm.Context.Config.Galaxies.TryGetValue(kv.Key, out var cfg)
                    && !string.IsNullOrEmpty(cfg?.Name) ? cfg.Name : kv.Key;
                string mark = kv.Key == gm.ActiveGalaxyKey ? " *" : "";
                int starCnt = kv.Value?.StarsMap?.Count ?? 0;
                Log($"  {kv.Key}: \"{name}\"  stars={starCnt}{mark}");
            }
        }

        void SwapGalaxyCmd()
        {
            var gm = GalaxyManager.Instance;
            if (gm == null) { Log("GalaxyManager не готов."); return; }
            if (string.IsNullOrEmpty(_lastArg))
            { Log("Usage: swapgalaxy <key>. Список: 'galaxies'."); return; }
            if (!gm.SwitchActiveGalaxy(_lastArg.Trim()))
            { Log($"Не удалось переключиться на '{_lastArg}'. Проверь ключ через 'galaxies'."); return; }
            Log($"Активная галактика: {gm.ActiveGalaxyKey}");
        }

        void TeleportCmd()
        {
            var gm = GalaxyManager.Instance;
            if (gm == null) { Log("GalaxyManager не готов."); return; }
            if (string.IsNullOrEmpty(_lastArg))
            { Log("Usage: teleport <star-name|uid> [@galaxy-key]"); return; }

            // Поддерживаем формат: "Alpha Centauri" и "Alpha Centauri @Andromeda".
            // '@<key>' в конце — явный указатель галактики. Просто "Alpha Centauri" не ломается.
            string trimmed = _lastArg.Trim();
            string galaxyKey = null;
            string arg = trimmed;
            int atIdx = trimmed.LastIndexOf('@');
            if (atIdx > 0 && atIdx < trimmed.Length - 1)
            {
                string tail = trimmed.Substring(atIdx + 1).Trim();
                if (gm.Galaxies.ContainsKey(tail))
                {
                    galaxyKey = tail;
                    arg = trimmed.Substring(0, atIdx).Trim();
                }
            }

            StarData target = null;
            string foundIn = null;

            if (!string.IsNullOrEmpty(galaxyKey))
            {
                if (!gm.Galaxies.TryGetValue(galaxyKey, out var g))
                { Log($"Галактика '{galaxyKey}' не найдена."); return; }
                target = ResolveStarArg(arg, g);
                if (target != null) foundIn = galaxyKey;
            }
            else
            {
                // Ищем по всем галактикам, приоритет — активной.
                if (gm.GeneratedGalaxy != null)
                {
                    target = ResolveStarArg(arg, gm.GeneratedGalaxy);
                    if (target != null) foundIn = gm.ActiveGalaxyKey;
                }
                if (target == null)
                    foreach (var kv in gm.Galaxies)
                    {
                        if (kv.Key == gm.ActiveGalaxyKey) continue;
                        var t = ResolveStarArg(arg, kv.Value);
                        if (t != null) { target = t; foundIn = kv.Key; break; }
                    }
            }

            if (target == null) { Log($"Звезда '{arg}' не найдена."); return; }
            gm.TeleportToStar(target);
            Log($"Телепорт → {target.Name} (галактика {foundIn}).");
        }

        // ── Червоточины ──────────────────────────────────────────────────────────

        void ListWormholes()
        {
            var g = GalaxyManager.Instance?.GeneratedGalaxy;
            if (g == null) { Log("No galaxy."); return; }
            int n = 0;
            foreach (var (wh, src) in WormholeService.All())
            {
                if (wh == null) continue;
                var dst = !string.IsNullOrEmpty(wh.TargetStarUid) && g.StarsMap.TryGetValue(wh.TargetStarUid, out var t) ? t : null;
                string dstLabel = dst != null ? dst.Name
                                  : (!string.IsNullOrEmpty(wh.TargetGalaxyId) ? $"{wh.TargetGalaxyId}:{Short(wh.TargetStarUid)}"
                                     : Short(wh.TargetStarUid));
                string ttl = wh.Phase == WormholePhase.Open ? $"{wh.OpenTurnsRemaining} ходов до закрытия" : $"{wh.DaysInPhase} в фазе {wh.Phase}";
                Log($"  {Short(wh.Uid)}  {src?.Name ?? "?"} → {dstLabel}  [{wh.Phase}]  {ttl}");
                n++;
            }
            Log($"Активных червоточин: {n} (из {WormholeService.CountActive(g)} по CountActive).");
        }

        void SpawnWormholeCmd()
        {
            string arg = _lastArg?.Trim() ?? "";
            if (arg == "?" || arg == "help") { PrintWormholeSpawnHelp(); return; }

            var g = GalaxyManager.Instance?.GeneratedGalaxy;
            if (g == null) { Log("No galaxy."); return; }

            // Разбор key=value (пробелы между парами; значение без пробелов).
            var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(arg))
            {
                foreach (var token in arg.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = token.IndexOf('=');
                    if (eq <= 0) { Log($"Ожидался key=value, получено: '{token}'."); return; }
                    pairs[token.Substring(0, eq).Trim()] = token.Substring(eq + 1).Trim();
                }
            }

            // Резолвим источник и цель.
            StarData srcStar = pairs.TryGetValue("src", out var srcArg) ? ResolveStarArg(srcArg, g) : null;
            if (srcArg != null && srcStar == null && !string.Equals(srcArg, "random", StringComparison.OrdinalIgnoreCase))
            { Log($"src: не нашёл звезду '{srcArg}'."); return; }

            StarData dstStar = pairs.TryGetValue("dst", out var dstArg) ? ResolveStarArg(dstArg, g) : null;
            if (dstArg != null && dstStar == null && !string.Equals(dstArg, "random", StringComparison.OrdinalIgnoreCase))
            { Log($"dst: не нашёл звезду '{dstArg}'."); return; }

            // Позиции.
            Vector2? srcPos = null, dstPos = null;
            if (pairs.TryGetValue("srcPos", out var srcPosArg))
            {
                srcPos = ResolvePosArg(srcPosArg, srcStar, out var srcErr);
                if (srcPos == null) { Log($"srcPos: {srcErr}"); return; }
            }
            if (pairs.TryGetValue("dstPos", out var dstPosArg))
            {
                dstPos = ResolvePosArg(dstPosArg, dstStar, out var dstErr);
                if (dstPos == null) { Log($"dstPos: {dstErr}"); return; }
            }

            // Собираем WormholeLocation? параметры (null = случайно у сервиса).
            WormholeLocation? source = null;
            if (srcStar != null) source = srcPos.HasValue ? new Point(srcStar, srcPos.Value) : (WormholeLocation)srcStar;
            else if (srcPos.HasValue)
            { Log("srcPos без src не имеет смысла (нужна звезда-источник)."); return; }

            WormholeLocation? target = null;
            if (dstStar != null) target = dstPos.HasValue ? new Point(dstStar, dstPos.Value) : (WormholeLocation)dstStar;
            else if (dstPos.HasValue)
            { Log("dstPos без dst не имеет смысла."); return; }

            // TTL и межгалактика.
            int? ttl = null;
            if (pairs.TryGetValue("ttl", out var ttlArg))
            {
                if (!int.TryParse(ttlArg, out var t) || t <= 0) { Log($"ttl: ожидается положительное целое, получено '{ttlArg}'."); return; }
                ttl = t;
            }
            pairs.TryGetValue("galaxy", out var galaxyId);

            // Графика.
            WormholeGraphics gfx = null;
            if (pairs.ContainsKey("opening") || pairs.ContainsKey("cycle") || pairs.ContainsKey("closing") || pairs.ContainsKey("icon"))
            {
                pairs.TryGetValue("opening", out var op);
                pairs.TryGetValue("cycle",   out var cy);
                pairs.TryGetValue("closing", out var cl);
                pairs.TryGetValue("icon",    out var ic);
                gfx = WormholeGraphics.Of(op, cy, cl, ic);
            }

            var wh = WormholeService.Spawn(source, target, ttl, gfx, galaxyId);
            if (wh == null) { Log("Спавн не удался (см. лог WormholeService)."); return; }
            Log($"Спавн: {Short(wh.Uid)}  {(srcStar?.Name ?? "рандом")} → {(dstStar?.Name ?? "рандом")}  " +
                $"[Opening]  ttl={(ttl?.ToString() ?? "default")}  gfx={(gfx != null ? "custom" : "default")}");
        }

        void PrintWormholeSpawnHelp()
        {
            Log("wh_spawn — гибкий спавн червоточины. Формат: wh_spawn key=value key=value ...");
            Log("  src=<name|uid|player|random>   — звезда-источник (без ключа = случайная).");
            Log("  srcPos=<x,y|near[:r]|edge>     — точка входа. near:5 = случ. в r=5 от звезды; edge = 40..90% радиуса.");
            Log("  dst=<name|uid|random>          — целевая звезда (без ключа = случ. в MinDist..MaxDist).");
            Log("  dstPos=<x,y|near[:r]|edge>     — фиксированная точка выхода (иначе рандом на каждый вход).");
            Log("  ttl=<turns>                    — длительность фазы Open (по умолчанию — Wormhole_OpenTurns).");
            Log("  galaxy=<id>                    — межгалактический прыжок (обязателен dst с валидным Uid).");
            Log("  opening=<path>  cycle=<path>  closing=<path>  icon=<path>  — переопределение графики.");
            Log("Примеры:");
            Log("  wh_spawn");
            Log("  wh_spawn src=player srcPos=near:5");
            Log("  wh_spawn src=Alpha dst=Beta ttl=30");
            Log("  wh_spawn src=player dst=Sol dstPos=0,0 ttl=50 opening=Effects/Wh_Open");
        }

        void SpawnWormholeNearCmd()
        {
            var p = PlayerShip.Instance?.ShipData;
            if (p?.CurrentStar == null) { Log("Игрок не в звёздной системе."); return; }
            float radius = 5f;
            if (!string.IsNullOrEmpty(_lastArg) && float.TryParse(_lastArg,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var r) && r > 0f)
                radius = r;
            var wh = WormholeService.Spawn(Positions.Near(p, radius));
            if (wh == null) { Log("Не удалось заспавнить."); return; }
            Log($"Спавн около игрока (r={radius}): {Short(wh.Uid)} → {Short(wh.TargetStarUid)}.");
        }

        // ── Резолверы аргументов ─────────────────────────────────────────

        static StarData ResolveStarArg(string arg, GalaxyData g)
        {
            if (string.IsNullOrEmpty(arg) || string.Equals(arg, "random", StringComparison.OrdinalIgnoreCase)) return null;
            if (string.Equals(arg, "player", StringComparison.OrdinalIgnoreCase))
                return PlayerShip.Instance?.ShipData?.CurrentStar;
            // Точный UID.
            if (g.StarsMap.TryGetValue(arg, out var byUid)) return byUid;
            // Имя (без учёта регистра).
            foreach (var s in g.StarsMap.Values)
                if (s != null && string.Equals(s.Name, arg, StringComparison.OrdinalIgnoreCase)) return s;
            // Префикс UID.
            foreach (var s in g.StarsMap.Values)
                if (s != null && s.Uid != null && s.Uid.StartsWith(arg, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        static Vector2? ResolvePosArg(string arg, StarData star, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(arg)) { error = "пусто"; return null; }
            if (string.Equals(arg, "random", StringComparison.OrdinalIgnoreCase))
            {
                if (star == null) { error = "random требует src/dst звезды"; return null; }
                return Positions.Near(star).Position;
            }
            if (string.Equals(arg, "edge", StringComparison.OrdinalIgnoreCase))
            {
                if (star == null) { error = "edge требует src/dst звезды"; return null; }
                return Positions.Near(star, -1f).Position;
            }
            if (arg.StartsWith("near", StringComparison.OrdinalIgnoreCase))
            {
                if (star == null) { error = "near требует src/dst звезды"; return null; }
                float r = -1f;
                int colon = arg.IndexOf(':');
                if (colon > 0 && float.TryParse(arg.Substring(colon + 1),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    r = parsed;
                return Positions.Near(star, r).Position;
            }
            // x,y
            int comma = arg.IndexOf(',');
            if (comma > 0)
            {
                var xs = arg.Substring(0, comma);
                var ys = arg.Substring(comma + 1);
                if (float.TryParse(xs, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
                 && float.TryParse(ys, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y))
                    return new Vector2(x, y);
            }
            error = $"неизвестный формат '{arg}'";
            return null;
        }

        void CloseWormholeCmd()
        {
            if (string.IsNullOrEmpty(_lastArg)) { Log("Usage: wh_close <uid|prefix>"); return; }
            var wh = ResolveWormhole(_lastArg);
            if (wh == null) { Log($"Червоточина '{_lastArg}' не найдена."); return; }
            if (WormholeService.ForceClose(wh)) Log($"Закрываю {Short(wh.Uid)}.");
            else Log($"{Short(wh.Uid)} уже в фазе Closing.");
        }

        void DestroyWormholeCmd()
        {
            if (string.IsNullOrEmpty(_lastArg)) { Log("Usage: wh_destroy <uid|prefix>"); return; }
            var wh = ResolveWormhole(_lastArg);
            if (wh == null) { Log($"Червоточина '{_lastArg}' не найдена."); return; }
            if (WormholeService.Destroy(wh.Uid)) Log($"Удалено: {Short(wh.Uid)}.");
            else Log("Не удалось удалить.");
        }

        static WormholeData ResolveWormhole(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return null;
            var direct = WormholeService.Find(arg);
            if (direct != null) return direct;
            // Префикс — сравниваем начало UID.
            foreach (var (wh, _) in WormholeService.All())
                if (wh != null && wh.Uid != null &&
                    wh.Uid.StartsWith(arg, StringComparison.OrdinalIgnoreCase))
                    return wh;
            return null;
        }

        static string Short(string uid) => string.IsNullOrEmpty(uid) ? "—" : uid.Substring(0, Math.Min(6, uid.Length));

        // ──────────────────────────────────────────────────────────────────────────

        void Log(string m)
        {
            _logs.Add(m);
            if (_logs.Count > MaxLog) _logs.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        void OnGUI()
        {
            if (!_showConsole) return;

            if (_pendingCmd != null)
            {
                string cmd = _pendingCmd;
                _pendingCmd = null;
                _input = "";
                _caret = 0;
                Exec(cmd);
            }

            float h = Screen.height * 0.4f;
            GUI.Box(new Rect(0, 0, Screen.width, h), "");
            GUILayout.BeginArea(new Rect(10, 10, Screen.width - 20, h - 20));

            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (var l in _logs) GUILayout.Label(l);
            GUILayout.EndScrollView();

            // Мигающая каретка в позиции _caret. Показываем |, ~2 Hz.
            bool caretBlink = ((int)(Time.unscaledTime * 2f) & 1) == 0;
            char caretChar  = caretBlink ? '|' : ' ';
            string left  = _caret > 0            ? _input.Substring(0, _caret)              : string.Empty;
            string right = _caret < _input.Length ? _input.Substring(_caret)                 : string.Empty;
            GUILayout.Label($"> {left}{caretChar}{right}");

            GUILayout.EndArea();
        }
    }
}
