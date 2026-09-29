# Документация модулей — Индекс

Эта папка содержит модульную техническую документацию проекта **New_SRG** (Space Roguelike, Unity C#),
созданную после рефакторинга июня 2026. Каждый модуль документирован по единому шаблону, достаточному для
воссоздания с нуля без чтения исходного кода.

## Структура шаблона

Каждый документ содержит:

1. **Назначение** — что делает модуль, за что отвечает.
2. **Публичный интерфейс** — функции/методы, доступные извне, с сигнатурами.
3. **Зависимости** — другие модули, от которых зависит, как именно.
4. **Алгоритмы и формулы** — нетривиальные процедуры с пошаговым описанием.
5. **Константы и настройки** — все пороги/коэффициенты с пояснением.
6. **Внутренняя структура данных** — ключевые типы.
7. **Известные ограничения / TODO** — что упрощено, что улучшить.

## Карта модулей

### Архитектурное ядро

- **[Turn Pipeline](turn_pipeline.md)** — `GalaxyManager`, `SystemViewManager`, `PlayerManager`, фазы хода (Planning ↔ Simulation), события `OnTurnCalculate`/`OnTurnAnimate`/`OnTurnComplete`, гиперпереход.
- **[NPC AI](npc_ai.md)** — `NpcBrain` + иерархия `NpcAction`/`NpcOrder`, `FactionDirective`, `TraderAI`, `NpcSpawner`.

### Игровые системы

- **[Combat](combat.md)** — `WeaponSystem`, `MissileSystem`, `AsteroidSystem` (death-репорт, damage pipeline, missile homing/swept-collision). Дополняет [weapons_system.md](../weapons_system.md).
- **[Equipment & Pull](equipment_and_pull.md)** — `EquipmentSystem`, `InventoryService`, `ShopService`, `PickupSystem`, `TowSystem`, `BoardingSystem`, `HyperjumpController`, `FuelService`. Дополняет [Ship_Landing_Pipeline.txt](../Ship_Landing_Pipeline.txt) и [equipment_tiers.md](../equipment_tiers%20(2).md).
- **[Trade & Economy](trade_economy.md)** — `TradeSystem`, `InflationSystem`, `EquipmentShopSystem`, `PlanetaryEventSystem`, `PlanetaryTechSystem`, `GovernmentChangeService`, `EconomicLog`. Дополняет [economy_implementation_plan.md](../economy_implementation_plan.md).
- **[Spawn](spawn.md)** — `SpawnSystem`, `ISpawnPolicy` (Civilian/Warrior/Ranger/Pirate/Linkor/Dominator), `DominationCalculator`, `GalaxyShipCounters`, `SubtypePicker`, `NpcSpawner`. Дополняет [Spawn_Rules_Consolidated.txt](../Spawn_Rules_Consolidated.txt) и [Dominator_Equipment_Consolidated.txt](../Dominator_Equipment_Consolidated.txt).

### Генерация и конфиг

- **[Galaxy Generation](galaxy_generation.md)** — `GalaxyGenerator.*`, `GalaxyDataModels`, `GalaxyConfigLoader`, `OrbitMath`, `VoronoiHelper`.
- (планируется) **Config & Constants** — `GameSettingsConfig`, `GalaxyConstants`, `GalaxyConfigurationModels` (частично покрыто в Galaxy Generation).

### Визуальная подсистема

- **[Visual](visual_subsystem.md)** — `SystemViewManager`, `BaseVisualController` + Planet/Satellite/Asteroid, `WeaponVisualSystem`, `ExplosionPlayback`, `ShipVisualController`, миникарты.
- **[Graphics Pipeline](graphics_pipeline.md)** — единая система загрузки/кэширования графики: `GraphicsManager`, `SheetHandle`, `FrameStepper`, правила добавления нового визуала.
- **[UI](ui_subsystem.md)** — `GalaxyMapController`, `ShipFormView`, `PlanetUIController`, `DialogUIController`, утилиты `UIBuilder` + `UIColorPalette`, диалоговая система.
- **[Dialog System](dialog_system.md)** — `DialogService`, `DialogTexts`, `DialogActions`, приветствия, пулы фраз, условия (тег-DSL + Lua). Разделение «структура в `DialogsConfig.json` / фразы в `TextsConfig.json`».

## Существующая документация (вне `modules/`)

| Файл | Описание |
|---|---|
| [`landing.md`](landing.md) | Посадка: геометрия, точки прицеливания, двухфазный цикл |
| [`missiles.md`](missiles.md) | Модель полёта ракет |
| [`equipment_improvement.md`](equipment_improvement.md) | Улучшение оборудования на научной базе |
| [`equipment_tiers (2).md`](../equipment_tiers%20(2).md) | GTL/ПТУ-балансировка по тирам |
| [`planetary_science_system.md`](../planetary_science_system.md) | Дизайн-документ системы изобретений |
| [`economy_implementation_plan.md`](../economy_implementation_plan.md) | План реализации экономики; что сделано/отложено |
| [`weapons_system.md`](../weapons_system.md) | Расчёт урона, эффекты, паттерны |
| [`world_generation.md`](../world_generation.md) | Алгоритмы генерации (старый формат) |
| [`ai_system.md`](../ai_system.md) | Старый обзор AI (см. `modules/npc_ai.md` для актуального) |
| [`planet_formulas.md`](../planet_formulas.md) | Формулы планет (размер, население, цены) |

## Состояние

Документация **в работе**. По состоянию на 2026-06-27 покрыты:

| Модуль | Статус |
|---|---|
| Turn Pipeline | ✅ Готов (`turn_pipeline.md`) |
| NPC AI | ✅ Готов (`npc_ai.md`) |
| Combat | ✅ Готов (`combat.md`) |
| Equipment & Pull | ✅ Готов (`equipment_and_pull.md`) |
| Trade & Economy | ✅ Готов (`trade_economy.md`) |
| Galaxy Generation | ✅ Готов (`galaxy_generation.md`) |
| Visual Subsystem | ✅ Готов (`visual_subsystem.md`) |
| Graphics Pipeline | ✅ Готов (`graphics_pipeline.md`) — унификация загрузки, июль 2026 |
| UI Subsystem | ✅ Готов (`ui_subsystem.md`) |
| Spawn | ✅ Готов (`spawn.md`) |
| Dialog System | ✅ Готов (`dialog_system.md`) — разделение структуры и фраз, июль 2026 |

Для остальных модулей актуальной является существующая документация в родительской папке `docs/`
плюс docstring'и в исходниках (после рефакторинга июня 2026 публичные API хорошо комментированы).
