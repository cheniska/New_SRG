using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using Newtonsoft.Json;
using SRG.Config;
using SRG.NpcAI;
using SRG.Simulation;

namespace SRG.Ships
{
    [System.Serializable]
    public class ShipPersonality
    {

        public float Aggression { get; set; }
        public float Caution { get; set; }
        public float Greed { get; set; }
        public float Discipline { get; set; }
        public float Tribalism { get; set; }
        public float Vendetta { get; set; }
        public Dictionary<string, float> PersonalRelationModifiers { get; set; } = new();
        public float CrimeRating { get; set; } = 0f;

        /// <summary>
        /// Счётчик «фрустрации» — растёт, когда корабль атакует одну и ту же цель
        /// много ходов подряд без результата, или когда теряет HP без ответа.
        /// Сбрасывается при убийстве цели или принятии перемирия.
        /// Используется в NpcBrain для смены тактики: высокая Frustration → выход из
        /// затянувшегося боя (Flee) или эскалация (Rob → Attack).
        /// Диапазон 0..100.
        /// </summary>
        public float Frustration { get; set; } = 0f;

        [JsonIgnore] public float EngageThreshold => Mathf.Clamp01(0.5f - (Aggression - 50f) / 100f);
        [JsonIgnore] public float FleeHullPercent => 0.2f * (Caution / 50f);

        public void AddFrustration(float amount)
            => Frustration = Mathf.Clamp(Frustration + amount, 0f, 100f);

        public void ReduceFrustration(float amount)
            => Frustration = Mathf.Clamp(Frustration - amount, 0f, 100f);
        public static ShipPersonality Generate(PersonalityRange range)
        {
            return new ShipPersonality
            {
                Aggression  = Random.Range(range.AggressionMin,  range.AggressionMax),
                Caution     = Random.Range(range.CautionMin,     range.CautionMax),
                Greed       = Random.Range(range.GreedMin,       range.GreedMax),
                Discipline  = Random.Range(range.DisciplineMin,  range.DisciplineMax),
                Tribalism   = Random.Range(range.TribalismMin,   range.TribalismMax),
                Vendetta    = Random.Range(range.VendettaMin,    range.VendettaMax),
            };
        }

        public float GetPersonalModifier(string targetUid)
        {
            if (string.IsNullOrEmpty(targetUid)) return 0f;
            PersonalRelationModifiers.TryGetValue(targetUid, out float mod);
            return mod;
        }

        public void ApplyHostilityPenalty(string attackerUid)
        {
            if (string.IsNullOrEmpty(attackerUid)) return;

            float penalty = Vendetta switch
            {
                > 95f => -50f,
                > 80f => -20f,
                _     => -10f
            };

            ApplyPersonalModifier(attackerUid, penalty);
        }

        public void ApplyAllyBonus(string allyUid)
        {
            if (string.IsNullOrEmpty(allyUid)) return;
            ApplyPersonalModifier(allyUid, +15f);
        }

        private void ApplyPersonalModifier(string uid, float delta)
        {
            PersonalRelationModifiers.TryGetValue(uid, out float current);
            float cap = Vendetta; // Cap = Vendetta (для штрафов)
            float newVal = Mathf.Clamp(current + delta, -cap, cap);
            PersonalRelationModifiers[uid] = newVal;
        }

        public void TickPersonalRelations()
        {
            if (PersonalRelationModifiers.Count == 0) return;

            float decayRate = (100f - Vendetta) / 100f;

            // Snapshot keys to allow safe removal during iteration
            var keys = new List<string>(PersonalRelationModifiers.Keys);
            foreach (var key in keys)
            {
                float val = PersonalRelationModifiers[key];
                val = val < 0f
                    ? Mathf.Min(0f, val + decayRate)
                    : Mathf.Max(0f, val - decayRate);

                if (Mathf.Abs(val) < 0.01f)
                    PersonalRelationModifiers.Remove(key);
                else
                    PersonalRelationModifiers[key] = val;
            }
        }

