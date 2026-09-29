# Turn Pipeline

Модули: `Core/GalaxyManager.cs`, `Core/SystemViewManager.cs`, `Core/PlayerManager.cs`,
`Systems/HyperjumpController.cs`, `Generation/GalaxyDataModels.cs` (часть `GalaxyData.GalaxyNextDay`,
`StarData.StarNextDay`, `TurnAnimationData`).

---

## Назначение

Центральный пошаговый цикл симуляции. Между ходами игрок планирует и отдаёт команды (Planning),
по нажатию Space или автоматически запускается симуляция на 1 день (Simulation), в течение
SimulationDuration секунд проигрываются анимации, затем цикл возвращается в Planning.

Также управляет:
- межсистемными переходами (гиперпрыжок, миграция кораблей между звёздами);
- инициализацией стартовой галактики и системы;
- сохранением/загрузкой;
- сигналами «нужно остановить автобой» (`PlanningReason`).

---

## Публичный интерфейс

### `GalaxyManager` (singleton MonoBehaviour, `Instance`)

| Метод | Описание |
|---|---|
| `GenerateNewGalaxy(int seed)` | Сгенерировать новую галактику, заспавнить игрока. |
| `LoadGame()` / `SaveGame()` | Загрузка/сохранение через `GalaxySaveManager`. |
| `StartTurn()` | Триггер симуляции (как нажатие Space). |
| `ExecuteInstantTurn()` | Ход без анимации (используется когда игрок на планете). |
| `RequestPlanning(PlanningReason)` / `ClearPlanning(PlanningReason)` / `ClearAllPlanning()` | Управление списком причин планировочной паузы. |
| `IsAutoMode` | Истина, если нет открытых `PlanningReason`. |
| `SetCurrentStar(StarData)` | Сменить отображаемую в SystemView звезду игрока. |
| `TeleportToStar(StarData)` | Мгновенный перенос (debug/галакарта). |
| `JumpToStar(StarData)` | Старт многоходового гиперпрыжка через `HyperjumpController`. |
| `CancelHyperjump()` | Отмена прыжка (если в `Travel`/`HyperEnter`). |
| `SignalRouteComplete()` | Сообщить, что маршрут отыгран — поставить `RouteCompleted` после анимации. |
| `CalcDistanceParsecs(a, b)` | Расстояние между двумя звёздами в единицах сетки. |

### События (`static event`)

| Событие | Когда | Подписчики |
|---|---|---|
| `OnTurnCalculate(TurnAnimationData)` | После расчёта симуляции, перед анимацией | HUD, мини-карта, лог |
| `OnTurnAnimate(progress, currentSubTurn)` | Каждый кадр Simulation-фазы | UI-индикаторы прогресса |
| `OnTurnComplete(TurnAnimationData)` | После окончания анимации | UI, дроп, гиперпорталы |
| `PlayerManager.OnPlanetLanded(PlanetData)` | Игрок приземлился | `PlanetUIController` (открывает экран) |
| `PlayerManager.OnPlanetLeft()` | Игрок взлетел | `PlanetUIController` (закрывает экран) |
| `PlayerManager.OnPlayerDeathAttempt(PlayerDeathInfo)` | HP игрока обнулился, до регистрации смерти | Перехватчики (Phoenix, Repair Droid) — могут поставить `Cancelled = true` |
| `PlayerManager.OnPlayerDeathConfirmed(PlayerDeathInfo)` | После взрыва, смерть подтверждена | `DeathScreenController` |

### `PlayerManager` (singleton)

| Метод | Описание |
|---|---|
| `GetOrFindPlayerShip()` | Получить ссылку на корабль игрока (с кэшем). Единая точка — старая копия `Relations.FindPlayer` теперь делегирует сюда. |
| `FindPlayerStar()` | Звезда, в которой находится игрок. |
| `SpawnPlayer(star, ctx, settings)` | Создать корабль игрока в стартовой системе. |
| `KillPlayer(PlayerDeathCause, killerName, killerOwner)` | Регистрация смерти. Вызывает `OnPlayerDeathAttempt` (можно перехватить). Возвращает `true`, если игрок реально умер. |
| `CheckPauseConditions(TurnAnimationData)` | По итогам хода добавляет в `anim.PlanningReasons` причины паузы (HeavyDamage / LowHull / EnemyDestroyed). |

