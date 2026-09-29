using UnityEngine;
using SRG.Config;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Politics;

namespace SRG.Economy
{
    /// <summary>
    /// Глобальная галактическая инфляция: линейный множитель ко всем ценам.
    /// Растёт ежемесячно (30 ходов) и от шок-событий (война, революция).
    /// Применяется в TradeSystem.ComputeBasePrice и ItemFactory при спавне предметов.
    /// </summary>
    public static class InflationSystem
    {
        /// <summary>Типы шок-событий, которые могут вызвать разовый прирост инфляции.</summary>
        public enum ShockType
        {
            War,
            Revolution
        }

        /// <summary>
        /// Ежемесячный тик: прибавить MonthlyDelta к InflationFactor, не превышая MaxFactor.
        /// Вызывается из GalaxyData.GalaxyNextDay при CurrentTurn % 30 == 0.
        /// </summary>
        public static void TickMonthly(GalaxyData galaxy, GalaxyConfig cfg)
        {
            if (galaxy == null || cfg?.Inflation == null) return;
            var inf = cfg.Inflation;
            float before = galaxy.InflationFactor;
            galaxy.InflationFactor = Mathf.Min(galaxy.InflationFactor + inf.MonthlyDelta, inf.MaxFactor);
            EconomicLog.Inflation(galaxy.CurrentTurn, "MONTHLY_TICK",
                $"factor_before={before:F4} factor_after={galaxy.InflationFactor:F4} delta={inf.MonthlyDelta:F4}");
        }

        /// <summary>
        /// Разовый прирост от события (война/революция). Использует boost из конфига для каждого типа.
        /// <paramref name="sourceSide"/> — сторона-источник шока (обычно контроллёр планеты, где
        /// произошло событие). Используется для фильтра новостей: игрок получает экономическую
        /// сводку только если сторона-источник ему дружественна.
        /// </summary>
        public static void ApplyShock(GalaxyData galaxy, GalaxyConfig cfg, ShockType type, string sourceSide = null)
        {
            if (galaxy == null || cfg?.Inflation == null) return;
            float boost = type switch
            {
                ShockType.War => cfg.Inflation.WarBoost,
                ShockType.Revolution => cfg.Inflation.RevolutionBoost,
                _ => 0f
            };
            if (boost <= 0f) return;
            float before = galaxy.InflationFactor;
            galaxy.InflationFactor = Mathf.Min(galaxy.InflationFactor + boost, cfg.Inflation.MaxFactor);
            EconomicLog.Inflation(galaxy.CurrentTurn, "SHOCK",
                $"type={type} boost={boost:F4} factor_before={before:F4} factor_after={galaxy.InflationFactor:F4}");
            float pct = (galaxy.InflationFactor - before) * 100f;
            string reasonKey = type == ShockType.War ? "inflation.reason.war" : "inflation.reason.revolution";
            string reason = NewsTexts.Format(reasonKey);
            if (string.IsNullOrEmpty(reason)) reason = type == ShockType.War ? "военные потрясения" : "политические потрясения";
            GalaxyNewsService.PostForSide(GalaxyNewsService.CAT_ECONOMY,
                NewsTexts.Format("inflation.shock",
                    ("reason", reason),
                    ("pct", pct.ToString("F1")),
                    ("factor", galaxy.InflationFactor.ToString("F2"))),
                sourceSide);
        }

        /// <summary>
        /// Безопасное чтение фактора с фолбэком на 1.0 — для случаев, когда galaxy ещё не готова.
        /// </summary>
        public static float GetFactor(GalaxyData galaxy)
        {
            return galaxy?.InflationFactor ?? 1.0f;
        }
    }
}
