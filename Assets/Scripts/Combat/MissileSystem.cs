using System;
using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.Ships.Movement;
using SRG.UI.Screens;
using SRG.Utils;

namespace SRG.Combat
{
    /// <summary>
    /// Управляет запуском, движением и детонацией активных ракет (HitPattern.Homing).
    /// Ракеты — это персистентные сущности в StarData.ActiveMissiles, которые
    /// преследуют цель между сабтёрнами и сохраняются между днями.
    ///
    /// Модель полёта (см. docs/modules/missiles.md):
    ///   • залп сходит с «направляющих», разнесённых поперёк корпуса, веером
    ///     фиксированной ширины SpreadDeg;
    ///   • в ход запуска ракета идёт по прямой (разгонный участок), затем —
    ///     упреждающее наведение на точку встречи с ограничением угловой скорости;
    ///   • если цель оказалась позади и ближе радиуса разворота, ракета «выносит»
    ///     петлю: летит прямо, пока не наберёт дистанцию для разворота;
    ///   • если цель уходит несколько ходов подряд — захват теряется, ракета
    ///     самоликвидируется;
    ///   • головка самонаведения (AutoReacquire) ищет новую цель в конусе перед носом.
    /// </summary>
    public static class MissileSystem
    {
        const float HitRadius = 0.15f;        // запас сверх радиуса коллизии корабля
        const float DefaultShipRadius = 0.35f; // фолбэк, если нет данных о корпусе
        const int   MaxSalvo  = 72;
        /// <summary>Во сколько радиусов разворота нужно отойти от цели, прежде чем начинать петлю.</summary>
        const float ExtendRadiusFactor = 2f;
        /// <summary>Потолок времени упреждения, в ходах: дальше прогноз позиции цели бессмыслен.</summary>
        const float MaxLeadTurns = 1.5f;

        /// <summary>
        /// Запускает залп ракет. Ракеты сходят с направляющих, равномерно разнесённых
        /// поперёк курса стрелка, и расходятся веером шириной SpreadDeg (крайние — по краям веера,
        /// одиночная ракета — строго по курсу). Боезапас и износ оружия списываются один раз за залп.
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

            // Триггеры залпа (Скоростная подача — «15% шанс двойного залпа»). ExtraSalvos прибавляется
            // к базовому размеру залпа; MaxSalvo продолжает капить.
            var trigCtx = new SRG.Equipment.TriggerContext
            {
                Event = SRG.Equipment.TriggerEvent.MissileFire,
                Slot  = EquipmentCategory.Weapons,
            };
            SRG.Equipment.TriggerBus.Fire(attacker, trigCtx);
            if (trigCtx.ExtraSalvos > 0)
                salvo = Mathf.Clamp(salvo + trigCtx.ExtraSalvos * Mathf.Max(1, mc?.SalvoCount ?? 1), 1, MaxSalvo);

            // Ракеты вылетают по курсу корабля; при неизвестном курсе — в сторону цели.
            float baseAngle = !float.IsNaN(attacker.CurrentHeading)
                ? attacker.CurrentHeading
                : Angles.Toward(attacker.Position, target.Position, fallback: Mathf.PI * 0.5f);
            Vector2 forward = Angles.Dir(baseAngle);
            Vector2 lateral = new Vector2(-forward.y, forward.x);

            float spreadRad   = (mc?.SpreadDeg ?? 24f) * Mathf.Deg2Rad;
            float railSpacing = mc?.RailSpacing ?? 0.06f;
            float noseOffset  = mc?.NoseOffset  ?? 0.05f;

            for (int i = 0; i < salvo; i++)
            {
                // u ∈ [-1..1]: позиция ракеты в залпе от левого края к правому.
                float u = salvo == 1 ? 0f : (2f * i / (salvo - 1)) - 1f;
                float angle = baseAngle + u * spreadRad * 0.5f;
                Vector2 spawnPos = attacker.Position
                                 + forward * noseOffset
                                 + lateral * (u * railSpacing * (salvo - 1) * 0.5f);
                CreateMissile(attacker, target, slotKey, weapon, mc, Angles.Dir(angle), spawnPos, subTurn, star, equipConfig, anim);
            }

            // Боезапас и износ — один раз на залп.
            float ammo = weapon.GetParam("Ammo", -1f);
            if (ammo > 0f) weapon.Params["Ammo"] = ammo - 1f;
            if (equipConfig != null)
                EquipmentSystem.ApplyWeaponShotWear(attacker, slotKey, equipConfig);
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

