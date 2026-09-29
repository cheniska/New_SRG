using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Config;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Politics;

namespace SRG.Combat
{
    public static class WeaponSystem
    {
        public const float ArmorAbsorptionCap = 0.75f;
        public const float EnergyShieldOverloadMult = 1.5f;
        public const float SlowStackCap = 0.75f;
        public const float ArmorDebuffCap = 200f;

        // Snowball-эффект: при атаке репутация атакующего владельца ко всем владельцам
        // планет звезды падает на StarReputationHitDelta (минус). Не применяется к своим
        // и к None/Mixed. Маленький модуль — поскольку эффект глобальный.
        public const int StarReputationHitDelta = 2;

        // Переиспользуемый буфер уникальных владельцев планет. WeaponSystem статичен и
        // вызывается из главного потока симуляции, гонок данных нет.
        private static readonly List<string> _uniqueOwnersBuf = new List<string>(8);

        /// <summary>
        /// Единая точка регистрации смерти цели. Для NPC — добавляет uid в anim.DeathUids
        /// (с защитой от дублей). Для игрока — вызывает <see cref="PlayerManager.KillPlayer"/>
        /// и добавляет uid в DeathUids только если игрок реально умер (Phoenix / Repair Droid
        /// и т.п. могут спасти и вернуть false). Возвращает <c>true</c>, если цель «считается
        /// мёртвой» (или это NPC), и <c>false</c>, если это игрок и его удалось спасти —
        /// в этом случае вызывающий должен откатить флаг TargetDestroyed.
        /// </summary>
        public static bool RegisterTargetDeath(
            ShipData target, PlayerDeathCause cause,
            string killerName, string killerOwner,
            TurnAnimationData anim,
            ShipData killer = null)
        {
            if (target == null) return true;
            if (target.IsPlayer)
            {
                bool died = PlayerManager.Instance != null
                    && PlayerManager.Instance.KillPlayer(cause, killerName, killerOwner);
                if (died && !(anim?.DeathUids.Contains(target.Uid) ?? false))
                    anim?.DeathUids.Add(target.Uid);
                if (died) EmitShipDeath(target, killer, cause);
                return died;
            }
            if (!(anim?.DeathUids.Contains(target.Uid) ?? false))
                anim?.DeathUids.Add(target.Uid);
            EmitShipDeath(target, killer, cause);
            return true;
        }

        private static void EmitShipDeath(ShipData victim, ShipData killer, PlayerDeathCause cause)
        {
            string tag = cause switch
            {
                PlayerDeathCause.Missile  => ShipDeathBus.CAUSE_MISSILE,
                PlayerDeathCause.Asteroid => ShipDeathBus.CAUSE_COLLISION, // столкновение — не «сбитие»
                _                         => ShipDeathBus.CAUSE_WEAPON
            };
            ShipDeathBus.Emit(victim, killer, tag);
        }

