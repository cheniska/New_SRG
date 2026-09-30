using System;
using System.Collections.Generic;
using UnityEngine;
using SRG.Combat;
using SRG.Galaxy;

namespace SRG.Equipment
{
    /// <summary>
    /// Универсальная шина событий для триггеров <see cref="EffectConfig.Triggers"/>.
    /// Место вызова из игровых систем — <see cref="Fire"/> с контекстом события. Шина обходит
    /// все установленные предметы корабля, каждому — источники бонусов (IntrinsicEmbed + SlotCode +
    /// Embeds + RuntimeBonuses), находит соответствующие событию триггеры, проверяет фильтры/шанс/
    /// EveryN и применяет <see cref="TriggerOp"/> к <see cref="TriggerContext"/>.
    ///
    /// Один триггер описывается декларативно (JSON) и не требует C#-обвязки для типового поведения
    /// (Импульсный конденсатор — «каждый 3-й энерговыстрел ×1.3»; Тепловая губка — «30% шанс обнулить энергоурон»;
    /// Форсажный стабилизатор — «износ двигателя при форсаже ×0.5» и т.п.).
    /// </summary>
    public static class TriggerBus
    {
        // Реестр операций: имя → обработчик. Пополняется в статическом ctor встроенными
        // операциями. Моды регистрируют свои через <see cref="RegisterOp"/>.
        private static readonly Dictionary<string, Action<TriggerContext, TriggerOp>> _ops = new();

        static TriggerBus()
        {
            RegisterOp(TriggerOps.DamageMult,      ApplyDamageMult);
            RegisterOp(TriggerOps.NullifyDamage,   ApplyNullifyDamage);
            RegisterOp(TriggerOps.ExtraSalvo,      ApplyExtraSalvo);
            RegisterOp(TriggerOps.WearMult,        ApplyWearMult);
            RegisterOp(TriggerOps.PreserveLoot,    ApplyPreserveLoot);
            RegisterOp(TriggerOps.AddWeaponEffect, ApplyAddWeaponEffect);
        }

        /// <summary>Зарегистрировать обработчик операции. Одновременно фиксирует имя в
        /// <see cref="EmbedRegistry"/> — валидатор перестаёт ругаться.</summary>
        public static void RegisterOp(string name, Action<TriggerContext, TriggerOp> handler)
        {
            if (string.IsNullOrEmpty(name) || handler == null) return;
            _ops[name] = handler;
            EmbedRegistry.RegisterTriggerOp(name);
        }

        /// <summary>Основная точка вызова из игровой системы. Обходит все установленные предметы
        /// <paramref name="ship"/>, находит совпадающие триггеры и применяет их операции к
        /// <paramref name="ctx"/>. Игровая система читает мутации из ctx после возврата.
        /// Ничего не делает, если ship=null или ctx=null.</summary>
        public static void Fire(ShipData ship, TriggerContext ctx)
        {
            if (ship?.Equipment == null || ctx == null || string.IsNullOrEmpty(ctx.Event)) return;

            foreach (var kv in ship.Equipment.Slots)
            {
                if (kv.Value == null) continue;
                if (!ship.AllItems.TryGetValue(kv.Value, out var carrier) || carrier == null) continue;
                // Сломанные предметы не эмитят свои SlotCode-триггеры (совпадает с логикой RecomputeCarrier).
                // IntrinsicEmbed и Embeds — работают всегда (это черта самой конструкции).
                foreach (var e in EmbedService.EnumerateBonusSources(carrier))
                {
                    if (e?.Triggers == null || e.Triggers.Count == 0) continue;
                    foreach (var trig in e.Triggers)
                        TryFire(ship, carrier, trig, ctx);
                }
            }
        }

        private static void TryFire(ShipData ship, ItemInstance carrier, TriggerSpec trig, TriggerContext ctx)
        {
            if (trig == null || trig.On != ctx.Event) return;
            if (!MatchFilter(trig.Filter, ctx)) return;

            // EveryN: инкремент до проверки шанса, чтобы счётчик был детерминирован.
            if (trig.EveryN > 0)
            {
                string ckey = !string.IsNullOrEmpty(trig.Id) ? trig.Id : (carrier.Uid + ":" + trig.On);
                carrier.TriggerCounters ??= new Dictionary<string, int>();
                carrier.TriggerCounters.TryGetValue(ckey, out var count);
                count++;
                carrier.TriggerCounters[ckey] = count;
                if (count % trig.EveryN != 0) return;
            }

            float chance = trig.Chance <= 0f ? 1f : trig.Chance;
            if (chance < 1f && UnityEngine.Random.value > chance) return;

            if (trig.Effects != null)
                foreach (var op in trig.Effects)
                {
                    if (op == null || string.IsNullOrEmpty(op.Op)) continue;
                    if (_ops.TryGetValue(op.Op, out var h)) h(ctx, op);
                }
        }