        public void ClearNegativeModifier(string uid)
        {
            if (PersonalRelationModifiers.TryGetValue(uid, out float val) && val < 0f)
                PersonalRelationModifiers.Remove(uid);
        }
    }

    public class PersonalityRange
    {
        public float AggressionMin,  AggressionMax;
        public float CautionMin,     CautionMax;
        public float GreedMin,       GreedMax;
        public float DisciplineMin,  DisciplineMax;
        public float TribalismMin,   TribalismMax;
        public float VendettaMin,    VendettaMax;

        /// <summary>Аварийный фолбэк (нейтральная середина) — только если в конфиге нет
        /// ни профиля типа, ни "Default". Диапазоны характеров живут в
        /// GalaxyConfig.json → Ships.PersonalityProfiles.</summary>
        private static readonly PersonalityRange Fallback = new()
        {
            AggressionMin = 30, AggressionMax = 70,
            CautionMin = 30,    CautionMax = 70,
            GreedMin = 20,      GreedMax = 60,
            DisciplineMin = 30, DisciplineMax = 70,
            TribalismMin = 30,  TribalismMax = 70,
            VendettaMin = 30,   VendettaMax = 70,
        };

        /// <summary>Профиль характера для типа корабля из конфига:
        /// ShipTypes[id].Personality → Ships.PersonalityProfiles[имя]. Без ссылки
        /// или при неизвестном имени берётся профиль "Default". Может вернуть null
        /// (конфиг ещё не загружен / секции нет) — вызывающие обязаны иметь фолбэк.</summary>
        public static PersonalityProfileConfig ProfileForShipType(string shipTypeId)
        {
            var ships = GameWorld.Context?.Config?.Ships;
            var profiles = ships?.PersonalityProfiles;
            if (profiles == null || profiles.Count == 0) return null;

            string name = null;
            if (!string.IsNullOrEmpty(shipTypeId)
                && ships.ShipTypes != null
                && ships.ShipTypes.TryGetValue(shipTypeId, out var typeCfg))
                name = typeCfg.Personality;

            if (string.IsNullOrEmpty(name) || !profiles.TryGetValue(name, out var profile))
                profiles.TryGetValue("Default", out profile);

            return profile;
        }

        /// <summary>Диапазон черт характера для типа корабля (из профиля в конфиге).</summary>
        public static PersonalityRange ForShipType(string shipTypeId)
        {
            var profile = ProfileForShipType(shipTypeId);
            return profile != null ? FromConfig(profile) : Fallback;
        }

        private static PersonalityRange FromConfig(PersonalityProfileConfig p)
        {
            var r = new PersonalityRange();
            (r.AggressionMin, r.AggressionMax) = MinMax(p.Aggression, Fallback.AggressionMin, Fallback.AggressionMax);
            (r.CautionMin,    r.CautionMax)    = MinMax(p.Caution,    Fallback.CautionMin,    Fallback.CautionMax);
            (r.GreedMin,      r.GreedMax)      = MinMax(p.Greed,      Fallback.GreedMin,      Fallback.GreedMax);
            (r.DisciplineMin, r.DisciplineMax) = MinMax(p.Discipline, Fallback.DisciplineMin, Fallback.DisciplineMax);
            (r.TribalismMin,  r.TribalismMax)  = MinMax(p.Tribalism,  Fallback.TribalismMin,  Fallback.TribalismMax);
            (r.VendettaMin,   r.VendettaMax)   = MinMax(p.Vendetta,   Fallback.VendettaMin,   Fallback.VendettaMax);
            return r;
        }

        private static (float, float) MinMax(float[] range, float defMin, float defMax)
            => range != null && range.Length >= 2 ? (range[0], range[1]) : (defMin, defMax);
    }

    public enum TargetDecision
    {
        Ignore,
        Attack,
        Flee,
        Protect,
        LootInstead,
    }
}
