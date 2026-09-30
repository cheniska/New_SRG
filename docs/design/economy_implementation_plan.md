# План реализации: торговля, транспорты, события, правительство, магазин оборудования, инфляция

Дата: 2026-06-23. Связано с диздоком `planetary_science_system.md` (наука отложена).

## Текущее состояние (короткий аудит)

| Подсистема | Что есть | Что отсутствует |
|---|---|---|
| TradeSystem | Расчёт цен с Race/Eco/Gov/Event-модификаторами, гиперболический stock-modifier, контрабанда | Ежедневный тик, ProductionDelta, дрейф, инфляция |
| Магазин товаров | `PlanetData.Shop.Goods` с записями Stock/BuyPrice/SellPrice | Динамика стока, обмен между планетами |
| Магазин оборудования | Однократное наполнение в `ItemFactory.GenerateInitialShop` (фикс размер `EquipmentShopSize=5`) | Еженедельное обновление, разбивка по типам, TL-разброс, влияние Eco/Gov |
| События планеты | `ActivePlanetEvent` + `PlanetaryTechSystem.ApplyEvent/RemoveEvent` | Триггеры/каскады/условия срабатывания; никто события не запускает автоматически |
| Правительство | Поле `Government: string`, читается в формулах. Меняется только при ручной правке | Смена через революцию |
| NPC транспорты | `NpcBrain` с personality+combat. Транспорт = `CombatClass.Civilian`, ведёт себя нейтрально | Торговое поведение (купить-везти-продать) |
| Инфляция | Нет | Нет |

---

## Подсистемы и зависимости

```
                          ┌──────────────────┐
                          │ Inflation (galaxy)│  → множитель ко всем ценам
                          └────────┬─────────┘
                                   │
        ┌──────────────────────────┼──────────────────────────┐
        ▼                          ▼                          ▼
┌────────────────┐         ┌────────────────┐         ┌────────────────┐
│ Trade TickDaily│◀─────── │ProductionDelta │         │ EquipmentShop  │
│  + price drift │         │ (per planet)   │         │  TickWeekly    │
└────────┬───────┘         └────────────────┘         └────────────────┘
         │                          ▲
         ▼                          │
┌────────────────┐         ┌────────────────┐
│ Planet stock   │◀────────│ NPC transports │
│  ±модификации  │         │  (real cargo)  │
└────────┬───────┘         └────────────────┘
         │
         ▼ (триггеры по цене/стоку)
┌────────────────┐         ┌────────────────┐
│ Event system   │────────▶│ Government     │
│ (Revolution,   │         │   change       │
│  Epidemic,...) │         └────────────────┘
└────────────────┘
```

**Порядок реализации продиктован зависимостями:**

1. **Inflation** — фундамент, на нём держатся цены. Маленькая, простая.
2. **ProductionDelta + Trade TickDaily** — суточная динамика стока/цены.
3. **EquipmentShop TickWeekly** — независимая, можно параллельно.
4. **Event triggers + chains** — после п. 2 (триггеры читают цены).
5. **Government change via event** — после п. 4.
6. **NPC transport trade AI** — последнее, на готовый рынок.

---

## Этап 1. Инфляция

### Дизайн
Простейшая time-based модель: `Galaxy.InflationFactor: float = 1.0`, прирастает раз
в месяц. Все цены при расчёте умножаются на этот коэффициент. Дополнительно
кратковременные шок-события (война в системе, революция) добавляют локальный bump.

### Параметры (в `GalaxyConfig.Economy`)

Целевая длительность игры — ~40 лет (480 месяцев). Хотим инфляцию ~×2 за это время,
**линейно** (по запросу пользователя).

```json
"Inflation": {
  "MonthlyDelta": 0.0021,         // +0.21% от 1.0 ежемесячно → +1 за 40 лет → factor=2.0
  "MaxFactor": 5.0,               // hard cap (на случай 200-летних игр)
  "WarBoost": 0.02,               // +0.02 разово на каждое объявление войны
  "RevolutionBoost": 0.01         // +0.01 на каждую революцию
}
```

Линейная модель: `factor += MonthlyDelta` каждый месяц (не compound). Шоки добавляют
дискретный bump к factor.

