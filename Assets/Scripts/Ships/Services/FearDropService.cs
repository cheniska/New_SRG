using System.Collections.Generic;
using UnityEngine;
using SRG.Core;
using SRG.Dialog;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.NpcAI;
using SRG.NpcAI.Actions;
using SRG.UI.Screens;

namespace SRG.Ships.Services
{
    /// <summary>
    /// Убегающий с грузом корабль сбрасывает часть трюма «в пользу» преследователя, чтобы тот отстал.
    /// Реплика берётся из пула <c>DropGoodsInFear</c> (TextsConfig).
    ///
    /// Условия срабатывания на ходу:
    ///   • у корабля <see cref="NpcBrain.InFear"/> = true (единый fear-флаг, унифицированный
    ///     с диалогами/JointAttack/EvaluateSituation);
    ///   • у него больше <see cref="DialogTuning.FearDropWeightMin"/> суммарного веса стеков;
    ///   • с предыдущего сброса прошло ≥ <see cref="DialogTuning.FearDropCooldownTurns"/> ходов.
    /// Сбрасывается доля <see cref="DialogTuning.FearDropFraction"/> от общего веса стеков (по крупнейшему стеку).
    ///
    /// Имя «преследователя» для сообщения — либо CombatTargetUid текущей activity,
    /// либо LastAttackerUid, либо ближайший враждебный корабль в звезде.
    /// </summary>
    public static class FearDropService
    {
        private const string LastDropKey = "_fear_last_drop_turn";

        /// <summary>Прогон по всем кораблям галактики. Вызывается 1 раз в ход из GalaxyData.GalaxyNextDay.</summary>
        public static void TickAll(GalaxyData galaxy)
        {
            if (galaxy == null) return;
            var tuning = GetTuning();
            int minWeight = Mathf.Max(1, tuning?.FearDropWeightMin ?? 20);
            int cooldown  = Mathf.Max(1, tuning?.FearDropCooldownTurns ?? 3);
            float fraction = Mathf.Clamp(tuning?.FearDropFraction ?? 0.25f, 0.01f, 1f);
            int now = galaxy.CurrentTurn;

            foreach (var star in galaxy.StarsMap.Values)
            {
                if (star?.Ships == null) continue;
                for (int i = 0; i < star.Ships.Count; i++)
                {
                    var ship = star.Ships[i];
                    if (ship == null || ship.CurrentHull <= 0 || ship.IsItem) continue;
                    if (ship.Brain == null || !ship.Brain.InFear) continue;
                    if ((ship.Inventory?.TotalStacksWeight() ?? 0) < minWeight) continue;

                    int lastDropTurn = ReadLastDropTurn(ship);
                    if (now - lastDropTurn < cooldown) continue;

                    string threatUid = ResolveThreatUid(ship, star);
                    DropOnce(ship, threatUid, fraction, star, now);
                }
            }
        }

        /// <summary>UID корабля, «в пользу» которого адресуется сброс. Приоритет:
        /// combat-цель активности (Flee/PursueAndAttack), затем последний агрессор, затем
        /// ближайший враждебный в звезде.</summary>
        private static string ResolveThreatUid(ShipData ship, StarData star)
        {
            var brain = ship.Brain;
            var act = brain?.CurrentActivity;
            if (act is ActionFlee flee && !string.IsNullOrEmpty(flee.ThreatShipUid))
                return flee.ThreatShipUid;
            string ctUid = act?.CombatTargetUid;
            if (!string.IsNullOrEmpty(ctUid)) return ctUid;
            if (!string.IsNullOrEmpty(ship.LastAttackerUid)) return ship.LastAttackerUid;
            return NpcTargeting.FindNearestHostileUid(star, ship, ship.Position);
        }

        private static void DropOnce(ShipData ship, string threatUid, float fraction, StarData star, int now)
        {
            // Сбрасываем долю от самого крупного стека — по одному контейнеру за раз, чтобы
            // сообщения в консоли не превращались в спам.
            var (id, weight) = CargoUtils.GetLargestCargo(ship);
            if (string.IsNullOrEmpty(id) || weight <= 0) return;
            int drop = Mathf.Max(1, Mathf.RoundToInt(weight * fraction));
            var chunk = ship.Inventory.TakeStack(id, drop);
            if (chunk == null || chunk.TotalWeight <= 0) return;

            ContainerFactory.SpawnContainerWithStack(ship, chunk, star);
            WriteLastDropTurn(ship, now);

            string threatName = FindShipName(star, threatUid);
            string cargoName  = chunk.Name ?? chunk.ItemId ?? id;

            // Реплика самого корабля — из пула DropGoodsInFear.Drop («<FullShipBad>! Я согласен
            // расстаться с частью груза»). Пул адресуется к преследователю, поэтому <…Bad>
            // подставляем им. Нет пула — остаётся служебная строка.
            string say = SRG.Dialog.DialogService.PickFromPool("DropGoodsInFear.Drop");
            if (!string.IsNullOrEmpty(say))
            {
                say = say.Replace("<FullShipBad>", threatName).Replace("<ShipBad>", threatName);
                GameConsoleController.AddEntry($"[Связь] {ship.Name}: {say}");
            }
            GameConsoleController.AddEntry(
                $"[Связь] {ship.Name} сбрасывает {chunk.TotalWeight} ед. \"{cargoName}\" в пользу {threatName}.");
        }

        private static string FindShipName(StarData star, string uid)
        {
            if (star == null || string.IsNullOrEmpty(uid)) return "преследователя";
            for (int i = 0; i < star.Ships.Count; i++)
                if (star.Ships[i].Uid == uid) return star.Ships[i].Name ?? uid;
            return uid;
        }

        // «Последний ход сброса» держим в самом ShipData через lightweight dict-хак: пусть будет
        // хранение в приватном extension через словарь по Uid (JsonIgnore — не сериализуется).
        private static readonly Dictionary<string, int> _lastDropTurnByUid = new();

        private static int ReadLastDropTurn(ShipData ship) =>
            _lastDropTurnByUid.TryGetValue(ship.Uid, out var t) ? t : -9999;

        private static void WriteLastDropTurn(ShipData ship, int turn) =>
            _lastDropTurnByUid[ship.Uid] = turn;

        private static DialogTuning GetTuning() =>
            GalaxyManager.Instance?.Context?.Config?.Dialogs?.Tuning;
    }
}
