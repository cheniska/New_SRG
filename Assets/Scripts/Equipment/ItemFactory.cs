using UnityEngine;
using Random = UnityEngine.Random;
using System.Linq;
using SRG.Combat;
using SRG.Config;
using SRG.Core;
using SRG.Economy;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Ships;

namespace SRG.Equipment
{
    public static class ItemFactory
    {
        public static ItemInstance Create(
            string category,
            string itemId,
            ItemsConfig equipConfig,
            GalaxyConfig galaxyConfig,
            int? gtlOverride = null,
            string overrideRace = null)
        {
            var cfg = equipConfig?.GetItem(category, itemId);
            if (cfg == null)
            {
                UnityEngine.Debug.LogError($"[ItemFactory] Item '{category}/{itemId}' not found in ItemsConfig.");
                return null;
            }

            string race = overrideRace ?? cfg.Manufacturer?.Race;
            float priceMult = 1f, durMult = 1f;

            if (race != null && galaxyConfig?.Races != null &&
                galaxyConfig.Races.TryGetValue(race, out var raceCfg))
            {
                // Durability мультипликатор — у всех категорий, кроме Hull:
                // у корпуса Durability = вместимость (Weight) и не масштабируется расой.
                if (raceCfg.EquipmentManufacturing != null && category != EquipmentCategory.Hull)
                    durMult *= raceCfg.EquipmentManufacturing.DurabilityMult;

                // Price-множитель:
                // - Hull: цена уже включает v11+sizes без расы (по дизайну).
                // - Engine/FuelTank/Forsage/Shield/Radar/Scanner/Droid/CargoGrabber: умножаем на race.PriceCoef.
                // - Weapons/Artefacts: пока используем EquipmentManufacturing.PriceMult (TODO: дописать формулу).
                if (category == EquipmentCategory.Weapons || category == EquipmentCategory.Artefacts)
                {
                    if (raceCfg.EquipmentManufacturing != null)
                        priceMult *= raceCfg.EquipmentManufacturing.PriceMult;
                }
                else if (category != EquipmentCategory.Hull)
                {
                    if (raceCfg.PriceCoef > 0f) priceMult *= raceCfg.PriceCoef;
                }
            }

            if ((category == EquipmentCategory.Weapons || category == EquipmentCategory.Artefacts) &&
                cfg.Manufacturer?.Side != null &&
                galaxyConfig?.Ships?.Owners != null &&
                galaxyConfig.Ships.Owners.TryGetValue(cfg.Manufacturer.Side, out var sideCfg) &&
                sideCfg.EquipmentManufacturing != null)
            {
                priceMult *= sideCfg.EquipmentManufacturing.PriceMult;
                durMult *= sideCfg.EquipmentManufacturing.DurabilityMult;
            }

            // Глобальная инфляция фиксируется в цене предмета на момент создания.
            // Старые предметы сохраняют старую цену, новые получают актуальный фактор.
            priceMult *= InflationSystem.GetFactor(GalaxyManager.Instance?.GeneratedGalaxy);

            var inst = ItemInstance.FromConfig(category, itemId, cfg, equipConfig, priceMult, durMult, gtlOverride);
            // У Hull раса закодирована в id и должна соответствовать графике — overrideRace не трогает её.
            if (inst != null && overrideRace != null && category != EquipmentCategory.Hull)
                inst.ManufacturerRace = overrideRace;
            // Применяем IntrinsicEmbed + SlotCode из конфига, чтобы Params сразу отражал бонусы
            // (нужно для тултипа в инвентаре и для последующих чтений). Cross-slot (ShipScope)
            // всё равно применится только при установке — через ShipBonusService.
            if (inst != null && (cfg.IntrinsicEmbed != null || cfg.SlotCode != null))
                EmbedService.RecomputeCarrier(inst, equipConfig);
            return inst;
        }

