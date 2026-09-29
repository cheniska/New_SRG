using SRG.Config;
using SRG.Core;
using SRG.NpcAI.Actions;
using SRG.NpcAI.Orders;
using SRG.Ships.Services;

namespace SRG.NpcAI
{
    /// <summary>
    /// Балансные параметры NPC AI, вынесенные из локальных <c>private const</c> в коде
    /// (этап балансировки, июнь 2026). Все значения кэшируются здесь как обычные static-поля —
    /// IL-доступ к ним идентичен по стоимости компиляторному инлайну const'ов
    /// (один memory load), что важно для горячих путей (OrderMoveTo проверяется
    /// каждый Tick AI на каждом NPC раз в ход).
    ///
    /// Источник значений: <see cref="GameSettingsConfig"/> (раздел «NPC AI Balance»).
    /// Загружается один раз через <see cref="LoadFromSettings"/> при инициализации галактики
    /// (см. <c>GalaxyManager.EnsureContextInitialized</c>). Значения здесь — fallback'и,
    /// идентичные исходным в коде; если Settings не передан или поле в нём дефолтное —
    /// сохраняется тот же балансовый профиль, что был до вынесения.
    ///
    /// Где используется (по полям — см. // в LoadFromSettings ниже).
    /// </summary>
    public static class NpcBalance
    {
        // ── Дистанции (мировые единицы; *Sq — квадраты) ──────────────────────────
        /// <summary>Радиус² «союзник в беде — лечу помогать» (NpcBrain.FindAlliedUnderAttack).
        /// Меньше, чем у эскорта: триггер на оппортунистическую помощь, а не на охрану цели.
        /// Эмпирически: 4 ед. (16 = 4²).</summary>
        public static float AllyHelpTriggerRadiusSq = 16f;

        /// <summary>Радиус² фиксации угрозы рядом с подопечным (ActionEscort.FindThreatTo).
        /// Больше, чем у help-trigger: эскорт реагирует раньше, его роль — защищать.
        /// Эмпирически: 5 ед. (25 = 5²).</summary>
        public static float EscortThreatRadiusSq = 25f;

        /// <summary>Дистанция, при которой OrderAttack считается выполненным (цель достигнута/догнана).
        /// Используется как «stop-following»-порог.</summary>
        public static float ChaseRadius = 8f;

        /// <summary>Длина побега в OrderFlee (норма-дистанция от угрозы).</summary>
        public static float FleeDistance = 10f;

        /// <summary>Дистанция позади leader-а в OrderFollow.</summary>
        public static float FollowDistance = 1.5f;

        /// <summary>Порог² «прибыли в точку» для OrderMoveTo и OrderPatrol (0.5²).</summary>
        public static float ArrivalThresholdSq = 0.25f;

        /// <summary>Радиус² «лут собран» для OrderLoot (0.6²).</summary>
        public static float PickupRadiusSq = 0.36f;

        /// <summary>Дистанция переговоров для OrderRequestCeasefire и OrderOfferMoneyRansom.</summary>
        public static float NegotiateRange = 2.0f;

        /// <summary>Дистанция передачи предмета для OrderTransfer.</summary>
        public static float TransferRange = 1.0f;

        /// <summary>Дальность боевого контакта: дальше цель не берётся (NpcBrain.WithinEngageRange)
        /// и лимитированная погоня прекращается (ActionPursueAndAttack, limitEngageRange=true).
        /// Военные директивы ГШ создают погоню без лимита.</summary>
        public static float MaxEngageRange = 10f;

        // ── Fear (NpcBrain.AssessFear) ────────────────────────────────────────────
        /// <summary>Радиус в звезде, в пределах которого враг «влияет» на fear-оценку.
        /// Меньше MaxEngageRange — для паники важно «прямо здесь», не далёкая перспектива.</summary>
        public static float Fear_MaxThreatRadius = 6f;

        /// <summary>Прирост порога при множественной угрозе: threshold_mul = 1 + (cnt-1) * этот коэф.
        /// SR2HD-аналог — «(cnt-1) * total * ~0..0.5» в разных классах; у нас единая константа.</summary>
        public static float Fear_CrowdMultiplier = 0.3f;

