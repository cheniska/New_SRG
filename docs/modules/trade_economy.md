# Trade & Economy

Модули: `Systems/TradeSystem.cs`, `Systems/InflationSystem.cs`, `Systems/EquipmentShopSystem.cs`,
`Systems/PlanetaryEventSystem.cs`, `Systems/PlanetaryTechSystem.cs`, `Systems/GovernmentChangeService.cs`,
`Systems/EconomicLog.cs`, `Systems/ShopService.cs` (фасад UI/AI),
`Galaxy/Models/*.cs` (`PlanetShop`, `ShopGoodEntry`, `PlanetEquipmentShop`).

Связанные документы: [`economy_implementation_plan.md`](../design/economy_implementation_plan.md) (что сделано / отложено),
[`planet_formulas.md`](../design/planet_formulas.md), [`planetary_science_system.md`](../design/planetary_science_system.md).

---

## Назначение

Экономическая симуляция: производство и потребление товаров на планетах, динамические цены
с учётом стока/инфляции/расы/правительства/типа экономики, легальность товаров (контрабанда),
магазин оборудования с реролл-обновлением, шок-события (войны, революции), технологический
прогресс (planetary science), логирование всех денежных операций.

---

## Публичный интерфейс

### `TradeSystem` (static)

```csharp
public const int TickStrideTurns = 3;     // экономика тикает каждые 3 хода (дельта × stride)

// Названия и цены.
public static string GetDisplayName(string goodId, GalaxyConfig cfg);
public static int CalculateBuyPrice(PlanetData, string goodId, int currentStock, GalaxyConfig);
public static int CalculateSellPrice(PlanetData, string goodId, int currentStock, GalaxyConfig);
public static int GetBaseStock(PlanetData, string goodId, GalaxyConfig);
public static bool IsLegal(PlanetData, string goodId, GalaxyConfig);

// Инициализация магазина при генерации.
public static void InitializeShop(PlanetData planet, GalaxyConfig cfg);

// Производство/потребление за раз в TickStrideTurns ходов.
public static void TickPlanet(PlanetData planet, GalaxyData galaxy, GalaxyConfig cfg);

// Production delta (стохастический модификатор) — реализовано в этапе 2 экономики.
public static float ComputeProductionDelta(PlanetData planet, string goodId, GalaxyConfig cfg);

// Цены покупки/продажи NPC-торговцами (для TraderAI).
public static int CalculateBuyPriceForNpc(...)  /* аналогично, без UI-побочек */;
```

### `InflationSystem` (static)

```csharp
public enum ShockType { War, Revolution }

public static void TickMonthly(GalaxyData galaxy, GalaxyConfig cfg);   // CurrentTurn % 30 == 0
public static void ApplyShock(GalaxyData galaxy, GalaxyConfig cfg, ShockType type);
public static float GetFactor(GalaxyData galaxy);                       // fallback = 1.0
```

### `ShopService` (static — фасад магазина для UI/AI)