        public static void EquipStarterKit(
            ShipData ship,
            System.Collections.Generic.Dictionary<string, string> kit,
            ItemsConfig equipConfig,
            GalaxyConfig galaxyConfig,
            System.Collections.Generic.List<string> starterWeapons = null)
        {
            if (equipConfig == null)
            {
                UnityEngine.Debug.LogError("[ItemFactory.EquipStarterKit] equipConfig is null");
                return;
            }
            if (kit == null && (starterWeapons == null || starterWeapons.Count == 0))
            {
                UnityEngine.Debug.LogError("[ItemFactory.EquipStarterKit] both kit and starterWeapons are empty");
                return;
            }


            // Стартовый комплект клеймим расой корабля как производителя — иначе у hand-written
            // предметов (Engine/Weapons/Artefacts/…) поле Manufacturer.Race остаётся null
            // и тултип/тег {ship_race} в диалогах не имеет источника данных. Для корпусов
            // overrideRace игнорируется в Create — у них раса всегда из id.
            string starterManufacturerRace = ship?.Race;

            if (kit != null)
            {
                if (kit.TryGetValue("Hull", out var hullId))
                {
                    // Stock-конфиг StarterKit фиксирует расу корпуса (Hull_Race3_*). Подставляем
                    // расу корабля, если такой корпус существует — иначе все NPC выглядят как Race3.
                    hullId = SubstituteHullRace(hullId, ship?.Race, equipConfig);
                    var hullInst = Create("Hull", hullId, equipConfig, galaxyConfig, overrideRace: starterManufacturerRace);
                    if (hullInst == null)
                    {
                        UnityEngine.Debug.LogError($"[ItemFactory.EquipStarterKit] Create('Hull', '{hullId}') returned null!");
                    }
                    else
                    {
                        ship.AllItems[hullInst.Uid] = hullInst;

                        var slotPlan = EquipmentSystem.GetHullSlotPlan(hullInst, equipConfig);
                        if (slotPlan == null)
                        {
                            UnityEngine.Debug.LogError($"[ItemFactory.EquipStarterKit] Не удалось определить слоты для корпуса '{hullId}' (HullType={hullInst.GetParamString("HullType")}, Race={hullInst.ManufacturerRace}).");
                            ship.AllItems.Remove(hullInst.Uid);
                        }
                        else if (slotPlan.Count == 0)
                        {
                            UnityEngine.Debug.LogError($"[ItemFactory.EquipStarterKit] SlotPlan пустой для '{hullId}' (HullType={hullInst.GetParamString("HullType")}, Race={hullInst.ManufacturerRace}). Проверь Hull.HullSlotsHullType и Hull.HullSlotsRace в ItemsConfig.json.");
                            ship.AllItems.Remove(hullInst.Uid);
                        }
                        else
                        {
                            EquipmentSystem.ApplySeriesToHull(hullInst, equipConfig);
                            ship.Equipment.InitForHullSlots(slotPlan, equipConfig);

                            ship.Equipment.Set(SlotKeys.Hull, hullInst.Uid);
                            ship.MaxHull = UnityEngine.Mathf.RoundToInt(hullInst.GetParam("HP", hullInst.MaxDurability > 0 ? hullInst.MaxDurability : 100f));
                            ship.CurrentHull = ship.MaxHull;
                            ShipFactory.RecalculateSpriteWorldSize(ship);
                        }
                    }
                }
                else
                {
                    UnityEngine.Debug.LogError("[ItemFactory.EquipStarterKit] No 'Hull' key in StarterKit!");
                }

                foreach (var kv in kit)
                {
                    if (kv.Key == "Hull") continue;
                    PlaceStarterItem(ship, kv.Key, kv.Value, starterManufacturerRace, equipConfig, galaxyConfig);
                }
            }

            if (starterWeapons != null)
                foreach (var weaponId in starterWeapons)
                    PlaceStarterItem(ship, "Weapons", weaponId, starterManufacturerRace, equipConfig, galaxyConfig);

            WeaponSystem.RebuildShieldState(ship);
            ship.InvalidateWeaponSlotsCache();
            ShipBonusService.RecomputeShipEquipment(ship);
        }

