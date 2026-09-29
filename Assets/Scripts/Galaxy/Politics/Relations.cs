using UnityEngine;
using SRG.Core;

namespace SRG.Galaxy.Politics
{
    /// <summary>
    /// Скриптовый фасад глобальной системы отношений. Работает поверх
    /// <see cref="OwnerRaceRelationsManager"/> + персональных дельт в GalaxyData.
    ///
    /// Все методы безопасны при null-сущностях и при отсутствующем менеджере.
    /// Эффективное отношение = clamp(фракционная база (Owner+Race) + персональная дельта по UID, 1..100).
    /// Категория: Hostile 1..20, Bad 21..40, Neutral 41..60, Good 61..80, Excellent 81..100.
    /// </summary>
    public static class Relations
    {
        private static OwnerRaceRelationsManager M => OwnerRaceRelationsManager.Instance;

        // ---------- Чтение ----------

        public static int Get(ShipData a, ShipData b)        => M?.GetRelation(a, b) ?? 50;
        public static int Get(ShipData a, PlanetData b)      => M?.GetRelation(a, b) ?? 50;
        public static int Get(PlanetData a, ShipData b)      => M?.GetRelation(b, a) ?? 50;
        public static int Get(PlanetData a, PlanetData b)    => M?.GetRelation(a, b) ?? 50;

        public static RelationLevel GetLevel(ShipData a, ShipData b)     => OwnerRaceRelationsManager.ToLevel(Get(a, b));
        public static RelationLevel GetLevel(ShipData a, PlanetData b)   => OwnerRaceRelationsManager.ToLevel(Get(a, b));
        public static RelationLevel GetLevel(PlanetData a, ShipData b)   => OwnerRaceRelationsManager.ToLevel(Get(a, b));
        public static RelationLevel GetLevel(PlanetData a, PlanetData b) => OwnerRaceRelationsManager.ToLevel(Get(a, b));

        public static bool AreHostile(ShipData a, ShipData b)     => M != null && M.AreHostile(a, b);
        public static bool AreHostile(ShipData a, PlanetData b)   => M != null && M.AreHostile(a, b);
        public static bool AreHostile(PlanetData a, ShipData b)   => M != null && M.AreHostile(b, a);
        public static bool AreHostile(PlanetData a, PlanetData b) => M != null && M.AreHostile(a, b);

        // Отношение «к игроку». Возвращает 50, если игрок не найден.
        public static int GetToPlayer(ShipData entity) => GetVsPlayer(entity?.Uid, entity?.Owner, entity?.Race);
        public static int GetToPlayer(PlanetData entity) => GetVsPlayer(entity?.Uid, entity?.Owner, entity?.Race);
        public static RelationLevel GetLevelToPlayer(ShipData entity) => OwnerRaceRelationsManager.ToLevel(GetToPlayer(entity));
        public static RelationLevel GetLevelToPlayer(PlanetData entity) => OwnerRaceRelationsManager.ToLevel(GetToPlayer(entity));

        private static int GetVsPlayer(string uid, string owner, string race)
        {
            if (M == null) return 50;
            var player = FindPlayer();
            if (player == null) return 50;
            return M.GetRelation(uid, owner, race, player.Uid, player.Owner, player.Race);
        }

        // ---------- Запись: персональная дельта ----------

        /// <summary>Установить эффективное отношение в конкретное значение (через подбор персональной дельты).</summary>
        public static void Set(ShipData a, ShipData b, int value)          => SetByUids(a?.Uid, a?.Owner, a?.Race, b?.Uid, b?.Owner, b?.Race, value);
        public static void Set(ShipData a, PlanetData b, int value)        => SetByUids(a?.Uid, a?.Owner, a?.Race, b?.Uid, b?.Owner, b?.Race, value);
        public static void Set(PlanetData a, ShipData b, int value)        => SetByUids(a?.Uid, a?.Owner, a?.Race, b?.Uid, b?.Owner, b?.Race, value);
        public static void Set(PlanetData a, PlanetData b, int value)      => SetByUids(a?.Uid, a?.Owner, a?.Race, b?.Uid, b?.Owner, b?.Race, value);

        /// <summary>Установить отношение в середину указанной категории.</summary>
        public static void SetLevel(ShipData a, ShipData b, RelationLevel lvl)        => Set(a, b, OwnerRaceRelationsManager.LevelToMidValue(lvl));
        public static void SetLevel(ShipData a, PlanetData b, RelationLevel lvl)      => Set(a, b, OwnerRaceRelationsManager.LevelToMidValue(lvl));
        public static void SetLevel(PlanetData a, ShipData b, RelationLevel lvl)      => Set(a, b, OwnerRaceRelationsManager.LevelToMidValue(lvl));
        public static void SetLevel(PlanetData a, PlanetData b, RelationLevel lvl)    => Set(a, b, OwnerRaceRelationsManager.LevelToMidValue(lvl));

        /// <summary>Сдвинуть отношение на delta пунктов (+/-). Меняет персональную дельту.</summary>
        public static void Adjust(ShipData a, ShipData b, int delta)       => M?.AdjustPersonalDelta(a?.Uid, b?.Uid, delta);
        public static void Adjust(ShipData a, PlanetData b, int delta)     => M?.AdjustPersonalDelta(a?.Uid, b?.Uid, delta);
        public static void Adjust(PlanetData a, ShipData b, int delta)     => M?.AdjustPersonalDelta(a?.Uid, b?.Uid, delta);
        public static void Adjust(PlanetData a, PlanetData b, int delta)   => M?.AdjustPersonalDelta(a?.Uid, b?.Uid, delta);

