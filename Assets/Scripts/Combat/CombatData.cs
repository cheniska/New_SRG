using System.Collections.Generic;
using UnityEngine;
using SRG.Equipment;
using SRG.Galaxy;

namespace SRG.Combat
{
    // ── COMBAT DATA MODELS ────────────────────────────────────────────────────────
    //
    // Все структуры данных, используемые WeaponSystem и EquipmentSystem в боевых расчётах.
    // Хранятся в ShipData.ActiveEffects и передаются между системами через CombatResult.

    // ── ТИПЫ УРОНА ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Физическая природа урона. Определяет взаимодействие с бронёй, щитом и
    /// восприимчивостью корпуса. Не равен классу оружия — ракета может нести
    /// любой тип заряда.
    /// </summary>
    public enum DamageType
    {
        Kinetic,    // Кинетический: хорошо работает против брони, слабо против щита
        Explosive,  // Взрывной: средне против брони, хорошо против незащищённых целей
        Energy,     // Энергетический: сильно перегружает щит, хуже против голого корпуса
    }

    // ── ПАТТЕРНЫ ВЫСТРЕЛА ─────────────────────────────────────────────────────────

    /// <summary>
    /// Паттерн распространения снаряда. Определяет визуал и механику попадания.
    /// Хранится в Params["HitPattern"] конкретного оружия.
    /// </summary>
    public enum HitPattern
    {
        Point,      // Точечный: один снаряд → одна цель
        Piercing,   // Пробивающий: снаряд проходит насквозь, задевая всё по пути
        Shotgun,    // Дробовик: конус снарядов, взрываются при касании цели
        Ricochet,   // Рикошет: снаряд отражается до 5 раз
        Chain,      // Цепной: луч/снаряд переходит от цели к цели
        AoE,        // Область: эффект вокруг корабля
        PointAoE,   // Точечный + Область: снаряд летит, затем взрыв на месте попадания
        Falloff,    // Затухающий: урон убывает с дистанцией
        Beam,       // Луч: отрезок от корабля до цели
        Homing,     // Самонаводящийся: ракеты и торпеды со своим HP
        Mine,       // Мина: остаётся на месте (таймер, команда, магнит)
    }

    // ── БОЕВЫЕ ЭФФЕКТЫ ────────────────────────────────────────────────────────────

    /// <summary>
    /// Тип активного боевого эффекта, наложенного на корабль.
    /// Эффекты стекаются (Slow, ArmorDebuff) или перезаписываются (Shutdown).
    /// Могут исходить от оружия, артефактов или модификаций.
    /// </summary>
    public enum CombatEffectType
    {
        // ── Боевые ───────────────────────────────────────────────────────────────
        Slow,           // Снижает скорость и манёвренность
        Shutdown,       // Временно отключает все системы (ЭМИ)
        ArmorDebuff,    // Снижает броню на N единиц временно (коррозия)
        Jamming,        // Снижает радар, дальность оружия, мощность сканера
        EngineDisable,  // Прямой урон двигателю (немедленный тактический эффект)
        Drain,          // Вампиризм: атакующий восстанавливает HP

        // ── Тактические (требуют сканер > защита цели) ───────────────────────────
        BlockWeapon,    // Блокирует оружие цели на N ходов
        BlockDroid,     // Блокирует дроида-ремонтника на N ходов
        ExecuteBonus,   // Бонус к урону по повреждённой цели (до +33%)

        // ── Экономические ────────────────────────────────────────────────────────
        LootFocus,      // Повышает шанс дропа оборудования при уничтожении цели
    }

    /// <summary>
    /// Активный экземпляр боевого эффекта на корабле.
    /// Хранится в ShipData.ActiveEffects, убывает каждый ход.
    /// </summary>
    [System.Serializable]
    public class ActiveCombatEffect
    {
        /// <summary>Тип эффекта.</summary>
        public CombatEffectType Type;

        /// <summary>Сколько ходов осталось. -1 = бессрочный (до конца боя).</summary>
        public int TurnsLeft;

        /// <summary>Числовая мощность эффекта (единицы замедления, брони, % и т.д.).</summary>
        public float Magnitude;

