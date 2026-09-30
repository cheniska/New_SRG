using UnityEngine;
using System.Collections.Generic;
using SRG.Config;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Galaxy.Politics;
using SRG.Galaxy.Simulation;
using SRG.NpcAI.Actions;
using SRG.Ships;
using SRG.Ships.Services;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.NpcAI
{
    public class NpcBrain
    {
        // Типизированный interrupt, который возвращают EvaluateSituation / *-Logic вместо
        // string-ключей. SetActivity диспатчит по Kind и сравнивает с _lastInterrupt, чтобы
        // не пересоздавать action каждый ход, пока решение не изменилось.
        private enum InterruptKind
        {
            PursueAndAttack, Flee, Rob, Extort, RequestCeasefire, OfferMoney,
            Resupply, SeekShelter, CallForHelp
        }
        private readonly struct Interrupt : System.IEquatable<Interrupt>
        {
            public readonly InterruptKind Kind;
            public readonly string TargetUid;  // null для безцелевых (Resupply)
            public Interrupt(InterruptKind k, string t = null) { Kind = k; TargetUid = t; }
            public bool Equals(Interrupt o) => Kind == o.Kind && TargetUid == o.TargetUid;
            public override bool Equals(object o) => o is Interrupt i && Equals(i);
            public override int GetHashCode() => ((int)Kind * 397) ^ (TargetUid?.GetHashCode() ?? 0);
        }

        // Тот же объект, что ship.Personality: сохраняется у корабля, после загрузки связывается заново.
        [field: System.NonSerialized] public ShipPersonality Personality { get; private set; }

        /// <summary>После загрузки сейва: вернуть общую ссылку на характер корабля.</summary>
        public void RestoreAfterLoad(ShipData ship)
        {
            if (ship.Personality != null) Personality = ship.Personality;
            else ship.Personality = Personality ??= ShipPersonality.Generate(PersonalityRange.ForShipType(ship.ShipTypeId));
        }
        public CombatClass CombatClass { get; private set; }
        public string CurrentOrder =>
            _directiveActivity != null ? _directiveName
            : _carried ? "Carried"
            : _currentActivity?.DebugName ?? "None";
        public NpcAction CurrentActivity => (_directiveActivity != null && !_directiveActivity.IsCompleted)
            ? _directiveActivity : _currentActivity;

        /// <summary>Дискретный флаг «в панике». Пересчитывается каждым Tick через AssessFear
        /// (не персистится). Читают: диалоги (ShipGreetingSelector.MatchesFear), FearDropService,
        /// JointAttackService (союзник в панике не соглашается на совместную атаку),
        /// EvaluateSituation (interrupt Flee/SeekShelter).</summary>
        public bool  InFear    { get; private set; }
        /// <summary>Непрерывная величина давления страха (0..1+). ≥1 → InFear=true.
        /// Не клэмпится сверху — UI/градации в диалогах могут использовать raw-величину.</summary>
        public float FearLevel { get; private set; }

        // UID цели, по которой текущая активность открывает огонь (null если действие нелетальное).
        // CombatSubTurn использует это, чтобы NPC стрелял по преследуемому игроку,
        // даже если отношения владельцев не считаются враждебными.
        public string GetCombatTargetUid() => CurrentActivity?.CombatTargetUid;

        private NpcAction _currentActivity;
        private Interrupt? _lastInterrupt;   // последний применённый interrupt (для дедупа в SetActivity)
        private bool _carried;               // корабль на буксире у союзника — activity заморожена
        private NpcAction _directiveActivity;
        private string _directiveName = "None";
        private float _cachedStrength;
        private int _strengthCacheAge;
        private bool _threatPresent;
        // Константы вынесены в NpcBalance.cs (раздел "NPC AI" в GameSettingsConfig).

        public NpcBrain(ShipData ship, StarData star)
        {
            // После загрузки сейва характер уже есть — не перегенерируем его.
            Personality = ship.Personality ?? ShipPersonality.Generate(PersonalityRange.ForShipType(ship.ShipTypeId));
            // Дублируем на ShipData: Directive.ShouldObey и переговоры (Ceasefire/Ransom)
            // читают ship.Personality. Раньше это делал только NpcSpawner.Attach — корабли
            // без визуала (вся галактика вне системы игрока) оставались с null и молча
            // игнорировали все директивы ГШ.
            ship.Personality = Personality;
            CombatClass = ResolveCombatClass(ship.ShipTypeId);

            _currentActivity = ChooseDefaultActivity(ship, star);
            _cachedStrength = CalculateStrength(ship);
        }

        public void Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            if (ship.CurrentHull <= 0) return;

            // Станция неподвижна: не строит маршрут, не садится, не прыгает, не берёт директивы.
            // Огонь по врагам ведётся автоматически в CombatSubTurn (по ближайшей враждебной цели),
            // поэтому ИИ станции сводится к «оставаться на месте». FreezeRoute — страховка от сноса.
            if (ship.IsStation) { ship.FreezeRoute(); return; }

            // Пристыкован к кораблю-носителю (CanBeLandedOn): пассивен, следует за носителем
            // (перенос позиции — в StarSimulator). Решения не принимает до расстыковки.
            if (!string.IsNullOrEmpty(ship.LandedOnShipUid)) return;

            // Если корабль абордирован/буксируется И носитель союзен или нейтрален — AI становится
            // пассивным («просто следует на привязи»): не принимает собственных решений, не выбирает
            // цели атаки. Враждебный буксируемый сохраняет обычное поведение (попытается стрелять/бежать).
            string carrierUid = !string.IsNullOrEmpty(ship.BoardedByUid) ? ship.BoardedByUid
                              : !string.IsNullOrEmpty(ship.TowedByUid)   ? ship.TowedByUid
                              : null;
            if (carrierUid != null)
            {
                ShipData carrier = FindShipByUid(star, carrierUid);
                var rel = OwnerRaceRelationsManager.Instance;
                bool hostile = rel != null && carrier != null
                    && rel.AreHostile(ship, carrier);
                if (!hostile)
                {
                    if (!_carried)
                    {
                        _currentActivity = new ActionIdle();
                        _lastInterrupt = null;
                        _carried = true;
                        _directiveActivity = null;
                        _directiveName = "None";
                    }
                    return; // никаких новых решений и текущая активность не тикает
                }
            }
            _carried = false;

            TickPersonality(ship);
            TickStrengthCache(ship);

            // Fear пересчитывается КАЖДЫЙ Tick, до директив и EvaluateSituation. Не персистится.
            // Читают: диалоги (greetings/greeting-фильтр), FearDropService, JointAttackService,
            // EvaluateSituation (fear как приоритетный interrupt).
            AssessFear(ship, star);

            // Партнёр в критической ситуации (fear/крит.HP) — обычная EvaluateSituation имеет
            // приоритет над partner-attend. Дизайн: NPC-follower не должен послушно лететь за
            // игроком, пока его расстреливают — сначала спасение, потом уже сопровождение.
            // Исключение: Military-класс (партнёры-военные, синтеты) — они не паникуют
            // и не отступают, их partner-логика неизменна.
            bool followerCritical = !string.IsNullOrEmpty(ship.PartnerLeaderUid)
                                    && InFear
                                    && CombatClass != CombatClass.Military;

            // Директива активна — её действие уже оттикало внутри EvaluateDirective.
            // Текущую (фоновую) активность НЕ тикаем: иначе она затирает движение директивы
            // (оба пишут ship.TargetPosition, а TickActivity выполнялся бы последним).
            if (!followerCritical && EvaluateDirective(ship, star, ctx)) return;

            var interrupt = EvaluateSituation(ship, star);
            if (interrupt.HasValue) SetActivity(ship, star, interrupt.Value);

            TickActivity(ship, star, ctx);
        }

        /// <summary>Единая формула страха. Считает давление угроз: доля вражеской силы в радиусе
        /// Fear_MaxThreatRadius против своей силы и поддержки союзников, с поправкой на число врагов, и сравнивает
        /// с порогом, зависящим от Caution/CombatClass/Frustration. Обновляет InFear/FearLevel.
        ///
        /// Post-filter'ы:
        ///   • В гиперпрыжке — 0 (решает HyperjumpController, Brain не переоценивает).
        ///   • Партнёр «сильного лидера» — 0 (лидер прикрывает).
        ///
        /// Hard trigger: у корабля не осталось ни одного рабочего оружия И в звезде есть враг →
        /// FearLevel := 1.5 (безусловный fear, не зависящий от Caution).</summary>
        private void AssessFear(ShipData ship, StarData star)
        {
            InFear = false;
            FearLevel = 0f;

            if (ship == null || star == null) return;
            if (ship.HyperjumpPhase != HyperjumpPhase.None) return;

            // Синтеты никогда не паникуют и не отступают — по дизайну (100% дисциплина,
            // 0 Caution). Hard-skip до всех остальных проверок.
            if (ship.Owner == "Dominators") return;

            // Партнёр сильного лидера не паникует — лидер прикроет.
            if (!string.IsNullOrEmpty(ship.PartnerLeaderUid))
            {
                var leader = FindShipByUid(star, ship.PartnerLeaderUid);
                if (leader != null && leader.CurrentHull > 0)
                {
                    float leaderStr = CalculateStrength(leader);
                    if (leaderStr * NpcBalance.Fear_PartnerLeaderStrengthMult >= _cachedStrength)
                        return;
                }
            }

            var rel = OwnerRaceRelationsManager.Instance;
            if (rel == null) return;

            // Early-out через PowerCache: нет враждебной силы во всей звезде — fear не считаем.
            // Это освобождает мирные системы (большинство ходов у большинства кораблей).
            star.RebuildPowerCache(GameWorld.GeneratedGalaxy?.CurrentTurn ?? 0);
            if (star.GetHostilePower(ship.Owner, ship.Race) <= 0f) return;

            // Hard trigger #1: безоружен + враг в звезде → гарантированный fear.
            if (!NpcTargeting.HasWorkingWeapon(ship))
            {
                InFear = true;
                FearLevel = 1.5f;
                return;
            }

            // Hard trigger #2: hp ниже критического (FleeHullPercent) + враг в звезде.
            if (Personality != null && ship.MaxHull > 0 && Personality.Caution > 10f)
            {
                float hullPct = (float)ship.CurrentHull / ship.MaxHull;
                if (hullPct < Personality.FleeHullPercent)
                {
                    InFear = true;
                    FearLevel = 1.3f;
                    return;
                }
            }

            // Баланс сил в радиусе: враги давят, свои поддерживают. Вклад каждого корабля
            // ослабевает с расстоянием (на краю радиуса — вдвое). Spatial hash сужает кандидатов.
            float radius = NpcBalance.Fear_MaxThreatRadius;
            float radiusSq = radius * radius;
            float enemyPower = 0f;
            float allyPower  = 0f;
            int   enemies = 0;
            foreach (var s in ShipSpatialHash.Nearby(star, ship.Position, radius))
            {
                if (s == null || s == ship || s.CurrentHull <= 0) continue;
                if (!string.IsNullOrEmpty(s.LandedOnShipUid)) continue;
                float d2 = (s.Position - ship.Position).sqrMagnitude;
                if (d2 > radiusSq) continue;

                float weight = 1f - 0.5f * Mathf.Sqrt(d2) / radius;
                if (rel.AreHostile(ship, s))
                {
                    enemyPower += CalculateStrength(s) * weight;
                    enemies++;
                }
                else if (s.Owner == ship.Owner)
                {
                    allyPower += CalculateStrength(s) * weight;
                }
            }
            if (enemies == 0) return;

            // Давление: доля вражеской силы против своей (своих союзников учитываем вполовину —
            // на них надейся, а сам не плошай). Одна равная угроза вплотную даёт 0.5.
            float defence = _cachedStrength + allyPower * NpcBalance.Fear_AllySupportShare;
            float raw = enemyPower / Mathf.Max(0.001f, enemyPower + defence);

            // Несколько врагов пугают сильнее, чем один той же суммарной силы.
            raw *= 1f + (enemies - 1) * NpcBalance.Fear_CrowdMultiplier;

            // Порог: 1.0 (одна равная угроза = fear=1.0) × class-мод × personality (Caution).
            // Caution 0..100 → +0..+1 к порогу (терпеливые не паникуют).
            float classMult = CombatClass switch
            {
                CombatClass.Military  => NpcBalance.Fear_ThresholdMult_Military,
                CombatClass.Pirate    => NpcBalance.Fear_ThresholdMult_Pirate,
                CombatClass.Mercenary => NpcBalance.Fear_ThresholdMult_Mercenary,
                CombatClass.Civilian  => NpcBalance.Fear_ThresholdMult_Civilian,
                _ => 1f
            };
            float cautionMod = 1f + Mathf.Clamp01((Personality?.Caution ?? 50f) / 100f);
            // Инвертируем: высокая Caution → выше порог? Наоборот — осторожные пугаются легче.
            // Compensation: делим на cautionMod, чтобы high-Caution снижал порог, не повышал.
            float threshold = classMult / cautionMod;
            if (threshold < 0.05f) threshold = 0.05f;

            FearLevel = raw / threshold;
            InFear = FearLevel >= 1f;
        }

        /// <summary>Сброс директивной активности — обязателен на каждом «директивы нет» выходе,
        /// иначе CurrentActivity/GetCombatTargetUid продолжают отдавать протухшее действие
        /// истёкшей директивы (стрельба по устаревшей цели, неверный приказ в UI).</summary>
        private void ClearDirective()
        {
            _directiveActivity = null;
            _directiveName = "None";
        }

        private bool EvaluateDirective(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            if (DirectiveManager.Instance == null) { ClearDirective(); return false; }
            // Партнёрство — per-ship путь, работает даже когда у Owner нет никаких директив.
            bool isPartner = !string.IsNullOrEmpty(ship.PartnerLeaderUid);
            if (!isPartner && !DirectiveManager.Instance.HasDirectives(ship.Owner)) { ClearDirective(); return false; }

            var directiveAction = DirectiveManager.Instance.GetDirectiveActionFor(ship, star);
            if (directiveAction == null) { ClearDirective(); return false; }

            string newName = directiveAction.DebugName;
            if (newName != _directiveName)
            {
                _directiveName = newName;
                _directiveActivity = directiveAction;
            }

            if (_directiveActivity != null && !_directiveActivity.IsCompleted)
            {
                _directiveActivity.Tick(ship, star, ctx);
                return true;
            }

            ClearDirective();
            return false;
        }

        private Interrupt? EvaluateSituation(ShipData ship, StarData star)
        {
            if (ship.MaxHull <= 0) return null;

            // ── Fear-приоритет (самый высокий) ────────────────────────────────
            // Заменяет три прежние flee-ветки (hullPct/Frustration/SystemPower):
            // все они теперь входят в AssessFear как компоненты давления.
            //  1) InFear + деньги + свежий агрессор → попытка откупиться (кроме военных).
            //  2) InFear + конкретная угроза рядом → Flee.
            //  3) InFear иначе → SeekShelter (станция → планета → соседняя система → fallback flee).
            if (InFear)
            {
                if (ship.Money >= 100 && CombatClass != CombatClass.Military)
                {
                    string atk = GetRecentAttackerUid(ship);
                    if (atk != null) return new Interrupt(InterruptKind.OfferMoney, atk);
                }
                string threat = FindNearestEnemyUid(ship, star);
                if (threat != null && Personality.Caution > 40f)
                    return new Interrupt(InterruptKind.Flee, threat);
                return new Interrupt(InterruptKind.SeekShelter, threat);
            }

            // Reactive defense: если кто-то недавно атаковал, и он жив — приоритетно отвечаем
            // (только для классов с боевой готовностью).
            string attackerUid = GetRecentAttackerUid(ship);
            if (attackerUid != null && CombatClass != CombatClass.Civilian)
            {
                ShipData attacker = FindShipByUid(star, attackerUid);
                if (attacker != null && attacker.CurrentHull > 0 && WithinEngageRange(ship, attacker))
                {
                    float ctw = ChanceToWin(attacker);
                    if (ctw >= Personality.EngageThreshold * 0.7f)
                        return new Interrupt(InterruptKind.PursueAndAttack, attackerUid);
                }
            }

            // SOS: атакованный NPC (не Military) зовёт подмогу. Military сам разбирается через
            // MilitaryLogic. Кулдаун: не чаще LastAttackerMemoryTurns/2 ходов, чтобы под непрерывной
            // атакой корабль не спамил уведомления и отношения.
            if (attackerUid != null && CombatClass != CombatClass.Military
                && CanRequestHelp(ship))
                return new Interrupt(InterruptKind.CallForHelp, attackerUid);

            var classResult = CombatClass switch
            {
                CombatClass.Military  => MilitaryLogic(ship, star),
                CombatClass.Pirate    => PirateLogic(ship, star),
                CombatClass.Civilian  => CivilianLogic(ship, star),
                CombatClass.Mercenary => MercenaryLogic(ship, star),
                _ => (Interrupt?)null
            };
            if (classResult.HasValue) return classResult;

            // Низкоприоритетный триггер восстановления боеспособности. Активируется только
            // когда классовая логика не нашла себе занятия (нет цели, нет вражды рядом).
            // Синтеты игнорируют посадки через флаг ShipTypeConfig.SkipsResupplyLandings.
            if (ShouldResupply(ship)) return new Interrupt(InterruptKind.Resupply);

            return null;
        }

        /// <summary>Composite «нужда» — сравнивается с <see cref="NpcBalance.Resupply_PressureThreshold"/>.
        /// Учитывает hull, топливо, деньги, наличие «сдаваемого». Игнорируется для типов кораблей,
        /// у которых установлен флаг <see cref="ShipTypeConfig.SkipsResupplyLandings"/> (Синтеты).</summary>
        private static bool ShouldResupply(ShipData ship)
        {
            if (ship == null) return false;
            if (ship.HyperjumpPhase != HyperjumpPhase.None) return false;
            // Лидер запретил подчинённому садиться (подменю «Настроить поведение») — тогда он
            // не уходит на дозаправку/ремонт сам и остаётся при лидере.
            if (!ship.AllowLand) return false;
            var shipType = RepairService.ResolveShipType(ship);
            if (shipType != null && shipType.SkipsResupplyLandings) return false;

            float pressure = 0f;

            if (ship.MaxHull > 0)
            {
                float hullPct = (float)ship.CurrentHull / ship.MaxHull;
                if (hullPct < NpcBalance.Resupply_HullPressureBelow) pressure += 1f;
            }

            int fuelCap = EquipmentSystem.GetFuelCapacity(ship);
            if (fuelCap > 0)
            {
                float fuelPct = (float)EquipmentSystem.GetCurrentFuel(ship) / fuelCap;
                if (fuelPct < NpcBalance.Resupply_FuelPressureBelow) pressure += 1f;
            }

            if (ship.Money > NpcBalance.Resupply_MoneyPressureAbove) pressure += 0.5f;

            if (HasSurplusToSell(ship)) pressure += 0.3f;
            if (HasWornEquipment(ship)) pressure += 0.5f;

            return pressure >= NpcBalance.Resupply_PressureThreshold;
        }

        private static bool HasSurplusToSell(ShipData ship)
        {
            if (ship.Inventory != null)
            {
                foreach (var kv in ship.Inventory.Stacks)
                {
                    if (kv.Value == null || kv.Value.TotalWeight <= 0) continue;
                    if (kv.Value.IsGoods && kv.Key == ship.TraderCargoGoodId) continue;
                    return true;
                }
            }
            if (ship.AllItems != null)
            {
                foreach (var kv in ship.AllItems)
                {
                    var item = kv.Value;
                    if (item == null) continue;
                    bool equipped = false;
                    foreach (var slotUid in ship.Equipment.Slots.Values)
                        if (slotUid == item.Uid) { equipped = true; break; }
                    if (!equipped) return true;
                }
            }
            return false;
        }

        private static bool HasWornEquipment(ShipData ship)
        {
            if (ship?.AllItems == null) return false;
            foreach (var kv in ship.AllItems)
            {
                var item = kv.Value;
                if (item == null || item.MaxDurability <= 0) continue;
                float dur = (float)item.Durability / item.MaxDurability;
                if (dur < 0.5f) return true;
            }
            return false;
        }

        // LastAttackerMemoryTurns вынесен в NpcBalance.cs

        private static ShipData FindShipByUid(StarData star, string uid) => star?.FindShip(uid);

        /// <summary>Прошёл ли кулдаун SOS у корабля. Кулдаун = LastAttackerMemoryTurns/2 ходов —
        /// достаточно частый, чтобы отвечать на новую волну атак, но не спамить каждый ход.</summary>
        private static bool CanRequestHelp(ShipData ship)
        {
            int currentTurn = GameWorld.GeneratedGalaxy?.CurrentTurn ?? 0;
            int cooldown = UnityEngine.Mathf.Max(3, NpcBalance.LastAttackerMemoryTurns / 2);
            return ship.LastHelpCallTurn < 0
                || currentTurn - ship.LastHelpCallTurn >= cooldown;
        }

        private static string GetRecentAttackerUid(ShipData ship)
        {
            if (string.IsNullOrEmpty(ship.LastAttackerUid)) return null;
            int currentTurn = GameWorld.GeneratedGalaxy?.CurrentTurn ?? 0;
            if (currentTurn - ship.LastAttackerTurn > NpcBalance.LastAttackerMemoryTurns)
            {
                ship.LastAttackerUid = null;
                return null;
            }
            return ship.LastAttackerUid;
        }

        private Interrupt? MilitaryLogic(ShipData ship, StarData star)
        {
            // Военные (регулярные армии Содружества + все синтеты) не ждут, пока враг подойдёт
            // на MaxEngageRange. При обнаружении hostile в звезде — лететь атаковать через
            // всю систему. ActionPursueAndAttack создаётся без limitEngageRange (см. SetActivity ниже).
            string enemy = FindAnyHostileForMilitary(ship, star);
            if (enemy != null) return new Interrupt(InterruptKind.PursueAndAttack, enemy);
            string criminal = FindHighCrimeShipUid(ship, star);
            if (criminal != null) return new Interrupt(InterruptKind.PursueAndAttack, criminal);
            return null;
        }

        /// <summary>Военный ищет hostile по ВСЕЙ звезде (без MaxEngageRange). Приоритет — по тому же
        /// score, что <see cref="GetTargetPriorityScore"/>: Military > Pirate > Mercenary > Civilian.
        /// EvaluateTarget обязателен, чтобы уважать personal-vendetta/Aggression-overrides.</summary>
        private string FindAnyHostileForMilitary(ShipData ship, StarData star)
        {
            return ShipQuery.In(star)
                .HostileTo(ship)
                .Where(candidate => EvaluateTarget(ship, star, candidate) == TargetDecision.Attack)
                .Best(candidate => GetTargetPriorityScore(ship, candidate))?.Uid;
        }

        private Interrupt? PirateLogic(ShipData ship, StarData star)
        {
            // Flee/OfferMoney по HP/агрессору теперь идёт через fear-ветку в EvaluateSituation
            // (InFear → OfferMoney→Flee→SeekShelter). Здесь только «нападаем/грабим».
            if (Personality.Greed > 40f)
            {
                string victim = FindVictimUid(ship, star);
                if (victim != null)
                {
                    // Приоритет: есть груз → грабим груз; иначе — если есть деньги → вымогаем.
                    // Проверяем через Preview сервисов (учтёт distance/pact/hard-target — если refuse,
                    // корабль отфильтруется и мы попробуем другого через FindVictimUid позже).
                    // Дедуп «уже Rob/Extort этой цели» встроен в SetActivity через _lastInterrupt.
                    var victimShip = FindShipByUid(star, victim);
                    if (victimShip != null)
                    {
                        var robPv = SRG.Ships.Services.CargoRobberyService.Preview(victimShip, ship);
                        if (robPv.Accepted) return new Interrupt(InterruptKind.Rob, victim);
                        int seed = SRG.Ships.Services.ExtortionService.SeedDemand(victimShip);
                        var exPv = SRG.Ships.Services.ExtortionService.Preview(victimShip, ship, seed);
                        if (exPv.Accepted) return new Interrupt(InterruptKind.Extort, victim);
                    }
                }
            }
            string weakEnemy = FindWeakEnemyUid(ship, star);
            if (weakEnemy != null) return new Interrupt(InterruptKind.PursueAndAttack, weakEnemy);
            return null;
        }

        private Interrupt? CivilianLogic(ShipData ship, StarData star)
        {
            // Гражданские реагируют и на формально-нейтрального недавнего агрессора
            // (например, игрок без вражды по фракции), а не только на «hostile by relation».
            // Собственно flee идёт через fear-ветку (InFear=true у гражданских при любом
            // боевом рядом — низкий Fear_ThresholdMult_Civilian). Здесь остаются мирные
            // переговоры: откуп или ceasefire, чтобы у купца был шанс до срыва в бегство.
            string attackerUid = GetRecentAttackerUid(ship);
            ShipData attacker = null;
            if (attackerUid != null)
            {
                var atk = FindShipByUid(star, attackerUid);
                if (atk != null && atk.CurrentHull > 0) attacker = atk;
            }
            if (attacker == null) attacker = FindNearestEnemy(ship, star);
            if (attacker == null) return null;

            var atkClass = ResolveCombatClass(attacker.ShipTypeId);

            // Если атакующий слаб, а «купец» вооружён и не сильно осторожен — принимает бой.
            // Это оправданный акт самообороны: делает атакующего личным врагом (ApplyHostilityPenalty
            // прибавляет вражду выше нейтрала) и переходит в PursueAndAttack. SOS будет разослан
            // отдельным Interrupt.CallForHelp через EvaluateSituation в следующий тик (см. reactive
            // branch выше — гейт CombatClass!=Military; жертва-купец под него подходит).
            float ctw = ChanceToWin(attacker);
            bool canFightBack = ctw >= Personality.EngageThreshold
                                && Personality.Caution < 70f
                                && WithinEngageRange(ship, attacker);
            if (canFightBack)
            {
                Personality.ApplyHostilityPenalty(attacker.Uid);
                return new Interrupt(InterruptKind.PursueAndAttack, attacker.Uid);
            }

            bool richTrader = ship.Money >= 500 && Personality.Greed < 70f;

            if (richTrader && atkClass != CombatClass.Civilian)
                return new Interrupt(InterruptKind.OfferMoney, attacker.Uid);
            if (atkClass != CombatClass.Civilian && Personality.Caution > 50f)
                return new Interrupt(InterruptKind.RequestCeasefire, attacker.Uid);
            return null;
        }

        private Interrupt? MercenaryLogic(ShipData ship, StarData star)
        {
            // Наёмники-«охотники за головами»: жадный наёмник трясёт пиратов и высококриминальных
            // (тот же механизм, что у пиратов, но цель легитимная).
            // Не трогает мирных гражданских (тех грабят только пираты — см. PirateLogic).
            if (Personality.Greed > 40f)
            {
                string victim = FindCriminalVictimUid(ship, star);
                if (victim != null)
                {
                    var victimShip = FindShipByUid(star, victim);
                    if (victimShip != null)
                    {
                        var robPv = SRG.Ships.Services.CargoRobberyService.Preview(victimShip, ship);
                        if (robPv.Accepted) return new Interrupt(InterruptKind.Rob, victim);
                        int seed = SRG.Ships.Services.ExtortionService.SeedDemand(victimShip);
                        var exPv = SRG.Ships.Services.ExtortionService.Preview(victimShip, ship, seed);
                        if (exPv.Accepted) return new Interrupt(InterruptKind.Extort, victim);
                    }
                }
            }

            // Flee по низкому CTW ушёл в fear-ветку: слабый vs сильный → высокое давление → InFear.
            ShipData enemy = FindBestTarget(ship, star);
            if (enemy != null)
            {
                float ctw = ChanceToWin(enemy);
                if (ctw >= Personality.EngageThreshold)
                    return new Interrupt(InterruptKind.PursueAndAttack, enemy.Uid);
            }
            if (Personality.Tribalism > 70f)
            {
                string alliedUnderAttack = FindAlliedUnderAttackUid(ship, star);
                if (alliedUnderAttack != null)
                    return new Interrupt(InterruptKind.PursueAndAttack, alliedUnderAttack);
            }
            return null;
        }

        // --- Target search ---

        /// <summary>Цель в пределах NpcBalance.MaxEngageRange — дальше атаку не начинаем
        /// (и ActionPursueAndAttack на той же дистанции прекращает погоню, поэтому
        /// согласованность порогов обязательна — иначе цикл «взял цель → бросил»).</summary>
        private static bool WithinEngageRange(ShipData self, ShipData target)
            => (target.Position - self.Position).sqrMagnitude
               <= NpcBalance.MaxEngageRange * NpcBalance.MaxEngageRange;

        private ShipData FindBestTarget(ShipData ship, StarData star)
        {
            var query = ShipQuery.In(star)
                .HostileTo(ship)
                .Within(ship.Position, NpcBalance.MaxEngageRange)
                .Where(candidate => EvaluateTarget(ship, star, candidate) == TargetDecision.Attack);
            return query.Best(candidate => GetTargetPriorityScore(ship, candidate));
        }

        private string FindBestTargetUid(ShipData ship, StarData star) => FindBestTarget(ship, star)?.Uid;

        private static ShipData FindNearestEnemy(ShipData ship, StarData star)
            => NpcTargeting.FindNearestHostile(star, ship, ship.Position);

        private string FindNearestEnemyUid(ShipData ship, StarData star) => FindNearestEnemy(ship, star)?.Uid;

        /// <summary>Легитимная цель для грабежа вольным пилотом: пиратский корабль в дистанции
        /// либо высококриминальный (CrimeRating ≥ 20) любого класса кроме Military/Ranger.
        /// EngageRange соблюдается: вольный пилот не гоняется за криминалом через полсистемы.</summary>
        private string FindCriminalVictimUid(ShipData ship, StarData star)
        {
            const float crimeThreshold = 20f;
            return ShipQuery.In(star)
                .Excluding(ship)
                .Within(ship.Position, NpcBalance.MaxEngageRange)
                .Where(s =>
                {
                    var cls = ResolveCombatClass(s.ShipTypeId);
                    if (cls == CombatClass.Pirate) return true;
                    if (cls == CombatClass.Military || cls == CombatClass.Mercenary) return false;
                    return s.CrimeRating >= crimeThreshold;
                })
                .FirstOrDefault()?.Uid;
        }

        private string FindVictimUid(ShipData ship, StarData star)
        {
            // Пират ищет жертву: гражданский корабль, враждебный (по фракции) или при высокой Aggression
            // любой. Ходит через ShipQuery — но фильтр «hostile OR aggressive» не hostile-only, поэтому
            // сканируем все Civilian в системе через OfClass, а вражду проверяем в Where.
            var rel = OwnerRaceRelationsManager.Instance;
            bool aggressive = Personality != null && Personality.Aggression > 80f;
            return ShipQuery.In(star)
                .Excluding(ship)
                .OfClass(CombatClass.Civilian)
                .Where(s => aggressive || (rel != null && rel.AreHostile(ship, s)))
                .FirstOrDefault()?.Uid;
        }

        private string FindWeakEnemyUid(ShipData ship, StarData star)
        {
            float bestCtw = Personality.EngageThreshold;
            ShipData best = null;
            foreach (var s in ShipQuery.In(star).HostileTo(ship).Within(ship.Position, NpcBalance.MaxEngageRange).Enumerate())
            {
                float ctw = ChanceToWin(s);
                if (ctw > bestCtw) { bestCtw = ctw; best = s; }
            }
            return best?.Uid;
        }

        private string FindHighCrimeShipUid(ShipData ship, StarData star)
        {
            const float crimeThreshold = 20f;
            // Криминальные корабли ищутся по всему star.Ships (crime не привязан к hostile);
            // ShipQuery без фильтра владельца — общий проход.
            return ShipQuery.In(star)
                .Excluding(ship)
                .Where(s => s.CrimeRating >= crimeThreshold)
                .FirstOrDefault()?.Uid;
        }

        private string FindAlliedUnderAttackUid(ShipData ship, StarData star)
        {
            var relations = OwnerRaceRelationsManager.Instance;
            if (relations == null) return null;
            // Внешний цикл — союзники (не-враждебные к ship через ShipQuery.FriendlyTo);
            // внутренний — атакующие в радиусе AllyHelpTriggerRadiusSq. Порядок 100× дешевле
            // прежнего O(N²) обхода star.Ships × star.Ships при 40+ кораблях в системе.
            float radius = Mathf.Sqrt(NpcBalance.AllyHelpTriggerRadiusSq);
            foreach (var ally in ShipQuery.In(star).Excluding(ship).FriendlyTo(ship).Enumerate())
            {
                var attacker = ShipQuery.In(star)
                    .Excluding(ship)
                    .HostileTo(ally)
                    .Within(ally.Position, radius)
                    .FirstOrDefault();
                if (attacker != null && attacker != ally) return attacker.Uid;
            }
            return null;
        }

        // --- EvaluateTarget ---

        public TargetDecision EvaluateTarget(ShipData self, StarData star, ShipData target)
        {
            if (target == null) return TargetDecision.Ignore;
            int relation = OwnerRaceRelationsManager.Instance?.GetRelation(self, target) ?? 50;
            float personalMod = Personality.GetPersonalModifier(target.Uid);
            relation = Mathf.Clamp(Mathf.RoundToInt(relation + personalMod), 0, 100);
            var targetClass = ResolveCombatClass(target.ShipTypeId);
            TargetDecision decision = DecisionTable.Evaluate(relation, targetClass);
            return ApplyPersonalityOverride(self, star, decision, target, relation);
        }

        public TargetDecision EvaluateTarget(ShipData self, StarData star, string targetUid)
        {
            var ships = star.Ships;
            for (int i = 0; i < ships.Count; i++)
                if (ships[i].Uid == targetUid) return EvaluateTarget(self, star, ships[i]);
            return TargetDecision.Ignore;
        }

        private TargetDecision ApplyPersonalityOverride(ShipData self, StarData star, TargetDecision baseDecision, ShipData target, int relation)
        {
            if (baseDecision == TargetDecision.Ignore && relation == 50)
            {
                if (Personality.Aggression > 60f && ResolveCombatClass(target.ShipTypeId) == CombatClass.Mercenary)
                    return TargetDecision.Attack;
            }

            if (Personality.Aggression > 95f && Personality.Discipline <= 70f)
            {
                if (ResolveCombatClass(target.ShipTypeId) == CombatClass.Civilian)
                    return TargetDecision.Attack;
            }

            if (Personality.Tribalism > 70f && baseDecision == TargetDecision.Ignore)
            {
                bool allied = !(OwnerRaceRelationsManager.Instance?.AreHostile(self, target) ?? false);
                if (!allied) return TargetDecision.Ignore;
            }

            if (baseDecision == TargetDecision.Attack)
            {
                // Синтеты никогда не оценивают шансы — атакуют всегда, кого бы ни встретили.
                bool alwaysEngage = self.Owner == "Dominators";
                if (!alwaysEngage)
                {
                    float ctw = ChanceToWin(target);
                    if (ctw < Personality.EngageThreshold)
                        return Personality.Caution > 50f ? TargetDecision.Flee : TargetDecision.Ignore;
                }
            }

            if (baseDecision == TargetDecision.Attack && Personality.Greed > 80f && star.Asteroids?.Count > 0)
                return TargetDecision.LootInstead;

            return baseDecision;
        }

        private float GetTargetPriorityScore(ShipData self, ShipData target)
        {
            var tc = ResolveCombatClass(target.ShipTypeId);
            float score = tc switch
            {
                CombatClass.Military  => 4f,
                CombatClass.Pirate    => 3f,
                CombatClass.Mercenary => 2f,
                _ => 1f
            };
            float vendettaMod = Personality.GetPersonalModifier(target.Uid);
            if (vendettaMod < 0f) score += Mathf.Abs(vendettaMod) / 20f;
            if (Personality.Tribalism > 70f)
            {
                bool allied = !(OwnerRaceRelationsManager.Instance?.AreHostile(self, target) ?? false);
                if (allied) score *= 0.1f;
            }
            return score;
        }

        public float ChanceToWin(ShipData enemy)
        {
            if (enemy == null) return 1f;
            float enemyStr = CalculateStrength(enemy);
            if (_cachedStrength + enemyStr < 0.001f) return 0.5f;
            return _cachedStrength / (_cachedStrength + enemyStr);
        }

        public static float CalculateStrength(ShipData ship)
        {
            float firepower = 0f;
            foreach (var slotKey in ship.GetSortedWeaponSlots())
            {
                string uid = ship.Equipment.GetItemUid(slotKey);
                if (uid != null && ship.AllItems.TryGetValue(uid, out var w) && w.IsWorking)
                    firepower += (w.GetParam("MinDmg") + w.GetParam("MaxDmg")) * 0.5f;
            }
            float armor = EquipmentSystem.GetHullParam(ship, "Armor");
            var shieldItem = EquipmentSystem.GetEquipped(ship, SlotKeys.Shield);
            float shieldBlock = (shieldItem != null && shieldItem.IsWorking) ? shieldItem.GetParam("BlockPercent", 0f) : 0f;
            return firepower + ship.CurrentHull * 0.3f + armor * 0.5f + shieldBlock * 0.5f;
        }

        private void TickStrengthCache(ShipData ship)
        {
            _strengthCacheAge++;
            int cacheTurns = _threatPresent ? 3 : NpcBalance.StrengthCacheTurns;
            if (_strengthCacheAge >= cacheTurns)
            {
                _cachedStrength = CalculateStrength(ship);
                _strengthCacheAge = 0;
            }
        }

        private void TickPersonality(ShipData ship)
        {
            Personality.TickPersonalRelations();
            // Пассивный decay Frustration в мирное время: если нет свежего агрессора
            // (LastAttackerUid null или память по нему уже протухла), Frustration
            // постепенно спадает — иначе в системах без боёв она остаётся замороженной.
            if (Personality.Frustration > 0f && GetRecentAttackerUid(ship) == null)
                Personality.ReduceFrustration(NpcBalance.FrustrationPassiveDecay);
        }

        private void TickActivity(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            if (_currentActivity == null)
            {
                _currentActivity = ChooseDefaultActivity(ship, star);
                _lastInterrupt = null;
                // Сразу тикнуть выбранную активность — чтобы её первый эффект (например,
                // постановка TargetPosition) применился в этот же ход, а не следующий.
                _currentActivity?.Tick(ship, star, ctx);
                return;
            }

            bool done = _currentActivity.Tick(ship, star, ctx);
            if (done)
            {
                _currentActivity = ChooseDefaultActivity(ship, star);
                _lastInterrupt = null;
                // То же самое для следующей активности: тикаем её немедленно, чтобы корабль
                // (особенно после гиперперехода) сразу получил актуальный курс/цель и не
                // простаивал на ходе выхода из портала.
                _currentActivity?.Tick(ship, star, ctx);
            }
        }

        /// <summary>Сбросить текущую активность и директивную активность. Следующий Tick
        /// пересоберёт всё с нуля через ChooseDefaultActivity + EvaluateSituation с актуальной
        /// звездой. Вызывать после смены системы (гиперпереход), чтобы стрелять по призракам из
        /// исходной системы прекратил не только action, но и sub-order.</summary>
        public void ResetActivity()
        {
            _currentActivity = null;
            _lastInterrupt = null;
            _directiveActivity = null;
            _directiveName = "None";
        }

        /// <summary>Принудительно поставить активность (используется action'ами при эскалации
        /// и тестами/сценариями). CurrentOrder в UI покажет action.DebugName.</summary>
        public void ForceActivity(NpcAction action)
        {
            if (action == null) return;
            _currentActivity = action;
            _lastInterrupt = null;
            _directiveActivity = null;
            _directiveName = "None";
        }

        private void SetActivity(ShipData ship, StarData star, Interrupt interrupt)
        {
            if (_lastInterrupt.HasValue && _lastInterrupt.Value.Equals(interrupt)) return;

            _lastInterrupt = interrupt;
            _threatPresent = interrupt.Kind == InterruptKind.PursueAndAttack;

            // Military-класс (регулярные армии + синтеты) ведёт погоню без range-лимита —
            // задача: истребить врага, где бы он ни был в системе. Пираты/наёмники/гражданские —
            // с лимитом (не увязают в бесконечной погоне через всю систему).
            bool unlimitedPursuit = CombatClass == CombatClass.Military;
            _currentActivity = interrupt.Kind switch
            {
                InterruptKind.PursueAndAttack  => new ActionPursueAndAttack(interrupt.TargetUid, limitEngageRange: !unlimitedPursuit),
                InterruptKind.Flee             => new ActionFlee(interrupt.TargetUid),
                InterruptKind.Rob              => new ActionRob(interrupt.TargetUid),
                InterruptKind.Extort           => new ActionExtort(interrupt.TargetUid),
                InterruptKind.RequestCeasefire => new ActionRequestCeasefire(interrupt.TargetUid),
                InterruptKind.OfferMoney       => new ActionOfferMoneyRansom(interrupt.TargetUid),
                InterruptKind.Resupply         => new ActionLandResupply(),
                InterruptKind.SeekShelter      => new ActionSeekShelter(interrupt.TargetUid),
                InterruptKind.CallForHelp      => new ActionCallForHelp(interrupt.TargetUid),
                _ => _currentActivity
            };
        }

        private NpcAction ChooseDefaultActivity(ShipData ship, StarData star)
        {
            // Ship spawned/loaded landed (LandedPlanetUid set): у ActionGoodsTrader есть
            // собственная landed-ветка (HandleLanded), а классовые дефолты (Patrol/Defend/Trade/Scavenge)
            // — нет. Без взлёта такие корабли навсегда останутся SetActive(false) на планете.
            // Делегируем в ActionLandResupply.DepartOnly: только взлёт, без Sell/Repair/Fuel/Upgrade
            // (магазинный цикл дорогой; для «просто взлететь и выбрать action» он избыточен).
            // Полный ActionLandResupply включается через InterruptKind.Resupply, когда composite
            // pressure действительно требует ремонта/дозаправки.
            if (!string.IsNullOrEmpty(ship.LandedPlanetUid) && ship.ShipTypeId != "Transport")
                return ActionLandResupply.DepartOnly();

            return CombatClass switch
            {
                CombatClass.Military  => new ActionDefend(Vector2.zero, SRUnits.ToWorld(star?.SystemSize ?? 1000) * 0.6f),
                CombatClass.Pirate    => new ActionPatrol(),
                CombatClass.Civilian  => ship.ShipTypeId == "Transport"
                    ? (NpcAction)new ActionGoodsTrader()
                    : new ActionTrade(),
                CombatClass.Mercenary => DecideMercenaryDefault(ship, star),
                _ => new ActionPatrol()
            };
        }

        private NpcAction DecideMercenaryDefault(ShipData ship, StarData star)
        {
            if (Personality.Greed > 60f && star?.Planets?.Count >= 2) return new ActionTrade();
            // AllowCollect=false — лидер запретил подчинённому подбирать вещи самому
            // (подменю «Настроить поведение»). Прямой приказ «собирай» флагом не гейтится.
            if (Personality.Greed > 40f && ship.AllowCollect) return new ActionScavenge();
            return new ActionPatrol();
        }

        /// <summary>Боевой класс типа корабля. Источник — конфиг: ShipTypes[id].CombatClass
        /// (Civilian/Mercenary/Military/Pirate, регистронезависимо).
        /// Нет конфига/типа или нераспознанное имя → Mercenary.</summary>
        public static CombatClass ResolveCombatClass(string shipTypeId)
        {
            var types = GameWorld.Context?.Config?.Ships?.ShipTypes;
            if (types != null && !string.IsNullOrEmpty(shipTypeId)
                && types.TryGetValue(shipTypeId, out var cfg)
                && System.Enum.TryParse<CombatClass>(cfg.CombatClass, ignoreCase: true, out var cls))
                return cls;
            return CombatClass.Mercenary;
        }
    }
}
