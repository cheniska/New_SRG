using UnityEngine;
using SRG.Dialog;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.Simulation;

namespace SRG.Ships.Services
{
    /// <summary>Причина отказа/успеха при попытке заступиться за жертву перед агрессором.</summary>
    public enum ProtectRefusalReason
    {
        Accepted = 0,
        NoVictim,          // жертвы, за которую можно вступиться, не нашлось
        AggressorRefuses,  // Warrior / Dominator — «на службе», отступать не буду
        AggressorTooBusy,  // корабль отступил (в страхе/в другом бою) — уже не преследует
    }

    /// <summary>Результат Preview/TryProtect.
    /// VictimUid — UID жертвы, за которую вступились (может быть != null и на отказе — для реплики).</summary>
    public struct ProtectResult
    {
        public ProtectRefusalReason Reason;
        public string VictimUid;
        public bool Accepted => Reason == ProtectRefusalReason.Accepted;

        public static ProtectResult Ok(string uid) => new() { Reason = ProtectRefusalReason.Accepted, VictimUid = uid };
        public static ProtectResult Refuse(ProtectRefusalReason r, string uid = null) => new() { Reason = r, VictimUid = uid };
    }

    /// <summary>Тип награды жертвы за спасение. Три варианта «спасибо» различаются тем, насколько
    /// жертва вообще нуждалась в помощи — от этого зависит её реплика:
    /// <c>Protect.TargetThanks</c> / <c>TargetNotFearShip</c> («я бы и один справился») /
    /// <c>TargetMayRunAway</c> («он бы меня не догнал»).</summary>
    public enum ProtectRewardKind
    {
        None = 0, Money, Goods, Thanks,
        ThanksNotAfraid,    // жертва не слабее агрессора
        ThanksMayRunAway,   // жертва быстрее агрессора
    }

    /// <summary>Результат <see cref="ClaimReward"/>: тип награды + сумма (для Money) или вес груза (для Goods).</summary>
    public struct ProtectReward
    {
        public ProtectRewardKind Kind;
        public int Amount;
    }

    /// <summary>
    /// Заступничество: игрок обращается к атакующему кораблю с просьбой отстать от жертвы,
    /// затем (если согласился) вызывает жертву — та благодарит наградой.
    ///
    /// Жертва определяется как ближайший к агрессору корабль, у которого <c>LastAttackerUid</c>
    /// указывает на агрессора и обида ещё свежа.
    /// </summary>
    public static class ProtectService
    {
        /// <summary>Через сколько ходов жертва «забывает» о спасении и перестаёт давать награду.</summary>
        public const int RewardMemoryTurns = 20;

        /// <summary>Помнит ли жертва о спасении именно сейчас (RescuedByPlayerTurn ещё свежий).</summary>
        public static bool IsRescueRewardPending(ShipData victim)
        {
            if (victim == null || victim.RescuedByPlayerTurn < 0) return false;
            int now = GameWorld.GeneratedGalaxy?.CurrentTurn ?? 0;
            return now - victim.RescuedByPlayerTurn <= RewardMemoryTurns;
        }

        /// <summary>Найти жертву target'а в его звезде (LastAttackerUid == target.Uid).</summary>
        public static ShipData FindVictim(ShipData aggressor)
        {
            if (aggressor?.CurrentStar == null) return null;
            var ships = aggressor.CurrentStar.Ships;
            ShipData best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < ships.Count; i++)
            {
                var v = ships[i];
                if (v == aggressor || v.CurrentHull <= 0) continue;
                if (v.LastAttackerUid != aggressor.Uid) continue;
                float dSq = (v.Position - aggressor.Position).sqrMagnitude;
                if (dSq < bestSq) { bestSq = dSq; best = v; }
            }
            return best;
        }

        public static ProtectResult Preview(ShipData aggressor, ShipData player)
        {
            if (aggressor == null || player == null) return ProtectResult.Refuse(ProtectRefusalReason.NoVictim);
            var victim = FindVictim(aggressor);
            if (victim == null) return ProtectResult.Refuse(ProtectRefusalReason.NoVictim);

            if (IsUnappealable(aggressor))
                return ProtectResult.Refuse(ProtectRefusalReason.AggressorRefuses, victim.Uid);

            return ProtectResult.Ok(victim.Uid);
        }