        /// <summary>Общий путь для не-Hull записей стартера: создаёт экземпляр (клеймит расой,
        /// удваивает вес для станции) и делегирует размещение <see cref="ItemGrantService.PlaceInstance"/>.
        /// Recompute отложен на конец <see cref="EquipStarterKit"/> — иначе получаем N расчётов бонусов.</summary>
        private static void PlaceStarterItem(ShipData ship, string category, string itemId,
                                             string starterRace,
                                             ItemsConfig equipConfig, GalaxyConfig galaxyConfig)
        {
            var inst = Create(category, itemId, equipConfig, galaxyConfig, overrideRace: starterRace);
            if (inst == null)
            {
                UnityEngine.Debug.LogError($"[ItemFactory.EquipStarterKit] Create('{category}', '{itemId}') returned null!");
                return;
            }
            ApplyStationWeight(ship, inst);

            var res = ItemGrantService.PlaceInstance(ship, inst, itemId,
                        ItemGrantService.GrantTarget.Auto, specificSlot: null, recompute: false);
            if (res.Placement == "inventory")
                UnityEngine.Debug.LogWarning(
                    $"[ItemFactory.EquipStarterKit] No slot for '{category}/{itemId}'. Sent to inventory. " +
                    $"Slots: {string.Join(", ", ship.Equipment.Slots.Keys)}");
        }

        /// <summary>Станции несут крупные модификации оборудования — вес каждого экипируемого предмета
        /// (кроме корпуса) удваивается. Вызывать сразу после Create, до установки в слот.</summary>
        private static void ApplyStationWeight(ShipData ship, ItemInstance inst)
        {
            if (ship != null && ship.IsStation && inst != null && inst.Category != EquipmentCategory.Hull)
                inst.Weight *= 2;
        }

        // Расы, для которых реально существуют файлы Hull_<Race>_<HullType>_c.png в Resources.
        // Шаблон Equipment.Hull синтезирует валидный ItemConfig для любой строки расы
        // (GetItem не возвращает null), поэтому валидируем именно по этому белому списку —
        // иначе ship.Race="Human" даст путь Hull_Human_R_c и спрайт не найдётся.
        private static readonly string[] FallbackHullRaces = { "Race1", "Race2", "Race3", "Race4", "Race5", "RaceDominators1", "RaceDominators2", "RaceDominators3" };

        private static bool IsKnownHullRace(string race)
        {
            if (string.IsNullOrEmpty(race)) return false;
            for (int i = 0; i < FallbackHullRaces.Length; i++)
                if (FallbackHullRaces[i] == race) return true;
            return false;
        }

        /// <summary>
        /// Подставляет расу в id корпуса StarterKit. С учётом Side-сегмента форматы такие:
        ///   Hull_&lt;Side&gt;_&lt;HullType&gt;_&lt;Line&gt;          — раса не указана, берём ship.Race
        ///   Hull_&lt;Side&gt;_&lt;Race&gt;_&lt;HullType&gt;_&lt;Line&gt;   — раса фиксирована, заменяем на ship.Race
        /// Если ship.Race не входит в список поддерживаемых корпусом рас — фолбэк на Race1.
        /// </summary>
        private static string SubstituteHullRace(string hullId, string shipRace, ItemsConfig equipConfig)
        {
            if (string.IsNullOrEmpty(hullId) || !hullId.StartsWith("Hull_", System.StringComparison.Ordinal))
                return hullId;

            var parts = hullId.Split('_');
            bool knownShipRace = IsKnownHullRace(shipRace);

            // Race-less form: Hull_<Side>_<HullType>_<Line>  (4 сегмента вкл. "Hull")
            if (parts.Length == 4)
            {
                string race = knownShipRace ? shipRace : FallbackHullRaces[0];
                return $"Hull_{parts[1]}_{race}_{parts[2]}_{parts[3]}";
            }

            // Full form: Hull_<Side>_<Race>_<HullType>_<Line>  (5 сегментов вкл. "Hull")
            if (parts.Length == 5 && knownShipRace && parts[2] != shipRace)
            {
                parts[2] = shipRace;
                return string.Join("_", parts);
            }
            return hullId;
        }

