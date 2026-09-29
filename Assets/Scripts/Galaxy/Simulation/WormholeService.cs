using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Core;
using SRG.Utils;

namespace SRG.Galaxy.Simulation
{
    /// <summary>
    /// Точка входа/выхода червоточины: звезда + опциональная позиция.
    /// <see cref="Position"/> == null означает «случайная точка внутри системы». Для входа
    /// (source) — точка выбирается в момент спавна; для выхода (target) — при каждом входе
    /// корабля (соответствует пустому <see cref="WormholeData.FixedTargetPosition"/>).
    /// StarData → WormholeLocation неявно приводится: <c>Spawn(myStar)</c> эквивалентно
    /// <c>Spawn(new WormholeLocation(myStar))</c>.
    /// </summary>
    public readonly struct WormholeLocation
    {
        public readonly StarData Star;
        public readonly Vector2? Position;

        public WormholeLocation(StarData star, Vector2 position) { Star = star; Position = position; }
        public WormholeLocation(StarData star) { Star = star; Position = null; }

        public static implicit operator WormholeLocation(StarData star) => new WormholeLocation(star);
        public static implicit operator WormholeLocation(Point p) => new WormholeLocation(p.Star, p.Position);
    }

    /// <summary>
    /// Скриптовый API для червоточин. Позиция входа/выхода передаётся через <see cref="WormholeLocation"/>
    /// (implicit-конверсия из <see cref="StarData"/> и <see cref="Point"/>) — используйте
    /// <see cref="Positions.Near"/>/<see cref="Positions.At"/> для их создания.
    ///
    /// Червоточины хранятся в <see cref="StarData.Wormholes"/>. Активных обычно ≤ 6, поиск по UID —
    /// линейный проход по звёздам, стоимость ничтожна.
    ///
    /// Естественный дневной спавн (<see cref="WormholeSystem"/>) вызывает <see cref="Spawn"/> без
    /// аргументов — тот сам подбирает случайные вход/цель по настройкам <see cref="GameSettings"/>.
    /// </summary>
    public static class WormholeService
    {

        // ── Единая Spawn ──────────────────────────────────────────────────────────

