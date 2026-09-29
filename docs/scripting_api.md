# Скриптовый API SRG

Полный перечень публичных скриптовых точек, доступных из данных (диалоги, конфиги), из дебаг-консоли и из C# (квесты/моды). На каждую сущность — где живёт, что делает, каким кодом дёргается.

Регистрируется на старте:
- `DialogActions.RegisterDefaults()` — базовые действия/теги диалогов.
- `DialogPartnerActions.RegisterDefaults()` — партнёрство/дроны.
- `GameConsoleController.RegisterCommands()` — дебаг-консоль.
- Публичные C#-классы (services/factories/utilities) — доступны прямо, `using` соответствующего неймспейса.

Точки расширения: `DialogService.RegisterAction/RegisterTag/RegisterParamTag(...)` из `Start()`/`Awake()`.

---

## 1. Действия диалога — `Actions` / `OnEnter`

Синтаксис узла:
```json
{
  "OnEnter": [{ "Method": "AddMoney", "Args": ["1000"] }],
  "Replies": [{ "Text": "Согласиться", "Actions": [{ "Method": "SetVar", "Args": ["accepted", "1"] }] }]
}
```

Каждый action — либо legacy `Method` + `Args` (см. таблицы ниже), либо Lua через поле `Script`:
```json
{ "Script": "player.Money = player.Money + 1000; ctx.Data['accepted'] = '1'" }
```
В Lua-контексте доступны локалы `ctx` (`DialogContext`), `player`, `target`, `planet`, `args` (список
строк из `Args`), а также все глобалы `LuaBindings`. Если `Script` задан — `Method` игнорируется.

### Базовый набор (`DialogActions`)

| Метод | Аргументы | Что делает |
|---|---|---|
| `EndDialog` | — | Закрывает диалог (`ctx.ShouldClose = true`). |
| `Log` | `msg...` | Печатает сообщение в игровую консоль (`GameConsoleController.AddEntry`). |
| `AddMoney` | `amount` | Прибавляет игроку кредиты. |
| `TakeMoney` | `amount` | Снимает с игрока (клампится в 0). |
| `Repair` | `hp` | Восстанавливает корпус (клампится в MaxHull). |
| `Damage` | `hp` | Наносит урон корпусу. |
| `SetVar` | `key`, `value` | Пишет в `ctx.Data` — доступно всем следующим репликам. |
| `LeavePlanet` | — | Закрывает диалог + `PlayerManager.LeavePlanet()`. |
| `SkipTurn` | — | Мгновенный ход (`GalaxyManager.ExecuteInstantTurn`). |

### Партнёрство и дроны (`DialogPartnerActions`)

| Метод | Аргументы | Что делает |
|---|---|---|
| `PartnerOfferInit` | — | Стартовая цена в `ctx.Data["partner_offer"]`. |
| `PartnerOfferLess` | — | Текущее предложение / 2. |
| `PartnerOfferMore` | — | Текущее предложение × 2 (кэп по `player.Money`). |
| `PartnerTryHire` | — | Вызов `PartnerService.TryHire`, пишет reason/fee. |
| `PartnerBreak` | — | `PartnerService.Break(target, PlayerDismiss)`. |
| `PartnerOrderFlyToMe` | — | `PartnerOrder = FlyToMe`. |
| `PartnerOrderAttack` | `uid` | `PartnerOrder = Attack`. |
| `PartnerOrderLandOn` | `planetUid` | `PartnerOrder = LandOn`. |
| `PartnerOrderFlyToStar` | `starUid` | `PartnerOrder = FlyToStar`. |
| `PartnerOrderForAll` | `kind`, `uid?` | Приказ всем партнёрам игрока в текущей звезде. |
| `DroneRequestReturn` | — | `DroneService.RequestReturn(target)`. |

---

## 2. Теги подстановки — `{tag}` / `{tag|fallback}`

### Собеседник-корабль

`ship_name` / `ship_type` / `ship_race` / `ship_owner`, `home_planet` / `home_star`, `last_planet` / `last_star`, `current_planet` / `current_star`, `next_planet` / `next_star`.

### Груз собеседника

`cargo_id` / `cargo_name` / `cargo_amount` / `cargo_unit_price` / `cargo_total_value` / `has_cargo`.

### Игрок / галактика

`player_name` / `player_money` / `player_planet` / `player_star`, `turn`, `date`.

### Партнёрство

`partner_offer` / `partner_fee` / `partner_refusal`, `is_hireable`, `is_drone`, `is_partner_of_player`, `max_partners`, `current_partners`, `partners_in_star`.

### Приветствия (обёртки над `PlanetGreetingRenderer`)

`planet_greeting`, `ship_greeting` — собирают итоговый текст правил из `DialogsConfig.PlanetGreetings.Rules` / `ShipGreetings.Rules`. Кэшируется в `ctx.Data["_greeting"]`/`["_ship_greeting"]`.

---

## 3. Параметризованные теги — `{prefix:arg}`

| Префикс | Пример | Что делает |
|---|---|---|
| `has_cargo` | `{has_cargo:Alcohol}` | «1» если товар в трюме есть. |
| `cargo_amount` | `{cargo_amount:Minerals}` | Кол-во конкретного товара. |
| `pool` | `{pool:Attack.<ShipType>Ok}` | Случайная строка из `Dialogs.StringPools[arg]`. В `arg` поддерживается подстановка `<ShipType>`, `<ShipRace>`, `<Ship>`, `<PlayerRace>`, `<Player>`. |
| `lua` | `{lua:Player().Money}` | Lua-выражение. Исполняется в `LuaHost.Core` с локалами `ctx` / `player` / `target` / `planet`. Результат стрингифицируется. `nil` → пусто (fallback берётся). Ошибка — тоже пусто, детали в логе. |

### `<...>`-подстановки в приветствиях (`PlanetGreetingRenderer`)

- Общие: `<Player>`, `<PlayerRace>`, `<CurPlanet>`, `<CurPlanetRace>`, `<CurPlanetGovernment>`, `<CurPlanetEconomy>`, `<CurStar>`, `<ToPlanet>`, `<ToPlanetRace>`, `<ToPlanetGovernment>`, `<ToPlanetEconomy>`, `<ToStar>`, `<Goods>`, `<FactionName>`.
- Только в `ship_greeting`: `<Ship>`, `<ShipName>`, `<FullShip>`, `<ShipRace>`, `<ShipType>`, `<PlayerRank>`, `<Ranger>` (алиас `<Player>`), `<Star>` (алиас `<CurStar>`), `<LastPlanet>`, `<LastPlanetStar>`, `<HomePlanet>`, `<HomePlanetStar>`, `<LastPlanetGoodsBuy>`, `<LastPlanetGoodsSale>`, `<ToPlanetGoodsBuy>`, `<ToPlanetGoodsSale>`.

