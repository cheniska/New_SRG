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

- **[Architecture](architecture.md)** — сборки (asmdef) и слои, `GameWorld` и интерфейсы хостов, `SimulationSession`, правила детерминизма.
- **[Saves](saves.md)** — формат сохранения, версии и миграции, что сохраняется, известные ограничения.
- **[Testing](testing.md)** — EditMode-тесты, golden-тесты детерминизма, `TestWorld`.
- **[Turn Pipeline](turn_pipeline.md)** — `GalaxyManager`, `SystemViewManager`, `PlayerManager`, фазы хода (Planning ↔ Simulation), события `OnTurnCalculate`/`OnTurnAnimate`/`OnTurnComplete`, гиперпереход.
- **[NPC AI](npc_ai.md)** — `NpcBrain` + иерархия `NpcAction`/`NpcOrder`, `FactionDirective`, `TraderAI`, `NpcSystemSpawner`.

### Игровые системы

- **[Combat](combat.md)** — `WeaponSystem`, `MissileSystem`, `AsteroidSystem` (death-репорт, damage pipeline, missile homing/swept-collision). Дополняет [weapons_system.md](../design/weapons_system.md).
- **[Equipment & Pull](equipment_and_pull.md)** — `EquipmentSystem`, `InventoryService`, `ShopService`, `PickupSystem`, `TowSystem`, `BoardingSystem`, `HyperjumpController`, `FuelService`. Дополняет [Ship_Landing_Pipeline.md](../design/Ship_Landing_Pipeline.md) и [equipment_tiers.md](../design/equipment_tiers.md).
- **[Trade & Economy](trade_economy.md)** — `TradeSystem`, `InflationSystem`, `EquipmentShopSystem`, `PlanetaryEventSystem`, `PlanetaryTechSystem`, `GovernmentChangeService`, `EconomicLog`. Дополняет [economy_implementation_plan.md](../design/economy_implementation_plan.md).
- **[Spawn](spawn.md)** — `SpawnSystem`, `ISpawnPolicy` (Civilian/Warrior/Ranger/Pirate/Linkor/Dominator), `DominationCalculator`, `GalaxyShipCounters`, `SubtypePicker`, `NpcSpawner`. Дополняет [Spawn_Rules_Consolidated.md](../design/Spawn_Rules_Consolidated.md) и [Dominator_Equipment_Consolidated.md](../design/Dominator_Equipment_Consolidated.md).

### Генерация и конфиг

- **[Galaxy Generation](galaxy_generation.md)** — `GalaxyGenerator.*`, модели `Galaxy/Models/*`, `GalaxyConfigLoader`, `OrbitMath`, `VoronoiHelper`. Дополняет [Galaxy_Map_Generation_Consolidated.md](../design/Galaxy_Map_Generation_Consolidated.md) и [world_generation.md](../design/world_generation.md).
- (планируется) **Config & Constants** — `GameSettingsConfig`, `GalaxyConstants`, модели `Config/Models/*` (частично покрыто в Galaxy Generation).

### Визуальная подсистема

- **[Visual](visual_subsystem.md)** — `SystemViewManager`, `BaseVisualController` + Planet/Satellite/Asteroid, `WeaponVisualSystem`, `ExplosionPlayback`, `ShipVisualController`, миникарты.
- **[Graphics Pipeline](graphics_pipeline.md)** — единая система загрузки/кэширования графики: `GraphicsManager`, `SheetHandle`, `FrameStepper`, правила добавления нового визуала.
- **[UI](ui_subsystem.md)** — `GalaxyMapController`, `ShipFormView`, `PlanetUIController`, `DialogUIController`, утилиты `UIBuilder` + `UIColorPalette`, диалоговая система.
- **[Dialog System](dialog_system.md)** — `DialogService`, `DialogTexts`, `DialogActions`, приветствия, пулы фраз, условия (тег-DSL + Lua). Разделение «структура в `DialogsConfig.json` / фразы в `TextsConfig.json`».

## Дизайн-документы (`docs/design/`)

| Файл | Описание |
|---|---|
| [`Ship_Landing_Pipeline.md`](../design/Ship_Landing_Pipeline.md) | Полная двухфазная пайплайн посадки (SR2HD-style) |
| [`Ship_Trajectory_*.md`](../design/) | Реализация кинематической сплайн-траектории кораблей |
| [`Galaxy_Map_Generation_Consolidated.md`](../design/Galaxy_Map_Generation_Consolidated.md) | Алгоритмы генерации галактики/секторов/звёзд |
| [`Spawn_Rules_Consolidated.md`](../design/Spawn_Rules_Consolidated.md) | Правила спавна NPC (политики, расы, типы) |
| [`Dominator_Equipment_Consolidated.md`](../design/Dominator_Equipment_Consolidated.md) | Оборудование доминаторов |
| [`equipment_tiers.md`](../design/equipment_tiers.md) | GTL/ПТУ-балансировка по тирам |
| [`planetary_science_system.md`](../design/planetary_science_system.md) | Дизайн-документ системы изобретений |
| [`economy_implementation_plan.md`](../design/economy_implementation_plan.md) | План реализации экономики; что сделано/отложено |
| [`weapons_system.md`](../design/weapons_system.md) | Расчёт урона, эффекты, паттерны |
| [`world_generation.md`](../design/world_generation.md) | Алгоритмы генерации (старый формат) |
| [`ai_system.md`](../archive/ai_system.md) | Старый обзор AI (см. `modules/npc_ai.md` для актуального) |
| [`planet_formulas.md`](../design/planet_formulas.md) | Формулы планет (размер, население, цены) |

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

| Architecture / Saves / Testing | ✅ Готов — сентябрь 2026 (слои, детерминизм, сохранения) |

Для остальных модулей актуальной является документация в `docs/design/`
плюс docstring'и в исходниках (после рефакторинга июня 2026 публичные API хорошо комментированы).