### Код
- Поле `GalaxyData.InflationFactor: float = 1.0` (сериализуемое).
- `InflationSystem.TickMonthly(galaxy, cfg)` — вызов из `GalaxyData.GalaxyNextDay`
  при `CurrentTurn % 30 == 0`.
- `TradeSystem.ComputeBasePrice` — финальный множитель `* galaxy.InflationFactor`.
- `ItemFactory.Create` / `EquipmentShop` — стоимость item × InflationFactor (на момент
  спавна предмета, фиксируется в `ItemInstance.BasePrice`).
- Hooks для шок-апдейтов: `InflationSystem.ApplyShock(galaxy, type, amount)` — вызывается
  из event handlers.

### Влияние на сейвы
Старые сейвы без `InflationFactor` → default 1.0 (no-op). Никакой миграции не нужно.

### Размер: ~50 строк кода, 1–2 часа.

---

## Этап 2. ProductionDelta + Trade TickDaily

### Дизайн (двухслойная экономика из §12.2 диздока)

**Слой 1 — внутренний поток:**

Каждый ход на каждой планете для каждого товара:
```
delta = EconomyDelta(EconomyType, goodId)        // базовый
      + RaceDelta(Race, goodId)                  // модификатор расы
      + Σ EventDelta(event, goodId)              // активные события
      * GovernmentMultiplier(Gov, goodId)        // правительство
      * PopulationScale                          // ~ log(Population)/log(1M)
planet.Shop.Goods[goodId].Stock += delta
clamp(Stock, 0, MaxStock)                        // потолок чтоб не накопить ∞
```

**Слой 2 — пересчёт цен после изменения стока:**

`TradeSystem.RecalculatePrices(planet, cfg)` (уже есть, нужно вызывать в конце тика).

### Параметры (в `GalaxyConfig`)

```json
"Planets": {
  "EconomyTypes": {
    "Agricultural": {
      "GoodProductionDelta": { "Food": 5.0, "Alcohol": 3.0, "Medicine": -1.0, "Equipment": -2.0 }
    },
    "Industrial": {
      "GoodProductionDelta": { "Equipment": 3.0, "Weapons": 2.0, "Food": -1.5, "Minerals": -2.0 }
    },
    "Mixed": {
      "GoodProductionDelta": { "Food": 1.0, "Equipment": 1.0, "Medicine": 0.5 }
    }
  }
},
"Races": {
  "Faean": {
    "GoodProductionDelta": { "Medicine": 1.0, "BioGoods": 1.5 }
  },
  ...
}
```

### Код
- `EconomyTypeConfig.GoodProductionDelta: Dictionary<string, float>` — расширение
  существующего конфига.
- `RaceConfig.GoodProductionDelta: Dictionary<string, float>`.
- `EventConfig.Trade.GoodStockDelta: Dictionary<string, float>` (новое поле, отличное
  от существующего `GoodPriceModifiers`).
- Новая функция `TradeSystem.TickDaily(planet, cfg, inflationFactor)`:
  - Накапливает delta по слоям.
  - Применяет к стоку, клампит.
  - Вызывает `RecalculatePrices`.
- Вызов из `GalaxyData.GalaxyNextDay` в цикле планет (перед `PlanetaryTechSystem.TickAll`).
- `MaxStock` — в конфиге `Trade.MaxStockMultiplier` (например `× 5` от базы).

### Балансировка
- На стерильно-аграрной планете без обмена: за месяц Food накопит +150 → цена упадёт
  до min (контр-эффект). Хорошо — это создаёт нужду в торговцах.
- На индустриальной планете без обмена: Food уйдёт в 0 → цена скакнёт к max → бунт
  через событие (см. этап 4).

### Размер: ~150 строк, 2–3 часа.

---

## Этап 3. EquipmentShop TickWeekly

### Дизайн (по запросу пользователя)

- Ассортимент = по каждому типу оборудования 0–4 предмета, TL ∈ `[max(1, PTU-3), PTU]`.
- Раз в 7 дней: сравнить текущий состав с целевым, выровнять (убрать лишнее, добавить
  недостающее).
- Eco/Gov-модификаторы влияют на **target count** для каждого типа.

### Алгоритм