        /// <summary>Поднять отношение до minValue, если текущее ниже. Если текущее уже выше — не меняет.</summary>
        public static void RaiseTo(ShipData a, ShipData b, int minValue)     => RaiseToValue(a?.Uid, a?.Owner, a?.Race, b?.Uid, b?.Owner, b?.Race, minValue);
        public static void RaiseTo(ShipData a, PlanetData b, int minValue)   => RaiseToValue(a?.Uid, a?.Owner, a?.Race, b?.Uid, b?.Owner, b?.Race, minValue);
        public static void RaiseTo(PlanetData a, ShipData b, int minValue)   => RaiseToValue(a?.Uid, a?.Owner, a?.Race, b?.Uid, b?.Owner, b?.Race, minValue);
        public static void RaiseTo(PlanetData a, PlanetData b, int minValue) => RaiseToValue(a?.Uid, a?.Owner, a?.Race, b?.Uid, b?.Owner, b?.Race, minValue);

        /// <summary>Опустить отношение до maxValue, если текущее выше. Если текущее уже ниже — не меняет.</summary>
        public static void LowerTo(ShipData a, ShipData b, int maxValue)     => LowerToValue(a?.Uid, a?.Owner, a?.Race, b?.Uid, b?.Owner, b?.Race, maxValue);
        public static void LowerTo(ShipData a, PlanetData b, int maxValue)   => LowerToValue(a?.Uid, a?.Owner, a?.Race, b?.Uid, b?.Owner, b?.Race, maxValue);
        public static void LowerTo(PlanetData a, ShipData b, int maxValue)   => LowerToValue(a?.Uid, a?.Owner, a?.Race, b?.Uid, b?.Owner, b?.Race, maxValue);
        public static void LowerTo(PlanetData a, PlanetData b, int maxValue) => LowerToValue(a?.Uid, a?.Owner, a?.Race, b?.Uid, b?.Owner, b?.Race, maxValue);

        /// <summary>Поднять отношение хотя бы до нижней границы указанной категории.</summary>
        public static void RaiseToLevel(ShipData a, ShipData b, RelationLevel lvl)        => RaiseTo(a, b, OwnerRaceRelationsManager.LevelMinValue(lvl));
        public static void RaiseToLevel(ShipData a, PlanetData b, RelationLevel lvl)      => RaiseTo(a, b, OwnerRaceRelationsManager.LevelMinValue(lvl));
        public static void RaiseToLevel(PlanetData a, ShipData b, RelationLevel lvl)      => RaiseTo(a, b, OwnerRaceRelationsManager.LevelMinValue(lvl));
        public static void RaiseToLevel(PlanetData a, PlanetData b, RelationLevel lvl)    => RaiseTo(a, b, OwnerRaceRelationsManager.LevelMinValue(lvl));

        /// <summary>Опустить отношение хотя бы до верхней границы указанной категории.</summary>
        public static void LowerToLevel(ShipData a, ShipData b, RelationLevel lvl)        => LowerTo(a, b, OwnerRaceRelationsManager.LevelMaxValue(lvl));
        public static void LowerToLevel(ShipData a, PlanetData b, RelationLevel lvl)      => LowerTo(a, b, OwnerRaceRelationsManager.LevelMaxValue(lvl));
        public static void LowerToLevel(PlanetData a, ShipData b, RelationLevel lvl)      => LowerTo(a, b, OwnerRaceRelationsManager.LevelMaxValue(lvl));
        public static void LowerToLevel(PlanetData a, PlanetData b, RelationLevel lvl)    => LowerTo(a, b, OwnerRaceRelationsManager.LevelMaxValue(lvl));

        // ---------- Внутренние ----------

        private static void SetByUids(string uidA, string ownerA, string raceA,
                                      string uidB, string ownerB, string raceB, int value)
        {
            if (M == null || string.IsNullOrEmpty(uidA) || string.IsNullOrEmpty(uidB)) return;
            int target = Mathf.Clamp(value, OwnerRaceRelationsManager.MIN_VALUE, OwnerRaceRelationsManager.MAX_VALUE);
            int baseRel = M.GetFactionRelation(ownerA, ownerB, raceA, raceB);
            M.SetPersonalDelta(uidA, uidB, target - baseRel);
        }

        private static void RaiseToValue(string uidA, string ownerA, string raceA,
                                         string uidB, string ownerB, string raceB, int minValue)
        {
            if (M == null || string.IsNullOrEmpty(uidA) || string.IsNullOrEmpty(uidB)) return;
            int current = M.GetRelation(uidA, ownerA, raceA, uidB, ownerB, raceB);
            if (current >= minValue) return;
            SetByUids(uidA, ownerA, raceA, uidB, ownerB, raceB, minValue);
        }

        private static void LowerToValue(string uidA, string ownerA, string raceA,
                                         string uidB, string ownerB, string raceB, int maxValue)
        {
            if (M == null || string.IsNullOrEmpty(uidA) || string.IsNullOrEmpty(uidB)) return;
            int current = M.GetRelation(uidA, ownerA, raceA, uidB, ownerB, raceB);
            if (current <= maxValue) return;
            SetByUids(uidA, ownerA, raceA, uidB, ownerB, raceB, maxValue);
        }

        private static ShipData FindPlayer() => PlayerManager.Instance?.GetOrFindPlayerShip();
    }
}
