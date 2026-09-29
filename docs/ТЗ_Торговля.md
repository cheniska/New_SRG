# Техническое задание: Торговая система
## 1. Общее описание
Торговля — один из основных способов заработка. Игрок покупает товары дёшево на одних планетах и продаёт дорого на других. Торговля доступна:
- На обитаемых планетах (при отношении не хуже «нормального»)
- На космических станциях (будет реализовано)
## 2. Источники данных в GalaxyConfig
| Секция | Что содержит |
|--------|-------------|
| `Goods` | Список товаров, базовые цены |
| `Trade` | Глобальные параметры, формулы, штрафы, базовый объём по размеру |
| `Trade.Formulas` | Все математические формулы системы |
| `Races[id].Trade` | Потоварные модификаторы цены и запреты по типу правления |
| `Planets.EconomyTypes[id].Trade` | Потоварные модификаторы цены и объёма |
| `Planets.GovernmentTypes[id]` | Потоварные модификаторы цены |
| `Events[id].Trade` | Временные модификаторы цены при событии |
## 3. Товары (`Goods`)
"Goods": {
    "food":      { "DisplayName": "Продовольствие", "BasePrice": 22  },
    "medicine":  { "DisplayName": "Медикаменты",    "BasePrice": 30  },
    "technics":  { "DisplayName": "Техника",        "BasePrice": 60  },
    "luxury":    { "DisplayName": "Роскошь",        "BasePrice": 150 },
    "minerals":  { "DisplayName": "Минералы",       "BasePrice": 9   },
    "alcohol":   { "DisplayName": "Алкоголь",       "BasePrice": 30  },
    "arms":      { "DisplayName": "Оружие",         "BasePrice": 75  },
    "narcotics": { "DisplayName": "Наркотики",      "BasePrice": 300 }
}
## 4. Глобальные параметры торговли (`Trade`)
"Trade": {
    "ContrabandPriceMultiplier": 3.0,
    "MinPriceMultiplier": 0.4,
    "MaxPriceMultiplier": 3.0,
    "PriceStockCurve": "Hyperbolic",
    "ContrabandRelationPenalty": -20,
    "PrisonSentenceMonths": 6,
    "BaseStockBySize": {
        "Tiny":   50,
        "Small":  100,
        "Normal": 200,
        "Big":    400,
        "Giant":  700
    },
    "SpecialLegality": {
        "StationsAlwaysLegal": true,
        "NewMolizonContrabandGoods": ["food", "medicine"]
    },
    "Formulas": {
        "FinalPrice":     "BasePrice * PriceCoef * GoodPriceModifier_Race * GoodPriceModifier_Economy * GoodPriceModifier_Government * EventModifier * StockModifier",
        "ContrabandPrice":"FinalPrice * ContrabandPriceMultiplier",
        "StockModifier":  "Hyperbolic(currentStock, baseStock, MinPriceMultiplier, MaxPriceMultiplier)",
        "BaseStock":      "BaseStockBySize[size] * GoodStockModifier_Economy"
    }
}
### 4.1 Описание формул
**`FinalPrice`** — итоговая цена товара на планете. Все множители перемножаются:
| Множитель | Источник |
|-----------|----------|
| `BasePrice` | `Goods[id].BasePrice` |
| `PriceCoef` | `Races[id].PriceCoef` — общий коэффициент расы на все товары |
| `GoodPriceModifier_Race` | `Races[id].Trade.GoodPriceModifiers[good]` |
| `GoodPriceModifier_Economy` | `Planets.EconomyTypes[id].Trade.GoodPriceModifiers[good]` |
| `GoodPriceModifier_Government` | `Planets.GovernmentTypes[id].GoodPriceModifiers[good]` |
| `EventModifier` | `Events[id].Trade.GoodPriceModifiers[good]` всех активных событий — перемножаются |
| `StockModifier` | вычисляется из текущего остатка товара |
Если множитель для конкретного товара не задан в конфиге — считается равным `1.0`.
**`StockModifier`** — гиперболическая зависимость цены от текущего остатка:
- `currentStock == 0` → цена = `BasePrice * MaxPriceMultiplier`
- `currentStock == baseStock` → цена = `BasePrice * 1.0`
- `currentStock >> baseStock` → цена стремится к `BasePrice * MinPriceMultiplier`
- Тип кривой задаётся `PriceStockCurve`, может быть заменён без правки кода
**`ContrabandPrice`** — применяется поверх `FinalPrice`, если товар является контрабандой. Действует и на покупку, и на продажу.
**`BaseStock`** — начальный объём товара при генерации планеты:
- Берётся из `Trade.BaseStockBySize[planet.size]`
- Умножается на `GoodStockModifier_Economy` из `Planets.EconomyTypes[id].Trade.GoodStockModifiers[good]`
- Если модификатор не задан — `1.0`
## 5. Модификаторы в расах (`Races[id].Trade`)
Каждая раса хранит:
- `GoodPriceModifiers` — потоварные множители цены
- `BannedGoodsByGovernment` — запреты товаров по типу правления
**Правила `BannedGoodsByGovernment`:**
- Ключ — id правления из `Planets.GovernmentTypes`
- Значение — список запрещённых товаров при этом правлении
- Если правление не упоминается в списке — считается, что список запретов для него пустой
- Чтобы разрешить все товары при любом правлении — достаточно оставить `BannedGoodsByGovernment` пустым объектом `{}`
## 6. Модификаторы в экономиках (`Planets.EconomyTypes[id].Trade`)
`GoodStockModifiers` влияет только на начальный объём (`BaseStock`), не на цену напрямую.
## 7. Модификаторы в правлениях (`Planets.GovernmentTypes[id]`)
Правления содержат только `GoodPriceModifiers` — никаких флагов легальности. Легальность целиком определяется расой через `BannedGoodsByGovernment`.
## 8. Алгоритм расчёта цены и легальности
Для каждого товара на конкретной планете:
1. race    = Races[planet.raceId]
2. economy = Planets.EconomyTypes[planet.economyType]
3. gov     = Planets.GovernmentTypes[planet.governmentType]
// --- Легальность ---
4. bannedList = race.Trade.BannedGoodsByGovernment[planet.governmentType] ?? []
   isLegal = good не входит в bannedList

   // Специальные условия — переопределяют расовые запреты
   если planet является станцией и Trade.SpecialLegality.StationsAlwaysLegal == true:
       isLegal = true
   если у рейнджера болезнь NewMolizon и good входит в Trade.SpecialLegality.NewMolizonContrabandGoods:
       isLegal = false  // перекрывает даже станционную легальность
