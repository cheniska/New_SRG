using UnityEngine;
using Newtonsoft.Json.Linq;
using SRG.Core;
using SRG.Galaxy;
using SRG.NpcAI;

namespace SRG.Equipment
{
    /// <summary>Универсальный вызов ИИ-стороны в конкретную точку. Использует существующие
    /// директивы фракций (<see cref="DirectiveAttackSystem"/>): выдаёт указанной стороне
    /// приказ атаковать целевую систему, что заставляет свободные боевые корабли этой стороны
    /// прыгать туда через <see cref="Ships.Movement.HyperNavigation"/>. Подходит для Маяка,
    /// квестовых волн, штурмов «сюжетных» узлов.
    ///
    /// Что не делает: собственно спавн подкреплений (fallback при нехватке доступных сил).
    /// В текущей итерации только «стягивает» существующих; при дефиците ГШ через
    /// <see cref="FactionHighCommand"/> сам решает, из каких плацдармов вытягивать флоты.
    /// TODO: реалистичный доспавн при <c>SpawnFallback=true</c>.
    /// </summary>
    public static class SummonService
    {
        /// <summary>Декларативное описание вызова. Читается из <c>ScriptParams.Summon</c>
        /// (см. Маяк) или собирается вручную из скриптов/квестов.</summary>
        public struct Spec
        {
            /// <summary>Сторона-агрессор (Coalition/Dominators/Pirates/...). Обязательное.</summary>
            public string Side;
            /// <summary>Опционально: конкретная раса стороны (Race1/Race2/...) — сузит директиву.</summary>
            public string RaceId;
            /// <summary>Сколько ходов действует директива до само-истечения. -1 = «до отмены».</summary>
            public int DurationTurns;
            /// <summary>Минимальная сила плацдарма перед прыжком. 0 = бить сразу.</summary>
            public float RequiredStagingPower;
            /// <summary>UID целевой звезды.</summary>
            public string TargetStarUid;
            /// <summary>UID звезды-плацдарма (опционально, обычно ближайшая к цели с силой стороны).</summary>
            public string StagingStarUid;

            public static Spec FromJson(JObject o)
            {
                if (o == null) return default;
                return new Spec
                {
                    Side                 = o.Value<string>("Side"),
                    RaceId               = o.Value<string>("RaceId"),
                    DurationTurns        = o.Value<int?>("DurationTurns") ?? 30,
                    RequiredStagingPower = o.Value<float?>("RequiredStagingPower") ?? 0f,
                    TargetStarUid        = o.Value<string>("TargetStarUid"),
                    StagingStarUid       = o.Value<string>("StagingStarUid"),
                };
            }
        }

        /// <summary>Запустить призыв. Если <paramref name="targetStarOverride"/> не пуст —
        /// перезаписывает <see cref="Spec.TargetStarUid"/> (для маяка: цель = система маяка).
        /// Возвращает false, если директиву не удалось выпустить (нет DirectiveManager,
        /// нет стороны или нет цели).</summary>
        public static bool Trigger(Spec spec, string targetStarOverride = null)
        {
            var dm = DirectiveManager.Instance;
            if (dm == null)
            {
                Debug.LogWarning("[SummonService] DirectiveManager отсутствует.");
                return false;
            }
            if (string.IsNullOrEmpty(spec.Side))
            {
                Debug.LogWarning("[SummonService] Spec.Side пуст.");
                return false;
            }
            string targetUid = targetStarOverride ?? spec.TargetStarUid;
            if (string.IsNullOrEmpty(targetUid))
            {
                Debug.LogWarning("[SummonService] Не задана целевая звезда призыва.");
                return false;
            }

            var directive = new DirectiveAttackSystem(
                ownerId: spec.Side,
                targetStarUid: targetUid,
                durationTurns: spec.DurationTurns > 0 ? spec.DurationTurns : 30,
                stagingStarUid: spec.StagingStarUid,
                raceId: spec.RaceId,
                requiredStagingPower: spec.RequiredStagingPower);
            dm.Issue(directive);
            Debug.Log($"[SummonService] Призыв {spec.Side} → {targetUid[..System.Math.Min(6, targetUid.Length)]} " +
                      $"на {spec.DurationTurns} ходов.");
            return true;
        }
    }
}
