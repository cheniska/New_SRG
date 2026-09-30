using System;
using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.Ships.Movement;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.Combat
{
    /// <summary>
    /// Управляет запуском, движением и детонацией активных ракет (HitPattern.Homing).
    /// Ракеты — это персистентные сущности в StarData.ActiveMissiles, которые
    /// преследуют цель между сабтёрнами и сохраняются между днями.
    ///
    /// Модель воспроизводит снаряды SR2HD (docs/Missile_Trajectory.txt):
    ///   • двухфазная — launch (день запуска: прямая по LaunchDirection) + homing
    ///     (дуговая, ограниченная TurnRadPerTurn);
    ///   • SR2-spread: 60°/(N+3) с чередованием знака + offset 8 (в наших — ~0.08) от ствола;
    ///   • Owner-inertia на старте: к Speed добавляется доля скорости стрелка;
    ///   • Speed ramp: разгон от стартовой к SpeedMax со скоростью SpeedRampPerTurn;
    ///   • MissJitter: при отдалении от цели включается «петля промаха» (зеркальное Y в atan2);
    ///   • Force-expire: если для долёта нужно &gt; MaxRangeFactor × оставшегося срока — смерть;
    ///   • AutoReacquire: ракеты класса homing/5 ищут ближайшего hostile при потере цели.
    /// </summary>
    public static class MissileSystem
    {
        const float HitRadius = 0.15f;        // запас сверх радиуса коллизии корабля
        const float DefaultShipRadius = 0.35f; // фолбэк, если нет данных о корпусе
        const int   MaxSalvo  = 72; // 72 * угол = полный круг при компактных стволах

        /// <summary>
        /// Запускает залп ракет. Углы и точки спавна — по SR2-формуле (Missile §2.1):
        /// первый снаряд (i=0) идёт по курсу корабля; пары i=1,2 — отклонение ±60°/(N+3);
        /// i=3,4 — ±2·60°/(N+3) и т.д. Точка спавна каждой ракеты смещается на SpawnOffset
        /// вдоль её угла, чтобы они физически не пересекались внутри одной точки.
        /// Боезапас и износ оружия списываются один раз за залп.
        /// </summary>
        public static void LaunchSalvo(
            ShipData attacker,
            ShipData target,
            string slotKey,
            ItemInstance weapon,
            StarData star,
            ItemsConfig equipConfig,
            TurnAnimationData anim,
            int subTurn)
        {
            if (attacker == null || target == null || weapon == null) return;

            var weaponItemCfg = equipConfig?.GetItem(EquipmentCategory.Weapons, weapon.ItemId);
            var mc = weaponItemCfg?.Missile;
            int salvo = Mathf.Clamp(mc?.SalvoCount ?? 1, 1, MaxSalvo);

            // Триггеры залпа (Ралс — «15% шанс двойного залпа»). ExtraSalvos прибавляется
            // к базовому размеру залпа; MaxSalvo продолжает капить.
            var trigCtx = new SRG.Equipment.TriggerContext
            {
                Event = SRG.Equipment.TriggerEvent.MissileFire,
                Slot  = EquipmentCategory.Weapons,
            };
            SRG.Equipment.TriggerBus.Fire(attacker, trigCtx);
            if (trigCtx.ExtraSalvos > 0)
                salvo = Mathf.Clamp(salvo + trigCtx.ExtraSalvos * Mathf.Max(1, mc?.SalvoCount ?? 1), 1, MaxSalvo);

            // Ракеты вылетают по курсу корабля (forward); фактический хоминг включится со
            // следующего хода (LaunchPhase=false). Это даёт визуальное «вылетание из ствола».
            Vector2 baseDir;
            float baseAngleDeg;
            if (!float.IsNaN(attacker.CurrentHeading))
            {
                baseDir = new Vector2(Mathf.Cos(attacker.CurrentHeading), Mathf.Sin(attacker.CurrentHeading));
                baseAngleDeg = attacker.CurrentHeading * Mathf.Rad2Deg;
            }
            else
            {
                baseDir = target.Position - attacker.Position;
                baseDir = baseDir.sqrMagnitude > 0.0001f ? baseDir.normalized : Vector2.up;
                baseAngleDeg = Mathf.Atan2(baseDir.y, baseDir.x) * Mathf.Rad2Deg;
            }

            for (int i = 0; i < salvo; i++)
            {
                float deviationDeg = SalvoAngleDeg(i, salvo);
                float spawnOffset = mc?.SpawnOffset ?? 0.08f;
                float finalAngleRad = (baseAngleDeg + deviationDeg) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(finalAngleRad), Mathf.Sin(finalAngleRad));
                Vector2 spawnPos = attacker.Position + dir * spawnOffset;
                CreateMissile(attacker, target, slotKey, weapon, mc, dir, spawnPos, subTurn, star, equipConfig, anim);
            }

            // Боезапас и износ — один раз на залп.
            float ammo = weapon.GetParam("Ammo", -1f);
            if (ammo > 0f) weapon.Params["Ammo"] = ammo - 1f;
            if (equipConfig != null)
                EquipmentSystem.ApplyWeaponShotWear(attacker, slotKey, equipConfig);
        }

        /// <summary>
        /// SR2-формула спреда (Missile §2.1): i=0 → 0°; пары симметрично нарастают
        /// с шагом 60°/(N+3). Чем больше залп — тем плотнее веер.
        /// </summary>
        static float SalvoAngleDeg(int i, int salvoCount)
        {
            if (i == 0) return 0f;
            int step = (i + 1) / 2;                          // 1,1,2,2,3,3,...
            int sign = (i & 1) == 1 ? 1 : -1;                // нечётный — +; чётный — −
            float spreadStep = 60f / (salvoCount + 3);
            return step * spreadStep * sign;
        }

        static void CreateMissile(
            ShipData attacker,
            ShipData target,
            string slotKey,
            ItemInstance weapon,
            MissileConfig mc,
            Vector2 launchDir,
            Vector2 spawnPos,
            int subTurn,
            StarData star,
            ItemsConfig equipConfig,
            TurnAnimationData anim)
        {
            var shot = WeaponSystem.BuildShotParams(attacker, target, slotKey, weapon);
            if (shot == null) return;

            float missileHp   = mc?.Hp          ?? 30f;
            float speedMax    = mc?.Speed       ?? 1.5f;
            int   lifedays    = mc?.Lifedays    ?? 5;
            string graphicPath = mc?.GraphicPath;
            float scale        = mc?.Scale      ?? 0.07f;
            bool  returnsOnDeath = mc?.ReturnsOnTargetDeath ?? false;
            float turnDeg       = mc?.TurnDeg   ?? 720f;
            float inertiaFactor = mc?.InertiaFactor ?? 0.5f;
            float speedRamp     = mc?.SpeedRampPerTurn ?? 0f;
            float launchMul     = mc?.LaunchSpeedMultiplier ?? 2.5f;

            // Owner inertia (SR2 §3.A): стартовая скорость зависит от скорости стрелка.
            // Если SpeedRampPerTurn > 0, ракета начинает с base ≤ SpeedMax и разгоняется;
            // если 0 — сразу на потолке, инерция лишь добавляет «выплеск».
            float attackerSpeed = attacker != null
                ? Mathf.Max(0f, SRUnits.ToWorld(attacker.ActualSpeed))
                : 0f;
            float startSpeed = (speedRamp > 0f ? speedMax * 0.5f : speedMax) + attackerSpeed * inertiaFactor;
            // Launch-фаза: ракета должна сразу же оторваться от стрелка, иначе визуально она
            // «висит под кораблём» (особенно медленные торпеды, у которых базовый Speed
            // сравним с скоростью атакующего). На остаток дня запуска стартовая скорость
            // умножается на launchMul; со следующего хода (LaunchPhase=false) используется
            // обычный Speed/SpeedMax.
            float launchStep = startSpeed * Mathf.Max(1f, launchMul);

            var missile = new ActiveMissile
            {
                Uid           = GameRng.NewUid(),
                AttackerUid   = attacker.Uid,
                AttackerOwner = attacker.Owner,
                AttackerRace  = attacker.Race,
                TargetUid     = target.Uid,
                WeaponId      = weapon.ItemId,
                WeaponSlotKey = slotKey,
                Position      = spawnPos,
                CurrentHp     = Mathf.RoundToInt(missileHp),
                MaxHp         = Mathf.RoundToInt(missileHp),
                Speed         = startSpeed,
                SpeedMax      = speedMax,
                SpeedRampPerTurn = speedRamp,
                LaunchSpeed   = launchStep,
                DaysLeft      = lifedays,
                GraphicPath   = graphicPath,
                Scale         = scale,
                LaunchDirection = launchDir,
                LaunchSubTurn   = subTurn,
                LaunchPhase     = true,
                CurrentHeading  = Mathf.Atan2(launchDir.y, launchDir.x),
                TurnRadPerTurn  = turnDeg * Mathf.Deg2Rad,
                ReturnsOnTargetDeath = returnsOnDeath,
                MaxRangeFactor       = mc?.MaxRangeFactor   ?? 2f,
                MissJitterEnabled    = mc?.MissJitter       ?? true,
                AutoReacquire        = mc?.AutoReacquire    ?? false,
                ReacquireRadius      = mc?.ReacquireRadius  ?? 5f,
                MinDmg        = shot.MinDmg,
                MaxDmg        = shot.MaxDmg,
                ArmorPenetration  = shot.ArmorPenetration,
                ShieldPenetration = shot.ShieldPenetration,
                EquipHitChance    = shot.EquipHitChance,
                EquipDamage       = shot.EquipDamage,
                DamageType        = shot.DamageType,
                Effects           = shot.Effects,
            };

            var frames = EnsureMissileFrames(missile.Uid, spawnPos, anim);
            // План на оставшуюся часть текущего дня. Сабтёрны < LaunchSubTurn
            // остаются на launchPos (ракета невидима до залпа); далее — прямая
            // по LaunchDirection, потому что LaunchPhase=true.
            PlanFrames(missile, target.Position, missile.LaunchSubTurn - 1, frames);
            star.ActiveMissiles.Add(missile);
        }

        /// <summary>
        /// Двигает все активные ракеты к цели. Вызывается в конце каждого CombatSubTurn.
        /// Сама траектория уже посчитана заранее в PlanFrames (на старте дня в
        /// InitMissileFrames либо в момент создания/смены цели) — здесь только
        /// читаем кадр, обновляем heading, считаем swept-попадание и
        /// при необходимости запускаем TryReacquireTarget / force-expire.
        /// </summary>
        public static void TickMissiles(
            StarData star,
            int subTurn,
            ItemsConfig equipConfig,
            TurnAnimationData anim)
        {
            for (int i = star.ActiveMissiles.Count - 1; i >= 0; i--)
            {
                var missile = star.ActiveMissiles[i];
                missile.AgeSubTurns++;

                ShipData target = FindShip(star, missile.TargetUid);
                ShipData attacker = FindShip(star, missile.AttackerUid);

                // Пристыкованная к носителю цель недосягаема — эквивалент потери цели.
                bool targetAlive = target != null && target.CurrentHull > 0
                    && string.IsNullOrEmpty(target.LandedOnShipUid);
                anim.MissileFrames.TryGetValue(missile.Uid, out var frames);

                // SR2 §4: после первого хода homing-ракеты пытаются перенацелиться, если
                // потеряли цель. Не применяется в фазе launch и для coast/return.
                if (missile.AutoReacquire
                    && !missile.LaunchPhase
                    && !missile.IsReturning
                    && !missile.TargetDeadCoasting
                    && !targetAlive)
                {
                    var reacquired = TryReacquireTarget(missile, star, attacker);
                    if (reacquired != null)
                    {
                        missile.PrevTargetUid = missile.TargetUid;
                        missile.TargetUid     = reacquired.Uid;
                        target = reacquired;
                        targetAlive = true;
                        if (frames != null) PlanFrames(missile, target.Position, subTurn - 1, frames);
                    }
                }

                // Цель погибла до прилёта снаряда.
                if (!missile.IsReturning && !missile.TargetDeadCoasting && !targetAlive)
                {
                    if (missile.ReturnsOnTargetDeath && attacker != null && attacker.CurrentHull > 0)
                    {
                        // Торпеда: перенаправляется к стрелявшему. Перепланируем оставшуюся
                        // часть хода — иначе ракета летела бы по плану на мертвую цель.
                        missile.IsReturning = true;
                        missile.TargetUid   = missile.AttackerUid;
                        target = attacker;
                        targetAlive = true;
                        if (frames != null) PlanFrames(missile, target.Position, subTurn - 1, frames);
                        GameLog.Add(
                            $"[Торпеда] Цель уничтожена — торпеда возвращается к {attacker.Name}.");
                    }
                    else
                    {
                        // Обычная ракета: запоминаем точку смерти цели и долетаем туда. Без этого
                        // вся «хвостовая» часть залпа взрывалась бы в воздухе в момент гибели цели.
                        missile.TargetDeadCoasting = true;
                        missile.CoastTargetPos = target != null ? target.Position : missile.Position;
                        if (frames != null && !missile.LaunchPhase)
                            PlanFrames(missile, missile.CoastTargetPos, subTurn - 1, frames);
                    }
                }

                // Возвращающаяся торпеда потеряла стрелявшего — взрыв в пустоте.
                if (missile.IsReturning && !targetAlive)
                {
                    ExplodeMissile(missile, subTurn, equipConfig, anim);
                    star.ActiveMissiles.RemoveAt(i);
                    continue;
                }

                // SR2 §3.D force-expire: если ракета не успевает долететь до цели за
                // оставшийся срок (с учётом текущей скорости) — взрываем сейчас, чтобы не
                // плодить бесконечно живущие хвосты.
                if (target != null && missile.MaxRangeFactor > 0f && !missile.LaunchPhase)
                {
                    float distToTarget = (target.Position - missile.Position).magnitude;
                    float effSpeed = Mathf.Max(missile.Speed, missile.SpeedMax) * GalaxyData.SubTurnsPerTurn;
                    if (effSpeed > 1e-4f)
                    {
                        float turnsNeeded = distToTarget / effSpeed;
                        if (turnsNeeded > missile.DaysLeft * missile.MaxRangeFactor)
                        {
                            GameLog.Add(
                                $"[Ракета] Цель слишком далеко — снаряд самоликвидируется.");
                            ExplodeMissile(missile, subTurn, equipConfig, anim);
                            star.ActiveMissiles.RemoveAt(i);
                            continue;
                        }
                    }
                }

                // Берём заранее посчитанную позицию для этого сабтёрна.
                Vector2 newPos = frames != null ? frames.SubTurns[subTurn] : missile.Position;

                // Обновляем CurrentHeading из фактического шага — нужно для отрисовки
                // спрайта и для стартового heading при перепланировании следующего дня.
                Vector2 step = newPos - missile.Position;
                if (step.sqrMagnitude > 1e-8f)
                    missile.CurrentHeading = Mathf.Atan2(step.y, step.x);

                // Coast: ракета летит к точке смерти цели. Проверяем сближение со стационарной
                // точкой — на подлёте взрываемся в пустоте (без урона) в самой точке трупа.
                if (missile.TargetDeadCoasting)
                {
                    Vector2 coast = missile.CoastTargetPos;
                    if (SweptCircleHit(missile.Position, newPos, coast, coast, HitRadius * 3f))
                    {
                        if (frames != null) frames.SubTurns[subTurn] = coast;
                        missile.Position = coast;
                        ExplodeMissile(missile, subTurn, equipConfig, anim);
                        star.ActiveMissiles.RemoveAt(i);
                    }
                    else
                    {
                        missile.Position = newPos;
                    }
                    continue;
                }

                // Swept-проверка попадания. Цель тоже движется в этом сабтёрне, поэтому
                // считаем коллизию двух движущихся кружков: ракета [missile.Position→newPos]
                // против цели [targetFrom→targetTo]. Без этого ракета "пролетает мимо" если
                // цель пересекает её путь, а её собственное движение чуть-чуть в стороне.
                Vector2 targetFrom, targetTo;
                if (anim != null && anim.ShipFrames.TryGetValue(target.Uid, out var tframes))
                {
                    targetFrom = tframes.SubTurns[Mathf.Max(0, subTurn - 1)];
                    targetTo   = tframes.SubTurns[subTurn];
                }
                else
                {
                    // Игрок/прочие без ShipFrames в этом anim — берём текущую позицию для обеих границ.
                    targetFrom = target.Position;
                    targetTo   = target.Position;
                }
                float combinedR = HitRadius + ShipCollisionRadius(target);
                bool hit = SweptCircleHit(missile.Position, newPos, targetFrom, targetTo, combinedR);
                if (hit)
                {
                    HandleMissileImpact(missile, target, attacker, subTurn, newPos, frames, equipConfig, anim);
                    star.ActiveMissiles.RemoveAt(i);
                }
                else
                {
                    missile.Position = newPos;
                }
            }
        }

        /// <summary>
        /// Планирует траекторию ракеты на остаток текущего хода (frames[fromSubTurn..SubTurnsPerTurn]).
        /// Шаг = один сабтёрн. Каждый день перепланируется заново с актуальной позицией цели
        /// (см. InitMissileFrames). Аналог ShipTrajectory.GenerateKinematicSpline, но без обхода
        /// препятствий — ракеты летят сквозь.
        ///
        /// LaunchPhase=true → прямая по CurrentHeading (день запуска, salvo spread фиксирован).
        /// LaunchPhase=false → дуговой хоминг к targetPos с:
        ///   • защитой от «вечной спирали» (inside-circle guard): если цель внутри окружности
        ///     минимального разворота — поворачиваем в противоположную сторону;
        ///   • Speed ramp (§3.B): на каждом шаге Speed подтягивается к SpeedMax;
        ///   • MissJitter (§3.B/§9.4): если расстояние до цели начало расти — включается
        ///     «петля промаха» (зеркальное Y в atan2 на N сабтёрнов).
        ///
        /// State, который персистится между ходами: missile.Speed, missile.LastDistSq,
        /// missile.MissJitterCnt.
        /// </summary>
        static void PlanFrames(
            ActiveMissile missile,
            Vector2 targetPos,
            int fromSubTurn,
            MissileSubTurnFrames frames)
        {
            int last = GalaxyData.SubTurnsPerTurn;
            if (fromSubTurn < 0) fromSubTurn = 0;
            if (fromSubTurn > last) return;

            Vector2 cur = missile.Position;
            float curA = missile.CurrentHeading;

            frames.SubTurns[fromSubTurn] = cur;

            if (missile.SpeedMax < 1e-6f && missile.Speed < 1e-6f)
            {
                for (int s = fromSubTurn + 1; s <= last; s++) frames.SubTurns[s] = cur;
                return;
            }

            if (missile.LaunchPhase)
            {
                // Launch: прямая по углу с бустированным шагом (LaunchSpeed). Это нужно,
                // чтобы торпеда/ракета сразу оторвалась от стрелка, а не «висела» под ним —
                // базовая скорость для медленных снарядов сопоставима со скоростью корабля.
                // LaunchSpeed=0 (старые сохранёнки) → fallback на обычный Speed.
                Vector2 fwd = new Vector2(Mathf.Cos(curA), Mathf.Sin(curA));
                float stepLen = missile.LaunchSpeed > 0f ? missile.LaunchSpeed : missile.Speed;
                for (int s = fromSubTurn + 1; s <= last; s++)
                {
                    cur += fwd * stepLen;
                    frames.SubTurns[s] = cur;
                }
                return;
            }

            float stepTurn = missile.TurnRadPerTurn / last;
            if (stepTurn < 1e-6f)
            {
                Vector2 fwd = new Vector2(Mathf.Cos(curA), Mathf.Sin(curA));
                float stepLen = missile.Speed;
                for (int s = fromSubTurn + 1; s <= last; s++)
                {
                    cur += fwd * stepLen;
                    frames.SubTurns[s] = cur;
                }
                return;
            }

            // SR2 §3.B: разгон/торможение на сабтёрн. SpeedRampPerTurn — единицы/ход;
            // в сабтёрне = ramp/last. SpeedMax — текущий потолок (не меняется в полёте).
            float speedRampPerStep = missile.SpeedRampPerTurn / last;
            float curSpeed = missile.Speed;

            // SR2 §3.B: за основу для длительности jitter берём SpeedMax (в SR2 — Speed_max/40..Speed_max/10
            // кадров). У нас сабтёрнов в ходе всего 10, поэтому масштабируем к более скромному окну:
            // 1..ceil(SubTurnsPerTurn/2) сабтёрнов.
            int jitterMin = 1;
            int jitterMax = Mathf.Max(2, last / 2);

            float lastDistSq = missile.LastDistSq;
            int jitterCnt = missile.MissJitterCnt;

            for (int s = fromSubTurn + 1; s <= last; s++)
            {
                // Speed ramp в сторону SpeedMax (вверх или вниз, если стартанули выше из-за инерции).
                if (speedRampPerStep > 0f && !Mathf.Approximately(curSpeed, missile.SpeedMax))
                {
                    if (Mathf.Abs(curSpeed - missile.SpeedMax) <= speedRampPerStep) curSpeed = missile.SpeedMax;
                    else if (curSpeed < missile.SpeedMax) curSpeed += speedRampPerStep;
                    else                                   curSpeed -= speedRampPerStep;
                }

                Vector2 delta = targetPos - cur;
                float distSq = delta.sqrMagnitude;

                // MissJitter (§3.B/§9.4): детектим начало отдаления и поворачиваем в зеркало.
                bool jitterActive = false;
                if (missile.MissJitterEnabled)
                {
                    if (jitterCnt > 0)
                    {
                        jitterActive = true;
                        jitterCnt--;
                    }
                    else if (lastDistSq >= 0f && distSq > lastDistSq && jitterCnt == 0)
                    {
                        // Только что начали отдаляться — запускаем промах.
                        int seed = StableHash.Of(missile.Uid) ^ s;
                        int range = jitterMax - jitterMin + 1;
                        jitterCnt = jitterMin + Mathf.Abs(seed) % range;
                        jitterActive = true;
                        jitterCnt--;
                    }
                    lastDistSq = distSq;
                }

                float desiredA;
                if (distSq < 1e-8f)
                    desiredA = curA;
                else if (jitterActive)
                    desiredA = Mathf.Atan2(-delta.y, delta.x);   // SR2 §3.B: инверсия Y = «петля»
                else
                    desiredA = Mathf.Atan2(delta.y, delta.x);

                float diff = ShipTrajectory.NormalizeAnglePi(desiredA - curA);

                if (Mathf.Abs(diff) <= stepTurn)
                {
                    curA = desiredA;
                }
                else
                {
                    float turnRadius   = curSpeed / Mathf.Sin(stepTurn);
                    float turnRadiusSq = turnRadius * turnRadius;
                    Vector2 ccwCenter = cur + new Vector2(-Mathf.Sin(curA),  Mathf.Cos(curA)) * turnRadius;
                    Vector2 cwCenter  = cur + new Vector2( Mathf.Sin(curA), -Mathf.Cos(curA)) * turnRadius;
                    bool insideCCW = (targetPos - ccwCenter).sqrMagnitude < turnRadiusSq;
                    bool insideCW  = (targetPos - cwCenter ).sqrMagnitude < turnRadiusSq;

                    int sign;
                    if      (insideCCW && insideCW) sign = diff > 0f ? 1 : -1; // обе недостижимы — летим в сторону цели
                    else if (insideCCW)             sign = -1;                 // принудительно CW
                    else if (insideCW)              sign = +1;                 // принудительно CCW
                    else                            sign = diff > 0f ? 1 : -1; // кратчайший разворот
                    curA += sign * stepTurn;
                }

                cur += new Vector2(Mathf.Cos(curA), Mathf.Sin(curA)) * curSpeed;
                frames.SubTurns[s] = cur;
            }

            // Сохраняем «жизненный» state в саму ракету — он переживает ход.
            missile.Speed         = curSpeed;
            missile.LastDistSq    = lastDistSq;
            missile.MissJitterCnt = jitterCnt;
        }

        /// <summary>
        /// SR2 §4: ищет ближайшего hostile к стрелявшему в радиусе missile.ReacquireRadius
        /// от текущей позиции ракеты. Игнорирует PrevTargetUid (мы только что её потеряли).
        /// Если в радиусе нет врагов — fallback на стрелка (SR2-поведение, см. §4).
        /// </summary>
        static ShipData TryReacquireTarget(ActiveMissile missile, StarData star, ShipData attacker)
        {
            if (star?.Ships == null) return null;
            float bestSq = missile.ReacquireRadius * missile.ReacquireRadius;
            ShipData best = null;
            for (int j = 0; j < star.Ships.Count; j++)
            {
                var s = star.Ships[j];
                if (s == null || s.CurrentHull <= 0) continue;
                if (!string.IsNullOrEmpty(s.LandedOnShipUid)) continue; // пристыкован «внутри» носителя
                if (s.Uid == missile.PrevTargetUid) continue;
                if (s.Uid == missile.AttackerUid)   continue;

                // hostile к стрелявшему: используем общий relations-фасад. Если attacker уже
                // мёртв — допускаем по сохранённым Owner/Race из самой ракеты.
                bool hostile;
                if (attacker != null) hostile = Relations.AreHostile(attacker, s);
                else
                {
                    var mgr = OwnerRaceRelationsManager.Instance;
                    hostile = mgr != null && mgr.AreHostile(missile.AttackerOwner, s.Owner, missile.AttackerRace, s.Race);
                }
                if (!hostile) continue;

                float d2 = (s.Position - missile.Position).sqrMagnitude;
                if (d2 < bestSq) { bestSq = d2; best = s; }
            }
            if (best != null) return best;

            // SR2-фоллбэк: если врагов рядом нет, ракета наводится на стрелка.
            // Реалистично только если стрелок ещё жив; иначе оставим target = null,
            // дальше TickMissiles перейдёт в coast/return.
            if (attacker != null && attacker.CurrentHull > 0)
                return attacker;
            return null;
        }

        /// <summary>
        /// Обрабатывает попадание ракеты в цель. Три исключающихся случая:
        ///   1) Возвращающаяся торпеда коснулась стрелявшего → восстановить боезапас, тихая смерть;
        ///   2) Атакующий погиб к моменту impact'а → AoE-взрыв без урона;
        ///   3) Обычный impact → ApplyMissileImpact + ShotEvent + DeathRegistration.
        /// Вызывающий должен убрать ракету из star.ActiveMissiles.
        /// </summary>
        static void HandleMissileImpact(
            ActiveMissile missile, ShipData target, ShipData attacker,
            int subTurn, Vector2 newPos, MissileSubTurnFrames frames,
            ItemsConfig equipConfig, TurnAnimationData anim)
        {
            if (missile.IsReturning)
            {
                // Торпеда вернулась к стрелявшему — возвращаем боезапас, без урона.
                RestoreAmmo(attacker, missile.WeaponSlotKey);
                GameLog.Add(
                    $"[Торпеда] Возвращена в {attacker.Name} — боезапас восстановлен.");
                FreezeFramesAfter(frames, subTurn, newPos);
                if (anim != null)
                {
                    anim.MissileDeathUids[missile.Uid] = subTurn;
                    anim.MissileSilentDeathUids.Add(missile.Uid); // вернулась без взрыва
                }
                return;
            }

            if (attacker == null || attacker.CurrentHull <= 0)
            {
                // Атакующий уже погиб к моменту impact'а — некому считать боевые эффекты
                // (scanner dominance, drain, артефакты и т.п.). Ракета теряет «голову»
                // и взрывается в пустоте без урона; визуально — обычный AoE-взрыв.
                GameLog.Add(
                    $"[Ракета] Стрелявший погиб — снаряд взорвался без эффекта.");
                FreezeFramesAfter(frames, subTurn, newPos);
                ExplodeMissile(missile, subTurn, equipConfig, anim);
                return;
            }

            var result = WeaponSystem.ApplyMissileImpact(missile, target, attacker, equipConfig);
            var weaponItemCfg = equipConfig?.GetItem(EquipmentCategory.Weapons, missile.WeaponId);

            anim?.Shots.Add(new ShotEvent
            {
                AttackerUid  = missile.Uid,
                TargetUid    = target.Uid,
                WeaponId     = missile.WeaponId,
                DamageDealt  = Mathf.RoundToInt(result.HullDamage),
                SubTurn      = subTurn,
                ShotDuration = 1,
                HitPattern   = HitPattern.Homing,
                DamageType   = missile.DamageType,
                Visual       = weaponItemCfg?.Visual,
                HitEffect    = equipConfig?.GetHitEffect("Homing", missile.DamageType.ToString()),
            });

            if (result.TargetDestroyed)
            {
                // Сохранение поведения: ракетный путь НЕ откатывает TargetDestroyed
                // при спасении игрока (в отличие от WeaponSystem). Возвращаемое значение
                // игнорируется намеренно.
                WeaponSystem.RegisterTargetDeath(target, PlayerDeathCause.Missile, attacker?.Name, attacker?.Owner, anim, attacker);
            }
            FreezeFramesAfter(frames, subTurn, newPos);
            if (anim != null) anim.MissileDeathUids[missile.Uid] = subTurn;
        }

        /// <summary>
        /// Заполняет фреймы ракеты после impact-сабтёрна последней позицией, чтобы
        /// визуальный спрайт не "отскакивал" обратно к startPos при дальнейшей анимации.
        /// </summary>
        static void FreezeFramesAfter(MissileSubTurnFrames frames, int subTurn, Vector2 pos)
        {
            if (frames == null) return;
            for (int s = subTurn + 1; s <= GalaxyData.SubTurnsPerTurn; s++)
                frames.SubTurns[s] = pos;
        }

        static void ExplodeMissile(ActiveMissile missile, int subTurn, ItemsConfig equipConfig, TurnAnimationData anim)
        {
            var weaponItemCfg = equipConfig?.GetItem(EquipmentCategory.Weapons, missile.WeaponId);
            anim?.Shots.Add(new ShotEvent
            {
                AttackerUid  = missile.Uid,
                TargetUid    = null,
                WeaponId     = missile.WeaponId,
                DamageDealt  = 0,
                SubTurn      = subTurn,
                ShotDuration = 1,
                HitPattern   = HitPattern.AoE,
                DamageType   = missile.DamageType,
                Visual       = weaponItemCfg?.Visual,
                HitEffect    = equipConfig?.GetHitEffect("AoE", missile.DamageType.ToString()),
            });

            if (anim != null && anim.MissileFrames.TryGetValue(missile.Uid, out var frames))
                FreezeFramesAfter(frames, subTurn, frames.SubTurns[subTurn]);

            GameLog.Add($"[Ракета] Цель уничтожена — снаряд взорвался в пустоте.");
            if (anim != null) anim.MissileDeathUids[missile.Uid] = subTurn;
        }

        static float ShipCollisionRadius(ShipData ship)
        {
            if (ship == null) return DefaultShipRadius;
            var hull = EquipmentSystem.GetEquipped(ship, SlotKeys.Hull);
            if (hull != null)
            {
                float sizeSmall = hull.GetParam("SizeSmall", 0f);
                if (sizeSmall > 0f) return SRUnits.ToWorld(sizeSmall) * 0.5f;
            }
            return DefaultShipRadius;
        }

        /// <summary>Свипанутая коллизия двух кружков, движущихся за один сабтёрн.
        /// Возвращает true, если их расстояние в любой момент t∈[0..1] было ≤ combinedRadius.</summary>
        static bool SweptCircleHit(Vector2 aFrom, Vector2 aTo, Vector2 bFrom, Vector2 bTo, float combinedRadius)
        {
            Vector2 relFrom = aFrom - bFrom;
            Vector2 relVel  = (aTo - aFrom) - (bTo - bFrom);
            float rSq = combinedRadius * combinedRadius;
            float a = Vector2.Dot(relVel, relVel);
            if (a < 1e-10f) return relFrom.sqrMagnitude <= rSq;
            float b = 2f * Vector2.Dot(relFrom, relVel);
            float c = Vector2.Dot(relFrom, relFrom) - rSq;
            float disc = b * b - 4f * a * c;
            if (disc < 0f) return false;
            float sqrtD = Mathf.Sqrt(disc);
            float t1 = (-b - sqrtD) / (2f * a);
            float t2 = (-b + sqrtD) / (2f * a);
            if (t2 < 0f) return false;
            if (t1 > 1f) return false;
            if (t1 < 0f && b >= 0f) return false;
            return true;
        }

        static ShipData FindShip(StarData star, string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            foreach (var ship in star.Ships)
                if (ship.Uid == uid) return ship;
            return null;
        }

        static void RestoreAmmo(ShipData ship, string slotKey)
        {
            if (ship == null || string.IsNullOrEmpty(slotKey)) return;
            string uid = ship.Equipment?.GetItemUid(slotKey);
            if (uid == null || !ship.AllItems.TryGetValue(uid, out var weapon)) return;
            float ammo = weapon.GetParam("Ammo", -1f);
            if (ammo >= 0f) weapon.Params["Ammo"] = ammo + 1f;
        }

        /// <summary>
        /// Уменьшает таймер ракет и удаляет истёкшие. Вызывается после всех сабтёрнов дня.
        /// </summary>
        public static void TickMissilesEndOfDay(StarData star, TurnAnimationData anim)
        {
            for (int i = star.ActiveMissiles.Count - 1; i >= 0; i--)
            {
                var missile = star.ActiveMissiles[i];
                missile.DaysLeft--;

                // На следующем дне ракета видна с первого сабтёрна — никакой задержки запуска.
                missile.LaunchSubTurn = 0;

                // Фаза запуска длится только до конца хода, в котором ракета вылетела.
                // Со следующего хода включается дуговой хоминг.
                missile.LaunchPhase = false;

                if (missile.DaysLeft <= 0)
                {
                    GameLog.Add(
                        $"[Ракета] Ракета не достигла цели и взорвалась в космосе.");
                    // Срок жизни истёк в конце хода — сабтёрн смерти = последний.
                    if (anim != null) anim.MissileDeathUids[missile.Uid] = GalaxyData.SubTurnsPerTurn;
                    star.ActiveMissiles.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Инициализирует фреймы ракет в начале дня и сразу планирует траекторию
        /// на этот ход (PlanFrames). Никаких ShotEvent-ов не добавляет — визуал полёта
        /// обеспечивается спрайтом из SystemViewManager.SpawnMissileVisual.
        /// </summary>
        public static void InitMissileFrames(StarData star, TurnAnimationData anim)
        {
            foreach (var missile in star.ActiveMissiles)
            {
                var frames = EnsureMissileFrames(missile.Uid, missile.Position, anim);
                Vector2 aimAt;
                if (missile.TargetDeadCoasting)
                {
                    // Coast-ракета летит к зафиксированной точке смерти цели.
                    aimAt = missile.CoastTargetPos;
                }
                else
                {
                    var target = FindShip(star, missile.TargetUid);
                    // Если цель потеряна между ходами — летим прямо, дальнейшая судьба
                    // (взрыв / возврат торпеды / reacquire) решится в TickMissiles.
                    aimAt = target != null
                        ? target.Position
                        : missile.Position + new Vector2(Mathf.Cos(missile.CurrentHeading),
                                                        Mathf.Sin(missile.CurrentHeading));
                }
                // На новом ходу LastDistSq «сбрасывается» (новая цель, новый план); jitter-counter
                // сохраняется — если ракета в петле, она её закончит.
                missile.LastDistSq = -1f;
                PlanFrames(missile, aimAt, 0, frames);
            }
        }

        static MissileSubTurnFrames EnsureMissileFrames(string uid, Vector2 startPos, TurnAnimationData anim)
        {
            if (!anim.MissileFrames.TryGetValue(uid, out var frames))
            {
                frames = new MissileSubTurnFrames();
                for (int i = 0; i <= GalaxyData.SubTurnsPerTurn; i++)
                    frames.SubTurns[i] = startPos;
                anim.MissileFrames[uid] = frames;
            }
            return frames;
        }
    }
}