### `SystemViewManager` (singleton)

| Метод | Описание |
|---|---|
| `BeginTurnAnimation(TurnAnimationData)` | Подготовка визуала к симуляции (включая взрывы). |
| `AnimateSystem(progress, currentSubTurn, anim)` | Один кадр анимации. Внутри — несколько private-методов: `UpdatePlanetVisuals`, `UpdateShipVisuals`, `UpdateTowBeams`, `AnimateAsteroidPositions` + ракеты и эффекты. |
| `UpdatePlanetPositions()` | Финальная позиция планет на конец хода (без интерполяции). |
| `RenderSystem(StarData)` | Полный re-render системы (при смене звезды). |
| `EnsureShipVisual(ShipData)` | Гарантировать визуал для нового корабля (например, спавн контейнера). |

### `HyperjumpController` (static)

| Метод | Описание |
|---|---|
| `RequestJump(ship, targetStar, galaxy)` | Запросить прыжок. Возвращает `false` при отсутствии топлива/двигателя/JumpRange. |
| `CancelJump(ship)` | Отмена. Возвращает `false` если уже не в активной фазе. |
| `FinalizeTurn(ship, star, galaxy)` | Перевод фазы прыжка после анимации (см. `GalaxyManager.FinalizeHyperjumpPhases`). |
| `CancelOnRouteChange(ship)` | Снять `Travel` при смене маршрута игроком. |

---

## Зависимости

```
GalaxyManager
   ├─► GalaxyData.GalaxyNextDay       (расчёт)
   ├─► NpcSystem.TickAllSystems       (AI всех звёзд)
   ├─► PlayerManager                  (find player, kill, pause conditions)
   ├─► SystemViewManager              (анимация)
   ├─► HyperjumpController            (фазы прыжка)
   ├─► LoadingScreenController        (показ при HyperExit)
   ├─► OwnerRaceRelationsManager      (init)
   ├─► GraphicsManager                (init)
   ├─► GalaxyConstants                (init из GameSettingsConfig)
   ├─► EconomicLog.Flush              (раз в 30 ходов + при выходе)
   ├─► GraphicsManager.PreloadSheets  (прогрев кэша, единый sink)
   └─► SpawnSystem                    (Recount при загрузке)
```

`SystemViewManager` зависит от: `GalaxyManager.CurrentStar`, `PlayerShip.Instance`,
`MinimapController`, всех Visual-контроллеров, `WeaponVisualSystem`, `ExplosionPlayback`.

`PlayerManager` зависит от: `GalaxyManager.GeneratedGalaxy`, `OwnerRaceRelationsManager`,
`PlayerShip.Instance` (для CheckPauseConditions).

---

## Алгоритмы и формулы

### Главный цикл (`Update`)

```
Update():
  if GameConsole open → ignore Space
  if Space pressed:
    if Phase == Simulation → RequestPlanning(PlayerInput)
    else if Phase == Planning → ClearAllPlanning(); StartTurn()
  if _pendingArrivalTransition:        # переход в новую систему отложен
    ProcessDeferredArrivalTransition() # migrate + RenderSystem + camera
    if IsAutoMode → ExecuteTurnCalculation()
    return
  if Phase == Simulation → TickSimulation()
```

### `ExecuteTurnCalculation`

