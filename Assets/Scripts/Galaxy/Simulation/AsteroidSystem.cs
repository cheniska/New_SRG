using System;
using System.Collections.Generic;
using UnityEngine;
using SRG.Combat;
using SRG.Config;
using SRG.Equipment;
using SRG.Galaxy.Generation;
using SRG.Galaxy.Politics;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.Galaxy.Simulation
{
    // Физика астероидов: спавн, гравитация, swept-circle коллизии, дроп.
    // Орбитальные позиции берутся из OrbitMath, чтобы не дублировать расчёты.
    public static class AsteroidSystem
    {
        /// <summary>Астероид уничтожен ВЫСТРЕЛОМ (не столкновением с планетой/звездой).
        /// Передаётся planet — если астероид угрожал планете (см. <see cref="AsteroidData.PredictedDangerPlanetUid"/>).
        /// Подписчик: <see cref="GalaxyNewsService"/> — публикует Star.Asteroid.Kill.* по расе планеты
        /// и начисляет награду игроку.</summary>
        public static event Action<AsteroidData, ShipData, StarData, PlanetData> OnAsteroidShotDown;
        public static void TrySpawnAsteroid(StarData star, GalaxyGenerationContext ctx,
            TurnAnimationData anim = null)
        {
            var cfg = ctx?.Config?.Asteroids;
            if (cfg == null) return;

            int maxAsteroids = ResolveMaxAsteroids(star);
            if (star.Asteroids.Count >= maxAsteroids) return;
            if (GameRng.Value > cfg.SpawnChancePerTurn) return;

            string typeId = cfg.RollRandomTypeId();
            if (typeId == null || !cfg.Types.TryGetValue(typeId, out var typeCfg)) return;

            var asteroid = CreateAsteroid(typeId, typeCfg, star);
            star.Asteroids.Add(asteroid);

            anim?.AsteroidSpawns.Add(new AsteroidSpawnEvent { AsteroidUid = asteroid.Uid });

            Debug.Log($"[AsteroidSystem] Spawned '{typeCfg.Name}' in '{star.Name}' | " +
                      $"pos=({asteroid.Position.x:F1},{asteroid.Position.y:F1}) " +
                      $"vel=({asteroid.Velocity.x:F2},{asteroid.Velocity.y:F2}) " +
                      $"graphic='{asteroid.GraphicPath}' | " +
                      $"total: {star.Asteroids.Count}/{maxAsteroids}");
        }

        public static void TickAsteroids(StarData star, int subTurn,
            TurnAnimationData anim, GalaxyGenerationContext ctx)
        {
            if (star.Asteroids.Count == 0) return;

            var cfg = ctx?.Config?.Asteroids;
            float gravityConst = cfg?.GravityConst ?? GalaxyConstants.ASTEROID_GRAVITY_CONST;
            float systemRadius = SRUnits.ToWorld(star.SystemSize);

            for (int i = star.Asteroids.Count - 1; i >= 0; i--)
            {
                var asteroid = star.Asteroids[i];
                if (asteroid.IsDestroyed) continue;

                asteroid.PreviousPosition = asteroid.Position;

                ApplyGravity(asteroid, gravityConst);
                asteroid.Position += asteroid.Velocity / GalaxyData.SubTurnsPerTurn;

                if (anim != null && anim.AsteroidFrames.TryGetValue(asteroid.Uid, out var frames))
                    frames.SubTurns[subTurn] = asteroid.Position;

                // Раз в ход (первый сабтёрн) пересчитываем «угрожает ли астероид планете».
                // Дешёвая проверка: distance текущей позиции ко всем планетам звезды.
                if (subTurn == 1) UpdatePredictedDanger(asteroid, star);

                if (!asteroid.IsDestroyed)
                    CheckCollisions(asteroid, star, anim, ctx, subTurn);

                if (!asteroid.IsDestroyed && asteroid.Position.magnitude > systemRadius * 1.5f)
                {
                    DestroyAsteroid(asteroid, anim,
                        new List<ItemInstance>(),
                        new List<ItemStack>(),
                        hitPlayer: false,
                        collisionType: AsteroidCollisionType.None,
                        subTurn: subTurn, tHit: 1f);
                    Debug.Log($"[AsteroidSystem] Asteroid '{SpriteUtility.ShortId(asteroid.Uid, 8)}' left system '{star.Name}'");
                }
            }

            if (subTurn == GalaxyData.SubTurnsPerTurn)
                star.Asteroids.RemoveAll(a => a.IsDestroyed);
        }

        private static void ApplyGravity(AsteroidData asteroid, float gravityConst)
        {
            Vector2 toStar = -asteroid.Position;
            float distSq = toStar.sqrMagnitude;

            const float minDistSq = 0.01f;
            if (distSq < minDistSq) return;

            float acceleration = gravityConst / distSq;
            Vector2 gravityDelta = toStar.normalized * acceleration / GalaxyData.SubTurnsPerTurn;
            asteroid.Velocity += gravityDelta;
        }

        private static (bool hit, float t) SweptCircleHit(
            Vector2 aFrom, Vector2 aTo,
            Vector2 bFrom, Vector2 bTo,
            float combinedRadius)
        {
            Vector2 relFrom = aFrom - bFrom;
            Vector2 relVel = (aTo - aFrom) - (bTo - bFrom);

            float rSq = combinedRadius * combinedRadius;
            float a = Vector2.Dot(relVel, relVel);

            if (a < 1e-10f)
            {
                bool inside = relFrom.sqrMagnitude <= rSq;
                return (inside, 0f);
            }

            float b = 2f * Vector2.Dot(relFrom, relVel);
            float c = Vector2.Dot(relFrom, relFrom) - rSq;
            float disc = b * b - 4f * a * c;

            if (disc < 0f) return (false, 0f);

            float sqrtD = Mathf.Sqrt(disc);
            float t1 = (-b - sqrtD) / (2f * a);
            float t2 = (-b + sqrtD) / (2f * a);

            if (t2 < 0f) return (false, 0f);
            if (t1 > 1f) return (false, 0f);
            if (t1 < 0f && b >= 0f) return (false, 0f);

            float tHit = Mathf.Max(0f, t1);
            return (true, tHit);
        }

        private static Vector2 GetPlanetPositionAtSubTurn(PlanetData planet, int subTurn)
            => OrbitMath.GetPlanetPositionAtSubTurn(planet, subTurn);

        private static float GetShipCollisionRadius(ShipData ship)
        {
            var hull = EquipmentSystem.GetEquipped(ship, SlotKeys.Hull);
            if (hull != null)
            {
                float sizeSmall = hull.GetParam("SizeSmall", 0f);
                if (sizeSmall > 0f) return SRUnits.ToWorld(sizeSmall) * 0.5f;
            }
            return 0.35f;
        }

        private static void CheckCollisions(AsteroidData asteroid, StarData star,
            TurnAnimationData anim, GalaxyGenerationContext ctx, int subTurn)
        {
            Vector2 aFrom = asteroid.PreviousPosition;
            Vector2 aTo = asteroid.Position;
            float ar = asteroid.CollisionRadius;

            float starRadius = star.StarRadius > 0f ? star.StarRadius : 0.6f;
            {
                var (hit, t) = SweptCircleHit(aFrom, aTo, Vector2.zero, Vector2.zero, ar + starRadius);
                if (hit) { OnCollisionWithStar(asteroid, anim, ctx, subTurn, t); return; }
            }

            foreach (var ship in star.Ships)
            {
                if (ship.CurrentHull <= 0) continue;
                // Пристыкованные «внутри» носителя — столкновение принимает сам носитель.
                if (!string.IsNullOrEmpty(ship.LandedOnShipUid)) continue;
                float shipRadius = GetShipCollisionRadius(ship);

                Vector2 shipFrom = ship.Position;
                Vector2 shipTo = ship.Position;
                if (anim != null && anim.ShipFrames.TryGetValue(ship.Uid, out var shipFrames))
                {
                    shipFrom = shipFrames.SubTurns[subTurn - 1];
                    shipTo = shipFrames.SubTurns[subTurn];
                }

                var (hit, t) = SweptCircleHit(aFrom, aTo, shipFrom, shipTo, ar + shipRadius);
                if (hit) { OnCollisionWithShip(asteroid, ship, anim, ctx, subTurn, t); return; }
            }

            foreach (var planet in star.Planets)
            {
                float planetRadius = GetPlanetCollisionRadius(planet, ctx);
                Vector2 pFrom = GetPlanetPositionAtSubTurn(planet, subTurn - 1);
                Vector2 pTo = GetPlanetPositionAtSubTurn(planet, subTurn);

                var (hit, t) = SweptCircleHit(aFrom, aTo, pFrom, pTo, ar + planetRadius);
                if (hit) { OnCollisionWithPlanet(asteroid, planet, anim, ctx, subTurn, t); return; }

                foreach (var sat in planet.Satellites)
                {
                    Vector2 sFrom = GetSatelliteWorldPosition(sat, planet, subTurn - 1);
                    Vector2 sTo = GetSatelliteWorldPosition(sat, planet, subTurn);
                    var (satHit, satT) = SweptCircleHit(aFrom, aTo, sFrom, sTo, ar + 0.1f);
                    if (satHit) { OnCollisionWithPlanet(asteroid, planet, anim, ctx, subTurn, satT); return; }
                }
            }
        }

        private static void OnCollisionWithStar(AsteroidData asteroid,
            TurnAnimationData anim, GalaxyGenerationContext ctx, int subTurn, float tHit)
        {
            Debug.Log($"[AsteroidSystem] Asteroid '{SpriteUtility.ShortId(asteroid.Uid, 8)}' hit star — burned up.");
            DestroyAsteroid(asteroid, anim,
                new List<ItemInstance>(),
                new List<ItemStack>(),
                hitPlayer: false,
                collisionType: AsteroidCollisionType.Star,
                subTurn: subTurn, tHit: tHit);
        }

        private static void OnCollisionWithShip(AsteroidData asteroid, ShipData ship,
            TurnAnimationData anim, GalaxyGenerationContext ctx, int subTurn, float tHit)
        {
            float damage = CalculateDamage(asteroid,
                ctx?.Config?.Asteroids?.DampingConstant ?? GalaxyConstants.ASTEROID_DAMPING_CONST,
                GetShipAsteroidProtection(ship));

            int dmgInt = Mathf.Max(1, Mathf.RoundToInt(damage));
            ship.CurrentHull = Mathf.Max(0, ship.CurrentHull - dmgInt);

            if (ship.CurrentHull <= 0)
            {
                // Возвращаемое значение игнорируется намеренно — сохраняем поведение,
                // существовавшее до унификации (астероидный путь не откатывает hull-урон).
                WeaponSystem.RegisterTargetDeath(
                    ship,
                    PlayerDeathCause.Asteroid,
                    $"астероид {SpriteUtility.ShortId(asteroid.Uid)}",
                    null,
                    anim);
            }

            Debug.Log($"[AsteroidSystem] Asteroid hit ship '{ship.Name}': -{dmgInt} HP " +
                      $"(hull={ship.CurrentHull}/{ship.MaxHull})");

            var (droppedItems, droppedStacks) = RollDrop(asteroid, ctx);

            List<ItemInstance> visualItems = droppedItems;
            List<ItemStack>    visualStacks = droppedStacks;

            if (ship.IsPlayer)
            {
                foreach (var item in droppedItems)
                    InventoryService.PutItem(ship, item);
                foreach (var stack in droppedStacks)
                    ship.Inventory.AddStack(stack);
                anim.PlanningReasons.Add(PlanningReason.AsteroidImpact);
                // Уже в инвентаре — не спавним визуальные дропы
                visualItems  = new List<ItemInstance>();
                visualStacks = new List<ItemStack>();
            }

            DestroyAsteroid(asteroid, anim, visualItems, visualStacks,
                hitPlayer: ship.IsPlayer,
                collisionType: AsteroidCollisionType.Ship,
                subTurn: subTurn, tHit: tHit);
        }

        private static void OnCollisionWithPlanet(AsteroidData asteroid, PlanetData planet,
            TurnAnimationData anim, GalaxyGenerationContext ctx, int subTurn, float tHit)
        {
            Debug.Log($"[AsteroidSystem] Asteroid hit planet '{planet.Name}'.");
            var (droppedItems, droppedStacks) = RollDrop(asteroid, ctx);
            DestroyAsteroid(asteroid, anim, droppedItems, droppedStacks,
                hitPlayer: false,
                collisionType: AsteroidCollisionType.Planet,
                subTurn: subTurn, tHit: tHit);
        }

        private static void DestroyAsteroid(AsteroidData asteroid, TurnAnimationData anim,
            List<ItemInstance> droppedItems, List<ItemStack> droppedStacks,
            bool hitPlayer, AsteroidCollisionType collisionType,
            int subTurn, float tHit)
        {
            asteroid.IsDestroyed = true;

            Vector2 contactPoint = Vector2.Lerp(asteroid.PreviousPosition, asteroid.Position, tHit);

            if (anim != null && anim.AsteroidFrames.TryGetValue(asteroid.Uid, out var frames))
            {
                for (int r = subTurn + 1; r <= GalaxyData.SubTurnsPerTurn; r++)
                    frames.SubTurns[r] = contactPoint;
            }

            anim?.AsteroidDestroys.Add(new AsteroidDestroyEvent
            {
                AsteroidUid = asteroid.Uid,
                Position = contactPoint,
                DroppedItems = droppedItems,
                DroppedStacks = droppedStacks,
                HitPlayer = hitPlayer,
                CollisionType = collisionType,
                CollisionSubTurn = subTurn,
                CollisionT = tHit,
                ExplosionPath = asteroid.ExplosionPath,
            });
        }

        /// <summary>
        /// Уничтожает астероид выстрелом игрока: лут идёт прямо в инвентарь.
        /// </summary>
        public static void PlayerShootAsteroid(
            ShipData player, AsteroidData asteroid,
            TurnAnimationData anim, int subTurn, GalaxyGenerationContext ctx)
        {
            if (asteroid == null || asteroid.IsDestroyed) return;
            var (droppedItems, droppedStacks) = RollDrop(asteroid, ctx);

            // Мгновенно добавляем в инвентарь игрока
            foreach (var stack in droppedStacks)
                player.Inventory.AddStack(stack);
            foreach (var item in droppedItems)
                InventoryService.PutItem(player, item);

            // Дропы уже в инвентаре — не спавним визуальные предметы
            DestroyAsteroid(asteroid, anim, new List<ItemInstance>(), new List<ItemStack>(),
                hitPlayer: false,
                collisionType: AsteroidCollisionType.Ship,
                subTurn: subTurn, tHit: 0.5f);

            // Событие для GalaxyNewsService и наградного механизма. Планета — если астероид
            // угрожал ей (см. UpdatePredictedDanger). subTurn ==1 гарантированно проходит
            // предикт до текущего выстрела; для повторных subTurn'ов используем последнее значение.
            StarData star = player?.CurrentStar;
            PlanetData planet = null;
            if (!string.IsNullOrEmpty(asteroid.PredictedDangerPlanetUid) && star != null)
            {
                for (int i = 0; i < star.Planets.Count; i++)
                    if (star.Planets[i].Uid == asteroid.PredictedDangerPlanetUid)
                    { planet = star.Planets[i]; break; }
            }
            OnAsteroidShotDown?.Invoke(asteroid, player, star, planet);
        }

        private static void UpdatePredictedDanger(AsteroidData asteroid, StarData star)
        {
            if (asteroid == null || star == null) { return; }
            var settings = GameWorld.Settings;
            float dangerR = settings?.AsteroidDangerCloseRadius ?? 5.0f;
            float dangerRSq = dangerR * dangerR;

            string closestUid = null;
            float closestDistSq = dangerRSq;
            for (int i = 0; i < star.Planets.Count; i++)
            {
                var planet = star.Planets[i];
                // Позиция планеты в момент сабтёрна 1 (используем как приближение — точная
                // подповторная проверка стоит дороже, чем польза).
                Vector2 pPos = OrbitMath.GetPlanetPositionAtSubTurn(planet, 1);
                float dSq = (asteroid.Position - pPos).sqrMagnitude;
                if (dSq < closestDistSq) { closestDistSq = dSq; closestUid = planet.Uid; }
            }
            asteroid.PredictedDangerPlanetUid = closestUid;
        }

        public static float CalculateDamage(AsteroidData asteroid,
            float dampingConstant, float asteroidProtection)
        {
            float speed = asteroid.Velocity.magnitude;
            float rawDamage = (asteroid.Mass * speed * speed) / Mathf.Max(1f, dampingConstant);
            return rawDamage * Mathf.Clamp01(1f - asteroidProtection);
        }

        public static (List<ItemInstance> items, List<ItemStack> stacks) RollDrop(
            AsteroidData asteroid, GalaxyGenerationContext ctx)
        {
            var items = new List<ItemInstance>();
            var stacks = new List<ItemStack>();

            var cfg = ctx?.Config?.Asteroids;
            if (cfg == null || !cfg.Types.TryGetValue(asteroid.TypeId, out var typeCfg))
                return (items, stacks);

            var itemsCfg = ctx.ItemsConfig;

            // Дроп стакабельного (goods или mineral) с натуральным флагом. Диапазон количества
            // задаёт сам астероид — MineralDropMin/Max в AsteroidTypeConfig.
            void TryDropStack(string id, float chance)
            {
                if (GameRng.Value >= chance) return;
                int max = Mathf.Max(1, typeCfg.MineralDropMax);
                int min = Mathf.Clamp(typeCfg.MineralDropMin, 1, max);
                int amount = GameRng.Range(min, max + 1);
                var stack = ItemGrantService.CreateStack(id, amount, ctx);
                if (stack != null) { stack.NaturalOrigin = true; stacks.Add(stack); }
            }
            TryDropStack("Minerals", typeCfg.CommonMineralChance);

            if (ctx.ItemsConfig != null && GameRng.Value < typeCfg.EquipmentDropChance)
            {
                var picked = ctx.ItemsConfig.GetRandomItem();
                if (picked.HasValue)
                {
                    var inst = ItemGrantService.CreateEquipment(
                        picked.Value.category, picked.Value.itemId, ctx);
                    if (inst != null)
                    {
                        inst.Durability = Mathf.RoundToInt(
                            inst.MaxDurability * GameRng.Range(0.3f, 0.8f));
                        items.Add(inst);
                    }
                }
            }

            return (items, stacks);
        }

        private static AsteroidData CreateAsteroid(string typeId,
            AsteroidTypeConfig typeCfg, StarData star)
        {
            float systemRadius = SRUnits.ToWorld(star.SystemSize);
            float spawnRadius = systemRadius * 1.2f;

            float spawnAngle = GameRng.Range(0f, 360f) * Mathf.Deg2Rad;
            var spawnPos = new Vector2(
                Mathf.Cos(spawnAngle) * spawnRadius,
                Mathf.Sin(spawnAngle) * spawnRadius);

            Vector2 toCenter = -spawnPos.normalized;
            float offsetAngle = GameRng.Range(-40f, 40f) * Mathf.Deg2Rad;
            float cos = Mathf.Cos(offsetAngle), sin = Mathf.Sin(offsetAngle);
            var direction = new Vector2(
                toCenter.x * cos - toCenter.y * sin,
                toCenter.x * sin + toCenter.y * cos);

            float speed = GameRng.Range(typeCfg.SpeedMin, typeCfg.SpeedMax);
            string graphicPath = ResolveSheetFolderPath(typeCfg.GraphicPath);

            return new AsteroidData
            {
                TypeId = typeId,
                Position = spawnPos,
                PreviousPosition = spawnPos,
                Velocity = direction * speed,
                Mass = GameRng.Range(typeCfg.MassMin, typeCfg.MassMax),
                CollisionRadius = typeCfg.CollisionRadius,
                GraphicPath = graphicPath,
                ExplosionPath = typeCfg.ExplosionPath,
                AnimFps = typeCfg.AnimFps,
                SelfRotationSpeed = GameRng.Range(
                    typeCfg.SelfRotationSpeedMin, typeCfg.SelfRotationSpeedMax)
                    * (GameRng.Value > 0.5f ? 1f : -1f)
            };
        }

        private static int ResolveMaxAsteroids(StarData star)
        {
            if (star.MaxAsteroids >= 0) return star.MaxAsteroids;
            return GalaxyConstants.DEFAULT_MAX_ASTEROIDS;
        }

        private static float GetShipAsteroidProtection(ShipData ship)
        {
            var hull = EquipmentSystem.GetEquipped(ship, SlotKeys.Hull);
            return hull?.GetParam("AsteroidProtection", 0f) ?? 0f;
        }

        private static float GetPlanetCollisionRadius(PlanetData planet, GalaxyGenerationContext ctx)
        {
            if (ctx?.Config?.Planets?.Sizes != null &&
                ctx.Config.Planets.Sizes.TryGetValue(planet.Size, out var sizeData))
                return sizeData.BaseScale * 0.6f;
            return 0.5f;
        }

        private static Vector2 GetPlanetWorldPosition(PlanetData planet)
            => OrbitMath.GetPlanetWorldPosition(planet);

        private static Vector2 GetSatelliteWorldPosition(SatelliteData sat, PlanetData parent)
        {
            Vector2 planetPos = GetPlanetWorldPosition(parent);
            float r = SRUnits.ToWorld(sat.OrbitRadius);
            float rad = sat.CurrentAngle * Mathf.Deg2Rad;
            return planetPos + new Vector2(Mathf.Cos(rad) * r, Mathf.Sin(rad) * r * 0.4f);
        }

        private static Vector2 GetSatelliteWorldPosition(SatelliteData sat, PlanetData parent, int subTurn)
        {
            Vector2 planetPos = GetPlanetPositionAtSubTurn(parent, subTurn);
            float r = SRUnits.ToWorld(sat.OrbitRadius);
            float t = (float)subTurn / GalaxyData.SubTurnsPerTurn;
            float satAngle = Mathf.LerpAngle(sat.CurrentAngle - 360f / sat.OrbitSpeed,
                                              sat.CurrentAngle, t);
            float rad = satAngle * Mathf.Deg2Rad;
            return planetPos + new Vector2(Mathf.Cos(rad) * r, Mathf.Sin(rad) * r * 0.4f);
        }

        // GraphicPath в конфиге астероида — «папка вариантов»: сама содержит 10–15 отдельных
        // атласов (например Rocky/00..14, Metallic/Blue00..Blue13, каждый — 100 кадров 10×10).
        // Единственный легитимный случай папочной семантики графики; folder-scan живёт в
        // GraphicsManager.EnumerateSheetPathsInFolder (единая точка для этого паттерна).

        /// <summary>Все конкретные атласы астероидов данного типа (для прогрева на старте).</summary>
        public static IEnumerable<string> EnumerateVariantSheetPaths(string configPath)
        {
            var gm = GameWorld.Graphics;
            if (gm == null || string.IsNullOrEmpty(configPath)) yield break;
            foreach (var path in gm.EnumerateSheetPathsInFolder(configPath))
                yield return path;
        }

        private static string ResolveSheetFolderPath(string configPath)
        {
            var gm = GameWorld.Graphics;
            if (gm == null || string.IsNullOrEmpty(configPath)) return configPath;
            var variants = gm.EnumerateSheetPathsInFolder(configPath);
            if (variants.Length == 0) return configPath; // fallback: старое поведение
            return variants[GameRng.Range(0, variants.Length)];
        }
    }

    public enum AsteroidCollisionType
    {
        None,
        Star,
        Planet,
        Ship,
    }
}