        private static float GetRacePriceCoef(PlanetData planet, GalaxyConfig galaxyConfig)
        {
            if (galaxyConfig?.Races == null || planet.Race == null) return 1f;
            if (galaxyConfig.Races.TryGetValue(planet.Race, out var race))
                return Mathf.Max(0.1f, race.PriceCoef);
            return 1f;
        }

        /// <summary>
        /// Инициализирует параметры планеты (население, экономика, политстрой, техуровень)
        /// и наполняет магазины. Вызывается из генератора после применения CustomProperties.
        /// Не трогает необитаемые планеты (Race == "None").
        /// </summary>
        public static void InitPlanetData(PlanetData planet, GalaxyGenerationContext ctx)
        {
            if (ctx == null) return;
            var galaxyConfig = ctx.Config;
            var equipConfig  = ctx.ItemsConfig;
            var itemsConfig  = ctx.ItemsConfig;
            var planetCfg = galaxyConfig?.Planets;
            if (planetCfg == null) return;

            bool inhabited = planet.Race != null &&
                             !string.Equals(planet.Race, GalaxyConstants.RACE_NONE_KEY,
                                 System.StringComparison.OrdinalIgnoreCase);

            // --- Политический строй ---
            if (planetCfg.GovernmentTypes?.Count > 0)
            {
                var govKeys = new System.Collections.Generic.List<string>(planetCfg.GovernmentTypes.Keys);
                planet.Settlement.Government = inhabited
                    ? govKeys[UnityEngine.Random.Range(0, govKeys.Count)]
                    : govKeys[0];
            }
            else
            {
                planet.Settlement.Government = inhabited ? "Anarchy" : "None";
            }

            if (!inhabited) return;

            // --- Тип экономики ---
            var econKeys = planetCfg.EconomyTypes?.Count > 0
                ? new System.Collections.Generic.List<string>(planetCfg.EconomyTypes.Keys)
                : null;
            planet.Settlement.EconomyType = econKeys != null
                ? econKeys[UnityEngine.Random.Range(0, econKeys.Count)]
                : "Mixed";

            // --- Технический уровень ---
            // Базовый диапазон 1-8, масштабированный на TechGrowthCoef экономики
            float techCoef = 1f;
            if (planetCfg.EconomyTypes != null &&
                planetCfg.EconomyTypes.TryGetValue(planet.Settlement.EconomyType, out var econCfg))
                techCoef = econCfg.TechGrowthCoef;
            int techBase = Mathf.RoundToInt(UnityEngine.Random.Range(1f, 5f) * techCoef);
            planet.Settlement.TechLevel = Mathf.Clamp(techBase, 1, 8);

            // --- Население ---
            if (planetCfg.Sizes != null &&
                planetCfg.Sizes.TryGetValue(planet.Size, out var sizeData) &&
                sizeData.Population?.Length >= 2 && sizeData.Population[1] > 0)
                planet.Settlement.Population = UnityEngine.Random.Range(sizeData.Population[0], sizeData.Population[1] + 1);

            // --- Магазин товаров (из GalaxyConfig.Goods с полным расчётом цен) ---
            if (galaxyConfig?.Goods != null)
                TradeSystem.InitPlanetShop(planet, galaxyConfig);
            else if (itemsConfig != null)
            {
                // Запасной вариант, если Goods не заданы в GalaxyConfig
                foreach (var kv in itemsConfig.EnumerateByKind(ItemKind.Goods))
                {
                    int buy  = Mathf.RoundToInt(kv.Value.BasePrice * GetRacePriceCoef(planet, galaxyConfig));
                    int sell = Mathf.RoundToInt(buy * 0.7f);
                    int stock = UnityEngine.Random.Range(50, 201);
                    planet.Settlement.Shop.Goods[kv.Key] = new ShopGoodEntry
                        { Stock = stock, BuyPrice = buy, SellPrice = sell };
                }
            }

            // --- Магазин оборудования ---
            EquipmentShopSystem.InitialFill(planet, ctx);
        }
    }
}
