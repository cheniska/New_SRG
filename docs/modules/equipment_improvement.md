# Улучшение оборудования на научной базе

Код: `Equipment/ImprovementService.cs`, конфиг — `ItemsConfig.json` → `Improvement`
(`ImprovementConfigModels.cs`), UI — `UI/Common/ImprovementDialog.cs`.

## Правила

- Каждый предмет можно улучшить один раз (обычное **или** продвинутое улучшение).
- Предмет со встроенным модулем улучшить нельзя.
- Масса и прочность меняются только продвинутым улучшением.

## Цена

```
money = round10(item.Price × CostShare[tier])            # Min / Avg / Max
advanced = round10(item.Price × CostShare[AdvancedBaseTier] × AdvancedCostMultiplier)
nodes = ceil(money / CreditsPerNode)                      # если предмет требует нейроядра
item.Price += money × ValueGainShare                      # после улучшения
```

## Прирост параметра

```
roll  = 0.75 + 0.25 × (rand + rand)       # 0.75..1.25, пик в 1
delta = max(|current| × Pct × roll, MinStep)
value = current + Sign × delta
```

`Pct` и `MinStep` — из `Tiers[tier] = [Pct, MinStep]` атрибута. `Sign` берётся у атрибута
(например, −1 для массы оборудования), иначе у категории.

Если в обычном улучшении выбрана конкретная характеристика, остальные атрибуты категории
растут на `SecondaryGrowthFactor` от полного прироста.

## NPC

При спавне каждая подходящая единица с шансом `NpcSpawnChance` получает улучшение
тира, выбранного по `NpcTierWeights`.
