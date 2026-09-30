using UnityEngine;
using SRG.Config;
using SRG.Dialog;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.Simulation;

namespace SRG.Ships.Services
{
    /// <summary>Причина, по которой TruceService.TryPropose отказал (или Accepted при успехе).
    /// Каждая причина мапится на реплику в диалоге; поле CounterFee у отказа
    /// OfferTooLow подсказывает, какую сумму ожидает корабль.</summary>
    public enum TruceRefusalReason
    {
        Accepted = 0,
        LongDistance,      // цель далеко — угроза не воспринимается всерьёз
        NotEnoughMoney,    // у игрока не хватает денег
        OfferTooLow,       // предложение ниже требуемой суммы (Fee в результате — минимум)
        ShipTypeRefuses,   // Warrior / Dominator — не идёт на выкуп
        NoNeedForTruce,    // корабль и так не враждебен
    }

    /// <summary>Результат TruceService.Preview/TryPropose.
    /// Если Accepted → Fee = списанная сумма.
    /// Если OfferTooLow → Fee = минимально приемлемая сумма (для UI-подсказки).</summary>
    public struct TruceResult
    {
        public TruceRefusalReason Reason;
        public int Fee;
        public bool Accepted => Reason == TruceRefusalReason.Accepted;

        public static TruceResult Ok(int fee) => new() { Reason = TruceRefusalReason.Accepted, Fee = fee };
        public static TruceResult Refuse(TruceRefusalReason r, int fee = 0) => new() { Reason = r, Fee = fee };
    }

    /// <summary>
    /// Заключение перемирия между игроком и NPC-кораблём.
    /// Работает поверх системы отношений: при успехе поднимает личную дельту до
    /// нейтрального уровня в обе стороны и очищает LastAttacker-метки, чтобы NPC
    /// перестал считать игрока целью.
    ///
    /// Экономика: минимальный выкуп зависит от типа цели и её ценности (стоимость корпуса
    /// / веса корабля), настраивается через <see cref="DialogTuning.TruceMinFeeBase"/>.
    /// Расстояние: если цель дальше <see cref="DialogTuning.TruceLongDistance"/> — отказ.
    /// </summary>
    public static class TruceService
    {
        /// <summary>Проверяет без побочек, согласится ли корабль на перемирие за offer.</summary>
        public static TruceResult Preview(ShipData target, ShipData player, int offer)
        {
            if (target == null || player == null) return TruceResult.Refuse(TruceRefusalReason.NoNeedForTruce);
            var tuning = GetTuning();

            // Военные и синтеты не идут на подкуп.
            if (IsUnbribable(target)) return TruceResult.Refuse(TruceRefusalReason.ShipTypeRefuses);

            // Если корабль и так не враждебен — перемирие не требуется.
            var rel = OwnerRaceRelationsManager.Instance;
            bool hostileFactionally = rel != null && rel.AreHostile(target, player);
            bool personalGrudge = !string.IsNullOrEmpty(target.LastAttackerUid) && target.LastAttackerUid == player.Uid;
            if (!hostileFactionally && !personalGrudge)
                return TruceResult.Refuse(TruceRefusalReason.NoNeedForTruce);

            // Дистанция «издалека» — угроза не воспринимается.
            float distSq = (target.Position - player.Position).sqrMagnitude;
            float longD = Mathf.Max(1f, tuning?.TruceLongDistance ?? 8f);
            if (distSq > longD * longD)
                return TruceResult.Refuse(TruceRefusalReason.LongDistance);

            int minFee = CalcMinFee(target, tuning);
            if (offer < minFee)
                return TruceResult.Refuse(TruceRefusalReason.OfferTooLow, minFee);

            if (player.Money < offer)
                return TruceResult.Refuse(TruceRefusalReason.NotEnoughMoney, minFee);

            return TruceResult.Ok(offer);
        }

        /// <summary>Списывает деньги, поднимает отношения до Normal, снимает LastAttackerUid у обеих сторон.</summary>
        public static TruceResult TryPropose(ShipData target, ShipData player, int offer)
        {
            var result = Preview(target, player, offer);
            if (!result.Accepted) return result;

            player.Money -= result.Fee;
            target.Money += result.Fee;

            // Взаимно снимаем свежую «обиду»
            if (target.LastAttackerUid == player.Uid) target.LastAttackerUid = null;
            if (player.LastAttackerUid == target.Uid) player.LastAttackerUid = null;

            // Персональная дельта до нейтрального уровня в обе стороны.
            Relations.RaiseToLevel(target, player, RelationLevel.Normal);
            Relations.RaiseToLevel(player, target, RelationLevel.Normal);

            return result;
        }

        /// <summary>Минимально приемлемая сумма выкупа. Формула: base × сила_цели / 1000 (round to nearest 50),
        /// не меньше <see cref="DialogTuning.TruceMinFeeBase"/>.</summary>
        public static int CalcMinFee(ShipData target, DialogTuning tuning)
        {
            int baseFee = Mathf.Max(1, tuning?.TruceMinFeeBase ?? 500);
            int hull = target != null ? Mathf.Max(1, target.MaxHull) : 1;
            // Чем крупнее корабль, тем дороже. Порядок величины ≈ base..base*10.
            float scaled = baseFee * (1f + hull / 500f);
            int rounded = Mathf.RoundToInt(scaled / 50f) * 50;
            return Mathf.Max(baseFee, rounded);
        }

        private static bool IsUnbribable(ShipData target)
        {
            if (string.IsNullOrEmpty(target?.ShipTypeId)) return false;
            // Warrior / Dominator — по типу, синтеты дополнительно по расе.
            if (target.ShipTypeId == "Warrior") return true;
            if (!string.IsNullOrEmpty(target.Race) && target.Race == "Dominators") return true;
            return false;
        }

        private static DialogTuning GetTuning() =>
            GameWorld.Context?.Config?.Dialogs?.Tuning;
    }
}