1. `LastTurnData = GeneratedGalaxy.GalaxyNextDay(ctx)` — расчёт нового состояния всех звёзд (внутри `StarNextDay` на каждую звезду).
2. `GeneratedGalaxy.MigrateShipsBetweenStars()` — корабли с `CurrentStarUid` отличным от их `star.Uid` перемещаются в соответствующие списки. Делается СРАЗУ (не в конце симуляции), чтобы `NpcSystem.TickAllSystems` увидел корабли в новых системах для AI следующего хода.
3. `NpcSystem.TickAllSystems(galaxy, ctx)` — каждый корабль (кроме игрока) получает один Tick от своего `NpcBrain`.
4. Раз в 30 ходов: `EconomicLog.Flush()`.
5. `PlayerManager.CheckPauseConditions(LastTurnData)` — добавляет в `LastTurnData.PlanningReasons` причины паузы.
6. Все `PlanningReason` из `LastTurnData` добавляются в `_planningRequests`.
7. `SystemViewManager.BeginTurnAnimation(LastTurnData)` — взрывы, прогрев VFX-pool.
8. Событие `OnTurnCalculate(LastTurnData)`.
9. Переход `Phase = Simulation`, `_simulationTimer = 0`.

### `TickSimulation`

```
_simulationTimer += Time.unscaledDeltaTime
progress = clamp01(_simulationTimer / SimulationDuration)
newSubTurn = ceil(progress * SubTurnsPerTurn)  # 1..SubTurnsPerTurn
SystemViewManager.AnimateSystem(progress, newSubTurn, LastTurnData)
OnTurnAnimate(progress, newSubTurn)

# Loading screen скрывается на первом тике HyperExit:
if LoadingScreen visible AND player.HyperjumpPhase == HyperExit:
    LoadingScreen.Hide()

if _simulationTimer >= SimulationDuration:
    CompleteCurrentTurn()
```

### `CompleteCurrentTurn`

1. `UpdatePlanetPositions()` — финальные позиции планет.
2. Событие `OnTurnComplete(LastTurnData)`.
3. Если `_simulationInterrupt` — добавить `RouteCompleted` в planning (заявка от `SignalRouteComplete`).
4. `FinalizeHyperjumpPhases()` — для всех кораблей всех звёзд: `HyperjumpController.FinalizeTurn`.
5. `HandleHyperjumpTransitions()` — реагирует на смену системы ИГРОКА:
   - `HyperExit` достигнут, `CurrentStarUid` отличается → `LoadingScreen.Show()`, ставим `_pendingArrivalTransition = true`, выходим (heavy work отложится на следующий кадр).
   - В фазе `Travel/HyperEnter/HyperExit` → снимаем `RouteCompleted`-паузу, чтобы 2-ходовая анимация шла без Space.
   - Только что мигрировали (`PreviousStarUid != CurrentStarUid`, TargetPosition≈Position) → `RequestPlanning(PlayerInput)`, чтобы игрок осмотрелся. PreviousStarUid «съедается» (присваивается CurrentStarUid), иначе условие срабатывало бы каждый ход.
6. Если `_pendingArrivalTransition` — выходим в `Planning` без запуска следующего хода.
7. Иначе `SystemViewManager.GetHyperPortals()?.Tick(1f)` — толкаем порталы.
8. Phase = Planning. Если `IsAutoMode` — снова `ExecuteTurnCalculation`.

### Гиперпереход — 2-ходовая модель

Прыжок занимает **2 хода**: `HyperEnter` (день 1) + `HyperExit` (день 2). См. memory `project_hyperjump_2turn`.

- День 1: корабль летит к `HyperjumpEdge` (край системы), фаза = `Travel` (если ещё не достиг края) или `HyperEnter` (если достиг и портал открыт).
- День 2: фаза `HyperEnter` финализируется в конце хода → переходит в `HyperExit`, `CurrentStarUid` меняется на целевую звезду, корабль появляется на `HyperjumpArrivalEdge`. Migrate + scene swap откладывается на следующий кадр через `_pendingArrivalTransition` (чтобы loading screen успел отрисоваться).
- День 3+: фаза снова `None`, игрок управляет в новой системе.

AI-приказ (`ActionHyperJumpTo`) для NPC ставится в `JumpToStar` (тест-режим: все NPC текущей системы летят за игроком).

---

## Константы и настройки

