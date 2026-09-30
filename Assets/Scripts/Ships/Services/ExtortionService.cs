using UnityEngine;
using SRG.Config;
using SRG.Dialog;
using SRG.Galaxy;
using SRG.Galaxy.Politics;

namespace SRG.Ships.Services
{
    /// <summary>Причина, по которой ExtortionService.TryDemand отказал (или Accepted при успехе).</summary>
    public enum ExtortRefusalReason
    {
        Accepted = 0,
        LongDistance,     // угроза издалека не работает — цель просто уйдёт
        SumIsVeryBig,     // требуемая сумма выше того, что цель готова отдать
        NotEnoughMoney,   // на счету цели меньше требуемой суммы
        ShipRefuses,      // Warrior / Ranger / Pirate — не платят вымогателям
        WeAlreadyHavePact,// уже заключено мирное соглашение (Normal+)
    }

    /// <summary>Результат Preview/TryDemand.
    /// SumIsVeryBig → Fee = максимально приемлемая сумма (подсказка UI).
    /// Accepted     → Fee = реально списанная (равна требуемой).</summary>
    public struct ExtortResult
    {
        public ExtortRefusalReason Reason;
        public int Fee;
        public bool Accepted => Reason == ExtortRefusalReason.Accepted;

        public static ExtortResult Ok(int fee) => new() { Reason = ExtortRefusalReason.Accepted, Fee = fee };
        public static ExtortResult Refuse(ExtortRefusalReason r, int fee = 0) => new() { Reason = r, Fee = fee };
    }

    /// <summary>
    /// Вымогательство денег у NPC-корабля игроком. При успехе:
    ///  • списывает деньги с корабля, добавляет игроку;
    ///  • отношение цели → игроку опускается (Bad);
    ///  • у игрока копится CrimeRating (как за пиратский акт).
    ///
    /// Корабли, которые НЕ платят: Warrior (устав), Ranger (принципы), Pirate (гордость).
    /// Все остальные соглашаются, если сумма разумна.
    ///
    /// Порог «разумной» суммы: min(target.Money × MaxPayRatio, base × hull_factor).
    /// </summary>
    public static class ExtortionService
    {
        public static ExtortResult Preview(ShipData target, ShipData player, int demand)
        {
            if (target == null || player == null) return ExtortResult.Refuse(ExtortRefusalReason.ShipRefuses);
            var tuning = DialogTuning.Current;

            if (ShipUtils.IsHardTargetForCriminalAct(target)) return ExtortResult.Refuse(ExtortRefusalReason.ShipRefuses);

            // Если у нас уже мирное соглашение — цель припомнит его.
            int rel = Relations.Get(target, player);
            if (rel >= OwnerRaceRelationsManager.NORMAL_MAX + 1)
                return ExtortResult.Refuse(ExtortRefusalReason.WeAlreadyHavePact);

            // Дистанция «издалека» — угроза несерьёзна. Используем RobberyLongDistance
            // (шире, чем TruceLongDistance — грабитель угрожает с боевой дистанции пушки).
            float distSq = (target.Position - player.Position).sqrMagnitude;
            float longD = Mathf.Max(1f, tuning?.RobberyLongDistance ?? 400f);
            if (distSq > longD * longD)
                return ExtortResult.Refuse(ExtortRefusalReason.LongDistance);

            if (demand <= 0)
                return ExtortResult.Refuse(ExtortRefusalReason.SumIsVeryBig, 1);

            int maxPay = CalcMaxAcceptableDemand(target, tuning);
            if (demand > maxPay)
                return ExtortResult.Refuse(ExtortRefusalReason.SumIsVeryBig, maxPay);

            if (target.Money < demand)
                return ExtortResult.Refuse(ExtortRefusalReason.NotEnoughMoney, target.Money);

            return ExtortResult.Ok(demand);
        }

        /// <summary>Списывает сумму с цели, зачисляет игроку, опускает отношение и добавляет CrimeRating.</summary>
        public static ExtortResult TryDemand(ShipData target, ShipData player, int demand)
        {
            var result = Preview(target, player, demand);
            if (!result.Accepted) return result;

            target.Money  -= result.Fee;
            player.Money  += result.Fee;

            // Личная обида → уровень Bad.
            Relations.LowerToLevel(target, player, RelationLevel.Bad);

            var tuning = DialogTuning.Current;
            float crimeGain = Mathf.Max(0f, tuning?.ExtortionCrimePerHundred ?? 0.5f);
            player.CrimeRating += crimeGain * (result.Fee / 100f);

            return result;
        }

        /// <summary>Стартовая «разумная» сумма для UI — половина от максимума.</summary>
        public static int SeedDemand(ShipData target)
        {
            int max = CalcMaxAcceptableDemand(target, DialogTuning.Current);
            return Mathf.Max(50, max / 2);
        }

        /// <summary>Максимальная сумма, которую цель согласится заплатить (mind money + hull).</summary>
        public static int CalcMaxAcceptableDemand(ShipData target, DialogTuning tuning)
        {
            if (target == null) return 0;
            float ratio = Mathf.Clamp01(tuning?.ExtortionMaxPayRatio ?? 0.5f);
            int byPurse = Mathf.RoundToInt(target.Money * ratio);
            int byHull  = Mathf.RoundToInt((tuning?.TruceMinFeeBase ?? 500) * (1f + target.MaxHull / 500f));
            return Mathf.Max(50, Mathf.Max(byPurse, byHull));
        }

    }
}
