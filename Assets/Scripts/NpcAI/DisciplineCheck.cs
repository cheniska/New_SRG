using UnityEngine;
using SRG.Ships;

namespace SRG.NpcAI
{
    /// <summary>
    /// Единая функция «подчиняется ли NPC приказу/директиве». До июля 2026 расходились:
    ///   Directive.ShouldObey:     &gt;70 всегда, &lt;40 никогда, между — только осторожные.
    ///   ActionPartnerAttend.Roll: &gt;=60 всегда, иначе шанс = discipline/100.
    /// Теперь единая формула с двумя режимами (директива фракции — жёстче, приказ лидера — мягче).
    /// </summary>
    public static class DisciplineCheck
    {
        public enum OrderKind
        {
            /// <summary>Фракционная директива ГШ. Осторожные могут проигнорировать.</summary>
            FactionDirective,
            /// <summary>Прямой приказ лидера партнёру. Дисциплинированный всегда подчиняется,
            /// низкодисциплинированный — по шансу.</summary>
            PartnerOrder,
        }

        /// <summary>Возвращает true, если корабль должен подчиниться. `null` личность — не подчиняется.</summary>
        public static bool ShouldObey(ShipPersonality personality, OrderKind kind)
        {
            if (personality == null) return false;
            float discipline = personality.Discipline;

            switch (kind)
            {
                case OrderKind.FactionDirective:
                    // Совместимо с прежним Directive.ShouldObey.
                    if (discipline > 70f) return true;
                    if (discipline < 40f) return false;
                    return personality.EngageThreshold <= 0.5f;

                case OrderKind.PartnerOrder:
                    // Совместимо с прежним RollObeyOrder.
                    if (discipline >= 60f) return true;
                    float chance = Mathf.Clamp01(discipline / 100f);
                    return Random.value < chance;

                default:
                    return false;
            }
        }
    }
}
