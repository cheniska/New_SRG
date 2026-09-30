using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Config;
using SRG.Galaxy;

namespace SRG.Ships
{
    /// <summary>
    /// Шесть пилот-скилов корабля (Accuracy/Mobility/Technical/Trader/Charm/Leadership)
    /// плюс прогрессия (Points / FreePoints) и разбивка опыта по категориям источников.
    ///
    /// База хранится в [0..ProgressionCap]. Эффективный скил = база + бонусы оборудования
    /// + дельты болезней/стимуляторов, зажат в [EffectiveMin..EffectiveMax]. Бонусные дельты
    /// складываются через AddTempModifier / ClearTempModifier (для болезней/стимуляторов).
    ///
    /// Сериализуется Newtonsoft. Все callsite-формулы (урон/износ/цены) читают эффективный
    /// скил через GetEffective(), никогда не дёргают BaseLevels напрямую.
    /// </summary>
    public class ShipSkills
    {
        /// <summary>База 6 скилов в порядке SkillType. Длина всегда = SkillTypeExtensions.SkillCount.</summary>
        [JsonProperty] public int[] BaseLevels { get; set; } = new int[SkillTypeExtensions.SkillCount];

        /// <summary>Совокупный накопленный опыт за всю игру (не уменьшается при тратах).
        /// Используется для рейтинга, статистики, ачивок.</summary>
        [JsonProperty] public int Points { get; set; }

        /// <summary>Свободные очки, доступные для прокачки скилов. Списываются при TryUpgrade.</summary>
        [JsonProperty] public int FreePoints { get; set; }

        /// <summary>Разбивка опыта по категориям источников (для diminishing-returns).
        /// Только для игрока имеет смысл; у NPC обычно пусто.</summary>
        [JsonProperty] public Dictionary<ExpCategory, float> ExpByCategory { get; set; } = new();

        /// <summary>Временные модификаторы скилов от болезней/стимуляторов/читов.
        /// Ключ — стабильный id (например "blind", "malocco_juice"). Каждый запись хранит
        /// набор дельт (длина = SkillCount) и номер хода, на котором эффект истекает.
        /// TickTurn(currentTurn) удаляет истёкшие.</summary>
        [JsonProperty] public Dictionary<string, TempSkillModifier> TempModifiers { get; set; } = new();

        /// <summary>Срабатывает после любой мутации (IncPoints / TryUpgrade /
        /// SetTempModifier / ClearTempModifier / прямая запись BaseLevels). UI-панель
        /// подписывается, чтобы перерисовываться в момент изменения, а не при перезаходе.
        /// Не сериализуется (подписчики живут только в рантайме).</summary>
        [JsonIgnore] public System.Action OnChanged;

        private void NotifyChanged() => OnChanged?.Invoke();

        public ShipSkills()
        {
            if (BaseLevels == null || BaseLevels.Length != SkillTypeExtensions.SkillCount)
                BaseLevels = new int[SkillTypeExtensions.SkillCount];
        }

        public static ShipSkills FromBase(int[] starting)
        {
            var s = new ShipSkills();
            if (starting == null) return s;
            int n = Mathf.Min(starting.Length, s.BaseLevels.Length);
            for (int i = 0; i < n; i++) s.BaseLevels[i] = starting[i];
            return s;
        }

        public int GetBase(SkillType skill) => BaseLevels[(int)skill];

        /// <summary>Прямо установить базу (для debug/читов/квестов). Срабатывает NotifyChanged.
        /// Cap по ProgressionCap применяется, если cfg передан.</summary>
        public void SetBase(SkillType skill, int value, SkillsConfig cfg = null)
        {
            if (cfg != null) value = Mathf.Clamp(value, 0, cfg.ProgressionCap);
            if (BaseLevels[(int)skill] == value) return;
            BaseLevels[(int)skill] = value;
            NotifyChanged();
        }

        /// <summary>Эффективный скил с учётом всех временных модификаторов, зажат конфигом.</summary>
        public int GetEffective(SkillType skill, SkillsConfig cfg)
        {
            int total = BaseLevels[(int)skill];
            foreach (var mod in TempModifiers.Values)
                if (mod?.Deltas != null && mod.Deltas.Length > (int)skill)
                    total += mod.Deltas[(int)skill];

            if (cfg != null)
                total = Mathf.Clamp(total, cfg.EffectiveMin, cfg.EffectiveMax);
            return total;
        }

        /// <summary>Стоимость следующей прокачки этого скила, или int.MaxValue если уже cap.</summary>
        public int GetUpgradeCost(SkillType skill, SkillsConfig cfg)
        {
            if (cfg == null) return int.MaxValue;
            int lvl = BaseLevels[(int)skill];
            if (lvl >= cfg.ProgressionCap) return int.MaxValue;
            return cfg.GetUpgradeCost(lvl);
        }

        public bool CanUpgrade(SkillType skill, SkillsConfig cfg)
        {
            int cost = GetUpgradeCost(skill, cfg);
            return cost != int.MaxValue && FreePoints >= cost;
        }

        /// <summary>Атомарно повысить скил на 1 уровень: списать FreePoints, инкрементить базу.</summary>
        public bool TryUpgrade(SkillType skill, SkillsConfig cfg)
        {
            if (!CanUpgrade(skill, cfg)) return false;
            int cost = GetUpgradeCost(skill, cfg);
            FreePoints -= cost;
            BaseLevels[(int)skill] += 1;
            NotifyChanged();
            return true;
        }