        public static CombatResult ProcessShot(
            ShipData attacker,
            ShipData target,
            string weaponSlotKey,
            ItemsConfig equipConfig,
            TurnAnimationData anim,
            int subTurn)
        {
            var result = new CombatResult();
            if (attacker == null || target == null || target.CurrentHull <= 0) return result;
            // Мёртвый стрелок не может выстрелить — защита от шотов от уже погибшего
            // корабля (например, если боевой код пытается отыграть запланированный
            // выстрел игрока после регистрации смерти).
            if (attacker.CurrentHull <= 0) return result;

            var weapon = EquipmentSystem.GetEquipped(attacker, weaponSlotKey);
            if (weapon == null || !weapon.IsWorking) return result;
            float ammo = weapon.GetParam("Ammo", -1f);
            if (ammo >= 0f && ammo < 0.5f) return result;

            var shotParams = BuildShotParams(attacker, target, weaponSlotKey, weapon);
            if (shotParams == null) return result;

            result.Hit = true;
            float baseDmg = Random.Range(shotParams.MinDmg, shotParams.MaxDmg);
            baseDmg = ApplyExecuteBonus(baseDmg, target, attacker);
            var (hullDmg, shieldDmg) = CalculateDamage(
                baseDmg, shotParams, target, attacker);

            result.ShieldDamage = shieldDmg;
            result.HullDamage = hullDmg;
            result.DamageDealt = hullDmg;
            int floor = shotParams.NonLethal ? 1 : 0;
            target.CurrentHull = Mathf.Max(floor, target.CurrentHull - Mathf.RoundToInt(hullDmg));
            if (shieldDmg > 0f && equipConfig != null)
                EquipmentSystem.ApplyShieldHitWear(target, equipConfig);

            ApplyHitConsequences(attacker, target);

            result.TargetDestroyed = target.CurrentHull <= 0;
            if (result.TargetDestroyed)
            {
                // Единственный кейс, где смерть может быть «отменена» — если игрок был спасён
                // (Phoenix и т.п.); в этом случае откатываем TargetDestroyed.
                if (!RegisterTargetDeath(target, PlayerDeathCause.Weapon, attacker?.Name, attacker?.Owner, anim, attacker))
                    result.TargetDestroyed = false;

                // Триггер Kill на атакующем: Локализатор взрывной волны копит LootPreserveBonus
                // для формируемого лут-контейнера. Само формирование — TODO (см. план п.21).
                if (result.TargetDestroyed)
                {
                    var killCtx = new TriggerContext
                    {
                        Event      = TriggerEvent.Kill,
                        DamageType = shotParams.DamageType.ToString(),
                    };
                    TriggerBus.Fire(attacker, killCtx);
                    // killCtx.LootPreserveBonus в будущем передаётся в LootDropService.
                }
            }

            if (shotParams.EquipDamage > 0f && Random.value < shotParams.EquipHitChance)
            {
                var equip = EquipmentSystem.GetRandomDamageableSlot(target, equipConfig);
                if (equip.item != null)
                {
                    equip.item.Durability = Mathf.Max(0, equip.item.Durability - Mathf.RoundToInt(shotParams.EquipDamage));
                    result.EquipmentHit = true;
                    result.EquipmentSlotHit = equip.slotKey;
                }
            }

            ApplyShotEffects(shotParams, attacker, target, result);
            if (equipConfig != null)
                EquipmentSystem.ApplyWeaponShotWear(attacker, weaponSlotKey, equipConfig);

            if (ammo > 0f)
            {
                weapon.Params["Ammo"] = ammo - 1f;
            }

            int shotDuration = Mathf.Max(1, Mathf.RoundToInt(weapon.GetParam("ShotDuration", 1f)));
            var weaponItemCfg = equipConfig?.GetItem(EquipmentCategory.Weapons, weapon.ItemId);
            anim?.Shots.Add(new ShotEvent
            {
                AttackerUid = attacker.Uid,
                TargetUid   = target.Uid,
                WeaponId    = weapon.ItemId,
                DamageDealt = Mathf.RoundToInt(hullDmg),
                SubTurn     = subTurn,
                ShotDuration = shotDuration,
                HitPattern  = shotParams.HitPattern,
                DamageType  = shotParams.DamageType,
                Visual      = weaponItemCfg?.Visual,
                HitEffect   = equipConfig?.GetHitEffect(shotParams.HitPattern.ToString(), shotParams.DamageType.ToString()),
            });

            return result;
        }