        /// <summary>
        /// Универсальный спавн:
        /// <list type="bullet">
        ///   <item><c>Spawn()</c> — фулл-рандом: случайная звезда-источник, случайная целевая
        ///     звезда в дистанции по настройкам, позиции авто-выбираются.</item>
        ///   <item><c>Spawn(myStar)</c> — источник в этой звезде (случ. позиция), цель случайная.</item>
        ///   <item><c>Spawn(NearShip(Player(), 100))</c> — источник в 100 ед. от игрока.</item>
        ///   <item><c>Spawn(NearPlanet(p,5), NearStar(target,3))</c> — фиксированы оба конца.</item>
        /// </list>
        /// Правила:
        ///   • <paramref name="source"/> == null → случайный источник по всей галактике.
        ///   • <paramref name="target"/> == null → случайная целевая звезда в дистанции
        ///     [Wormhole_MinDistancePc, Wormhole_MaxDistancePc], точка выхода — случайная на каждый вход.
        ///   • Если <see cref="WormholeLocation.Position"/> у target == null — точка выхода
        ///     тоже случайная на каждый вход; иначе — фиксирована (пишется в FixedTargetPosition).
        ///   • <paramref name="lifetimeTurns"/> == null → берётся из настроек.
        /// </summary>
        public static WormholeData Spawn(
            WormholeLocation? source = null,
            WormholeLocation? target = null,
            int? lifetimeTurns = null,
            WormholeGraphics graphics = null,
            string targetGalaxyId = null)
        {
            var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
            if (galaxy == null)
            {
                Debug.LogWarning("[WormholeService] Spawn: нет активной галактики.");
                return null;
            }
            var settings = GalaxyManager.Instance?.Settings;
            if (settings == null)
            {
                Debug.LogWarning("[WormholeService] Spawn: нет GameSettings.");
                return null;
            }

            // 1. Резолвим источник (звезда + конкретная точка).
            StarData sourceStar = source?.Star ?? PickRandomStar(galaxy);
            if (sourceStar == null)
            {
                Debug.LogWarning("[WormholeService] Spawn: нет звёзд в галактике.");
                return null;
            }
            Vector2 sourcePos = source?.Position ?? Positions.Near(sourceStar); // -1 → 40..90% радиуса

            // 2. Резолвим цель.
            bool crossGalaxy = !string.IsNullOrEmpty(targetGalaxyId);
            StarData targetStar = target?.Star;
            string targetStarUid;
            if (crossGalaxy)
            {
                // Скрипт должен явно указать target.Star.Uid при межгалактическом переходе
                // (проверка по StarsMap невозможна — другая галактика).
                targetStarUid = targetStar?.Uid;
                if (string.IsNullOrEmpty(targetStarUid))
                {
                    Debug.LogWarning("[WormholeService] Spawn: cross-galaxy требует target.Star с валидным Uid.");
                    return null;
                }
            }
            else
            {
                if (targetStar == null)
                {
                    targetStar = PickRandomTargetInRange(galaxy, sourceStar,
                        settings.Wormhole_MinDistancePc, settings.Wormhole_MaxDistancePc);
                    if (targetStar == null)
                    {
                        Debug.LogWarning("[WormholeService] Spawn: нет подходящей целевой звезды в диапазоне.");
                        return null;
                    }
                }
                if (targetStar == sourceStar)
                {
                    Debug.LogWarning("[WormholeService] Spawn: source == target.");
                    return null;
                }
                targetStarUid = targetStar.Uid;
            }

            // 3. Резолвим точку выхода: FixedTargetPosition явно задана → она; иначе рандом
            // в 30..80% радиуса целевой системы. Точка фиксируется на спавне, а не выбирается
            // при каждом входе — это позволяет заранее создать парную червоточину-выход.
            Vector2? exitPos = target?.Position;
            if (!crossGalaxy && !exitPos.HasValue)
            {
                float tgtRadius = SRUnits.ToWorld(targetStar.SystemSize);
                float arrR = Random.Range(tgtRadius * 0.3f, tgtRadius * 0.8f);
                float arrAng = Random.Range(0f, Mathf.PI * 2f);
                exitPos = new Vector2(Mathf.Cos(arrAng) * arrR, Mathf.Sin(arrAng) * arrR);
            }

            // 4. Создаём источник и парный выход. Пара живёт синхронно (одинаковый lifetime и фазы).
            //    Cross-galaxy: пара не создаётся (target-звезда в другой галактике недоступна).
            var wh = new WormholeData
            {
                Position = sourcePos,
                TargetStarUid = targetStarUid,
                TargetGalaxyId = targetGalaxyId,
                FixedTargetPosition = exitPos,
                LifetimeOverride = lifetimeTurns.HasValue ? Mathf.Max(1, lifetimeTurns.Value) : -1,
                Graphics = graphics,
                Phase = WormholePhase.Opening,
                DaysInPhase = 0,
                CreatedTurn = galaxy.CurrentTurn,
            };
            sourceStar.Wormholes ??= new List<WormholeData>();
            sourceStar.Wormholes.Add(wh);

            if (!crossGalaxy && exitPos.HasValue)
            {
                var pair = new WormholeData
                {
                    Position = exitPos.Value,
                    TargetStarUid = sourceStar.Uid,
                    TargetGalaxyId = null,
                    FixedTargetPosition = sourcePos,
                    LifetimeOverride = wh.LifetimeOverride,
                    Graphics = graphics,
                    Phase = WormholePhase.Opening,
                    DaysInPhase = 0,
                    CreatedTurn = galaxy.CurrentTurn,
                    PairedWormholeUid = wh.Uid,
                };
                wh.PairedWormholeUid = pair.Uid;
                targetStar.Wormholes ??= new List<WormholeData>();
                targetStar.Wormholes.Add(pair);
            }

            string targetLabel = crossGalaxy ? $"{targetGalaxyId}:{targetStarUid[..System.Math.Min(6, targetStarUid.Length)]}" : targetStar.Name;
            Debug.Log($"[WormholeService] Спавн: {sourceStar.Name}@{sourcePos:F1} → {targetLabel}" +
                      $"{(exitPos.HasValue ? $"@{exitPos.Value:F1}" : " (случ. точка)")}, " +
                      $"uid={wh.Uid[..6]}" +
                      $"{(wh.PairedWormholeUid != null ? $"↔{wh.PairedWormholeUid[..6]}" : "")}, " +
                      $"lifetime={(lifetimeTurns?.ToString() ?? "default")}.");
            return wh;
        }

        /// <summary>Overload Spawn: назначить графику прямо через пути к спрайтам.
        /// Позиционные параметры <paramref name="source"/>/<paramref name="target"/> обязательны
        /// (передайте <c>null</c> для случайных).</summary>
        public static WormholeData Spawn(
            WormholeLocation? source,
            WormholeLocation? target,
            string openingPath,
            string cyclePath = null,
            string closingPath = null,
            string iconPath = null,
            int? lifetimeTurns = null,
            string targetGalaxyId = null)
            => Spawn(source, target, lifetimeTurns,
                     WormholeGraphics.Of(openingPath, cyclePath, closingPath, iconPath),
                     targetGalaxyId);

        // ── Поиск / инвентаризация ────────────────────────────────────────────────