            // Ракета наследует часть скорости носителя. С разгонным двигателем (SpeedRampPerTurn > 0)
            // стартует с половины крейсерской и дальше разгоняется; без него — сразу крейсерская.
            float attackerSpeed = Mathf.Max(0f, SRUnits.ToWorld(attacker.ActualSpeed));
            float startSpeed = (speedRamp > 0f ? speedMax * 0.5f : speedMax) + attackerSpeed * inertiaFactor;
            // Launch-фаза: ракета должна сразу же оторваться от стрелка, иначе визуально она
            // «висит под кораблём» (особенно медленные торпеды, у которых базовый Speed
            // сравним с скоростью атакующего). На остаток дня запуска стартовая скорость
            // умножается на launchMul; со следующего хода (LaunchPhase=false) используется
            // обычный Speed/SpeedMax.
            float launchStep = startSpeed * Mathf.Max(1f, launchMul);

            var missile = new ActiveMissile
            {
                Uid           = Guid.NewGuid().ToString(),
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
                CurrentHeading  = Angles.Of(launchDir),
                TurnRadPerTurn  = turnDeg * Mathf.Deg2Rad,
                ReturnsOnTargetDeath = returnsOnDeath,
                MaxRecedingTurns     = mc?.MaxRecedingTurns ?? 2,
                OvershootExtend      = mc?.OvershootExtend  ?? true,
                AutoReacquire        = mc?.AutoReacquire    ?? false,
                ReacquireRadius      = mc?.ReacquireRadius  ?? 5f,
                SeekerConeRad        = (mc?.SeekerConeDeg   ?? 120f) * Mathf.Deg2Rad,
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
            ObserveTarget(missile, target.Position);
            PlanFrames(missile, target.Position, Vector2.zero, missile.LaunchSubTurn - 1, frames);
            star.ActiveMissiles.Add(missile);
        }

        /// <summary>
        /// Двигает все активные ракеты к цели. Вызывается в конце каждого CombatSubTurn.
        /// Сама траектория уже посчитана заранее в PlanFrames (на старте дня в
        /// InitMissileFrames либо в момент создания/смены цели) — здесь только
        /// читаем кадр, обновляем heading, считаем swept-попадание и
        /// при необходимости перенацеливаем головку.
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

                // Головка самонаведения ищет новую цель, если старая потеряна.
                // Не работает на разгонном участке и для coast/return.
                if (missile.AutoReacquire
                    && !missile.LaunchPhase
                    && !missile.IsReturning
                    && !missile.TargetDeadCoasting
                    && !targetAlive)
                {
                    var reacquired = TryReacquireTarget(missile, star, attacker);
                    if (reacquired != null)
                    {
                        missile.TargetUid = reacquired.Uid;
                        missile.RecedingTurns = 0;
                        target = reacquired;
                        targetAlive = true;
                        ObserveTarget(missile, target.Position);
                        if (frames != null) PlanFrames(missile, target.Position, Vector2.zero, subTurn - 1, frames);
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
                        ObserveTarget(missile, target.Position);
                        if (frames != null) PlanFrames(missile, target.Position, Vector2.zero, subTurn - 1, frames);
                        GameConsoleController.AddEntry(
                            $"[Торпеда] Цель уничтожена — торпеда возвращается к {attacker.Name}.");
                    }
                    else
                    {
                        // Обычная ракета: запоминаем точку смерти цели и долетаем туда. Без этого
                        // вся «хвостовая» часть залпа взрывалась бы в воздухе в момент гибели цели.
                        missile.TargetDeadCoasting = true;
                        missile.CoastTargetPos = target != null ? target.Position : missile.Position;
                        if (frames != null && !missile.LaunchPhase)
                            PlanFrames(missile, missile.CoastTargetPos, Vector2.zero, subTurn - 1, frames);
                    }
                }

                // Возвращающаяся торпеда потеряла стрелявшего — взрыв в пустоте.
                if (missile.IsReturning && !targetAlive)
                {
                    ExplodeMissile(missile, subTurn, equipConfig, anim);
                    star.ActiveMissiles.RemoveAt(i);
                    continue;
                }

                // Берём заранее посчитанную позицию для этого сабтёрна.
                Vector2 newPos = frames != null ? frames.SubTurns[subTurn] : missile.Position;

