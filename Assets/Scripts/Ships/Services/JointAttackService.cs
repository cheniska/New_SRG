using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Dialog;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.NpcAI;
using SRG.NpcAI.Orders;
using SRG.Simulation;

namespace SRG.Ships.Services
{
    /// <summary>Причина отказа/успеха для совместного нападения.</summary>
    public enum JointAttackRefusalReason
    {
        Accepted = 0,
        NoTarget,          // цель не найдена
        Fear,              // противник слишком силён
        HaveBusiness,      // корабль на партнёрстве / контракте / посадке
        Suspect,           // игрок мало доверия
        WeFriends,         // цель — друг корабля
        WeAlreadyHavePact, // с целью уже мирное соглашение
        InFear,            // союзник сам в панике (Brain.InFear) — ему не до чужих боёв
    }

    /// <summary>Результат Preview/TryEnlist.</summary>
    public struct JointAttackResult
    {
        public JointAttackRefusalReason Reason;
        public bool Accepted => Reason == JointAttackRefusalReason.Accepted;
        public static JointAttackResult Ok() => new() { Reason = JointAttackRefusalReason.Accepted };
        public static JointAttackResult Refuse(JointAttackRefusalReason r) => new() { Reason = r };
    }

    /// <summary>
    /// Согласование совместной атаки. Игрок просит союзника атаковать цель — сервис проверяет
    /// готовность и, при согласии, ставит союзнику OrderAttack.
    ///
    /// Отказы:
    ///  • Fear — сила цели > <see cref="DialogTuning.JointAttackFearRatio"/> × суммы сил (игрок+союзник).
    ///  • InFear — союзник сам в панике (<see cref="NpcBrain.InFear"/>), независимо от силы цели.
    ///  • Suspect — отношение союзника к игроку ниже Normal.
    ///  • WeFriends — отношение союзника к цели ≥ Good.
    ///  • WeAlreadyHavePact — отношение союзника к цели ≥ Normal И у цели нет свежей обиды на союзника.
    ///  • HaveBusiness — союзник уже партнёр / приземляется / гиперпрыгает.
    /// </summary>
    public static class JointAttackService
    {
        public static JointAttackResult Preview(ShipData ally, ShipData player, ShipData target)
        {
            if (ally == null || player == null || target == null)
                return JointAttackResult.Refuse(JointAttackRefusalReason.NoTarget);
            if (target.CurrentHull <= 0)
                return JointAttackResult.Refuse(JointAttackRefusalReason.NoTarget);
            var tuning = GetTuning();

            // Занят: партнёрство / посадка / гиперпрыжок.
            if (!string.IsNullOrEmpty(ally.PartnerLeaderUid) && ally.PartnerLeaderUid != player.Uid)
                return JointAttackResult.Refuse(JointAttackRefusalReason.HaveBusiness);
            if (!string.IsNullOrEmpty(ally.LandingPlanetUid) || !string.IsNullOrEmpty(ally.LandedPlanetUid))
                return JointAttackResult.Refuse(JointAttackRefusalReason.HaveBusiness);
            if (ally.HyperjumpPhase != HyperjumpPhase.None)
                return JointAttackResult.Refuse(JointAttackRefusalReason.HaveBusiness);

            // Suspect: доверие к игроку ниже Normal.
            int trust = Relations.Get(ally, player);
            if (trust <= OwnerRaceRelationsManager.BAD_MAX)
                return JointAttackResult.Refuse(JointAttackRefusalReason.Suspect);

            // WeFriends: явно дружит с целью.
            int allyToTarget = Relations.Get(ally, target);
            if (allyToTarget >= OwnerRaceRelationsManager.NORMAL_MAX + 1)
                return JointAttackResult.Refuse(JointAttackRefusalReason.WeFriends);

            // WeAlreadyHavePact: нейтральное отношение и нет свежей обиды с обеих сторон.
            if (allyToTarget > OwnerRaceRelationsManager.BAD_MAX
                && ally.LastAttackerUid != target.Uid
                && target.LastAttackerUid != ally.Uid)
                return JointAttackResult.Refuse(JointAttackRefusalReason.WeAlreadyHavePact);

            // Отказ по страху бывает двух видов, и диалог отвечает на них разными фразами:
            //   InFear — союзник уже в панике по своей внутренней оценке (Brain.InFear).
            //            Ловит случаи «на союзника давит толпа / он безоружен» — без этой
            //            проверки он согласился бы под самой угрозой смерти, потому что
            //            классическая формула смотрит только на «нашу мощь vs мощь одиночной
            //            цели», не учитывая контекст в звезде.
            //   Fear   — сила цели > (сила игрока + союзника) × fearRatio.
            if (ally.Brain != null && ally.Brain.InFear)
                return JointAttackResult.Refuse(JointAttackRefusalReason.InFear);

            float allyStr   = NpcBrain.CalculateStrength(ally);
            float playerStr = NpcBrain.CalculateStrength(player);
            float targetStr = NpcBrain.CalculateStrength(target);
            float sum = allyStr + playerStr;
            float ratio = Mathf.Max(0.5f, tuning?.JointAttackFearRatio ?? 1.5f);
            if (sum > 0.001f && targetStr / sum > ratio)
                return JointAttackResult.Refuse(JointAttackRefusalReason.Fear);

            return JointAttackResult.Ok();
        }