        /// <summary>Найти червоточину по UID. Возвращает и саму запись, и звезду-источник.</summary>
        public static bool TryFind(GalaxyData galaxy, string uid, out WormholeData wormhole, out StarData sourceStar)
        {
            wormhole = null; sourceStar = null;
            if (galaxy == null || string.IsNullOrEmpty(uid)) return false;
            foreach (var star in galaxy.StarsMap.Values)
            {
                if (star.Wormholes == null) continue;
                for (int i = 0; i < star.Wormholes.Count; i++)
                {
                    if (star.Wormholes[i]?.Uid == uid)
                    {
                        wormhole = star.Wormholes[i];
                        sourceStar = star;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Найти по UID в текущей галактике (без out-параметров).</summary>
        public static WormholeData Find(string uid)
            => TryFind(GalaxyManager.Instance?.GeneratedGalaxy, uid, out var wh, out _) ? wh : null;

        /// <summary>Все активные червоточины текущей галактики (плоский список).</summary>
        public static IEnumerable<(WormholeData wh, StarData sourceStar)> All()
        {
            var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
            if (galaxy == null) yield break;
            foreach (var star in galaxy.StarsMap.Values)
            {
                if (star.Wormholes == null) continue;
                for (int i = 0; i < star.Wormholes.Count; i++)
                    if (star.Wormholes[i] != null)
                        yield return (star.Wormholes[i], star);
            }
        }

        /// <summary>Число активных червоточин в галактике.</summary>
        public static int CountActive(GalaxyData galaxy)
        {
            if (galaxy == null) return 0;
            int n = 0;
            foreach (var star in galaxy.StarsMap.Values)
                if (star.Wormholes != null) n += star.Wormholes.Count;
            return n;
        }

        // ── Закрытие / удаление ───────────────────────────────────────────────────

        /// <summary>Форсированное закрытие: фаза → Closing, удалится на следующий ход.
        /// Парная червоточина (если есть) закрывается вместе — они всегда живут синхронно.</summary>
        public static bool ForceClose(WormholeData wormhole)
        {
            if (wormhole == null) return false;
            bool changed = ForceCloseOne(wormhole);
            if (!string.IsNullOrEmpty(wormhole.PairedWormholeUid))
            {
                var pair = Find(wormhole.PairedWormholeUid);
                if (pair != null) ForceCloseOne(pair);
            }
            return changed;
        }

        private static bool ForceCloseOne(WormholeData wh)
        {
            if (wh.Phase == WormholePhase.Closing) return false;
            wh.Phase = WormholePhase.Closing;
            wh.DaysInPhase = 0;
            wh.OpenTurnsRemaining = 0;
            Debug.Log($"[WormholeService] Форсированное закрытие червоточины {wh.Uid[..6]}.");
            return true;
        }

        /// <summary>Форсированное закрытие по UID в текущей галактике.</summary>
        public static bool ForceClose(string uid)
            => TryFind(GalaxyManager.Instance?.GeneratedGalaxy, uid, out var wh, out _) && ForceClose(wh);

        /// <summary>Немедленное удаление без анимации (для отладки). Парная червоточина
        /// удаляется вместе — иначе в целевой системе останется «сирота-выход».</summary>
        public static bool Destroy(string uid)
        {
            var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
            if (!TryFind(galaxy, uid, out var wh, out var star)) return false;
            string pairedUid = wh.PairedWormholeUid;
            star.Wormholes.Remove(wh);
            Debug.Log($"[WormholeService] Мгновенное удаление червоточины {uid[..6]} в {star.Name}.");
            if (!string.IsNullOrEmpty(pairedUid)
                && TryFind(galaxy, pairedUid, out var pair, out var pairStar))
            {
                pairStar.Wormholes.Remove(pair);
                Debug.Log($"[WormholeService] Мгновенное удаление парной {pairedUid[..6]} в {pairStar.Name}.");
            }
            return true;
        }

        // ── Внутренние ────────────────────────────────────────────────────────────

        private static StarData PickRandomStar(GalaxyData galaxy)
        {
            int total = galaxy.StarsMap.Count;
            if (total == 0) return null;
            int idx = Random.Range(0, total);
            int i = 0;
            foreach (var star in galaxy.StarsMap.Values)
            {
                if (i == idx) return star;
                i++;
            }
            return null;
        }

        private static StarData PickRandomTargetInRange(GalaxyData galaxy, StarData from, float minDistPc, float maxDistPc)
        {
            float min2 = minDistPc * minDistPc, max2 = maxDistPc * maxDistPc;
            List<StarData> candidates = null;
            foreach (var star in galaxy.StarsMap.Values)
            {
                if (star == from) continue;
                float sq = (star.Position - from.Position).sqrMagnitude;
                if (sq < min2 || sq > max2) continue;
                (candidates ??= new()).Add(star);
            }
            return candidates == null || candidates.Count == 0
                ? null
                : candidates[Random.Range(0, candidates.Count)];
        }
    }
}