                // Обновляем CurrentHeading из фактического шага — нужно для отрисовки
                // спрайта и для стартового heading при перепланировании следующего дня.
                Vector2 step = newPos - missile.Position;
                missile.CurrentHeading = Angles.Of(step, missile.CurrentHeading);

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
        /// Шаг = один сабтёрн. В начале каждого хода план строится заново по актуальной позиции цели
        /// (см. InitMissileFrames). Препятствия не учитываются — ракеты летят сквозь.
        ///
        /// LaunchPhase=true → разгонный участок: прямая по CurrentHeading с шагом LaunchSpeed.
        /// LaunchPhase=false → наведение:
        ///   • целимся в упреждённую точку: targetPos + targetVel × t, где t — время подлёта
        ///     на текущей скорости (не более MaxLeadTurns);
        ///   • поворот ограничен TurnRadPerTurn;
        ///   • «вынос петли» (OvershootExtend): если цель сзади и ближе ExtendRadiusFactor радиусов
        ///     разворота, ракета держит курс, пока не отойдёт на дистанцию, с которой успеет развернуться;
        ///   • скорость подтягивается к SpeedMax на SpeedRampPerTurn за ход.
        /// </summary>
        /// <param name="targetVel">Скорость цели, мировых единиц за сабтёрн (ноль — цель неподвижна).</param>
        static void PlanFrames(
            ActiveMissile missile,
            Vector2 targetPos,
            Vector2 targetVel,
            int fromSubTurn,
            MissileSubTurnFrames frames)
        {
            int last = GalaxyData.SubTurnsPerTurn;
            if (fromSubTurn < 0) fromSubTurn = 0;
            if (fromSubTurn > last) return;

            Vector2 cur = missile.Position;
            float heading = missile.CurrentHeading;
            frames.SubTurns[fromSubTurn] = cur;

            bool guided = !missile.LaunchPhase && missile.TurnRadPerTurn > 1e-5f;
            float speed = missile.LaunchPhase && missile.LaunchSpeed > 0f ? missile.LaunchSpeed : missile.Speed;
            float maxTurn = missile.TurnRadPerTurn / last;
            float accel = missile.SpeedRampPerTurn / last;

            for (int s = fromSubTurn + 1; s <= last; s++)
            {
                if (guided)
                {
                    if (accel > 0f) speed = Mathf.MoveTowards(speed, missile.SpeedMax, accel);
                    heading = GuidanceHeading(missile, cur, heading, speed, maxTurn,
                                              targetPos + targetVel * (s - fromSubTurn), targetVel);
                }
                cur += Angles.Dir(heading) * speed;
                frames.SubTurns[s] = cur;
            }

            if (guided) missile.Speed = speed;
        }

        /// <summary>Курс ракеты на следующий сабтёрн при наведении на движущуюся точку.</summary>
        static float GuidanceHeading(ActiveMissile missile, Vector2 pos, float heading, float speed,
                                     float maxTurn, Vector2 targetPos, Vector2 targetVel)
        {
            Vector2 toTarget = targetPos - pos;
            float dist = toTarget.magnitude;
            if (dist < 1e-4f || speed < 1e-6f) return heading;

            // Упреждение: где будет цель к моменту подлёта.
            float leadSteps = Mathf.Min(dist / speed, MaxLeadTurns * GalaxyData.SubTurnsPerTurn);
            Vector2 aimPoint = targetPos + targetVel * leadSteps;
            float desired = Angles.Toward(pos, aimPoint, heading);

            if (missile.OvershootExtend && maxTurn > 1e-6f)
            {
                // Радиус разворота на текущей скорости. Цель сзади и слишком близко —
                // доворот всё равно не успеет, поэтому летим прямо и набираем дистанцию.
                float turnRadius = speed / maxTurn;
                bool behind = Mathf.Abs(Angles.WrapPi(desired - heading)) > Mathf.PI * 0.5f;
                if (behind && dist < turnRadius * ExtendRadiusFactor) return heading;
            }
            return Angles.StepToward(heading, desired, maxTurn);
        }

        /// <summary>Запоминает позицию цели на начало плана — по двум наблюдениям подряд
        /// оценивается её скорость для упреждения.</summary>
        static void ObserveTarget(ActiveMissile missile, Vector2 targetPos)
        {
            missile.LastTargetPos = targetPos;
            missile.HasTargetFix = true;
        }

        /// <summary>
        /// Головка самонаведения: ищет враждебный стрелку корабль в радиусе ReacquireRadius
        /// и в конусе SeekerConeRad перед носом ракеты. Предпочтение — цели ближе к оси конуса
        /// и ближе по дистанции. Если никого нет — null (ракета продолжит по инерции).
        /// </summary>
        static ShipData TryReacquireTarget(ActiveMissile missile, StarData star, ShipData attacker)
        {
            if (star?.Ships == null) return null;
            float radiusSq = missile.ReacquireRadius * missile.ReacquireRadius;
            float halfCone = missile.SeekerConeRad * 0.5f;
            ShipData best = null;
            float bestScore = float.MaxValue;
            for (int j = 0; j < star.Ships.Count; j++)
            {
                var s = star.Ships[j];
                if (s == null || s.CurrentHull <= 0) continue;
                if (!string.IsNullOrEmpty(s.LandedOnShipUid)) continue; // пристыкован «внутри» носителя
                if (s.Uid == missile.AttackerUid) continue;

                Vector2 to = s.Position - missile.Position;
                float d2 = to.sqrMagnitude;
                if (d2 > radiusSq) continue;
                float off = Mathf.Abs(Angles.WrapPi(Angles.Of(to, missile.CurrentHeading) - missile.CurrentHeading));
                if (off > halfCone) continue;
                if (!IsHostileToShooter(missile, attacker, s)) continue;

                // Смещение от оси «удлиняет» дистанцию: цель на краю конуса вдвое «дальше».
                float score = Mathf.Sqrt(d2) * (1f + off / Mathf.Max(halfCone, 1e-3f));
                if (score < bestScore) { bestScore = score; best = s; }
            }
            return best;
        }