```python
TickWeekly(planet, ctx):
    if (planet.Rnd + galaxy.Day) % 7 != 0: return

    for category in [Hull, FuelTanks, Engine, Radar, Scaner,
                     RepairRobot, CargoHook, DefGenerator, Weapons]:
        baseTarget = Random(0..4)            # естественный разброс
        target = baseTarget
              + EconomyMod(planet.Economy, category)    # см. таблицу
              + GovMod(planet.Government, category)
        target = clamp(target, 0, 5)

        current = CountInShop(planet, category)
        diff = target - current

        if diff > 0:
            for i in range(diff):
                tl = Random(max(1, planet.PTU - 3), planet.PTU)
                item = ItemFactory.Create(category, ..., tl, ...)
                shop.Items[item.Uid] = item

        elif diff < 0:
            # Удаляем -diff случайных предметов этой категории
            removeCount = -diff
            candidates = shop where category matches
            for i in range(removeCount):
                pick = Random(candidates)
                shop.Items.Remove(pick.Uid)
                candidates.Remove(pick)
```

### Eco/Gov-модификаторы (черновик)

| | Hull | Engine | Weapons | Radar/Scanner | RepairRobot | DefGen |
|---|---|---|---|---|---|---|
| Agricultural | -1 | 0 | -1 | 0 | +1 | 0 |
| Industrial | +1 | +1 | 0 | 0 | 0 | +1 |
| Mixed | 0 | 0 | 0 | 0 | 0 | 0 |
| Anarchy | -1 | 0 | +1 | -1 | 0 | -1 |
| Despot | 0 | 0 | +2 | 0 | 0 | +1 |
| Monarchy | 0 | 0 | 0 | 0 | 0 | 0 |
| Democracy | +1 | 0 | -1 | +1 | 0 | 0 |
| Communism | 0 | +1 | 0 | 0 | +1 | +1 |

(Тюнить позже; вынести в конфиг `EquipmentShopMods`.)

### Код
- Удалить из `ItemFactory.GenerateInitialShop` старую логику с `EquipmentShopSize`.
- Новый `EquipmentShopSystem.cs`:
  - `InitialFill(planet, ctx)` — однократное наполнение при генерации галактики
    (по тем же правилам что TickWeekly, но с full converge до target).
  - `TickWeekly(planet, ctx)` — еженедельное обновление.
- Вызов `InitialFill` — из `GalaxyGenerator.cs` после генерации планеты.
- Вызов `TickWeekly` — из `GalaxyData.GalaxyNextDay` после `TradeSystem.TickDaily`.

### Цена предметов
Умножается на текущий `Galaxy.InflationFactor` в момент спавна (через `ItemFactory`).
То есть в магазине старого галактического года предметы дешевле, чем новые. Это
особенно заметно при долгой игре.

### Размер: ~200 строк, 3–4 часа.

---

## Этап 4. Event triggers + chains

### Дизайн

События должны иметь:
- **Триггеры** — условия, при которых событие может само себя запустить.
- **Эффекты** — что делает (уже частично есть: PTU, Trade-модификаторы).
- **Цепочки** — какое событие может следовать.

### Новые поля в `EventConfig`

```json
"Unrest": {
  "DisplayName": "Беспорядки",
  "DurationDays": [7, 14],
  "Triggers": [
    { "Type": "PriceAbove", "GoodId": "Food", "MultiplierVsBase": 2.0, "ForDays": 5 },
    { "Type": "PriceAbove", "GoodId": "Medicine", "MultiplierVsBase": 2.5, "ForDays": 7 }
  ],
  "TriggerLogic": "Or",
  "Effects": {
    "Trade": { "GoodPriceModifiers": { "Food": 1.3, "Alcohol": 1.5 } },
    "PTU": { "GrowthMultiplier": 0.5 }
  },
  "ChainOnExpire": { "EventId": "Protests", "Chance": 0.4 }
},
"Protests": {
  "DurationDays": [10, 20],
  "Effects": { ... },
  "ChainOnExpire": { "EventId": "Revolution", "Chance": 0.25 }
},
"Revolution": {
  "DurationDays": [3, 5],
  "Effects": { ... },
  "ImmediateActions": [
    { "Type": "ChangeGovernment", "Mode": "Random" }
    // или "Mode": "Weighted", "Weights": { "Anarchy": 0.3, "Democracy": 0.4, "Despot": 0.3 }
  ]
}
```

### Триггер-типы (extensible)