| Имя | Источник | По умолч. | Назначение |
|---|---|---|---|
| `SimulationDuration` | `settings.TurnDuration` (GameSettingsConfig) | 2.0 сек | Длина анимационной фазы |
| `GalaxyData.SubTurnsPerTurn` | константа в `GalaxyData` | 10 | Число сабтёрнов на 1 ход (мелкий шаг физики/боя) |
| `HEAVY_DAMAGE_FRACTION_PER_TURN` | `PlayerManager` | 0.20 | Порог HeavyDamage-паузы в автобое (% MaxHull за ход) |
| `LOW_HULL_AUTOCOMBAT_FRACTION` | `PlayerManager` | 0.05 | Порог LowHull-паузы в автобое (% MaxHull) |
| `LOW_HULL_MANUAL_FRACTION` | `PlayerManager` | 0.25 | Порог LowHull-паузы в ручном бою (% MaxHull) |
| `30` | `GalaxyManager` (магия в `% 30 == 0`) | — | Период flush'а EconomicLog в ходах |

---

## Внутренняя структура данных

### `TurnAnimationData` (Generation/GalaxyDataModels.cs)

Аккумулятор данных «как анимировать ход». Заполняется в `StarNextDay`/`CombatSubTurn`, читается
`SystemViewManager.AnimateSystem`. Ключевые поля:

- `PlanetAngles: Dictionary<string, (From, To)>` — для интерполяции орбит.
- `ShipFrames: Dictionary<string, ShipSubTurnFrames>` — позиции каждого корабля по сабтёрнам.
- `ShipRenderPaths: Dictionary<string, List<Vector2>>` — плотный путь для красивой анимации (включая буксиры, см. `TowSystem.RebuildTowedRenderPaths`).
- `Shots: List<ShotEvent>` — выстрелы (визуал — `WeaponVisualSystem`).
- `AsteroidFrames / AsteroidSpawns / AsteroidDestroys` — астероиды.
- `MissileFrames: Dictionary<string, MissileSubTurnFrames>` + `MissileDeathUids` + `MissileSilentDeathUids`.
- `DeathUids: HashSet<string>` — UID погибших кораблей этого хода (для визуала взрывов).
- `PlanningReasons: HashSet<PlanningReason>` — причины автостопа.

### `PlanningReason` (enum)

- `PlayerInput` — игрок нажал Space или прибыл в новую систему;
- `RouteCompleted` — маршрут отыгран;
- `HeavyDamage` — потеря >20% HP за ход в автобое;
- `LowHull` — HP ниже порога;
- `EnemyDestroyed` — цель follow-режима погибла.

### `HyperjumpPhase` (enum)

- `None` — не прыгает;
- `Travel` — летит к `HyperjumpEdge`;
- `HyperEnter` — портал открыт, последний ход в исходной системе;
- `HyperExit` — первый ход в целевой системе (CurrentStarUid уже сменился, корабль на `HyperjumpArrivalEdge`).

### `TurnPhase` (enum)

- `Planning` — игрок управляет;
- `Simulation` — анимация хода в процессе.

---

## Известные ограничения / TODO

- `SystemViewManager.AnimateSystem` после рефакторинга B2 сокращён, но **внутренний missile-блок (~150 строк)** ещё не разбит — там сложное переплетение spawn/destroy/animate с фильтром по `animating` и `_firedDestroyEvents`. Разбор оставлен на следующий заход.
- `StarData.StarNextDay` (188 строк) **продолжает совмещать данные и логику симуляции**. Вынесение в отдельный `StarSimulator` — Тир C рефакторинга (отложено решением пользователя).
- `_planningRequests` — `HashSet<PlanningReason>` без приоритетов: если активна одна причина, добавление другой не меняет поведение. Если потребуется отображать «почему остановлены» — лог уже идёт через `RequestPlanning`, но UI не отрисовывает.
- 2-ходовая модель гиперперехода: в визуале между HyperEnter и HyperExit есть «шов» в один кадр (показ loading screen), что воспринимается как мерцание. Текущая обработка через `_pendingArrivalTransition` сглаживает, но архитектурно это костыль вокруг отсутствия multi-frame deferred render Unity'ем.
- Период flush'а EconomicLog (30 ходов) — магическое число в `ExecuteTurnCalculation:146`. Если экономика расширится — вынести в const.