        /// <summary>Враждебен ли корабль стрелку. Если стрелок уже мёртв — по Owner/Race из самой ракеты.</summary>
        static bool IsHostileToShooter(ActiveMissile missile, ShipData attacker, ShipData other)
        {
            if (attacker != null) return Relations.AreHostile(attacker, other);
            var mgr = OwnerRaceRelationsManager.Instance;
            return mgr != null && mgr.AreHostile(missile.AttackerOwner, other.Owner, missile.AttackerRace, other.Race);
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
                GameConsoleController.AddEntry(
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
                GameConsoleController.AddEntry(
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

            GameConsoleController.AddEntry($"[Ракета] Цель уничтожена — снаряд взорвался в пустоте.");
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
                    GameConsoleController.AddEntry(
                        $"[Ракета] Ракета не достигла цели и взорвалась в космосе.");
                    // Срок жизни истёк в конце хода — сабтёрн смерти = последний.
                    if (anim != null) anim.MissileDeathUids[missile.Uid] = GalaxyData.SubTurnsPerTurn;
                    star.ActiveMissiles.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Инициализирует фреймы ракет в начале дня и планирует траекторию на этот ход.
        /// Здесь же ведётся учёт «потери захвата»: если цель отдалилась за прошедший ход
        /// MaxRecedingTurns раз подряд, ракета самоликвидируется. Никаких ShotEvent-ов для
        /// полёта не добавляет — визуал обеспечивает SystemViewManager.SpawnMissileVisual.
        /// </summary>
        public static void InitMissileFrames(StarData star, ItemsConfig equipConfig, TurnAnimationData anim)
        {
            for (int i = star.ActiveMissiles.Count - 1; i >= 0; i--)
            {
                var missile = star.ActiveMissiles[i];
                var frames = EnsureMissileFrames(missile.Uid, missile.Position, anim);

                if (missile.TargetDeadCoasting)
                {
                    // Coast-ракета летит к зафиксированной точке смерти цели.
                    PlanFrames(missile, missile.CoastTargetPos, Vector2.zero, 0, frames);
                    continue;
                }

                var target = FindShip(star, missile.TargetUid);
                if (target == null)
                {
                    // Цель потеряна между ходами — летим прямо, дальнейшая судьба
                    // (взрыв / возврат торпеды / перенацеливание) решится в TickMissiles.
                    PlanFrames(missile, missile.Position + Angles.Dir(missile.CurrentHeading), Vector2.zero, 0, frames);
                    continue;
                }

                Vector2 targetVel = Vector2.zero;
                if (missile.HasTargetFix)
                {
                    targetVel = (target.Position - missile.LastTargetPos) / GalaxyData.SubTurnsPerTurn;
                    if (UpdateLockLoss(missile, target.Position))
                    {
                        GameConsoleController.AddEntry("[Ракета] Цель уходит — захват потерян, снаряд самоликвидируется.");
                        ExplodeMissile(missile, 0, equipConfig, anim);
                        star.ActiveMissiles.RemoveAt(i);
                        continue;
                    }
                }
                ObserveTarget(missile, target.Position);
                missile.LastTargetDist = (target.Position - missile.Position).magnitude;
                PlanFrames(missile, target.Position, targetVel, 0, frames);
            }
        }

        /// <summary>Обновляет счётчик ходов, в которые цель отдалялась. True — захват потерян.</summary>
        static bool UpdateLockLoss(ActiveMissile missile, Vector2 targetPos)
        {
            if (missile.MaxRecedingTurns <= 0 || missile.IsReturning || missile.LastTargetDist < 0f) return false;
            float dist = (targetPos - missile.Position).magnitude;
            missile.RecedingTurns = dist > missile.LastTargetDist ? missile.RecedingTurns + 1 : 0;
            return missile.RecedingTurns >= missile.MaxRecedingTurns;
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