        /// <summary>Успешный «отстань от X»: снимает свежую обиду у жертвы и агрессора, поднимает
        /// отношение жертва→игрок до Good, стирает LastAttackerUid у жертвы, если он указывал на агрессора.</summary>
        public static ProtectResult TryProtect(ShipData aggressor, ShipData player)
        {
            var pv = Preview(aggressor, player);
            if (!pv.Accepted) return pv;
            var star = aggressor.CurrentStar;
            var victim = star?.FindShip(pv.VictimUid);
            if (victim == null) return ProtectResult.Refuse(ProtectRefusalReason.NoVictim);

            if (victim.LastAttackerUid == aggressor.Uid) victim.LastAttackerUid = null;
            if (aggressor.LastAttackerUid == victim.Uid) aggressor.LastAttackerUid = null;

            Relations.RaiseToLevel(aggressor, victim, RelationLevel.Normal);
            Relations.RaiseToLevel(victim, player, RelationLevel.Good);

            int now = GameWorld.GeneratedGalaxy?.CurrentTurn ?? 0;
            victim.RescuedByPlayerTurn = now;

            return pv;
        }

        /// <summary>Забрать награду у жертвы — вызывается когда игрок открывает диалог со спасённым.
        /// Отдаёт деньги если у жертвы есть, иначе груз, иначе только благодарность.
        /// Меняет счёт/трюм жертвы и игрока.</summary>
        public static ProtectReward ClaimReward(ShipData victim, ShipData player)
        {
            if (victim == null || player == null) return default;
            if (!IsRescueRewardPending(victim)) return default;
            // Однократная выплата: гасим маркер сразу, независимо от типа награды.
            victim.RescuedByPlayerTurn = -1;
            var tuning = GetTuning();
            if (victim.Money > 0)
            {
                int fraction = Mathf.Max(50, Mathf.RoundToInt(victim.Money * Mathf.Clamp01(tuning?.ProtectRewardMoneyRatio ?? 0.2f)));
                int give = Mathf.Min(victim.Money, fraction);
                victim.Money -= give;
                player.Money += give;
                return new ProtectReward { Kind = ProtectRewardKind.Money, Amount = give };
            }

            int stacksWeight = victim.Inventory?.TotalStacksWeight() ?? 0;
            if (stacksWeight > 0)
            {
                var star = victim.CurrentStar
                          ?? GameWorld.GeneratedGalaxy?.StarsMap[victim.CurrentStarUid];
                float frac = Mathf.Clamp(tuning?.ProtectRewardGoodsRatio ?? 0.25f, 0.05f, 1f);
                int totalDropped = 0;
                var ids = new System.Collections.Generic.List<string>(victim.Inventory.Stacks.Keys);
                foreach (var id in ids)
                {
                    if (!victim.Inventory.Stacks.TryGetValue(id, out var st) || st == null || st.TotalWeight <= 0) continue;
                    int take = Mathf.Max(1, Mathf.RoundToInt(st.TotalWeight * frac));
                    var chunk = victim.Inventory.TakeStack(id, take);
                    if (chunk == null || chunk.TotalWeight <= 0) continue;
                    ContainerFactory.SpawnContainerWithStack(victim, chunk, star);
                    totalDropped += chunk.TotalWeight;
                }
                if (totalDropped > 0) return new ProtectReward { Kind = ProtectRewardKind.Goods, Amount = totalDropped };
            }

            return new ProtectReward { Kind = ThanksFlavour(victim), Amount = 0 };
        }

        /// <summary>Платить нечем — остаётся благодарность, но её тон зависит от того, была ли
        /// помощь нужна. Агрессор берётся из <c>LastAttackerUid</c> жертвы: к моменту получения
        /// награды он уже отстал, но метка обиды ещё указывает на него.</summary>
        private static ProtectRewardKind ThanksFlavour(ShipData victim)
        {
            var aggressor = string.IsNullOrEmpty(victim.LastAttackerUid)
                ? null : victim.CurrentStar?.FindShip(victim.LastAttackerUid);
            if (aggressor == null) return ProtectRewardKind.Thanks;

            if (SRG.NpcAI.NpcBrain.CalculateStrength(victim)
                >= SRG.NpcAI.NpcBrain.CalculateStrength(aggressor))
                return ProtectRewardKind.ThanksNotAfraid;
            if (SRG.Equipment.EquipmentSystem.CalculateSpeed(victim)
                > SRG.Equipment.EquipmentSystem.CalculateSpeed(aggressor))
                return ProtectRewardKind.ThanksMayRunAway;
            return ProtectRewardKind.Thanks;
        }

        private static bool IsUnappealable(ShipData aggressor)
        {
            if (string.IsNullOrEmpty(aggressor?.ShipTypeId)) return false;
            if (aggressor.ShipTypeId == "Warrior") return true;
            if (!string.IsNullOrEmpty(aggressor.Race) && aggressor.Race == "Dominators") return true;
            return false;
        }

        private static DialogTuning GetTuning() =>
            GameWorld.Context?.Config?.Dialogs?.Tuning;
    }
}