        /// <summary>
        /// Начислить опыт. Для category != None применяется diminishing-returns:
        /// scaled = amount / (catExp * K + 1). Затем Points и FreePoints растут на scaled,
        /// ExpByCategory[cat] тоже растёт.
        /// Для category == None штрафа нет (квестовые награды, нейроядра, читы).
        /// </summary>
        public void IncPoints(int amount, ExpCategory category, SkillsConfig cfg)
        {
            if (amount <= 0 || cfg == null) return;
            float scaled = amount;

            if (category != ExpCategory.None)
            {
                ExpByCategory.TryGetValue(category, out float catExp);
                scaled = amount / (catExp * cfg.DiminishingK + 1f);
                ExpByCategory[category] = catExp + scaled;
            }

            int rounded = Mathf.RoundToInt(scaled);
            if (rounded <= 0) return;
            Points     += rounded;
            FreePoints += rounded;
            NotifyChanged();
        }

        // ── Временные модификаторы ────────────────────────────────────────────────

        /// <summary>Добавить/обновить модификатор. deltas длиной = SkillCount, conditionId —
        /// стабильный ключ. endTurn — номер хода, ПОСЛЕ которого эффект исчезает (т.е.
        /// модификатор активен пока currentTurn ≤ endTurn). source — отображаемое имя.</summary>
        public void SetTempModifier(string conditionId, int[] deltas, int endTurn, string source = null)
        {
            if (string.IsNullOrEmpty(conditionId) || deltas == null) return;
            var copy = new int[SkillTypeExtensions.SkillCount];
            int n = Mathf.Min(deltas.Length, copy.Length);
            for (int i = 0; i < n; i++) copy[i] = deltas[i];
            TempModifiers[conditionId] = new TempSkillModifier
            {
                Deltas  = copy,
                EndTurn = endTurn,
                Source  = source,
            };
            NotifyChanged();
        }

        public void ClearTempModifier(string conditionId)
        {
            if (string.IsNullOrEmpty(conditionId)) return;
            if (TempModifiers.Remove(conditionId))
                NotifyChanged();
        }

        /// <summary>Очищает все модификаторы (для debug или полного лечения).</summary>
        public void ClearAllTempModifiers()
        {
            if (TempModifiers.Count == 0) return;
            TempModifiers.Clear();
            NotifyChanged();
        }

        /// <summary>Удаляет все модификаторы, у которых ненулевая дельта по указанному скилу.
        /// Возвращает количество удалённых. Если что-то снято — фаерит NotifyChanged.</summary>
        public int ClearModifiersForSkill(SkillType skill)
        {
            if (TempModifiers.Count == 0) return 0;
            int idx = (int)skill;
            List<string> toRemove = null;
            foreach (var kv in TempModifiers)
            {
                var deltas = kv.Value?.Deltas;
                if (deltas != null && deltas.Length > idx && deltas[idx] != 0)
                    (toRemove ??= new List<string>()).Add(kv.Key);
            }
            if (toRemove == null) return 0;
            foreach (var id in toRemove) TempModifiers.Remove(id);
            NotifyChanged();
            return toRemove.Count;
        }

        /// <summary>Удаляет модификаторы, чья длительность истекла к currentTurn (currentTurn > EndTurn).
        /// Вызывается раз в ход. Если что-то удалили — фаерит NotifyChanged.</summary>
        public void TickTurn(int currentTurn)
        {
            if (TempModifiers.Count == 0) return;
            List<string> expired = null;
            foreach (var kv in TempModifiers)
                if (currentTurn > kv.Value.EndTurn)
                    (expired ??= new List<string>()).Add(kv.Key);

            if (expired == null) return;
            foreach (var id in expired) TempModifiers.Remove(id);
            NotifyChanged();
        }

        // ── Хелперы формул (используются callsite'ами) ────────────────────────────

        /// <summary>
        /// Расчёт урона выстрела с учётом Accuracy атакующего и Mobility защитника.
        /// step = (max - min) / DamageRollScale; центр сдвигается на (acc - mob) * step / 2.
        /// Центр зажат в [min..max], затем спред = symmetric до ближайшего края.
        /// Возвращает float — округление на стороне callsite'а.
        /// </summary>
        public static float RollWeaponDamage(int min, int max, int accuracy, int mobility, SkillsConfig cfg)
        {
            if (max <= min) return min;
            int scale = (cfg != null && cfg.DamageRollScale > 0) ? cfg.DamageRollScale : 15;
            float step     = (max - min) / (float)scale;
            float accPoint = min + accuracy * step;
            float mobPoint = max - mobility * step;
            float center   = (accPoint + mobPoint) * 0.5f;
            center = Mathf.Clamp(center, min, max);
            float d = Mathf.Min(center - min, max - center);
            if (d <= 0f) return center;
            return Random.Range(center - d, center + d);
        }
    }

    /// <summary>Запись о временном модификаторе скилов (болезнь/стимулятор/чит/квестовый бонус).
    /// EndTurn — номер хода игры (GalaxyData.CurrentTurn), на котором эффект ещё активен.
    /// Если currentTurn &gt; EndTurn — TickTurn удалит запись.</summary>
    public class TempSkillModifier
    {
        [JsonProperty] public int[] Deltas { get; set; }
        [JsonProperty] public int   EndTurn { get; set; }
        [JsonProperty] public string Source { get; set; }
    }
}