        /// <summary>UID корабля-источника эффекта (для Drain и ExecuteBonus).</summary>
        public string SourceUid;

        public ActiveCombatEffect(CombatEffectType type, int turns, float magnitude, string sourceUid = null)
        {
            Type = type;
            TurnsLeft = turns;
            Magnitude = magnitude;
            SourceUid = sourceUid;
        }
    }

    // ── СОСТОЯНИЕ ЩИТА ────────────────────────────────────────────────────────────

    // Щит — плоский процентный барьер (BlockPercent), без ёмкости и регенерации.
    // Износ списывается с предмета щита через WearOnHit при попадании.
    // ShieldPenetration снаряда снижает эффективный процент блока.

    // ── ПАРАМЕТРЫ ВЫСТРЕЛА ────────────────────────────────────────────────────────

    /// <summary>
    /// Параметры одного выстрела, собранные из конфига оружия и наложенных эффектов.
    /// Передаётся в WeaponSystem.ProcessShot().
    /// </summary>
    public class WeaponShotParams
    {
        public string AttackerUid;
        public string TargetUid;
        public string WeaponSlotKey;

        public DamageType DamageType;
        public HitPattern HitPattern;

        public float MinDmg;
        public float MaxDmg;
        public float Range;

        /// <summary>Единицы брони, игнорируемые при расчёте.</summary>
        public float ArmorPenetration;

        /// <summary>Доля урона [0..1], проходящего сквозь щит без снятия ёмкости.</summary>
        public float ShieldPenetration;

        /// <summary>Шанс попасть по оборудованию [0..1].</summary>
        public float EquipHitChance;

        /// <summary>Урон по оборудованию при попадании.</summary>
        public float EquipDamage;

        /// <summary>Патроны: -1 = бесконечно.</summary>
        public int Ammo;

        /// <summary>Эффекты, накладываемые при попадании.</summary>
        public List<WeaponEffect> Effects = new List<WeaponEffect>();

        /// <summary>Флаг: пробить броню и щит полностью (аналог SR2 Undefendable). Ставится
        /// микромодулем через <c>WeaponFlags: ["IgnoreArmorAndShield"]</c>.</summary>
        public bool IgnoreArmorAndShield;

        /// <summary>Флаг: не может добить цель до 0 HP.</summary>
        public bool NonLethal;
    }

    /// <summary>Один эффект, заданный в конфиге оружия (Effects: ["Slow:2:0.3"]).</summary>
    public class WeaponEffect
    {
        public CombatEffectType Type;
        public int DurationTurns;   // -1 = до конца боя
        public float Magnitude;
        public float Chance;        // 0..1: вероятность наложения (1 = всегда)
    }

    // ── РЕЗУЛЬТАТ БОЯ ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Результат одного выстрела. Возвращается из WeaponSystem.ProcessShot().
    /// Используется для заполнения TurnAnimationData.Shots и UI-лога боя.
    /// </summary>
    public class CombatResult
    {
        public bool Hit;
        public float DamageDealt;
        public float ShieldDamage;
        public float HullDamage;
        public bool EquipmentHit;
        public string EquipmentSlotHit;
        public List<CombatEffectType> EffectsApplied = new List<CombatEffectType>();
        public float HealedByDrain;
        public bool TargetDestroyed;
    }

    // ── АКТИВНАЯ РАКЕТА ───────────────────────────────────────────────────────────

    /// <summary>
    /// Самонаводящийся снаряд, запущенный оружием с HitPattern.Homing.
    /// Персистируется в StarData.ActiveMissiles между ходами.
    /// Имеет HP, скорость и таймер жизни в днях.
    /// </summary>
    [System.Serializable]
    public class ActiveMissile
    {
        public string Uid;
        public string AttackerUid;
        public string AttackerOwner;
        public string AttackerRace;
        public string TargetUid;
        public string WeaponId;
        public string WeaponSlotKey;

        public UnityEngine.Vector2 Position;
        public int    CurrentHp;
        public int    MaxHp;
        public float  Speed;
        public int    DaysLeft;
        public string GraphicPath;
        public float  Scale;