См. [`equipment_and_pull.md`](equipment_and_pull.md#shopservice-static--фасад-магазина-планеты).

### `EquipmentShopSystem` (static)

```csharp
public static void RefreshShop(PlanetData planet, GalaxyData galaxy, GalaxyConfig);
   // Раз в RefreshIntervalDays днях обновляет ассортимент: дроп старых, спавн новых
   // через ItemFactory с учётом TechLevel планеты и расы

public static List<ItemInstance> GetCurrentStock(PlanetData planet);
public static bool TryBuyEquipment(ShipData buyer, PlanetData planet, string itemUid, GalaxyConfig);
public static bool TrySellEquipment(ShipData seller, PlanetData planet, ItemInstance item, GalaxyConfig);
```

### `PlanetaryEventSystem` (static)

```csharp
public static void TickPlanet(PlanetData planet, GalaxyData galaxy, GalaxyConfig);
   // Генерирует случайные события (война, революция, эпидемия и т.п.) с учётом
   // вероятностей из EventsConfig. Меняет Government, Population, ApplyShock в InflationSystem.

public static void ApplyEvent(PlanetData planet, GalaxyData, GalaxyConfig, string eventId);
```

### `PlanetaryTechSystem` (static)

```csharp
public static void TickPlanet(PlanetData planet, GalaxyData galaxy, GalaxyConfig);
   // Накопление science points → разблокирование изобретений (PlanetaryScience).
   // 7 категорий: см. project_planetary_science memory note.
```

### `GovernmentChangeService` (static)

```csharp
public static void ChangePlanetGovernment(PlanetData, string newGovernment, GalaxyData, GalaxyConfig);
   // Меняет правительство планеты. Обновляет легальность товаров, ставки налогов,
   // отношения с фракциями. Триггерится из PlanetaryEventSystem (Revolution).
```

### `EconomicLog` (static)

Буферизованный лог денежных/торговых событий для пост-аналитики.

```csharp
public const int FlushThreshold = ???;    // авто-flush при достижении

public static void Trade(int turn, string actor, string opcode, string details);
public static void Inflation(int turn, string opcode, string details);
public static void Shop(int turn, string planet, string opcode, string details);

public static string Safe(string s);      // экранирование строк
public static void Flush();               // принудительный сброс на диск
```

Авто-flush:
- При достижении `FlushThreshold` буфера;
- Из `GalaxyManager.ExecuteTurnCalculation` раз в 30 ходов;
- В `GalaxyManager.OnApplicationQuit`.

---

## Зависимости

```
TradeSystem
   ├─► GalaxyConfig (Goods, Trade, Races, Planets.EconomyTypes/GovernmentTypes, Events, Inflation)
   ├─► InflationSystem.GetFactor
   ├─► EconomicLog.Trade
   └─► PlanetData.{Shop, BaseStockCache, Race, Government, EconomyType, Size, ...}

InflationSystem
   ├─► GalaxyData.InflationFactor
   ├─► GalaxyConfig.Inflation.{MonthlyDelta, MaxFactor, WarBoost, RevolutionBoost}
   └─► EconomicLog.Inflation

ShopService
   ├─► TradeSystem.CalculateBuyPrice / CalculateSellPrice / GetDisplayName
   └─► ShipData.{Money, Inventory.AddStack/TakeStack}

PlanetaryEventSystem
   ├─► InflationSystem.ApplyShock
   ├─► GovernmentChangeService.ChangePlanetGovernment
   ├─► PlanetData.{Population, Government, EconomyType, Race}
   └─► GalaxyConfig.Events (вероятности и эффекты)

EquipmentShopSystem
   ├─► ItemFactory (для спавна нового оборудования)
   ├─► InflationSystem.GetFactor (для цен)
   └─► PlanetData.EquipmentShop

PlanetaryTechSystem
   └─► GalaxyConfig (Inventions, Categories)
```

---

## Алгоритмы и формулы

### `TradeSystem.ComputeBasePrice`

(см. реализацию `TradeSystem.cs`)

```
base = GoodConfig.BasePrice
inflate = InflationFactor (1.0 .. cfg.Inflation.MaxFactor)
sizeMul = Trade.PriceMultiplierBySize[planet.Size]   // обычно 1.0 ± 0.2
econMul = Planets.EconomyTypes[planet.EconomyType].Trade.GoodPriceModifiers[goodId]  // напр. Mining: Minerals=0.6
govMul = Planets.GovernmentTypes[planet.Government].Trade.GoodPriceModifiers[goodId]
raceMul = Races[planet.Race].Trade.GoodPriceModifiers[goodId]

// Влияние стока: чем меньше товара, тем выше цена
baseStock = GetBaseStock(planet, goodId, cfg)
scarcityFactor = clamp(2.0 - currentStock / baseStock, MinScarcity, MaxScarcity)

price = base * inflate * sizeMul * econMul * govMul * raceMul * scarcityFactor
```

### `TradeSystem.CalculateBuyPrice` vs `CalculateSellPrice`

```
buyPrice = ComputeBasePrice(...)
sellPrice = ComputeBasePrice(...) * 0.7    // планета платит игроку 70% от своей buy-цены
```

Контрабанда: если `!IsLegal(planet, goodId, cfg)` — цена умножается на
`cfg.Trade.ContrabandPriceMultiplier` (default 3.0).

### `TradeSystem.IsLegal`

```
races = cfg.Races[planet.Race]
если races == null → return true (нет таблицы — всё легально)
bannedByGov = races.Trade.BannedGoodsByGovernment
если bannedByGov == null или planet.Government пуст → return true
bannedList = bannedByGov[planet.Government]
return bannedList == null или !bannedList.Contains(goodId)
```

### `TradeSystem.GetBaseStock` (с кэшем)

```
если planet.BaseStockCache.TryGetValue(goodId, out cached) → return cached

baseStock = Trade.BaseStockBySize[planet.Size] ?? 200
mod = Planets.EconomyTypes[planet.EconomyType].Trade.GoodStockModifiers[goodId] ?? 1
result = round(baseStock * mod)

planet.BaseStockCache[goodId] = result
return result
```

Размер/экономика после генерации не меняются → кэш живёт всё время существования планеты.

### `TradeSystem.TickPlanet` (раз в `TickStrideTurns`=3 ходов)

```
foreach (goodId, entry) in planet.Shop.Goods:
    productionDelta = ComputeProductionDelta(planet, goodId, cfg)
    entry.Stock = clamp(entry.Stock + productionDelta * TickStrideTurns,
                        0, entry.MaxStock)
    RecalculatePrices(entry, planet, goodId, cfg)
    EconomicLog.Shop(turn, planet, "PROD", ...)
```

`ComputeProductionDelta` зависит от: производственная мощность планеты по этому товару
(econ type + size + population), потребление (тот же набор модификаторов), случайный jitter
±20%.

### `InflationSystem.TickMonthly`

Вызывается из `GalaxyData.GalaxyNextDay` при `CurrentTurn % 30 == 0`:
```
before = galaxy.InflationFactor
galaxy.InflationFactor = min(before + MonthlyDelta, MaxFactor)
EconomicLog.Inflation(turn, "MONTHLY_TICK", "factor_before=..., factor_after=..., delta=...")
```

### `InflationSystem.ApplyShock`

```
boost = type == War → cfg.Inflation.WarBoost
        type == Revolution → cfg.Inflation.RevolutionBoost
если boost <= 0 → return
galaxy.InflationFactor = min(before + boost, MaxFactor)
лог
```

### `EquipmentShopSystem.RefreshShop`

```
если turn - planet.EquipmentShop.LastRefreshDay < RefreshIntervalDays → return

// дроп старого ассортимента
planet.EquipmentShop.AvailableItems.Clear()

// генерация нового
budget = baseSlots * tier_multiplier_for_planet
foreach slot in [0..budget]:
    category = pickCategory(planet.Race, planet.TechLevel)
    item = ItemFactory.Create(category, tier=clamp(planet.TechLevel ± 1, ...), race=planet.Race, ...)
    planet.EquipmentShop.AvailableItems.Add(item)
planet.EquipmentShop.LastRefreshDay = turn
```

Этап реализован в июне 2026 — см. memory `project_economy_implementation`.

---

## Константы и настройки

| Имя | Источник | Назначение |
|---|---|---|
| `TickStrideTurns` | `TradeSystem` const = 3 | Период производства/потребления в ходах |
| `0.7f` | `CalculateSellPrice` literal | Sell = 70% Buy |
| `cfg.Trade.ContrabandPriceMultiplier` | конфиг (default 3.0) | Наценка на контрабанду |
| `cfg.Trade.ContrabandRelationPenalty` | конфиг (default -20) | Штраф к отношениям за покупку контрабанды |
| `cfg.Inflation.MonthlyDelta` | конфиг | Прибавка к InflationFactor каждые 30 ходов |
| `cfg.Inflation.MaxFactor` | конфиг | Потолок инфляции |
| `cfg.Inflation.WarBoost / RevolutionBoost` | конфиг | Шок-приросты |
| `cfg.Trade.BaseStockBySize` | конфиг | `{Small:100, Mid:200, Large:400}` примерно |
| `30` | `InflationSystem.TickMonthly` trigger | Период «месяца» в ходах (магия в `GalaxyNextDay`) |
| Модификаторы цен/стока | `cfg.Planets.EconomyTypes/GovernmentTypes` + `cfg.Races` | Балансные множители по таксономии |

---

## Внутренняя структура данных

### `PlanetShop` (PlanetData.Shop)

```
Goods : Dictionary<string, ShopGoodEntry>   // goodId → entry
```

### `ShopGoodEntry`

```
Stock : int           // текущий запас (изменяется TickPlanet / Buy / Sell)
MaxStock : int        // потолок (вычислен при InitializeShop)
BuyPrice : int        // цена покупки игроком (пересчёт при изменении Stock)
SellPrice : int       // цена продажи планете
BasePrice : int       // эталон без модификаторов (для аналитики)
Legal : bool          // (опционально) кэш IsLegal
```

### `PlanetEquipmentShop` (PlanetData.EquipmentShop)

```
AvailableItems : List<ItemInstance>
LastRefreshDay : int
```

### `GalaxyData.InflationFactor`

`float` ∈ [1.0 .. cfg.Inflation.MaxFactor]. Применяется ко всем ценам в `ComputeBasePrice` и
при спавне предметов в `ItemFactory.Create`.

### `PlanetData` экономические поля

```
Size : string                   // Small / Mid / Large (определяет BaseStockBySize)
EconomyType : string            // Agricultural / Industrial / Mining / Trade / Research
Government : string             // Democracy / Monarchy / Junta / Anarchy / Theocracy / ...
Race : string                   // (для BannedGoodsByGovernment и TechLevel)
TechLevel : int                 // (1..10) — для EquipmentShop ассортимента
Population : long
BaseStockCache : Dictionary<string, int>   // ленивый кэш GetBaseStock
Shop : PlanetShop
EquipmentShop : PlanetEquipmentShop
TraderTargetPlanetUid : string  // (на корабле — для TraderAI)
TraderCargoGoodId : string      // (на корабле)
TraderCargoWeight : int         // (на корабле)
TraderIdleTurnsLeft : int       // (на корабле)
```

---

## Известные ограничения / TODO

1. **NPC-транспорт реализован, но не все этапы плана**: согласно memory `project_economy_implementation`,
   этапы 1, 2, 3, 6 реализованы, **4 и 5 отложены**. Среди отложенных — динамическая корректировка
   цен из истории торгов, и пер-расовые торговые «тренды». См. `economy_implementation_plan.md`
   для деталей.
2. **`InflationFactor` — единый глобальный**. Нет per-system или per-planet инфляции, что упрощает
   модель, но не позволяет смоделировать «изолированную экономику внутри блокированного сектора».
3. **`EconomicLog.FlushThreshold` зашит в EconomicLog.cs** — нет конфигурации частоты.
4. **`TickStrideTurns = 3`** значит экономика обновляется каждые 3 хода. Если игрок будет много
   стоять на одной планете, прироста товаров между его покупками может не быть — это by design
   (балансная инвестиция в время).
5. **`PlanetaryEventSystem` отвечает за войны и революции**, но текущая частота событий — конфигурная.
   Если события появляются «слишком часто или слишком редко» — балансится через `cfg.Events.*`.
6. **`PlanetaryTechSystem` (planetary science)** реализован по диздоку (см. memory
   `project_planetary_science`): 7 категорий, гибридный пул 80% общий + 20% расовый. Изобретения
   двигают ПТУ и ГТУ — это меняет ассортимент EquipmentShop.
