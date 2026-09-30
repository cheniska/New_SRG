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
                "code: BK|CB|MC|PB|RC|RG|WB|SB (содружество) или Blazer|Keller|Terron (синтеты). " +
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
    }
}