// --- Цена ---
5. price = Goods[good].BasePrice
         * race.PriceCoef
         * (race.Trade.GoodPriceModifiers[good] ?? 1.0)
         * (economy.Trade.GoodPriceModifiers[good] ?? 1.0)
         * (gov.GoodPriceModifiers[good] ?? 1.0)
         * StockModifier(currentStock, baseStock)
         * произведение (event.Trade.GoodPriceModifiers[good] ?? 1.0) по всем активным событиям планеты
6. если !isLegal:
       price = price * Trade.ContrabandPriceMultiplier
7. price = clamp(price,
               Goods[good].BasePrice * Trade.MinPriceMultiplier,
               Goods[good].BasePrice * Trade.MaxPriceMultiplier)
Шаг 7 (clamp) применяется к базовой цене до контрабандного множителя — контрабанда может выходить за пределы clamp. Уточнить при реализации.
## 9. Объём товара
baseStock[good] = Trade.BaseStockBySize[planet.size]
                * (economy.Trade.GoodStockModifiers[good] ?? 1.0)
- Объём изменяется при каждой сделке: покупка уменьшает, продажа увеличивает
- На станциях убыль товара крайне мала — товар фактически накапливается навсегда
- NPC со станциями не торгуют
## 10. События (`Events[id].Trade`)
Каждое активное событие добавляет `GoodPriceModifiers` в расчёт цены. При нескольких одновременных событиях модификаторы на один товар перемножаются.
"Events": {
    "Epidemic": {
        "DisplayName": "Эпидемия",
        "DurationMonths": [3, 8],
        "NewsMessage": "На планете {planet} вспышка эпидемии!",
        "Trade": { "GoodPriceModifiers": { "medicine": 2.5 } },
        "PTU":   { "GrowthMultiplier": 0.05, "PopulationEffect": -0.02 }
    },
    "Famine": {
        "DisplayName": "Голод",
        "DurationMonths": [2, 6],
        "NewsMessage": "На планете {planet} неурожай и голод.",
        "Trade": { "GoodPriceModifiers": { "food": 2.2 } },
        "PTU":   { "GrowthMultiplier": 0.05, "PopulationEffect": -0.02 }
    },
    "MineralShortage": {
        "DisplayName": "Нехватка минералов",
        "DurationMonths": [3, 10],
        "NewsMessage": "На планете {planet} дефицит минералов.",
        "Trade": { "GoodPriceModifiers": { "minerals": 1.8 } },
        "PTU":   { "GrowthMultiplier": 0.4, "PopulationEffect": 0.0 }
    },
    "TechShortage": {
        "DisplayName": "Нехватка техники",
        "DurationMonths": [3, 10],
        "NewsMessage": "На планете {planet} дефицит техники.",
        "Trade": { "GoodPriceModifiers": { "technics": 1.8 } },
        "PTU":   { "GrowthMultiplier": 0.4, "PopulationEffect": 0.0 }
    },
    "GovernmentChange": {
        "DisplayName": "Смена власти",
        "DurationMonths": [1, 1],
        "NewsMessage": "На планете {planet} сменилось правительство.",
        "Trade": { "GoodPriceModifiers": { "arms": 1.8 } },
        "PTU":   null
    },
    "PirateOccupation": {
        "DisplayName": "Захват пиратами",
        "DurationMonths": null,
        "NewsMessage": "Планета {planet} захвачена пиратами!",
        "Trade": { "GoodPriceModifiers": {} },
        "PTU":   { "GrowthMultiplier": 0.6, "PopulationEffect": 0.0, "PTUPenalty": 0 }
    },
    "DominatorOccupation": {
        "DisplayName": "Захват доминаторами",
        "DurationMonths": null,
        "NewsMessage": "Планета {planet} захвачена доминаторами!",
        "Trade": { "GoodPriceModifiers": {} },
        "PTU":   { "GrowthMultiplier": 0.0, "PopulationEffect": -0.1, "PTUPenalty": 1 }
    }
}
`DurationMonths: null` — событие длится до внешнего условия (освобождение планеты), не по таймеру.
## 11. Последствия контрабанды
| Ситуация | Параметр конфига | Значение |
|----------|------------------|----------|
| Штраф к отношениям за попытку продажи | `Trade.ContrabandRelationPenalty` | -20 |
| Срок заключения при посадке с враждебными отношениями | `Trade.PrisonSentenceMonths` | 6 |
## 13. Открытые вопросы
| # | Вопрос | Статус |
|---|--------|--------|
| 7 | Торговля с NPC-кораблями | Отложено, будет отдельное ТЗ (диалоговая форма) |