        /// <summary>Модификаторы порога страха по CombatClass. >1 → пугается сложнее.
        /// Military стоит до последнего, Civilian — самый пугливый.</summary>
        public static float Fear_ThresholdMult_Military  = 1.3f;
        public static float Fear_ThresholdMult_Pirate    = 1.0f;
        public static float Fear_ThresholdMult_Mercenary = 1.0f;
        public static float Fear_ThresholdMult_Civilian  = 0.7f;

        /// <summary>Post-filter: партнёр «сильного лидера» не паникует. Лидер считается сильным,
        /// если его Strength × этот коэф ≥ Strength self. Аналог SR2HD «партнёр рейнджера при
        /// сильном лидере не паникует».</summary>
        public static float Fear_PartnerLeaderStrengthMult = 1.0f;

        /// <summary>Прирост эффективного давления страха от Frustration (0..100):
        /// raw *= 1 + Frustration/100 * K. K=1 удваивает давление при Frustration=100.</summary>
        public static float Fear_FrustrationBoost = 1.0f;

        // ── Тайминги (в ходах) ───────────────────────────────────────────────────
        /// <summary>Период инвалидации Power-кэша звезды в NpcBrain.</summary>
        public static int StrengthCacheTurns = 10;

        /// <summary>Через сколько ходов NpcBrain «забывает» LastAttackerUid.</summary>
        public static int LastAttackerMemoryTurns = 20;

        /// <summary>Период проверки прогресса по HP цели (рост Frustration) — ActionPursueAndAttack.</summary>
        public static int FrustrationCheckInterval = 5;

        /// <summary>Пассивный decay Frustration за ход, когда нет свежего агрессора (LastAttackerUid == null
        /// либо LastAttackerTurn протух). Применяется в NpcBrain.TickPersonality. Дефолт: 1 ед./ход.</summary>
        public static float FrustrationPassiveDecay = 1f;

        /// <summary>Максимум циклов добычи — ActionMine.</summary>
        public static int MaxMineCycles = 3;

        /// <summary>Idle между циклами добычи — ActionMine.</summary>
        public static int MineTurns = 4;

        /// <summary>Idle загрузки/выгрузки в точке A/B — ActionDeliver.</summary>
        public static int LoadTurns = 2;

        /// <summary>Idle у планеты между перелётами — ActionTrade.</summary>
        public static int DockDuration = 3;

        // ── Trader-логика (TraderAI + ActionGoodsTrader) ────────────────────────
        /// <summary>Минимальная прибыль за рейс (за единицу).</summary>
        public static int MinProfitPerUnit = 2;

        /// <summary>Сколько ходов «отдыхать» при отсутствии прибыльного рейса
        /// (TraderAI.PlanRoute = null → ActionGoodsTrader.HandleLanded ставит TraderIdleTurnsLeft).</summary>
        public static int IdleWaitTurns = 8;

        /// <summary>Глубина поиска по соседним системам в TraderAI.</summary>
        public static int MaxJumpHops = 1;

        /// <summary>Топ-N кандидатов для глубокой оценки прибыли в TraderAI.</summary>
        public static int CandidatePruneLimit = 6;

        // ── Боевые проценты ─────────────────────────────────────────────────────
        /// <summary>Доля стека, передаваемого как «выкуп» в OrderRequestCeasefire.</summary>
        public static float CeasefireOfferFraction = 0.3f;

        // ── Repair / Outfitter / LandResupply ──────────────────────────────────
        /// <summary>Базовая стоимость восстановления 1 ед. Durability / HP, кредитов (см. RepairService).</summary>
        public static int Repair_CostPerDurabilityPoint = 10;
        /// <summary>Прибавка на тир: finalPerPoint = base × (1 + TL × TLMult).</summary>
        public static float Repair_TLMultiplier = 0.5f;
        /// <summary>Порог гистерезиса апгрейда: score(new) ≥ score(current) × (1 + Δ). NpcOutfitter.</summary>
        public static float Outfitter_MinImprovement = 0.10f;
        /// <summary>Неприкосновенный запас денег, ниже которого NPC не тратится (Outfitter/RepairService).</summary>
        public static int Outfitter_EmergencyCashReserve = 200;
        /// <summary>Резерв «на будущее топливо» = стоимость полного бака × этот множитель.</summary>
        public static float Outfitter_FuelReserveMultiplier = 2f;
        /// <summary>Порог composite-нужды, при котором NpcBrain включает ActionLandResupply.</summary>
        public static float Resupply_PressureThreshold = 1.0f;
        /// <summary>Доля HP корпуса, ниже которой pressure += 1 (нужен ремонт).</summary>
        public static float Resupply_HullPressureBelow = 0.80f;
        /// <summary>Доля топлива, ниже которой pressure += 1 (нужна дозаправка).</summary>
        public static float Resupply_FuelPressureBelow = 0.50f;
        /// <summary>Сумма денег, выше которой pressure += 0.5 (можно тратить на апгрейд).</summary>
        public static int Resupply_MoneyPressureAbove = 1500;