        public static void TickEffects(ShipData ship)
        {
            if (ship?.ActiveEffects == null || ship.ActiveEffects.Count == 0) return;

            // Криоколония: ускоренное убывание Slow-эффекта (быстро восстанавливаем скорость).
            // Значение Artefacts.SpeedRecoveryMult — целое число доп. ходов, которые снимаются
            // со Slow за один тик (1 = каждый ход Slow снимается «двойной» скорости).
            int slowExtraDecay = Mathf.Max(0, Mathf.RoundToInt(
                SRG.Equipment.StatBus.SumShipCategory(ship, SRG.Equipment.EquipmentCategory.Artefacts, "SpeedRecoveryMult")));

            for (int i = ship.ActiveEffects.Count - 1; i >= 0; i--)
            {
                var effect = ship.ActiveEffects[i];
                if (effect.TurnsLeft > 0)
                {
                    int dec = 1;
                    if (slowExtraDecay > 0 && effect.Type == CombatEffectType.Slow)
                        dec += slowExtraDecay;
                    effect.TurnsLeft = Mathf.Max(0, effect.TurnsLeft - dec);
                    if (effect.TurnsLeft <= 0)
                        ship.ActiveEffects.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Заглушка для совместимости — щит больше не имеет состояния (ёмкости/регенерации).
        /// Износ списывается с предмета щита в ApplyShieldHitWear, BlockPercent читается напрямую.
        /// </summary>
        public static void RegenerateShield(ShipData ship) { }

        /// <summary>Заглушка для совместимости — щит больше не имеет состояния.</summary>
        public static void RebuildShieldState(ShipData ship) { }

        /// <summary>
        /// Рассчитывает итоговый урон по корпусу и блокированный щитом урон.
        ///
        /// Порядок: уязвимость → броня → щит (BlockPercent с учётом ShieldPenetration) → корпус.
        /// </summary>
        public static (float hullDamage, float shieldBlocked) CalculateDamage(
            float baseDamage,
            WeaponShotParams shot,
            ShipData target,
            ShipData attacker = null)
        {
            // 1. Уязвимость корпуса к типу урона
            float vuln = GetHullSusceptibility(target, shot.DamageType);
            float afterVuln = baseDamage * vuln;

            // 1a. Ракетанг и подобные: снижение урона от ракет (только Homing-снарядов).
            // Ключ Hull.MissileDamageMult — дельта от 1.0, отрицательные значения уменьшают урон.
            if (shot.HitPattern == HitPattern.Homing)
            {
                float missileMod = EquipmentSystem.GetHullParam(target, "MissileDamageMult");
                if (!Mathf.Approximately(missileMod, 0f))
                    afterVuln *= Mathf.Max(0f, 1f + missileMod);
            }

            // 1b. Триггеры Hit: Тепловая губка («30% шанс обнулить энергоурон»), Фазовый рассеиватель (ReflectChance)
            // и подобные. TriggerBus обходит все SlotCode.Triggers артефактов цели.
            var hitCtx = new TriggerContext
            {
                Event      = TriggerEvent.Hit,
                DamageType = shot.DamageType.ToString(),
                Slot       = EquipmentCategory.Hull,
            };
            TriggerBus.Fire(target, hitCtx);
            if (hitCtx.NullifyDamage) return (0f, 0f);
            if (!Mathf.Approximately(hitCtx.DamageMult, 1f))
                afterVuln *= hitCtx.DamageMult;

            // Флаг IgnoreArmorAndShield: полностью обходит броню и щит, весь урон уходит в корпус.
            if (shot.IgnoreArmorAndShield)
                return (Mathf.Max(0f, afterVuln), 0f);

            // 2. Броня (с учётом дебаффа и пробития; кап = ArmorAbsorptionCap% от удара)
            float armorDebuff = GetTotalArmorDebuff(target);
            float armor = Mathf.Max(0f, EquipmentSystem.GetHullParam(target, "Armor") - armorDebuff);
            float armorAbsorption = Mathf.Clamp(
                Mathf.Max(0f, armor - shot.ArmorPenetration),
                0f, afterVuln * ArmorAbsorptionCap);
            float afterArmor = Mathf.Max(0f, afterVuln - armorAbsorption);

            // 3. Щит — плоский процент. ShieldPenetration снижает эффективный блок.
            float shieldBlocked = 0f;
            float afterShield = afterArmor;
            var shield = EquipmentSystem.GetEquipped(target, SlotKeys.Shield);
            if (shield != null && shield.IsWorking)
            {
                float blockNorm = Mathf.Clamp01(shield.GetParam("BlockPercent", 0f) / 100f);
                float effectiveBlock = Mathf.Max(0f, blockNorm - Mathf.Clamp01(shot.ShieldPenetration));
                afterShield = afterArmor * (1f - effectiveBlock);
                shieldBlocked = afterArmor - afterShield;
            }

            return (Mathf.Max(0f, afterShield), shieldBlocked);
        }

        public static WeaponShotParams BuildShotParams(
            ShipData attacker, ShipData target, string slotKey, ItemInstance weapon)
        {
            if (weapon == null) return null;

            var p = new WeaponShotParams
            {
                AttackerUid = attacker.Uid,
                TargetUid = target.Uid,
                WeaponSlotKey = slotKey,
                MinDmg = weapon.GetParam("MinDmg"),
                MaxDmg = weapon.GetParam("MaxDmg"),
                Range = weapon.GetParam("Range"),
                ArmorPenetration = weapon.GetParam("ArmorPenetration", weapon.GetParam("ArmorPierce")),
                ShieldPenetration = weapon.GetParam("ShieldPenetration") / 100f,
                EquipHitChance = weapon.GetParam("EquipmentHitChance"),
                EquipDamage = weapon.GetParam("EquipmentDamage"),
                Ammo = Mathf.RoundToInt(weapon.GetParam("Ammo", -1f)),
            };
            string dmgTypeStr = weapon.GetParamString("DamageType", "Kinetic");
            p.DamageType = ParseDamageType(dmgTypeStr);
            string patternStr = weapon.GetParamString("ShotPattern", weapon.GetParamString("HitPattern", "Point"));
            p.HitPattern = ParseHitPattern(patternStr);

            // Энергошунт и подобные: усиление урона по типу оружия. Bonus
            // "Weapons.EnergyDamageMult" — дельта от 1.0 (+0.6 → +60%). Cross-slot артефакта
            // уже вложил дельту в weapon.Params, здесь достаём и умножаем.
            if (p.DamageType == DamageType.Energy)
            {
                float energyMod = weapon.GetParam("EnergyDamageMult", 0f);
                if (!Mathf.Approximately(energyMod, 0f))
                {
                    float mul = Mathf.Max(0f, 1f + energyMod);
                    p.MinDmg *= mul;
                    p.MaxDmg *= mul;
                }
            }
            string effectsStr = weapon.GetParamString("Effects", "");
            if (!string.IsNullOrEmpty(effectsStr))
                p.Effects = ParseEffects(effectsStr);

            AppendArtifactEffects(attacker, p);
            EmbedService.AppendWeaponEffects(weapon, p.Effects);

            // Триггеры выстрела (Импульсный конденсатор — «каждый 3-й энерго-выстрел ×1.3»,
            // Дробитель — «Explosive ×1.2», Термолипучка — «AddWeaponEffect: HeatDamage»).
            var trigCtx = new TriggerContext
            {
                Event      = TriggerEvent.Fire,
                DamageType = p.DamageType.ToString(),
                Slot       = EquipmentCategory.Weapons,
            };
            TriggerBus.Fire(attacker, trigCtx);
            if (!Mathf.Approximately(trigCtx.DamageMult, 1f))
            {
                p.MinDmg *= trigCtx.DamageMult;
                p.MaxDmg *= trigCtx.DamageMult;
            }
            if (trigCtx.AddedWeaponEffects != null)
                foreach (var eff in trigCtx.AddedWeaponEffects)
                    p.Effects.Add(eff);

            // NoDamageDelta: MinDmg подтягивается к MaxDmg.
            if (EmbedService.HasWeaponFlag(weapon, EmbedWeaponFlags.NoDamageDelta))
                p.MinDmg = p.MaxDmg;
            // AmmoFree: не тратим боезапас — маркируем безлимитом.
            if (EmbedService.HasWeaponFlag(weapon, EmbedWeaponFlags.AmmoFree))
                p.Ammo = -1;
            p.IgnoreArmorAndShield = EmbedService.HasWeaponFlag(weapon, EmbedWeaponFlags.IgnoreArmorAndShield);
            p.NonLethal = EmbedService.HasWeaponFlag(weapon, EmbedWeaponFlags.NonLethal);

            return p;
        }

        private static float ApplyExecuteBonus(float baseDmg, ShipData target, ShipData attacker)
        {
            bool hasExecute = HasEffect(attacker, CombatEffectType.ExecuteBonus) ||
                              HasEffect(target, CombatEffectType.ExecuteBonus);
            if (!hasExecute || target.MaxHull <= 0) return baseDmg;

            float damageFraction = 1f - (float)target.CurrentHull / target.MaxHull;
            return baseDmg * (1f + 0.33f * damageFraction);
        }

        private static void ApplyShotEffects(
            WeaponShotParams shot, ShipData attacker, ShipData target, CombatResult result)
        {
            if (shot.Effects == null || shot.Effects.Count == 0) return;

            bool scannerCheck = CheckScannerDominance(attacker, target);

            foreach (var eff in shot.Effects)
            {
                if (Random.value > eff.Chance) continue;

                bool isTactical = eff.Type == CombatEffectType.BlockWeapon ||
                                  eff.Type == CombatEffectType.BlockDroid;
                if (isTactical && !scannerCheck) continue;

                switch (eff.Type)
                {
                    case CombatEffectType.Slow:
                        ApplyStackableEffect(target, eff.Type, eff.DurationTurns, eff.Magnitude, SlowStackCap);
                        break;

                    case CombatEffectType.ArmorDebuff:
                        ApplyStackableEffect(target, eff.Type, eff.DurationTurns, eff.Magnitude, ArmorDebuffCap);
                        break;

                    case CombatEffectType.Shutdown:
                        RemoveEffect(target, CombatEffectType.Shutdown);
                        target.ActiveEffects.Add(new ActiveCombatEffect(eff.Type, eff.DurationTurns, eff.Magnitude, attacker.Uid));
                        break;

                    case CombatEffectType.Drain:
                        float healed = result.HullDamage * eff.Magnitude;
                        attacker.CurrentHull = Mathf.Min(attacker.MaxHull,
                            attacker.CurrentHull + Mathf.RoundToInt(healed));
                        result.HealedByDrain += healed;
                        break;

                    case CombatEffectType.EngineDisable:
                        var engine = EquipmentSystem.GetEquipped(target, SlotKeys.Engine);
                        if (engine != null)
                            engine.Durability = Mathf.Max(0, engine.Durability - Mathf.RoundToInt(eff.Magnitude));
                        break;

                    case CombatEffectType.LootFocus:
                        ApplyOrRefreshEffect(attacker, eff.Type, eff.DurationTurns, eff.Magnitude, attacker.Uid);
                        break;

                    default:
                        ApplyOrRefreshEffect(target, eff.Type, eff.DurationTurns, eff.Magnitude, attacker.Uid);
                        break;
                }

                result.EffectsApplied.Add(eff.Type);
            }
        }

        private static void ApplyStackableEffect(
            ShipData target, CombatEffectType type, int turns, float magnitude, float cap)
        {
            float current = GetTotalEffectMagnitude(target, type);
            float toAdd = Mathf.Min(magnitude, cap - current);
            if (toAdd <= 0f) return;
            target.ActiveEffects.Add(new ActiveCombatEffect(type, turns, toAdd));
        }
        private static void ApplyOrRefreshEffect(
            ShipData ship, CombatEffectType type, int turns, float magnitude, string sourceUid)
        {
            foreach (var e in ship.ActiveEffects)
            {
                if (e.Type == type && e.SourceUid == sourceUid)
                {
                    e.TurnsLeft = Mathf.Max(e.TurnsLeft, turns);
                    return;
                }
            }
            ship.ActiveEffects.Add(new ActiveCombatEffect(type, turns, magnitude, sourceUid));
        }

        private static void RemoveEffect(ShipData ship, CombatEffectType type)
        {
            ship.ActiveEffects.RemoveAll(e => e.Type == type);
        }

        public static float GetTotalEffectMagnitude(ShipData ship, CombatEffectType type)
        {
            if (ship?.ActiveEffects == null) return 0f;
            float total = 0f;
            foreach (var e in ship.ActiveEffects)
                if (e.Type == type) total += e.Magnitude;
            return total;
        }

        public static bool HasEffect(ShipData ship, CombatEffectType type)
        {
            if (ship?.ActiveEffects == null) return false;
            foreach (var e in ship.ActiveEffects)
                if (e.Type == type) return true;
            return false;
        }

        public static float GetSpeedMultiplierFromEffects(ShipData ship)
        {
            if (ship?.ActiveEffects == null) return 1f;
            if (HasEffect(ship, CombatEffectType.Shutdown)) return 0f;
            float slow = GetTotalEffectMagnitude(ship, CombatEffectType.Slow);
            return Mathf.Max(0f, 1f - Mathf.Min(slow, SlowStackCap));
        }

        public static float GetHullSusceptibility(ShipData ship, DamageType dmgType)
        {
            var hull = EquipmentSystem.GetEquipped(ship, SlotKeys.Hull);
            if (hull?.WeaponVulnerability == null) return 1f;
            string key = dmgType.ToString();
            return hull.WeaponVulnerability.TryGetValue(key, out float v) ? v : 1f;
        }
        private static float GetTotalArmorDebuff(ShipData ship) =>
            GetTotalEffectMagnitude(ship, CombatEffectType.ArmorDebuff);

        /// <summary>«Доминирование сканера»: сканер атакующего сильнее сканера цели.
        /// Используется как гейт для тактических эффектов (BlockWeapon/BlockDroid) —
        /// более слабый сканер не пробивает защиту цели.</summary>
        private static bool CheckScannerDominance(ShipData attacker, ShipData target)
        {
            float atkPow = EquipmentSystem.GetScannerPower(attacker);
            float tgtPow = EquipmentSystem.GetScannerPower(target);
            return atkPow > tgtPow;
        }

        private static void AppendArtifactEffects(ShipData attacker, WeaponShotParams shot)
        {
            foreach (var slotKey in attacker.Equipment.GetOccupiedSlotsOfCategory(EquipmentCategory.Artefacts))
            {
                string uid = attacker.Equipment.GetItemUid(slotKey);
                if (uid == null || !attacker.AllItems.TryGetValue(uid, out var art) || !art.IsWorking) continue;

                string artEffects = art.GetParamString("ShotEffects", "");
                if (!string.IsNullOrEmpty(artEffects))
                    shot.Effects.AddRange(ParseEffects(artEffects));
            }
        }

        // GetRandomDamageableSlot перенесён в EquipmentSystem (см. EquipmentSystem.GetRandomDamageableSlot).

        /// <summary>
        /// Применяет урон от ракеты к цели с учётом всех боевых расчётов.
        /// Вызывается из MissileSystem.TickMissiles при попадании.
        /// </summary>
        public static CombatResult ApplyMissileImpact(
            ActiveMissile missile,
            ShipData target,
            ShipData attacker,
            ItemsConfig equipConfig)
        {
            var result = new CombatResult();
            if (target == null || target.CurrentHull <= 0) return result;

            var shot = new WeaponShotParams
            {
                AttackerUid       = missile.AttackerUid,
                TargetUid         = missile.TargetUid,
                WeaponSlotKey     = missile.WeaponSlotKey,
                MinDmg            = missile.MinDmg,
                MaxDmg            = missile.MaxDmg,
                ArmorPenetration  = missile.ArmorPenetration,
                ShieldPenetration = missile.ShieldPenetration,
                EquipHitChance    = missile.EquipHitChance,
                EquipDamage       = missile.EquipDamage,
                DamageType        = missile.DamageType,
                HitPattern        = HitPattern.Homing,
                Effects           = missile.Effects ?? new List<WeaponEffect>(),
            };

            result.Hit = true;
            float baseDmg = Random.Range(shot.MinDmg, shot.MaxDmg);
            baseDmg = ApplyExecuteBonus(baseDmg, target, attacker);
            var (hullDmg, shieldDmg) = CalculateDamage(baseDmg, shot, target, attacker);

            result.HullDamage  = hullDmg;
            result.ShieldDamage = shieldDmg;
            result.DamageDealt = hullDmg;
            int mFloor = shot.NonLethal ? 1 : 0;
            target.CurrentHull = Mathf.Max(mFloor, target.CurrentHull - Mathf.RoundToInt(hullDmg));
            if (shieldDmg > 0f && equipConfig != null)
                EquipmentSystem.ApplyShieldHitWear(target, equipConfig);

            ApplyHitConsequences(attacker, target);

            result.TargetDestroyed = target.CurrentHull <= 0;

            if (shot.EquipDamage > 0f && Random.value < shot.EquipHitChance)
            {
                var equip = EquipmentSystem.GetRandomDamageableSlot(target, equipConfig);
                if (equip.item != null)
                {
                    equip.item.Durability = Mathf.Max(0, equip.item.Durability - Mathf.RoundToInt(shot.EquipDamage));
                    result.EquipmentHit     = true;
                    result.EquipmentSlotHit = equip.slotKey;
                }
            }

            ApplyShotEffects(shot, attacker, target, result);
            return result;
        }

        public static DamageType ParseDamageType(string s)
        {
            return s switch
            {
                "Kinetic" or "kinetic" => DamageType.Kinetic,
                "Explosive" or "explosive" or "Missile" or "missile" => DamageType.Explosive,
                "Energy" or "energy" => DamageType.Energy,
                _ => DamageType.Kinetic
            };
        }

        public static HitPattern ParseHitPattern(string s)
        {
            return s switch
            {
                "Point" => HitPattern.Point,
                "Piercing" => HitPattern.Piercing,
                "Shotgun" => HitPattern.Shotgun,
                "Ricochet" => HitPattern.Ricochet,
                "Chain" => HitPattern.Chain,
                "AoE" => HitPattern.AoE,
                "PointAoE" => HitPattern.PointAoE,
                "Falloff" => HitPattern.Falloff,
                "Beam" => HitPattern.Beam,
                "Homing" => HitPattern.Homing,
                "Mine" => HitPattern.Mine,
                _ => HitPattern.Point
            };
        }

        /// <summary>
        /// Реакция жертвы и мира на попадание:
        /// 1) запоминаем агрессора (LastAttackerUid + Turn) — NPC будут отвечать;
        /// 2) накладываем личностную месть на отношение жертва→агрессор;
        /// 3) snowball: репутация Owner атакующего падает у всех Owner планет звезды.
        /// </summary>
        private static void ApplyHitConsequences(ShipData attacker, ShipData target)
        {
            if (attacker == null || target == null) return;

            int currentTurn = GalaxyManager.Instance?.GeneratedGalaxy?.CurrentTurn ?? 0;
            target.LastAttackerUid = attacker.Uid;
            target.LastAttackerTurn = currentTurn;

            target.Personality?.ApplyHostilityPenalty(attacker.Uid);

            // Маскировка: атака по кораблю расы X мгновенно раскрывает эту расу. Для той расы
            // маска больше не работает до её сброса (см. DisguiseService.TryDetect).
            if (attacker.Disguise != null && !string.IsNullOrEmpty(target.Race))
                SRG.Ships.Disguise.DisguiseService.TryDetect(attacker, target.Race);

            var rel = OwnerRaceRelationsManager.Instance;
            var star = target.CurrentStar;
            if (star == null && !string.IsNullOrEmpty(target.CurrentStarUid))
                GalaxyManager.Instance?.GeneratedGalaxy?.StarsMap.TryGetValue(target.CurrentStarUid, out star);
            if (rel != null && star != null && !string.IsNullOrEmpty(attacker.Owner))
            {
                _uniqueOwnersBuf.Clear();
                for (int i = 0; i < star.Planets.Count; i++)
                {
                    string owner = star.Planets[i].Owner;
                    if (string.IsNullOrEmpty(owner) || owner == attacker.Owner) continue;
                    if (_uniqueOwnersBuf.Contains(owner)) continue;
                    _uniqueOwnersBuf.Add(owner);
                    rel.AdjustOwnerRelation(attacker.Owner, owner, -StarReputationHitDelta);
                }
            }
        }

        public static List<WeaponEffect> ParseEffects(string effectsStr)
        {
            var result = new List<WeaponEffect>();
            if (string.IsNullOrEmpty(effectsStr)) return result;

            string cleaned = effectsStr.Replace("[", "").Replace("]", "")
                                       .Replace("\"", "").Replace("'", "").Trim();

            foreach (var token in cleaned.Split(','))
            {
                string t = token.Trim();
                if (string.IsNullOrEmpty(t)) continue;

                var parts = t.Split(':');
                if (parts.Length < 1) continue;

                if (!System.Enum.TryParse(parts[0].Trim(), out CombatEffectType effectType)) continue;

                var eff = new WeaponEffect
                {
                    Type = effectType,
                    DurationTurns = parts.Length > 1 && int.TryParse(parts[1].Trim(), out int d) ? d : -1,
                    Magnitude = parts.Length > 2 && float.TryParse(parts[2].Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float m) ? m : 1f,
                    Chance = parts.Length > 3 && float.TryParse(parts[3].Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float c) ? c : 1f,
                };
                result.Add(eff);
            }
            return result;
        }
    }
}