| Type | Поля | Описание |
|---|---|---|
| `PriceAbove` | GoodId, MultiplierVsBase, ForDays | Цена > X% от BasePrice в течение N дней |
| `PriceBelow` | то же | Дефляция/обвал |
| `StockBelow` | GoodId, Threshold | Дефицит |
| `DaysSinceLast` | EventId, Days | Не было события N дней |
| `GovernmentIs` | Gov | Текущий тип правления |
| `GTUAbove` | Level | Технический порог |
| `RandomChance` | Chance, PerDays | Базовая случайность |

### Код
- `EventConfig` расширяется (Triggers, ChainOnExpire, ImmediateActions).
- Новый `PlanetaryEventSystem.cs`:
  - `TickPlanet(planet, cfg)` — ежедневный проход:
    - тик длительности (с миграцией из `PlanetaryTechSystem.TickEvents`);
    - при истечении — применить ChainOnExpire;
    - проверить все возможные триггеры — запустить если выполнено.
  - `EvaluateTrigger(trigger, planet)` — диспатч по типу.
  - `ApplyEvent` — мигрирует из `PlanetaryTechSystem`, расширяется на ImmediateActions.
- Track price/stock history для триггеров `*ForDays` — кольцевой буфер 30 дней
  в `PlanetData.PriceHistory: Dictionary<string, RingBuffer<int>>` (только основные товары).

### Размер: ~300 строк, 4–6 часов.

---

## Этап 5. Government change через события

Само событие Revolution (см. этап 4) умеет применять `ChangeGovernment`. Здесь — только
расширение:

- Per-race таблица весов для нового правительства: `RaceConfig.GovTransitionWeights`.
- Уведомление в `GameConsole` («На планете X произошла революция — новый строй: Y»).
- Сброс отношений с рейнджерами (если у нас есть relation-state на планете — TBD).
- `Galaxy.InflationFactor` получает `RevolutionBoost` через `InflationSystem.ApplyShock`.

### Простые предпосылки революции (из чата)

Вместо отдельного «индикатора напряжённости» используем существующие триггеры (этап 4):
- `PriceAbove(Food, 2.0×, 10 дней)` → запускает `Unrest`.
- `Unrest` истекает → 40% шанс `Protests`.
- `Protests` истекает → 25% шанс `Revolution`.

Эффективная вероятность: 10% от прокатившейся продовольственной волны → революция.
Это достаточно редко чтобы не раздражать, но достаточно часто чтобы галактика
менялась.

### Размер: ~80 строк (надстройка над этапом 4), 1–2 часа.

---

## Этап 6. NPC transport trade AI

### Дизайн

Транспорт (`CombatClass.Civilian` + флаг `Personality.IsTrader`) при простое на планете:

```
PlanRoute(ship):
    homePlanet = ship.LandedPlanetUid
    nearby = systems within 3-5 jumps from current star
    bestDeal = null
    bestMargin = 0

    for each good in homePlanet.Shop:
        if homePlanet.Shop[good].Stock < threshold: continue   # нечего покупать
        buyPrice = homePlanet.Shop[good].BuyPrice
        for each targetPlanet in nearby.Planets:
            sellPrice = targetPlanet.Shop[good].SellPrice
            margin = (sellPrice - buyPrice) * cargo_capacity - fuel_cost
            if margin > bestMargin:
                bestMargin = margin
                bestDeal = (good, targetPlanet)

    if bestDeal exists and bestMargin > minProfitThreshold:
        buy(bestDeal.good, cargo_capacity)
        ship.TargetPlanet = bestDeal.targetPlanet
        ship.CurrentRouteIntent = "Sell"
    else:
        wait 5-10 days, try again
```

### Что меняется в коде

1. **ShipData / Inventory:**
   - Уже есть `ShipInventory` — расширить для хранения goods (не только items).
   - Поле `TraderState: TraderState` (текущий контракт: что везёт, куда, цена покупки).

2. **NpcBrain / Personality:**
   - Расширить `ShipPersonality.IsTrader: bool`.
   - В `DecideTargetForShip` для трейдеров приоритет — торговый маршрут.

3. **Новый файл `Assets/Scripts/NpcAI/TraderAI.cs`:**
   - `PlanRoute(ship, galaxy, cfg)` — анализ ближайших систем.
   - `OnArriveAtPlanet(ship, planet)` — продажа груза, поиск нового рейса.
   - `OnLeavePlanet(ship, planet)` — закупка.