        private static bool MatchFilter(Dictionary<string, string> filter, TriggerContext ctx)
        {
            if (filter == null || filter.Count == 0) return true;
            foreach (var kv in filter)
            {
                if (!ctx.TryGetString(kv.Key, out var actual)) return false;
                if (!string.Equals(actual, kv.Value, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        // ── Встроенные операции ──────────────────────────────────────────────

        private static void ApplyDamageMult(TriggerContext ctx, TriggerOp op)
        {
            float mul = op.Value == 0f ? 1f : op.Value;
            ctx.DamageMult *= mul;
        }

        private static void ApplyNullifyDamage(TriggerContext ctx, TriggerOp op) => ctx.NullifyDamage = true;

        private static void ApplyExtraSalvo(TriggerContext ctx, TriggerOp op)
        {
            int extra = op.Value > 0 ? Mathf.RoundToInt(op.Value) : 1;
            ctx.ExtraSalvos += extra;
        }

        private static void ApplyWearMult(TriggerContext ctx, TriggerOp op)
        {
            float mul = op.Value == 0f ? 1f : op.Value;
            ctx.WearMult *= mul;
        }

        private static void ApplyPreserveLoot(TriggerContext ctx, TriggerOp op)
        {
            float bonus = op.Value == 0f ? 0.5f : op.Value;
            ctx.LootPreserveBonus += bonus;
        }

        private static void ApplyAddWeaponEffect(TriggerContext ctx, TriggerOp op)
        {
            ctx.AddedWeaponEffects ??= new List<WeaponEffect>();
            if (!Enum.TryParse<CombatEffectType>(op.GetString("Type"), out var type)) return;
            ctx.AddedWeaponEffects.Add(new WeaponEffect
            {
                Type = type,
                DurationTurns = op.GetInt("Duration", 1),
                Magnitude = op.GetFloat("Magnitude", 0f),
                Chance = op.GetFloat("Chance", 1f),
            });
        }
    }

    /// <summary>Имена встроенных операций триггеров. Расширяется модами через
    /// <see cref="TriggerBus.RegisterOp"/> — при регистрации имя автоматически попадает
    /// в <see cref="EmbedRegistry"/>.</summary>
    public static class TriggerOps
    {
        public const string DamageMult      = "DamageMult";
        public const string NullifyDamage   = "NullifyDamage";
        public const string ExtraSalvo      = "ExtraSalvo";
        public const string WearMult        = "WearMult";
        public const string PreserveLoot    = "PreserveLoot";
        public const string AddWeaponEffect = "AddWeaponEffect";
    }

    /// <summary>Контекст события триггера. Игровая система заполняет входные поля (Event, DamageType,
    /// Cause, Slot), а также in/out — мутируемые в ходе применения операций (DamageMult, WearMult,
    /// NullifyDamage, ExtraSalvos, LootPreserveBonus, AddedWeaponEffects). Один экземпляр — на одно
    /// событие; читатель применяет мутации после <see cref="TriggerBus.Fire"/>.</summary>
    public class TriggerContext
    {
        // ── Входные ──
        public string Event;
        public string DamageType;   // Kinetic/Explosive/Energy — для Filter{DamageType}
        public string Slot;         // Категория слота (Engine/Shield/…) — для Filter{Slot}
        public string Cause;        // Метка причины (Move/Hit/Shot/Turn/Forsage) — для Filter{Cause}

        // ── Мутации ──
        public float DamageMult      = 1f;
        public bool  NullifyDamage   = false;
        public int   ExtraSalvos     = 0;
        public float WearMult        = 1f;
        public float LootPreserveBonus = 0f;
        public List<WeaponEffect> AddedWeaponEffects;

        public bool TryGetString(string key, out string value)
        {
            switch (key)
            {
                case "DamageType": value = DamageType; return DamageType != null;
                case "Slot":       value = Slot;       return Slot != null;
                case "Cause":      value = Cause;      return Cause != null;
                default:           value = null;       return false;
            }
        }
    }
}