        /// <summary>При согласии — ставит союзнику PartnerOrder=Attack с уидом цели (если он партнёр)
        /// либо просто помечает LastAttackerUid, чтобы NpcBrain включил ActionPursueAndAttack.</summary>
        public static JointAttackResult TryEnlist(ShipData ally, ShipData player, ShipData target)
        {
            var pv = Preview(ally, player, target);
            if (!pv.Accepted) return pv;

            // Если союзник — партнёр игрока, поставим прямой приказ атаковать.
            if (ally.PartnerLeaderUid == player.Uid)
            {
                ally.PartnerOrder = PartnerOrderKind.Attack;
                ally.PartnerOrderTargetUid = target.Uid;
            }
            else
            {
                // Иначе — сделаем цель врагом союзника: понижаем отношения до Hostile,
                // а свежая «обида» через LastAttackerUid подтолкнёт NpcBrain выбрать её как цель.
                Relations.LowerToLevel(ally, target, RelationLevel.Hostile);
                int now = GameWorld.GeneratedGalaxy?.CurrentTurn ?? 0;
                ally.LastAttackerUid = target.Uid;
                ally.LastAttackerTurn = now;
            }

            return pv;
        }

        /// <summary>Живые корабли в звезде — кандидаты на совместную атаку.
        /// <para>Предложить можно <b>любой</b> корабль, а не только враждебного игроку: решение
        /// «нападём или нет» принимает союзник, и у него на это есть свои фразы — <c>WeFriends</c>
        /// («это друг мой»), <c>WeAlreadyHavePact</c> («у нас с ним мир»), <c>Fear</c>. Прежний
        /// фильтр по враждебности отбирал этот выбор у игрока заранее и при пустом списке ронял
        /// узел в <c>NoTarget</c>, для которого реплики нет вовсе.</para>
        /// <para>Из списка выпадают только те, кого атаковать нельзя технически: сам игрок,
        /// сам <paramref name="ally"/> (просить напасть на себя — бессмыслица), мёртвые,
        /// контейнеры-предметы, станции и пристыкованные к кораблю.</para></summary>
        /// <param name="ally">Собеседник, которого просят напасть. Исключается из списка.</param>
        public static List<ShipData> ListPossibleTargets(ShipData player, ShipData ally = null)
        {
            var list = new List<ShipData>();
            var star = player?.CurrentStar;
            if (star == null) return list;
            for (int i = 0; i < star.Ships.Count; i++)
            {
                var s = star.Ships[i];
                if (s == player || s == ally || s.CurrentHull <= 0) continue;
                if (s.IsItem || s.IsStation) continue;
                if (!string.IsNullOrEmpty(s.LandedOnShipUid)) continue;
                list.Add(s);
            }
            return list;
        }

        private static DialogTuning GetTuning() =>
            GameWorld.Context?.Config?.Dialogs?.Tuning;
    }
}