        // Начальное направление полёта (= forward стрелявшего + отклонение залпа).
        // Используется только в визуальном/отладочном контексте; для движения берётся CurrentHeading.
        public UnityEngine.Vector2 LaunchDirection;

        // Сабтёрн запуска. Спрайт ракеты невидим в анимации, пока currentSubTurn < LaunchSubTurn
        // (иначе ракета "висит" под кораблём с начала дня до момента залпа).
        // В TickMissilesEndOfDay сбрасывается в 0 — со следующего дня всегда видима.
        public int    LaunchSubTurn;

        // Фаза запуска: ракета летит строго прямо по LaunchDirection до конца хода запуска,
        // без хоминга. В TickMissilesEndOfDay сбрасывается → со следующего хода начинается
        // плавная кинематическая дуга на цель.
        public bool   LaunchPhase;

        // Кинематика как у кораблей: текущий курс и максимальная угловая скорость поворота
        // (в радианах/ход). Каждый сабтёрн рассчитывается ограниченный по углу шаг.
        public float  CurrentHeading;
        public float  TurnRadPerTurn = UnityEngine.Mathf.PI * 4f; // 720°/ход по умолчанию

        // Торпеды (ReturnsOnTargetDeath=true) при смерти цели перенаправляются к стрелявшему
        // и при достижении возвращают единицу боезапаса. Обычные ракеты — продолжают полёт к
        // последней известной позиции цели (TargetDeadCoasting=true) и взрываются на месте трупа,
        // чтобы визуально не было «преждевременной» детонации в воздухе.
        public bool   ReturnsOnTargetDeath;
        public bool   IsReturning;
        public bool   TargetDeadCoasting;
        public UnityEngine.Vector2 CoastTargetPos;

        public float  MinDmg;
        public float  MaxDmg;
        public float  ArmorPenetration;
        public float  ShieldPenetration;
        public float  EquipHitChance;
        public float  EquipDamage;
        public DamageType DamageType;
        public List<WeaponEffect> Effects = new List<WeaponEffect>();

        // ── SR2-расширения (Missile_Trajectory.md §§3.A–D, 4) ────────────────────

        /// <summary>Потолок скорости — куда ракета разгоняется при SpeedRampPerTurn &gt; 0.
        /// Если SpeedRampPerTurn = 0, Speed = SpeedMax с первого сабтёрна.</summary>
        public float  SpeedMax;
        public float  SpeedRampPerTurn;

        /// <summary>Шаг полёта в launch-фазе (день запуска). Обычно = Speed × LaunchSpeedMultiplier,
        /// чтобы ракета сразу же отрывалась от стрелка. Используется только пока LaunchPhase=true.</summary>
        public float  LaunchSpeed;

        /// <summary>SR2 §3.D: ракета умирает раньше Lifedays, если требуемое
        /// время долёта &gt; MaxRangeFactor × оставшийся срок. 0 = выключено.</summary>
        public float  MaxRangeFactor;

        /// <summary>SR2 §4: автозамена цели после age &gt;= SubTurnsPerTurn (после первого
        /// хода жизни). Только для homing-классов.</summary>
        public bool   AutoReacquire;
        public float  ReacquireRadius;

        /// <summary>SR2 §3.B: счётчик «петли промаха». &gt; 0 — летим в зеркальном
        /// направлении эти сабтёрны; &lt; 0 — jitter подавлен (после расхода); 0 — нет jitter,
        /// можно запустить при первом отдалении.</summary>
        public bool   MissJitterEnabled;
        public int    MissJitterCnt;

        /// <summary>SR2 §3.B: предыдущий квадрат расстояния до цели — для детекции
        /// «начала отдаления». −1 = не инициализирован (бесконечно далеко).</summary>
        public float  LastDistSq = -1f;

        /// <summary>SR2 §4: для AutoReacquire — куда «нельзя» снова навестись. Это та цель,
        /// которую мы потеряли (умерла или ушла). Без этой защиты ракета крутится между двух врагов.</summary>
        public string PrevTargetUid;

        /// <summary>Общий возраст ракеты в сабтёрнах (нужен для AutoReacquire и force-expire).</summary>
        public int    AgeSubTurns;
    }
}