---

## 4. Условия — `Condition` (в репликах)

`&&`, `||`, `!`, скобки. Сравнения: `==`, `!=`, `>=`, `<=`, `>`, `<`. Одиночный тег без сравнения — истинно, если резолвится не пустой и не «falsey» (`0`, `false`, `no`).

```json
"Condition": "player_money >= 1000"
"Condition": "is_partner_of_player == 1 && !is_drone"
"Condition": "has_cargo:Narcotics"
```

Реализация — `DialogService.DefaultEvaluateCondition`.

---

## 5. Дебаг-консоль (`SUDO` → `GameConsoleController`)

Открытие — `` ` `` (по умолчанию). Две ветки исполнения:

- **Команды.** Строки без `.`, `(`, `:`, `=` — команды из встроенного реестра (`save`, `spawntest N` и т.п., см. ниже).
- **Lua-режим.** Всё остальное уходит в [`LuaHost.Core`](../Assets/Scripts/Scripting/LuaHost.cs) как выражение или statement.
  Результат печатается через `= <value>`, ошибка — `! <msg>`. Пример:
  ```lua
  WormholeService.Spawn(Positions.Near(Player(), 100))
  x = Player().Money;  Log(x)
  ```
  Доступные глобалы: `Player()`, `Galaxy()`, `Star()`, `Turn()`, `Log(msg)`, `Rand(a,b)`, `RandInt(a,b)`,
  все сервисы из `LuaBindings.StaticApiTypes` (`WormholeService`, `PartnerService`, `AmmoService`, `Positions`, …),
  data-модели (`ShipData`, `PlanetData`, …) и `Mathf`. Полный список — в `Assets/Scripts/Scripting/LuaBindings.cs`.
  Сэндбокс `Preset_HardSandbox`: без `io`/`os`/`debug`/`require`, всё остальное из стандартного Lua доступно.

### Общие команды
`save`, `load`, `regen`, `step`, `stop`, `planning`, `next` / `prev`, `clear`, `quit`, `help`.

### Тестовые массы
`spawntest [N]`, `cleartest`, `ships`.

### Захват / буксировка
`grabradius`, `grabsystem`, `grabclear`.

### Партнёрство / дроны
`spawndrone [race]`, `givedrone [race]`, `hire`, `dismiss [index]`, `partners`.

### Скилы пилота
`showskills`, `addexp <N> [cat]`, `upskill <type>`, `setskill <type> <N>`, `addbonus <skill> <amount> [turns]`, `addpenalty <skill> <amount> [turns]`, `clearmod <id>`, `clearskill <skill>`, `clearmods`, `mods`.

---

## 6. Пространственный DSL — `Positions` / `Point`

Пространство `SRG.Utils`. Используется всеми подсистемами спавна (NPC, дроны, контейнеры, червоточины, патрули). Без inline-`Random.insideUnitCircle`.

`Point` — точка в системе (`Star` + `Position`). Неявно приводится к `Vector2`. Deconstruct: `var (star, pos) = point;`.

### Абстрактное сэмплирование

| Метод | Что возвращает |
|---|---|
| `Positions.RandomInCircle(r)` | Случайная точка в диске [0, r]. |
| `Positions.RandomInRing(rMin, rMax)` | В кольце. |
| `Positions.RandomOnCircle(r)` | На окружности (равномерно по углу). |
| `Positions.SystemRadius(star)` | Радиус системы (`star.SystemSize / 100`). |

### `Near` — случайно около опоры

| Форма | Что делает |
|---|---|
| `Near(star, radius = -1)` | В диске [0, r]; при `-1` — в кольце 40..90 % радиуса системы. |
| `Near(planet, radius)` | Диск вокруг мировой позиции планеты (`OrbitMath.GetPlanetWorldPosition`), клампится к системе. |
| `Near(ship, radius)` | Диск вокруг корабля, клампится. |
| `Near(point, radius)` | Диск вокруг `Point`. |
| `Near(object, radius)` | Runtime-диспатч (для скриптов). |

### `At` — текущая позиция

`At(star)` — начало координат; `At(planet)` — текущая мировая; `At(ship)` — позиция + `CurrentStar`; `At(star, x, y)` — DSL; `At(object)` — runtime-диспатч.

### Композиция
```csharp
WormholeService.Spawn(Near(Player(), 100));
WormholeService.Spawn(At(sourceStar), Near(targetPlanet, 5));
```

---

## 7. Червоточины — `WormholeService`

Пространство `SRG.Galaxy.Simulation`.

### Спавн
```csharp
WormholeService.Spawn();                                  // всё случайно
WormholeService.Spawn(someStar);                          // источник в звезде
WormholeService.Spawn(new Point(src, srcPos), new Point(dst, dstPos)); // оба конца
WormholeService.Spawn(src, dst, lifetimeTurns: 30, graphics: WormholeGraphics.Of("openPath","cyclePath","closePath"));
WormholeService.Spawn(src, dst, "openPath", "cyclePath", "closePath"); // shortcut
```

Правила: `source == null` → случайная звезда; `target == null` → в диапазоне `Wormhole_MinDistancePc..MaxDistancePc`; `target.Position == null` → рандом на каждый вход, иначе `FixedTargetPosition`; `targetGalaxyId != null` → межгалактика (обязателен `target.Star.Uid`).

### Поиск / удаление

| Метод | Что делает |
|---|---|
| `TryFind(galaxy, uid, out wh, out sourceStar)` | Ищет по UID. |
| `Find(uid)` | В текущей галактике. |
| `All()` | `IEnumerable<(wh, sourceStar)>`. |
| `CountActive(galaxy)` | Счётчик. |
| `ForceClose(wh)` / `ForceClose(uid)` | В фазу Closing (удалится анимационно). |
| `Destroy(uid)` | Немедленно, без анимации. |

### Прыжок
`HyperjumpController.RequestJumpViaWormhole(ship, wh, sourceStar, galaxy)` — требует `wh.Phase == Open` и `ship.HyperjumpPhase == None`; топливо не тратится.

### Данные (`WormholeData`)
`Uid`, `Position`, `TargetStarUid`, `TargetGalaxyId`, `FixedTargetPosition`, `Phase` (`Opening→Open→Closing`), `DaysInPhase`, `OpenTurnsRemaining`, `CreatedTurn`, `LifetimeOverride`, `Graphics` (`OpeningPath/CyclePath/ClosingPath/IconPath`).

Автоспавн — `WormholeSystem.DailyTick` под `Wormhole_SpawnAvgIntervalTurns` / `Wormhole_MaxActive` (GameSettings).

---

## 8. Партнёрство и дроны

### `PartnerScriptApi` (пространство `SRG.Ships.Services`)
| Метод | Что делает |
|---|---|
| `SpawnDrone(host, race?, shipTypeId?)` | Активный дрон рядом с хостом, партнёром. |
| `GiveDronePackage(recipient, race?, shipTypeId?)` | Упакованный дрон в инвентарь. |
| `ForceHire(target, leader, contractYears = -1)` | Партнёрство без проверок (кроме MaxPartners). |
| `BreakContract(follower, reason = PlayerDismiss)` | Разорвать контракт. |
| `FindNearestShip(player, filter)` | Ближайший корабль-по-фильтру в системе. |

Константы: `DefaultDroneShipType = "Drone"`, `DefaultDroneRace = "Race1"`.

### `PartnerService`
| Метод | Что делает |
|---|---|
| `Preview(target, hirer, offer)` | Расчёт цены/причины без установки. |
| `TryHire(target, hirer, offer)` | Нанять; вернёт `HireResult`. |
| `Break(follower, reason)` | Разорвать. |
| `SetPartner(follower, leader, currentTurn, endTurn)` | Прямая установка (для сейв-миграций/скриптов). |
| `MaxPartners(hirer)` | Лимит по конфигу. |
| `CheckBreak(follower, currentTurn)` | Дневной тик — истёк ли контракт. |
| `FindShipInGalaxy(uid)` | Поиск корабля по UID. |

### `DroneService`
| Метод | Что делает |
|---|---|
| `Deploy(host, packedItem)` | Развернуть упакованного дрона из предмета. |
| `RequestReturn(drone)` | Приказ вернуться. |
| `TryFinishReturn(drone)` | Проверить контакт с хозяином, если да — упаковать. |
| `Pack(drone, host)` | Свернуть в предмет и удалить корабль. |

---

## 9. Ремонт / боеприпасы / топливо

### `RepairService`
| Метод | Что делает |
|---|---|
| `CostPerPoint(techLevel)` | Цена одного HP по техуровню планеты. |
| `EstimateFullRepairCost(ship)` | Оценка полного ремонта корпуса + оборудования. |
| `EstimateHullRepairCost(ship)` | Только корпус. |
| `RepairHullOnly(ship, planet)` | Ремонт корпуса, возвращает потраченные деньги. |
| `RepairShipAtPlanet(ship, planet, shipType?)` | Полный ремонт (по приоритетам), `RepairReport`. |
| `GetPriorityWeight(shipType, category)` | Вес приоритета ремонта для NPC. |
| `ResolveShipType(ship)` | Быстрый лукап `ShipTypeConfig`. |

### `AmmoService`
| Метод | Что делает |
|---|---|
| `CostPerRound(techLevel)` | Цена патрона. |
| `MissingAmmo(weapon)` | Сколько до полного. |
| `EstimateReloadCost(ship)` | Оценка полной перезарядки. |
| `HasReloadableWeapons(ship)` | Есть ли что дозарядить. |
| `ReloadAllAmmo(ship, planet)` | Дозарядить всё, вернуть потраченное. |

### `FuelService`
| Метод | Что делает |
|---|---|
| `RefillToFull(ship, planet)` | Заправить до полного, вернуть потраченное. |

---

## 10. Груз и контейнеры

### `CargoUtils` (`SRG.Ships`)
| Метод | Что делает |
|---|---|
| `HasAnyCargo(ship)` | Есть ли хоть что-то в трюме. |
| `HasCargoOfType(ship, goodId)` | Конкретный товар. |
| `GetCargoAmount(ship, goodId)` | Единиц конкретного товара. |
| `GetAllCargo(ship)` | `List<(goodId, amount)>` весь груз. |
| `GetLargestCargo(ship)` | Самый крупный стек. |

### `ContainerFactory`
| Метод | Что делает |
|---|---|
| `SpawnContainerWithItem(originShip, item, spawnStar, spawnRadius=0.4)` | Контейнер (IsItem-корабль) с предметом. |
| `SpawnContainerWithStack(originShip, stack, spawnStar, spawnRadius=0.4)` | Контейнер со стеком товара/минералов. |
| `UnloadContainerInto(container, target)` | Перенести груз из контейнера в цель, вернуть число перенесённых единиц. |

---

## 11. Оборудование

### `EquipmentSystem` (`SRG.Equipment`)
Общие методы (частично `partial` — есть `EquipmentSystem.Fuel`, `EquipmentSystem.Speed`, `EquipmentSystem.Wear`).

| Метод | Что делает |
|---|---|
| `Install(ship, item, slotKey, allowInSpace=false)` | Установить в слот; `EquipResult`. |
| `Uninstall(ship, slotKey, allowInSpace=false)` | Снять, вернуть предмет. |
| `GetHullCapacity(ship)` | Ёмкость корпуса. |
| `GetEquippedWeight(ship)` / `GetFreeSpace(ship)` | Вес и свободное. |
| `GetEquipped(ship, slotKey)` | Установленный предмет. |
| `FindEquipmentByCategory(ship, category, requireWorking=true)` | Первый предмет категории. |
| `GetHullParam(ship, key)` / `GetHullVulnerability(ship, damageType)` | Параметры корпуса. |
| `SlotCategory(slotKey)` | Категория слота. |
| `GetHullSlotPlan(hull, equipConfig)` | План слотов данного корпуса. |
| `ApplySeriesToHull(hull, equipConfig)` | Наложить линейку (HullType). |
| `EquipResult.Ok(msg)` / `Fail(msg)` | Фабрики результатов. |
| `GetCurrentFuel(ship)`, `GetFuelCapacity(ship)`, `ConsumeFuel(ship, cost)` | Из `EquipmentSystem.Fuel`. |
| `GetActualSpeed(ship)`, `GetJumpRange(ship)` | Из `EquipmentSystem.Speed`. |

### `InventoryService`
| Метод | Что делает |
|---|---|
| `TakeFromInventory(ship, uid)` | Забрать из инвентаря по UID. |
| `AddToInventory(ship, item)` | Положить. |
| `RemoveFromInventory(ship, uid)` | Удалить по UID. |
| `InventoryContains(ship, uid)` | Есть ли. |
| `UnequipFromSlot(ship, slotKey)` | Снять со слота в инвентарь. |
| `EquipToSlot(ship, slotKey, item)` | Поставить в слот из инвентаря. |
| `RemoveCompletely(ship, item)` | Полное удаление (из инвентаря + слотов). |
| `TransferInventoryItem(from, to, itemUid)` | Передать между кораблями. |

### `ItemFactory`
| Метод | Что делает |
|---|---|
| `Create(category, itemId, equipConfig, techLevel?, race?, side?)` | Создать `ItemInstance`. |
| `EquipStarterKit(ship, kit, equipConfig, galaxyConfig, weapons?)` | Установить стартовый набор. |
| `InitPlanetData(planet, ctx)` | Инициализация магазинов планеты. |

### `SlotKeys` / `EquipmentCategory` (константы)

`SlotKeys.Hull`, `Engine`, `FuelTank`, `Forsage`, `Shield`, `Radar`, `Scanner`, `Weapon_0..N`, ... — ключи слотов.
`EquipmentCategory.Hull/Engine/Weapon/Radar/CompanionDrone/...` — категории.

### `StackGraphics`
| Метод | Что делает |
|---|---|
| `ResolveIcon(itemId, amount)` | Иконка стека по количеству (GraphicSteps). |
| `ResolveSprite(itemId, amount)` | Мировой спрайт стека. |

### Артефактные Lua-скрипты (`TurnScript` / `UseScript` / `SubturnScript`)

Артефакт (или любое оборудование) может нести Lua-код прямо в конфиге:

```json
"ArtPDTurret": {
  "Name": "а'Эгис",
  "Params": { "Range": 150, "MaxShots": 5 },
  "SubturnAt": 5,
  "SubturnScript": "local r = Api.SRToWorld(Api.GetParam(item, 'Range', 150)); local killed = Api.KillMissilesInRadius(ship, r, Api.GetParam(item, 'MaxShots', 5), true); if killed > 0 and ship.IsPlayer then Api.PostPlayerNews(string.format('а\\'Эгис сбил ракет: %d.', killed)) end"
}
```

Три точки входа:

| Поле | Когда срабатывает | Аргументы Lua |
|---|---|---|
| `TurnScript` | Раз в ход, после ApplyTurnEffects. Тик пассивных эффектов (ремонт, топливо, аналитика). | `ship`, `item`, `cfg` |
| `SubturnScript` + `SubturnAt` | Один раз в указанный сабтёрн боя (1..10), после движения кораблей и ракет. Для ПРО, локальных сканов и «сейчас-действий». | `ship`, `item`, `cfg`, `subTurn` |
| `UseScript` | При активации предмета игроком (см. `Activatable`). Должен вернуть таблицу `{ ok, message, consume, wear }` или nil. | `ship`, `item`, `cfg` |

Устаревшие `TurnCode`/`UseCode`/`SubturnCode` (id C#-скрипта в реестре) остаются для будущих C#-обработчиков — Lua-версия перебивает их.

Компиляция — один раз при загрузке `EquipmentConfig` в `LuaArtefactScripts.CompileAndRegisterAll` (вызывается из `GalaxyManager.Awake`); скрипт хранится как замыкание в реестрах `ArtefactTurnRegistry`/`ArtefactSubturnRegistry`/`ArtefactActionRegistry`. Ошибка одного вызова логируется один раз per script id и не глушит артефакт.

Регистры (C#, если нужно писать обработчики нативно):
- `ArtefactTurnRegistry.Register(id, IArtefactTurnScript)` — `OnTurn(ship, item, cfg)`, вызывается из `EquipmentSystem.ApplyTurnEffects`.
- `ArtefactSubturnRegistry.Register(id, IArtefactSubturnScript)` — `OnSubturn(ship, item, cfg, subTurn)`, из `StarSimulator.CombatSubTurn` после `TickMissiles`. Срабатывает только на сабтёрне, равном `cfg.SubturnAt`.
- `ArtefactActionRegistry.Register(id, IArtefactActionScript)` — `TryUse(ship, item, cfg)` возвращает `ActionResult`, из `EquipmentSystem.ActivateItem`.

### `ArtefactApi` — крупнозернистый Lua-фасад

`SRG.Equipment.ArtefactApi` (в Lua доступен как `Api`) — единственная точка сбора для скриптов артефактов. Правило: минимум мелких вызовов из Lua, максимум готовых операций.

**Утилиты единиц / контекст**

| Метод | Что возвращает |
|---|---|
| `Api.SRToWorld(sr)` | `sr / 100` — SR-единицы → мировые. Для старых `Params.Range` вроде 150. |
| `Api.CurrentTurn()` | Текущий ход галактики. |
| `Api.SubTurnsPerTurn` | Константа 10. |
| `Api.IsPlanning()` / `Api.IsSimulation()` | Фаза `GalaxyManager`. |
| `Api.Console(msg)` | Печать в игровую консоль. |

**Категории новостей** (для `Api.PostNews`)

`Api.News.PLAYER` / `ATTACK` / `SYSTEM` / `PLANET` / `ECONOMY` / `SCIENCE` / `ASTEROID`.

**Чтение конфига предмета**

| Метод | Что возвращает |
|---|---|
| `Api.GetConfigString(cfg, item, key, def?)` | Строка из `ScriptParams[key]`. |
| `Api.GetConfigFloat(cfg, item, key, def?)` / `Api.GetConfigInt(...)` | Число из `ScriptParams[key]`. |
| `Api.GetConfigObject(cfg, item, key)` | JObject-поддерева `ScriptParams[key]`. |
| `Api.GetParam(item, key, def?)` | Значение из runtime-Params предмета (после StatBus/бонусов). Дешевле, чем ScriptParams. |
| `Api.SumCategory(ship, category, key)` | Сумма параметра по всем установленным предметам категории. |

**Списки**

| Метод | Что возвращает |
|---|---|
| `Api.DamagedItems(ship, threshold, except?)` | Установленные предметы с прочностью < `threshold` (0..1). |
| `Api.EquippedItems(ship)` | Все установленные. |
| `Api.FindByCategory(ship, category, requireWorking=true)` | Первый рабочий предмет категории (или nil). |
| `Api.HostileMissiles(ship, worldRange, ignoreReturning=true)` | Активные ракеты в радиусе (не свои, не возвращающиеся). |
| `Api.NearbyShips(ship, worldRange, "hostile"/"allied"/nil)` | Живые корабли в радиусе. |
| `Api.AttackDirectivesNear(ship, scopePc)` | Активные атакующие директивы, чьи цели в радиусе `scopePc` парсек. Элементы: `.OwnerId`, `.TargetStar`, `.TargetStarName`, `.TargetStarUid`. |
| `Api.PickRandom(list)` | Случайный элемент или nil. |

Итерация: возвращаются `List<T>` (C#-нотация). В Lua — `for i = 0, list.Count - 1 do local x = list[i] end`.

**Действия — прочность/HP/топливо**

| Метод | Что делает |
|---|---|
| `Api.RepairItem(item, points)` / `Api.RepairItemPercent(item, pct)` | Восстанавливает прочность. Возвращает фактически восстановлено. |
| `Api.SpendDurability(item, n)` / `Api.MarkBroken(item)` | Списание/полный сбой. |
| `Api.AddFuel(ship, amount)` | Долить топливо (клампится). Возвращает фактически залито. |
| `Api.RepairHull(ship, hp)` / `Api.DamageHull(ship, hp)` | Корпус, без учёта щита/брони. |

**Действия — ракеты**

| Метод | Что делает |
|---|---|
| `Api.KillMissile(ship, missile)` | Убрать одну ракету из системы. |
| `Api.KillMissilesInRadius(ship, worldRange, maxShots, ignoreReturning=true)` | Сбить до N враждебных ракет. Возвращает число сбитых. |

**Действия — бонусы (StatBus)**

| Метод | Что делает |
|---|---|
| `Api.ApplyBonus(item, key, sourceId, delta, hidden=false, label=nil)` | Аддитивный бонус на предмет. |
| `Api.ClearBonus(item, sourceId)` | Снять. |
| `Api.ApplyShipCategoryBonus(ship, category, key, sourceId, delta, hidden=false)` | На все установленные предметы категории. |

**Действия — события мира**

| Метод | Что делает |
|---|---|
| `Api.PostNews(category, text)` / `Api.PostPlayerNews(text)` | Публикация в ленту. |
| `Api.SpawnWormholeNearShip(ship, worldRadius=0.05)` | Червоточина случайного назначения рядом с кораблём. |
| `Api.SummonSideToShip(ship, spec)` | Призыв стороны (Lua-таблица со спекой). |
| `Api.SummonSideFromConfig(ship, cfg, item, key="Summon")` | То же, но спека читается из `ScriptParams[key]`. |

**Действия — маскировка**

| Метод | Что делает |
|---|---|
| `Api.SetDisguise(ship, sourceItemUid, targetRace, targetOwner, visualPath, displayName)` | Надеть. |
| `Api.ClearDisguise(ship)` | Снять. |
| `Api.ActiveDisguiseItemUid(ship)` | UID артефакта-камуфляжа или пусто. |
| `Api.DisguiseDetectedRaces(ship)` | Список рас, раскрывших маску. |
| `Api.ToggleDisguiseFromConfig(ship, cfg, item)` | Готовый обработчик активации/деактивации, читающий все ScriptParams. Возвращает Lua-таблицу-результат для `UseScript`. |

### Примеры

Нанитоиды (`TurnScript`):
```lua
local threshold = Api.GetParam(item, 'RepairThreshold', 0.60)
local healPct   = Api.GetParam(item, 'HealPercent',     0.10)
if healPct <= 0 then return end
local list = Api.DamagedItems(ship, threshold, item)
if list.Count == 0 then return end
local target = Api.PickRandom(list)
local healed = Api.RepairItemPercent(target, healPct)
if healed > 0 and ship.IsPlayer then
  Api.PostPlayerNews(string.format('Нанитоиды подлатали <b>%s</b> (+%d).', target.Name, healed))
end
```

а'Эгис (`SubturnScript`, `SubturnAt: 5` — срабатывает в середине хода):
```lua
local range = Api.SRToWorld(Api.GetParam(item, 'Range', 150))
local maxShots = math.max(1, math.floor(Api.GetParam(item, 'MaxShots', 5) + 0.5))
local killed = Api.KillMissilesInRadius(ship, range, maxShots, true)
if killed > 0 and ship.IsPlayer then
  Api.PostPlayerNews(string.format('а\'Эгис сбил ракет: %d.', killed))
end
```

Субпортал (`UseScript`, `consume=true` — одноразовый):
```lua
local radius = Api.GetConfigFloat(cfg, item, 'SpawnRadius', 5)
local wh = Api.SpawnWormholeNearShip(ship, radius)
if wh == nil then return { ok=false, message='[Активация]: не удалось создать субпортал.' } end
return { ok=true, consume=true, message='[Активация]: субпортал открыт.' }
```

---

## 12. Корабли и графика

### `ShipFactory` (`SRG.Ships`)
| Метод | Что делает |
|---|---|
| `BuildShipData(shipTypeId, ownerId, raceId, availableTypes, ctx)` | Собрать `ShipData` с базовым корпусом и параметрами. |
| `RecalculateSpriteWorldSize(ship)` | Пересчитать мировой размер спрайта. |
| `HullSizeMultiplier(ship)` / `HullSizeMultiplier(maxHull, baseSize)` | Множитель размера от MaxHull. |
| `ResolveSpriteWorldSize(spritePath)` | Мировой размер по атласу. |
| `GetRandomShipTypeForOwner(ownerId, ...)` | Случайный тип для владельца по конфигу. |

### `ShipGraphicsResolver` (`SRG.Presentation`)
| Метод | Что делает |
|---|---|
| `Resolve(...)` | Резолвит путь к спрайту с учётом Owner/Race/manufacturerSide. |
| `ResolveFromBase(basePath, ownerId, raceId)` | Быстрый вариант от basePath. |
| `ClearCache()` | Сбросить кэш. |

### `ShipRatingService`
| Метод | Что делает |
|---|---|
| `Initialize()` | Настройка (вызывается при инициализации). |
| `GetScore(ship, cfg)` | Балл конкретного рейтинга. |
| `Build(galaxy, cfg)` | Отсортированный список `(ship, score)`. |
| `FindRating(config, id)` | `ShipRatingConfig` по id (`"Rangers"`/…). |
| `TickIfDue(galaxy, config)` | Периодический тик. |

### Стыковка кораблей и станций

`ShipDockingService` — посадка малых судов на носитель (линкор) и стыковка со станциями.
Единый пайплайн: `ShipData` реализует `ILandingSite` (Kind=Station|Carrier по `IsStation`), сам
`ShipDockingService` не различает станцию/линкор — разница только в `AsLandingSite` (для станций
лениво инициализируется магазин оборудования).
| Метод | Что делает |
|---|---|
| `GetDockRadius(carrier)` | Радиус стыковки. |
| `TypeAllowsLanding(carrier)` | Разрешает ли тип корабля садиться (станция → всегда, линкор → `CanBeLandedOn`). |
| `CanLandOn(ship, carrier, out reason)` | Проверка возможности. |
| `Land(ship, carrier)` / `Undock(ship, carrier)` | Пристыковать / отстыковать. |
| `AsLandingSite(carrier)` | Готовит носитель как посадочную цель (создаёт `Settlement`, для станций — стокает магазин). |

### `ILandingSite` (`SRG.Galaxy`)
Общий контракт «место посадки» для планет, станций и линкоров. Реализуется `PlanetData`
и `ShipData`. Сервисы (Fuel/Repair/Ammo/Trade/Shop) и UI работают с `ILandingSite`, не с
конкретным типом. `Kind` = Planet|Station|Carrier. Геометрия — `CenterPosition` + `LandingRadius`
(extension `GetLandingRadiusSq()` в `LandingSiteExtensions`). Поиск site по UID — через
`LandingSiteRegistry.Find(star, uid)` / `.ResolveActiveTarget(ship, star)`.

### `ShipLoadoutService`
| Метод | Что делает |
|---|---|
| `IsOverloaded(ship)` | Перегруз. |
| `TickTurn(ship, star, turn, equipConfig)` | Ежеходовый апдейт (авто-надеть подобранное). |
| `TryAutoEquipUpgrades(ship, equipConfig)` | Установить апгрейды из инвентаря. |
| `ResolveOverload(ship, star?)` | Сброс груза при перегрузе (стеки → трюм → установленное). |

---

## 13. Движение и гиперпрыжок

### `HyperjumpController` (`SRG.Ships.Movement`)
| Метод | Что делает |
|---|---|
| `IsApproachingHyperEdge(ship)` | Корабль подходит к краю системы. |
| `CanRequestJump(ship, target, galaxy, out reason)` | Проверка возможности прыжка. |
| `CalcFuelCost(from, to)` | Топливо по дистанции. |
| `CalcDistance(a, b)` | Между звёздами. |
| `RequestJump(ship, target, galaxy)` | Заявить прыжок (обычный). |
| `RequestJumpViaWormhole(ship, wh, srcStar, galaxy)` | Через червоточину. |
| `CancelJump(ship)` | Отменить (стадия `HyperEnter`). |
| `CancelOnRouteChange(ship)` | Отмена при смене маршрута. |
| `PrepareTurn(ship, star)` / `FinalizeTurn(ship, star, galaxy)` | Ежеходовые хуки. |

### `HyperNavigation`
| Метод | Что делает |
|---|---|
| `GetHopDistance(galaxy)` | Максимальная дальность одного прыжка. |
| `GetNeighbors(star, galaxy)` | Все соседние звёзды в радиусе прыжка. |
| `ActionToward(ship, currentStar, destStarUid)` | Выбрать `NpcAction` для следующего хопа к цели. |
| `PickNextHopToward(ship, currentStar, destStar, galaxy)` | Ближайший промежуточный узел. |

---

## 14. Бой и абордаж

### `WeaponSystem` — расчёт выстрелов (внутренний, но публичен).

### `MissileSystem`
| Метод | Что делает |
|---|---|
| `LaunchSalvo(...)` | Спавн ракет из батареи. |
| `TickMissiles(...)` | Тик за подхода/наведения. |
| `TickMissilesEndOfDay(star, anim)` | Хвост-обработка на конце дня. |
| `InitMissileFrames(star, anim)` | Инициализация анимации кадров. |

### `BoardingSystem`
| Метод | Что делает |
|---|---|
| `FindGrapplingHook(ship)` | Активный крюк, если есть. |
| `CanBoard(attacker, target, equipConfig, out reason)` | Можно ли брать на абордаж. |
| `TryBoard(attacker, target, equipConfig)` | Начать. |
| `EndBoarding(attacker, target)` | Завершить. |
| `SnapBoardedTarget(attacker, target, equipConfig)` | Привязать позицию цели к атакующему. |
| `TransferItem(from, to, itemUid)` | Перенос предмета во время абордажа. |

### `PickupSystem`
| Метод | Что делает |
|---|---|
| `FindCargoGrabber(ship)` | Активный захват. |
| `EnqueuePull(tug, targetUid)` | Добавить цель в очередь. |
| `EnqueueAllItemsInRadius(tug, candidates)` | Всё в радиусе. |
| `EnqueueAllItemsInSystem(tug, candidates)` | Всё в системе. |
| `CleanPullQueue(tug, uidLookup)` | Убрать мёртвые. |
| `ProcessPullQueueSubturn(...)` / `UpdatePullStep(...)` | Тик. |
| `EndPull(tug, target)` / `ClearPullState(target)` | Завершение. |
| `TickPullDistanceCheck(target, ...)` | Проверка выхода за радиус. |

### `TowSystem`
| Метод | Что делает |
|---|---|
| `FindTowingRig(ship)` | Активная буксирная снасть. |
| `CanRigTowStations(rig)` | Может ли рибо буксировать станции. |
| `CanTow(tug, target, equipConfig, out reason)` / `TryTow(...)` | Начать буксировку. |
| `AttachToAnchor(...)` / `FindFreeAnchor(...)` | Хук-система якорей. |
| `ReleaseTow(tug, towed, ...)` / `DetachSubtree(root, uidLookup)` | Отпустить. |
| `EnumerateTowedDescendants(...)` / `CountTowedSubtree(root, uidLookup)` | Обход поддерева. |
| `UpdateTowChain(tug, equipConfig, ...)` | Тик цепи. |
| `ComputeChainAttachPos(prev, towed, anchor)` | Позиция звена. |
| `RebuildTowedRenderPaths(...)` | Пересобрать рендерные пути. |

### `ShipDeathBus`
| Метод | Что делает |
|---|---|
| `event OnShipDestroyed(victim, killer, cause)` | Подписаться на события смерти. |
| `Emit(victim, killer, cause)` | Оповестить (обычно из `WeaponSystem`/`MissileSystem`). |

---

## 15. Политика и отношения

### `Relations` (`SRG.Galaxy.Politics`)
| Метод | Что делает |
|---|---|
| `Get(a, b)` (для Ship/Planet пар) | Число 0..100. |
| `GetLevel(a, b)` | `RelationLevel` (Hostile/Bad/Normal/Good/Best). |
| `AreHostile(a, b)` | Быстрый чекер. |
| `GetToPlayer(entity)` / `GetLevelToPlayer(entity)` | vs игрок. |
| `Set(a, b, value)` | Установка «личной» правки поверх фракционного. |

### `OccupationService`
| Метод | Что делает |
|---|---|
| `event OnPlanetControlChanged(planet, newOwner)` | Событие. |
| `event OnSystemControlChanged(star, oldOwner, newOwner)` | Событие. |
| `event OnFactionDefeated(ownerId)` | Событие. |
| `GetControllingOwner(planet)` | Кто держит планету (OccupiedBy или Owner). |
| `OccupyPlanet(planet, star, occupierOwnerId, turn)` | Оккупировать. |
| `LiberatePlanet(planet, star, turn)` | Освободить. |
| `BlockLanding(planet)` / `AllowLanding(planet)` | Флаг посадки. |
| `RecomputeSystemControl(star)` | Пересчитать `CurrentSystemController`. |
| `GetOccupationMode(ownerId)` | Режим по конфигу. |

### `OccupationAutoRule`
- `Tick(galaxy)` — правила автоматической смены контроля.

### `GovernmentChangeService`
- `ChangeGovernment(planet, cfg, action, turn)` — смена режима планеты (события/скрипты).

---

## 16. Экономика

### `TradeSystem` (`SRG.Economy`)
| Метод | Что делает |
|---|---|
| `GetDisplayName(goodId, cfg)` | Локализованное имя. |
| `CalculateBuyPrice(planet, goodId, stock, cfg)` | Цена покупки планетой. |
| `CalculateSellPrice(planet, goodId, stock, cfg)` | Цена продажи планетой. |
| `IsLegal(planet, goodId, cfg)` | Легален ли товар. |
| `GetBaseStock(planet, goodId, cfg)` | Базовый стек. |
| `InitPlanetShop(planet, cfg)` / `RecalculatePrices(planet, cfg)` | Начальная настройка / пересчёт. |
| `TickAll(galaxy, cfg)` / `TickDaily(planet, cfg)` | Тики. |

### `ShopService`
| Метод | Что делает |
|---|---|
| `GetEntry(planet, goodId)` | Запись из шопа. |
| `TryBuy(...)` / `TrySell(...)` | Покупка/продажа через шоп. |
| `RecalculatePrices(entry, planet, goodId, cfg)` | Пересчёт цены записи. |

### `EquipmentShopSystem`
| Метод | Что делает |
|---|---|
| `InitialFill(planet, ctx)` | Начальное наполнение магазина оборудования. |
| `TickAll(galaxy, ctx)` / `RefreshShop(planet, ctx)` | Тик и обновление ассортимента. |
| `InvalidateCandidateCache()` | Сброс кэша кандидатов. |

### `InflationSystem`
| Метод | Что делает |
|---|---|
| `TickMonthly(galaxy, cfg)` | Ежемесячный тик. |
| `ApplyShock(galaxy, cfg, type)` | Событие-шок цен. |
| `GetFactor(galaxy)` | Текущий `InflationFactor`. |

---

## 17. Наука и события планет

### `ScienceSystem`
- `TickAll(galaxy, ctx)` — прогресс изобретений (Distribute).

### `UnlockEffectsService`
- `ApplyAll(galaxy, ctx, ...)` — применить эффекты открытых изобретений (цены/шаблоны/события).

### `PlanetaryTechSystem`
| Метод | Что делает |
|---|---|
| `TickAll(galaxy, ctx)` | Прогресс техуровня. |
| `ApplyEvent(planet, eventId, cfg)` | Наложить событие. |
| `RemoveEvent(planet, eventId)` | Снять. |

### `PlanetaryEventSystem`
- `TickAll(galaxy, ctx)` — активные события планет.

---

## 18. NPC / спавн / ИИ

### `NpcSystem`
- `TickAllSystems(galaxy, ctx)` — общий тик AI за ход.

### `NpcSpawner` / `NpcSystemSpawner`
| Метод | Что делает |
|---|---|
| `NpcSpawner.Attach(obj, ship, star)` | Подключить `NpcController` к GameObject корабля. |
| `NpcSystemSpawner.PopulateStarWithNpcs(star, ctx)` | Начальный спавн NPC в системе. |
| `NpcSystemSpawner.PopulateSectorEntities(sector, ctx)` | То же для сектора. |
| `NpcSystemSpawner.FinalizeAfterGeneration(galaxy, ctx)` | Пост-генерация. |

### `NpcTargeting`
| Метод | Что делает |
|---|---|
| `FindNearestHostile(star, self, fromPos, ...)` | Ближайший враждебный корабль. |
| `FindNearestHostileUid(star, self, fromPos, ...)` | То же, вернёт UID. |
| `FindAnyHostileUid(star, self)` | Любой враждебный. |

### `NpcOutfitter`
| Метод | Что делает |
|---|---|
| `ScoreItem(item, shipType)` | Балл предмета для NPC-типа. |
| `ComputeFuelReserve(ship)` / `ComputeUpgradeBudget(ship)` | Резервы. |
| `TryUpgrade(ship, planet, ctx)` | Попытка апгрейда на планете. |
| `SellItemToShop(ship, planet, item)` | Продать шопу. |

### `NpcBalance` — константы AI (радиусы, дистанции): `AllyHelpTriggerRadiusSq`, `EscortThreatRadiusSq`, `ChaseRadius`, `FleeDistance`, `FollowDistance`, `ArrivalThresholdSq`, `PickupRadiusSq`, `NegotiateRange`, `ScanRange`, `TransferRange`, `MaxEngageRange`, ...

### `NpcBrain` (не static; сервисные static-методы)
- `CalculateStrength(ship)` — стрельба + HP + броня + щит.

### `SpawnFormulas`
| Метод | Что делает |
|---|---|
| `Shortage(count, target)` | Насколько недобрано. |
| `ShouldSpawn(count, target, baseChance)` | Триггерить ли спавн. |
| `StarCooldownReady(star, cfg)` | Кулдаун звезды (защита от каскадов). |

### `SpawnSystem` — `DailyTick`, счётчики (`Counters.GetShipsAt*`).

### `SubtypePicker`, `DominationCalculator`, `TraderAI` — внутренние подсистемы, вызывать редко.

### `DecisionTable` — реестр весов действий по CombatClass.

---

## 19. Новости и логи

### `GalaxyNewsService`
| Метод | Что делает |
|---|---|
| `Initialize()` | Инициализация подписок. |
| `Post(category, text)` | Опубликовать новость. |
| `event OnNewsAdded(entry)` / `OnNewsRemoved(entry)` | События. |
| `RemoveById(id)` | Удалить по id. |
| `ClassifyKind(category)` | Категория → `NewsKind`. |
| `Entries` | `IReadOnlyList<GalaxyNewsEntry>` (FIFO-буфер). |

### `GalaxyLogger` / `HighCommandLog` / `EconomicLog`
Флаги логирования (`Enabled`, `LogTrade`, `LogInflation`, ...) и методы `Trade/Shop/Inflation/Init/Eval/Directive/Expire/Progress/Flush`. Пишут в `logs/*.txt`.

---

## 20. Сохранение

### `GalaxySaveManager`
| Метод | Что делает |
|---|---|
| `SaveGalaxy(data)` | Сериализовать в JSON и записать. |
| `LoadGalaxy()` | Прочитать/десериализовать. |

---

## 21. Скилы пилота

### `SkillType` (enum) / `ExpCategory` (enum) / `SkillTypeExtensions`
- `SkillTypeExtensions.SkillCount = 6`, `.All` — массив всех.
- `SkillType.DisplayNameRu()` — локализованное имя.

### `ShipSkills`
| Метод | Что делает |
|---|---|
| `FromBase(int[] starting)` | Собрать из стартового массива. |
| `RollWeaponDamage(min, max, accuracy, mobility, cfg)` | Роллер урона по скилам. |

Динамические методы объекта — прокачка/бонусы/штрафы (не static, но публичны).

---

## 22. Утилиты владения / генерации

### `OwnerResolver`
| Метод | Что делает |
|---|---|
| `ResolveOwner(child, parent, ...)` / `ResolveRace(...)` | Наследование от родителя. |
| `Resolve(child, parent, ...)` | Общая точка. |
| `IsFixedOwner(owner, owners)` / `IsFixed(...)` / `IsFixedRace(race, races)` | Фиксирован ли. |
| `ComputeOwnerFromChildren(childOwners)` / `ComputeRaceFromChildren(...)` | Агрегация из детей (Single→он, Multi→Mixed, None→NONE). |

### `CustomPropertyResolver`
- Работа с `CustomProperties` планет/звёзд/секторов.

### `GenerationHelpers`
- Помощники для GalaxyGenerator (не для рантайма).

### `OwnershipDisplayResolver`
- Резолвит отображаемое имя/цвет владения.

### `GalaxyConstants`
- Ключи-константы: `OWNER_NONE_KEY`, `OWNER_MIXED_KEY`, `RACE_NONE_KEY`, `RACE_MIXED_KEY`, `DEFAULT_COLOR`, ...

### `GalaxyConfigLoader`
- Загрузка/парсинг всех JSON-конфигов (`LoadAll(...)`), доступ через `GalaxyManager.Context.Config`.

---

## 23. Приветствия — `PlanetGreetingRenderer` / селекторы

| Метод | Что делает |
|---|---|
| `PlanetGreetingRenderer.Render(player, planet, cfg, galaxy, rng?)` | Итоговый текст приветствия планеты. |
| `PlanetGreetingRenderer.RenderShip(player, ship, cfg, galaxy, rng?)` | То же для корабля. |
| `PlanetGreetingSelector.Choose(...)` | Голое правило + подобранная ToPlanet. |
| `ShipGreetingSelector.Choose(...)` | То же для ship-канала. |
| `PlayerGreetingProfile.ResolveStatus(ship)` / `ResolveRank(ship)` / `ResolveRating(ship, cfg)` | Производные признаки игрока. |
| `PlanetGreetingSelector.QuantOf*(...)` (`ShipCount`, `Stock`, `Price`, `Population`, `Money`) | Кванты в имена (Mini/Small/…/Huge). |

Схема правил и все поля — `PlanetGreetings/PlanetGreetingConfig.cs`.

---

## 24. Утилиты дебага — `DebugScripting`

`using static SRG.Utils.DebugScripting;`

| Функция | Что возвращает |
|---|---|
| `Player()` | Корабль игрока (null в главном меню). |
| `Galaxy()` | Текущая `GalaxyData`. |
| `CurStar(obj)` | Звезда для `StarData` / `ShipData` / `PlanetData` / `SatelliteData` / `WormholeData`. |
| `StarGalaxy(star)` | Галактика звезды (для мультигалактического режима). |

Композиция: `StarGalaxy(CurStar(Player()))`, `WormholeService.Spawn(Near(Player(), 100))`.

---

## 25. Как добавить свою скриптовую функцию

Из любого `MonoBehaviour.Start()`/`Awake()`:

```csharp
DialogService.RegisterAction("MyMethod", (ctx, args) =>
{
    if (args.Count == 0) return;
    Debug.Log($"MyMethod: {args[0]}");
});

DialogService.RegisterTag("my_flag",
    ctx => ctx?.Data.TryGetValue("flag", out var v) == true ? v : "0");

DialogService.RegisterParamTag("my_lookup",
    (ctx, arg) => LookupSomething(arg));
```

Реестры регистронезависимы, перезапись по имени допускается. `DialogService.Reset()` очищает всё — вызывать при выгрузке сцены / hot-reload.

Из консольной команды:
```csharp
// В GameConsoleController.RegisterCommands:
Reg("mycmd", "mycmd — описание", () => { /* ... */ });
```