4. **Перформанс:**
   - Анализ 3–5 систем × 10 товаров × ~5 планет на систему = ~250 операций на рейс.
   - Рейс ~30–100 ходов → амортизированно ~3-8 операций/ход/транспорт.
   - При 100 транспортах в галактике: ~500 op/ход. OK для не-real-time.

5. **Деньги планеты:**
   - Решено: **бездонная казна** (планета всегда платит без ограничений).
   - Ограничитель торговли — только `Stock` (нельзя купить чего нет, нельзя продать
     сверх `MaxStock`).
   - Это сильно упрощает логику: не нужно отслеживать `PlanetData.Money` и источники
     дохода казны.

6. **Спавн транспортов:**
   - Решено: **привязаны к домашней планете** (`ShipData.HomePlanetUid` уже есть).
   - Каждая обитаемая планета получает базовое число транспортов:
     - Маленькая (Population < 100k) → 1.
     - Средняя (100k–1M) → 2.
     - Крупная (> 1M) → 3.
     - Industrial Economy → +1 (больше торговли).
   - При гибели — респавн через `30..60` ходов через `NpcSpawner` с правильным
     `HomePlanetUid`.
   - Если домашняя планета захвачена клингами → транспорт переходит в режим
     «искать новую базу» (присоединяется к ближайшей коалиционной планете того же
     Race) или сдаётся в утиль (TBD).

### Эффект на рынок

Это и есть **главный механизм равновесия**. Без транспортов:
- Аграрная планета: Food → max stock → min price (постоянно).
- Индустриальная: Food → 0 stock → max price + риск Unrest.

С транспортами:
- Транспорт видит маржу, везёт еду из аграрной в индустриальную.
- Уровни выравниваются в области с активной торговлей.
- Изолированные/осаждённые системы остаются с экстремальными ценами → игроку выгодно
  туда возить.

### Размер: ~500 строк, 5–7 часов (плюс playtesting/балансировка).

---

## Сводка по этапам

| # | Этап | LOC | Время | Зависит от |
|---|---|---|---|---|
| 1 | Inflation | ~50 | 1–2 ч | — |
| 2 | ProductionDelta + TickDaily | ~150 | 2–3 ч | 1 |
| 3 | EquipmentShop TickWeekly | ~200 | 3–4 ч | 1 |
| 4 | Event triggers + chains | ~300 | 4–6 ч | 2 |
| 5 | Government change | ~80 | 1–2 ч | 4 |
| 6 | NPC transport AI | ~500 | 5–7 ч | 2, 3 |

**Итого: ~1280 LOC, ~16–24 часа чистого времени** (без учёта тестирования и
балансировки).

Этапы 1–3 можно делать параллельно (если бы было кому). Этапы 4–5 тесно связаны.
Этап 6 — наиболее «исследовательский», может растянуться.

---

## Открытые вопросы

### Решённые
- ✅ **Money планет** — бездонная казна, ограничитель только сток.
- ✅ **Длительность игры** — ориентир 40 лет, инфляция линейная до ×2.
- ✅ **Спавн транспортов** — привязаны к домашней планете (1–3 шт. зависит от Population
  + Economy).

### Открытые (не блокирующие, можно решить по ходу)
1. **Goods MaxStock** — фиксированный множитель к BasePrice, или per-good в
   `GoodConfig.MaxStock`? Дефолт — per-Economy + per-Size scaling (предложу при
   реализации этапа 2).
2. **Шок-инфляция** — только война/революция, или ещё и крупные галактические события?
   Дефолт — пока только эти два, добавим триггеры позже при необходимости.
3. **Деградация цен в магазинах оборудования** при `TickWeekly` — если предмет
   «застрял» 4+ недели — снижать цену? Дефолт — нет (предметы могут лежать долго,
   это нормально).
4. **Сохранение price history** — 30 дней × 8 товаров × ~200 планет = 48000 int.
   Дефолт — сериализуем (нужно для триггеров после загрузки сейва).
5. **Транспорт-сирота при захвате домашней планеты** — переходит на ближайшую коалицию
   той же расы, или сдаётся в утиль? Решим при реализации этапа 6.

---

## Следующий шаг

Все блокирующие вопросы решены. **Стартуем с этапа 1 (инфляция).**
