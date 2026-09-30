using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Combat;
using SRG.Config;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.Ships
{
    /// <summary>
    /// Создаёт контейнеры — ShipData с IsItem=true, в инвентаре которых лежат выкинутые предметы.
    /// Контейнер ставится в star.Ships и обрабатывается системой захвата (см. <see cref="PickupSystem"/>):
    /// его можно подцепить CargoGrabber'ом, дотащить до буксирующего. Лёгкий контейнер выгружается
    /// в инвентарь; тяжёлый — крепится на якорь через <see cref="TowSystem"/>.
    ///
    /// Графика контейнера. Резолвится в порядке убывания приоритета:
    ///   1. <c>item.Params["ContainerGraphic"]</c> — переопределение для конкретного предмета.
    ///   2. Для микромодулей (category="MicroModule") — <c>ItemConfig.ContainerGraphic</c>.
    ///   3. <c>ItemsConfig.GetCategoryCommon(item.Category).ContainerGraphic</c> — общий
    ///      для категории (задаётся в шаблоне Defaults или в *Common-блоке).
    ///   4. Случайный спрайт <c>Container_1..ContainerVariants</c> в <see cref="ContainersBasePath"/>.
    ///
    /// Допустимые форматы значения: абсолютный путь "Graphics/..." либо короткое имя файла
    /// (без расширения) в <see cref="ContainersBasePath"/> — например "Container_3", "MM_T1",
    /// "QuarkBomb". Никаких иных форматов не принимаем.
    /// </summary>
    public static class ContainerFactory
    {
        public static string ContainersBasePath => GalaxyConstants.PATH_CONTAINERS;
        public const int ContainerVariants = 5;
        public const string ContainerShipTypeId = "Container";

        /// <summary>Создать контейнер с одним предметом внутри. Регистрирует его в star.Ships
        /// рядом с originShip (Position = origin + случайный отступ в пределах spawnRadius мира).
        /// Возвращает созданный ShipData (или null, если spawnStar не задан).</summary>
        public static ShipData SpawnContainerWithItem(
            ShipData originShip, ItemInstance item, StarData spawnStar, float spawnRadius = 0.4f)
        {
            if (originShip == null || item == null || spawnStar == null) return null;

            Vector2 pos = originShip.Position + Positions.RandomOnCircle(spawnRadius);

            var data = new ShipData
            {
                Name = item.Name ?? "Container",
                ShipTypeId = ContainerShipTypeId,
                Owner = string.Empty,
                Race  = string.Empty,
                IsItem = true,
                ItemFreezeFrame = 0,
                Position = pos,
                PreviousPosition = pos,
                TargetPosition = pos,
                CurrentStarUid = spawnStar.Uid,
                PreviousStarUid = spawnStar.Uid,
                CurrentStar = spawnStar,
                CustomBodyGraphicPath = ResolveContainerGraphic(item),
                MaxHull = 1,
                CurrentHull = 1,
            };
            data.RefreshSpritesheetPath();
            ShipFactory.RecalculateSpriteWorldSize(data);
            data.Inventory.Add(item);

            spawnStar.Ships.Add(data);
            return data;
        }

        /// <summary>Создать контейнер с фьюнджабельным стеком товара/минералов внутри. Графика — общий
        /// фолбэк (Container_N) или категорийная, как у <see cref="SpawnContainerWithItem"/>. Стек
        /// должен быть уже изъят из исходного инвентаря (через TakeStack), иначе будет дубликат веса.</summary>
        public static ShipData SpawnContainerWithStack(
            ShipData originShip, ItemStack stack, StarData spawnStar, float spawnRadius = 0.4f)
        {
            if (originShip == null || stack == null || stack.TotalWeight <= 0 || spawnStar == null) return null;

            Vector2 pos = originShip.Position + Positions.RandomOnCircle(spawnRadius);

            // Графика — категорийная (если есть в Defaults) или случайный фолбэк.
            string gfx = null;
            var equip = GameWorld.Context?.ItemsConfig;
            string fromCategory = equip?.GetCategoryCommon(stack.Category)?.ContainerGraphic;
            gfx = NormalizeContainerPath(fromCategory);
            if (gfx == null)
            {
                int n = Random.Range(1, ContainerVariants + 1);
                gfx = $"{ContainersBasePath}/Container_{n}";
            }

            var data = new ShipData
            {
                Name = stack.Name ?? stack.ItemId ?? "Container",
                ShipTypeId = ContainerShipTypeId,
                Owner = string.Empty,
                Race  = string.Empty,
                IsItem = true,
                ItemFreezeFrame = 0,
                Position = pos,
                PreviousPosition = pos,
                TargetPosition = pos,
                CurrentStarUid = spawnStar.Uid,
                PreviousStarUid = spawnStar.Uid,
                CurrentStar = spawnStar,
                CustomBodyGraphicPath = gfx,
                MaxHull = 1,
                CurrentHull = 1,
            };
            data.RefreshSpritesheetPath();
            ShipFactory.RecalculateSpriteWorldSize(data);
            data.Inventory.AddStack(stack);

            spawnStar.Ships.Add(data);
            return data;
        }

        /// <summary>
        /// Спавнит «свободный» предмет в космосе с его собственной иконкой (без контейнерной обёртки).
        /// Используется для не-стакабельных единичных предметов, у которых своя визитка (микромодули,
        /// артефакты-находки): вместо ящика в космос падает сам предмет со своей графикой
        /// (<see cref="ItemInstance.GraphicPath"/>). Механически — тот же <c>IsItem=true</c>-шип,
        /// подцепляемый CargoGrabber'ом и разгружаемый через <see cref="UnloadContainerInto"/>.
        /// </summary>
        public static ShipData SpawnLooseItemInSpace(
            ShipData originShip, ItemInstance item, StarData spawnStar, float spawnRadius = 0.4f)
        {
            if (originShip == null || item == null || spawnStar == null) return null;

            Vector2 pos = originShip.Position + Positions.RandomOnCircle(spawnRadius);

            // Приоритет: item.GraphicPath (иконка предмета). Если пусто — фолбэк на контейнер,
            // чтобы предмет вообще был виден.
            string gfx = !string.IsNullOrEmpty(item.GraphicPath)
                ? item.GraphicPath
                : ResolveContainerGraphic(item);

            var data = new ShipData
            {
                Name = item.Name ?? "Item",
                ShipTypeId = ContainerShipTypeId,
                Owner = string.Empty,
                Race  = string.Empty,
                IsItem = true,
                ItemFreezeFrame = 0,
                Position = pos,
                PreviousPosition = pos,
                TargetPosition = pos,
                CurrentStarUid = spawnStar.Uid,
                PreviousStarUid = spawnStar.Uid,
                CurrentStar = spawnStar,
                CustomBodyGraphicPath = gfx,
                MaxHull = 1,
                CurrentHull = 1,
            };
            data.RefreshSpritesheetPath();
            ShipFactory.RecalculateSpriteWorldSize(data);
            data.Inventory.Add(item);

            spawnStar.Ships.Add(data);
            return data;
        }

        /// <summary>Содержимое контейнера → инвентарь target-корабля; контейнер помечается как удалённый
        /// (CurrentHull=0, IsItem=true), star.Ships его уберёт стандартной очисткой. Возвращает кол-во перенесённых.</summary>
        public static int UnloadContainerInto(ShipData container, ShipData target)
        {
            if (container == null || target == null) return 0;
            if (!container.IsItem) return 0;
            int moved = 0;
            if (container.Inventory?.Items != null)
            {
                foreach (var it in new List<ItemInstance>(container.Inventory.Items))
                {
                    if (it == null) continue;
                    // Теневые ItemInstance стеков переносить нельзя — они зеркалят Stacks,
                    // которые мы перенесём отдельно через AddStack/TakeStack ниже.
                    if (it.Uid != null && it.Uid.StartsWith(ShipInventory.StackItemUidPrefix, System.StringComparison.Ordinal))
                        continue;
                    container.Inventory.Remove(it.Uid);
                    target.Inventory.Add(it);
                    moved++;
                }
            }
            // Стеки (Goods/Mineral) переносим через стандартное API — Items target'а
            // получит теневые ItemInstance автоматически.
            if (container.Inventory?.Stacks != null)
            {
                var keys = new List<string>(container.Inventory.Stacks.Keys);
                foreach (var key in keys)
                {
                    var taken = container.Inventory.TakeStack(key, int.MaxValue);
                    if (taken == null || taken.TotalWeight <= 0) continue;
                    target.Inventory.AddStack(taken);
                    moved++;
                }
            }
            container.CurrentHull = 0;
            return moved;
        }

        private static string ResolveContainerGraphic(ItemInstance item)
        {
            // 1. Переопределение для конкретного экземпляра.
            string raw = item?.GetParamString("ContainerGraphic");
            string resolved = NormalizeContainerPath(raw);
            if (resolved != null) return resolved;

            if (item != null && !string.IsNullOrEmpty(item.Category))
            {
                var ctx = GameWorld.Context;

                // 2. Микромодули не живут в ItemsConfig — свой конфиг с ContainerGraphic.
                if (item.Category == MicroModuleFactory.CategoryKey)
                {
                    string fromMm = ctx?.ItemsConfig?.GetMicroModule(item.ItemId)?.ContainerGraphic;
                    resolved = NormalizeContainerPath(fromMm);
                    if (resolved != null) return resolved;
                }

                // 3. Общая графика для категории — из ItemsConfig.GetCategoryCommon(category).
                string fromCategory = ctx?.ItemsConfig?.GetCategoryCommon(item.Category)?.ContainerGraphic;
                resolved = NormalizeContainerPath(fromCategory);
                if (resolved != null) return resolved;
            }

            // 4. Фолбэк — случайный спрайт.
            int n = Random.Range(1, ContainerVariants + 1);
            return $"{ContainersBasePath}/Container_{n}";
        }

        private static string NormalizeContainerPath(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            if (raw.StartsWith("Graphics/", System.StringComparison.Ordinal)) return raw;
            // Короткое имя (Container_3 / MM_T1 / QuarkBomb) — разворачивается в ContainersBasePath.
            return $"{ContainersBasePath}/{raw}";
        }
    }
}