        /// <summary>
        /// Загружает значения из <see cref="GameSettingsConfig"/> (раздел NPC AI Balance).
        /// Вызывается из <c>GalaxyManager.EnsureContextInitialized</c> один раз при старте.
        /// Если settings == null — поля остаются с code-defaults (сохраняется прежнее поведение).
        /// </summary>
        public static void LoadFromSettings(GameSettingsConfig s)
        {
            if (s == null) return;
            AllyHelpTriggerRadiusSq  = s.NpcAi_AllyHelpTriggerRadiusSq;
            EscortThreatRadiusSq     = s.NpcAi_EscortThreatRadiusSq;
            ChaseRadius              = s.NpcAi_ChaseRadius;
            FleeDistance             = s.NpcAi_FleeDistance;
            FollowDistance           = s.NpcAi_FollowDistance;
            ArrivalThresholdSq       = s.NpcAi_ArrivalThresholdSq;
            PickupRadiusSq           = s.NpcAi_PickupRadiusSq;
            NegotiateRange           = s.NpcAi_NegotiateRange;
            TransferRange            = s.NpcAi_TransferRange;
            MaxEngageRange           = s.NpcAi_MaxEngageRange;

            Fear_MaxThreatRadius            = s.NpcAi_Fear_MaxThreatRadius;
            Fear_CrowdMultiplier            = s.NpcAi_Fear_CrowdMultiplier;
            Fear_ThresholdMult_Military     = s.NpcAi_Fear_ThresholdMult_Military;
            Fear_ThresholdMult_Pirate       = s.NpcAi_Fear_ThresholdMult_Pirate;
            Fear_ThresholdMult_Mercenary    = s.NpcAi_Fear_ThresholdMult_Mercenary;
            Fear_ThresholdMult_Civilian     = s.NpcAi_Fear_ThresholdMult_Civilian;
            Fear_PartnerLeaderStrengthMult  = s.NpcAi_Fear_PartnerLeaderStrengthMult;
            Fear_FrustrationBoost           = s.NpcAi_Fear_FrustrationBoost;

            StrengthCacheTurns       = s.NpcAi_StrengthCacheTurns;
            LastAttackerMemoryTurns  = s.NpcAi_LastAttackerMemoryTurns;
            FrustrationCheckInterval = s.NpcAi_FrustrationCheckInterval;
            FrustrationPassiveDecay  = s.NpcAi_FrustrationPassiveDecay;
            MaxMineCycles            = s.NpcAi_MaxMineCycles;
            MineTurns                = s.NpcAi_MineTurns;
            LoadTurns                = s.NpcAi_LoadTurns;
            DockDuration             = s.NpcAi_DockDuration;
            MinProfitPerUnit         = s.NpcAi_MinProfitPerUnit;
            IdleWaitTurns            = s.NpcAi_IdleWaitTurns;
            MaxJumpHops              = s.NpcAi_MaxJumpHops;
            CandidatePruneLimit      = s.NpcAi_CandidatePruneLimit;
            CeasefireOfferFraction   = s.NpcAi_CeasefireOfferFraction;

            Repair_CostPerDurabilityPoint    = s.Repair_CostPerDurabilityPoint;
            Repair_TLMultiplier              = s.Repair_TLMultiplier;
            Outfitter_MinImprovement         = s.Outfitter_MinImprovement;
            Outfitter_EmergencyCashReserve   = s.Outfitter_EmergencyCashReserve;
            Outfitter_FuelReserveMultiplier  = s.Outfitter_FuelReserveMultiplier;
            Resupply_PressureThreshold       = s.Resupply_PressureThreshold;
            Resupply_HullPressureBelow       = s.Resupply_HullPressureBelow;
            Resupply_FuelPressureBelow       = s.Resupply_FuelPressureBelow;
            Resupply_MoneyPressureAbove      = s.Resupply_MoneyPressureAbove;
        }
    }
}
