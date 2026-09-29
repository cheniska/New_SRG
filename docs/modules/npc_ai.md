# NPC AI

Модули: `NpcAI/NpcBrain.cs`, `NpcAI/Actions/Action*.cs` (18 файлов), `NpcAI/Orders/Order*.cs` (18 файлов),
`NpcAI/NpcAction.cs` (база), `NpcAI/NpcOrder.cs` (база), `NpcAI/FactionDirective.cs`,
`NpcAI/TraderAI.cs`, `NpcAI/NpcSystem.cs`, `NpcAI/NpcSpawner.cs`, `NpcAI/NpcController.cs`,
`NpcAI/NpcDecisionTable.cs`, `Ships/ShipPersonality.cs`, `Ships/ShipRole.cs`.

Этап рефакторинга **B1/B1b** разнёс ранее монолитные `NpcAction.cs` (886 LOC, 18 классов) и
`NpcOrder.cs` (727 LOC, 18 классов + утилита) по отдельным файлам в `Actions/` и `Orders/`.
Поведение не менялось.

---

## Назначение

Двухуровневая система принятия решений для NPC-кораблей:

- **Order** (приказ) — атомарное «как сделать»: лететь к точке, атаковать, прыгать, ждать. Длится сколько нужно.
- **Action** (тактика) — «что делать»: патрулировать, преследовать и атаковать, торговать, бежать. Внутри использует один или несколько Order'ов.
- **Brain** — каждый ход выбирает Action на основе личности корабля (ShipPersonality), боевой ситуации, директив фракции.
- **FactionDirective** — приказы сверху от фракции (SuppressPiracy, RaidEnemyTerritory и т.п.), которые переопределяют локальный Brain-выбор.
- **TraderAI** — специализированная под-логика поиска выгодного межпланетного рейса для `ActionGoodsTrader`.

---

## Публичный интерфейс

### `NpcSystem` (static)

Единственная публичная точка входа из ядра.

```csharp
public static void TickAllSystems(GalaxyData galaxy, GalaxyGenerationContext ctx);
```

Вызывается из `GalaxyManager.ExecuteTurnCalculation` после `GalaxyNextDay`. Для каждого живого NPC
во всех звёздах:

1. Если `Brain == null` → создать `new NpcBrain(ship, star)`.
2. `Brain.Tick(ship, star, ctx)`.

### `NpcBrain` (instance, поле `ShipData.Brain`)

| Член | Описание |
|---|---|
| `Personality { get; }` | `ShipPersonality` (Aggression/Caution/Greed/Discipline/Frustration). |
| `CombatClass { get; }` | `Civilian / Pirate / Mercenary / Military` (по `ResolveCombatClass(shipTypeId)`). |
| `CurrentOrder => directiveActivity?._name ?? _currentActivityName` | Имя для UI. |
| `CurrentActivity` | Текущий `NpcAction`. |
| `GetCombatTargetUid()` | UID цели огня (если есть). Используется `CombatSubTurn` чтобы NPC стрелял по преследуемому игроку даже без формальной вражды владельцев. |
| `Tick(ship, star, ctx)` | Один ход AI: оценить директиву, оценить ситуацию, при необходимости сменить Action, выполнить Tick текущего Action. |
| `ForceActivity(NpcAction action, string debugName)` | Снаружи (например, `GalaxyManager.JumpToStar`) ставит конкретную активность. |
| `static ResolveCombatClass(string shipTypeId)` | Маппинг типа корабля в класс. |
| `static CalculateStrength(ShipData)` | Боевая мощь корабля (используется в `EvaluateSituation`). |

### `NpcAction` (abstract)

```csharp
public abstract bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx);
public bool IsCompleted { get; protected set; }
public virtual string DebugName => GetType().Name;
public virtual string CombatTargetUid => null;        // UID цели для CombatSubTurn (если есть)
protected static ShipData FindShip(StarData star, string uid);
protected static ShipData FindAliveShip(StarData star, string uid);
```

Все наследники — по одному в файле `Actions/Action*.cs`. Полный список:
- `ActionPatrol` — патруль между двумя случайными точками внутри 50% радиуса системы.
- `ActionPursueAndAttack` — преследование+атака конкретной цели; растит Frustration если HP цели не падает.
- `ActionDisable` — атака до пороговых HP цели (для абордажа/допроса).
- `ActionRob` — двухфазная: Disable → Negotiate (предложение Ceasefire) + 50% грабёж стека.
- `ActionFlee` — бежать от конкретной угрозы (через `OrderFlee`).
- `ActionRetreatAndRepair` — лететь к ближайшей дружественной планете, ждать `RepairTurns=5`, восстановить HP.
- `ActionEscort` — следовать за подопечным; при обнаружении угрозы в радиусе `ESCORT_THREAT_RADIUS_SQ=25` — `ActionPursueAndAttack`.
- `ActionDefend` — стоять в зоне радиусом `_radius` (по умолч. 4); при появлении врага — преследовать.
- `ActionScavenge` — бесцельные перелёты по случайным точкам; через 15 ходов сменить точку.
- `ActionMine` — лететь к ближайшему астероиду, idle 4 хода; повторить 3 раза.
- `ActionTrade` — простой перелёт между планетами с задержкой `DockDuration=3`.
- `ActionGoodsTrader` — настоящий торговец: использует `TraderAI.PlanRoute` для поиска прибыльного рейса; машина состояний (Hyper Jump → Land → Sell → Plan → Buy → Launch).
- `ActionDeliver` — A → idle → B → idle (для контрактов).
- `ActionRecon` — облёт всех планет системы.
- `ActionIdle` — простоять заданное число ходов.
- `ActionHyperJumpTo` — обёртка над `OrderHyperJump` для конкретной целевой звезды.
- `ActionRequestCeasefire` — попросить агрессора о перемирии (передача 30% груза).
- `ActionOfferMoneyRansom` — предложить деньги преследователю; сумма от 20% (полный HP) до 80% (нулевой) накоплений.

### `NpcOrder` (abstract)

```csharp
public abstract bool Execute(ShipData ship, StarData star, GalaxyGenerationContext ctx);   // true = выполнено
public abstract bool IsCompleted(ShipData ship, StarData star);
protected static void SetMoveTarget(ShipData ship, StarData star, Vector2 target);          // прокладывает путь через ShipTrajectory.BuildPath
protected static ShipData FindShip / FindAliveShip;
```

Полный список (`Orders/Order*.cs`):
- `OrderMoveTo(target)` — лететь до точки (порог `ArrivalThresholdSq=0.25`).
- `OrderFollow(targetUid)` — следовать за кораблём, точка позади leader-а с jitter ±45° по UID-seed.
- `OrderOrbit(center, radius, speed=15°)` — облёт точки.
- `OrderLand(planetUid)` — двухфазная посадка: фаза 1 (подлёт) → ShipVisualController ставит `LandingPhase=Fading` → фаза 2 (финализация в OrderLand) — `LandedPlanetUid` присвоен. Для NPC в чужой звезде (без визуала) fallback по `distSq <= R_land²`.
- `OrderUndock` — улететь на 1.5 ед. в случайном направлении (одноразово).
- `OrderHyperJump(targetStarUid)` — запросить прыжок через `HyperjumpController`, дальше фазы крутятся в `StarNextDay`.
- `OrderAttack(targetUid)` — преследовать до `stopDist = max(sprite, targetSprite) * 1.5`, не зависать сверху. Считается выполненным когда цель в радиусе `ChaseRadius=8`.
- `OrderHold(position, turns=∞)` — стоять (с интерполяцией позиции к point).
- `OrderFlee(fromTargetUid)` — бежать на `FleeDistance=10` от угрозы, но не дальше `sysR * 0.85` от центра системы.
- `OrderLoot(lootPos)` — лететь к точке, активируется при `< 0.6²` (одноразово).
- `OrderTransfer(targetUid, itemId?, amount?)` — лететь к получателю (`TransferRange=1.0`), передать предмет/стек.
- `OrderScan(targetUid)` — лететь в `ScanRange=2`, считать HP/Owner/Race цели в лог.
- `OrderRequestCeasefire(aggressorUid)` — приблизиться (`NegotiateRange=2`), если Greed агрессора 40..60 — отклонить, > 60 — принять с передачей 30% стека + бонус отношений «солидарность планеты».
- `OrderOfferMoneyRansom(receiverUid, offer)` — приблизиться, рассчитать приём по классу/жадности/CrimeRating; шанс `0.3 + (offer/threshold) * 0.4`.
- `OrderIdle(turns=3)` — стоять и ждать.
- `OrderWait(cond, maxTurns=20)` — стоять до условия или таймаута.
- `OrderPatrol(a, b)` — пинг-понг между точками.
- `OrderNone` — синоним «ничего не делать».

Утилита `CeasefireSolidarity` (в `OrderOfferMoneyRansom.cs`):

```csharp
public static void ApplyHomePlanetBonus(ShipData payer, ShipData beneficiary, StarData star);
```

При успешном перемирии — всем военным кораблям в звезде, у которых `HomePlanetUid == beneficiary.HomePlanetUid`, ставится `ApplyAllyBonus(payer.Uid)`. Военные одной планеты держатся заодно: откупился от одного — смягчаются и остальные.

### `FactionDirective`

Директивы от фракции, выдаваемые `DirectiveManager`. Реализованные:
- `DirectiveSuppressPiracy` — отправить военных в атаку на пиратов в системе. `TurnsRemaining=30`.
- `DirectiveRaidEnemyTerritory` — атаковать корабли враждебной фракции.
- `DirectiveDefendHomeSystem` — оборона своей звезды.

Brain в `EvaluateDirective` смотрит свои `ship.ActiveDirectives` и (если applicable) запускает соответствующий Action через `_directiveActivity`.

### `TraderAI` (static)

```csharp
public const int IdleWaitTurnsConst = 8;     // ходов отдыха при no_profit (используется и ActionGoodsTrader)
public static TradeRoute PlanRoute(ShipData ship, StarData star, GalaxyData galaxy, GalaxyConfig cfg);
public static bool BuyCargo(ShipData ship, PlanetData planet, string goodId, int amount, GalaxyConfig cfg);
public static void SellCargo(ShipData ship, PlanetData planet, GalaxyConfig cfg);
```

Кэш `_reachableCache` живёт один ход (инвалидация по `_cacheTurn`) — общий для всех трейдеров.

---

## Зависимости

```
NpcSystem.TickAllSystems
   └─► NpcBrain.Tick
         ├─► FactionDirective (если в ship.ActiveDirectives)
         ├─► OwnerRaceRelationsManager.AreHostile (поиск целей)
         ├─► NpcAction (текущая активность)
         │     ├─► NpcOrder (внутри Action)
         │     │     ├─► ShipTrajectory.BuildPath (через SetMoveTarget)
         │     │     ├─► HyperjumpController.RequestJump (для OrderHyperJump)
         │     │     └─► PlanetGeometry.PredictNpcLandingTarget (для OrderLand)
         │     └─► TraderAI.PlanRoute (для ActionGoodsTrader)
         └─► NpcBrain.CalculateStrength (оценка системной мощи)

NpcController : ShipVisualController (RequireComponent)
   └─► Brain ↔ ShipData.Brain
```

Зависимости НА уровень UI/Visual:
- `NpcController` имеет `[RequireComponent(typeof(ShipVisualController))]` — AI привязан к рендеру (см. TODO).
- `OrderOfferMoneyRansom`, `OrderRequestCeasefire` (через `CeasefireSolidarity`) пишут в `GameConsoleController.AddEntry` — лог UI.

---

## Алгоритмы и формулы

### `NpcBrain.Tick(ship, star, ctx)`

```
1. EvaluateDirective(ship, star, ctx):
     если в ActiveDirectives есть FactionDirective.Applicable(ship) — выставить _directiveActivity
2. Если _directiveActivity не null и не IsCompleted → Tick(directiveActivity), return
3. EvaluateSituation(ship, star):
     - оценить системную мощь (CalculateStrength всех своих vs всех враждебных)
     - если враги * 2.5 > свои и Caution > 70 → SetActivity(ActionFlee)
     - если Frustration > 70 → SetActivity(ActionHyperJumpTo(closest_friendly_star))
     - если HP < FleeHullPercent (по Personality) → ActionFlee или ActionOfferMoneyRansom (если деньги > 100)
     - если HP < 0.35 и CombatClass == Pirate → ActionRequestCeasefire
     - если союзник под атакой в радиусе ALLY_HELP_TRIGGER_RADIUS_SQ=16 и Aggression > 60 → ActionPursueAndAttack(threat)
     - если Greed > 40 и торговец рядом → ActionRob
     - если Greed > 60 и trader-класс → ActionGoodsTrader
     - военный в радиусе зоны 0.6 от центра → ActionDefend
     - иначе → ActionPatrol (или ActionScavenge / ActionMine по типу)
4. _currentActivity.Tick(ship, star, ctx)
```

### `OrderFollow` — место в строю «клин»

```
leaderHeading = leader.CurrentHeading (или направление self→leader, если NaN)
hash = ship.Uid.GetHashCode()
row  = 1 + hash % 3            # ряд 1..3 назад
side = ±1 по биту hash         # левый/правый борт
offset = normalize(back·(0.6 + 0.4·row) + flank·(0.5·row)) · (0.8 + 0.2·row)
target = leader.Position + offset · FollowDistance
```

Место зависит только от курса ведущего и UID ведомого, поэтому цель не дрожит между ходами,
а несколько ведомых выстраиваются клином, не сходясь в одну точку.

### `OrderAttack` — STOP-distance

Чтобы не зависать сверху цели:
```
stopDist = max(self.SpriteWorldSize, target.SpriteWorldSize) * 1.5
если |self - target| > 0.0001 → dir = (self - target).normalized
иначе → dir по противоположному курсу цели (или Vector2.right если NaN)
SetMoveTarget(self, star, target.Position + dir * stopDist)
вернуть (dist > ChaseRadius=8)   # пока цель далеко — Order не завершён, продолжаем
```

### `OrderHyperJump`

Идемпотентен: повторные `Execute` безопасны.
```
если ship.CurrentStarUid == _targetStarUid → return true
если !_requested и phase==None → _requested = HyperjumpController.RequestJump(...)
вернуть (_requested и phase != None)
```

### `ActionOfferMoneyRansom.CalculateOffer`

```
если ship.Money <= 0 или ship.MaxHull <= 0 → 0
hpLost = 1 - CurrentHull / MaxHull            # 0 (полный HP) .. 1 (нулевой HP)
fraction = clamp01(0.2 + hpLost * 0.6)        # 20%..80% от Money
return max(50, round(ship.Money * fraction))
```

### `OrderOfferMoneyRansom.EvaluateMoneyRansomAcceptance`

Базовый порог принятия по классу получателя:

| Класс | Threshold |
|---|---|
| Civilian | 100 |
| Pirate | `500 - p.Greed * 4` (greedy → 100, low → 500) |
| Mercenary | `800 - p.Greed * 6` (greedy → 200, low → 800) |
| Military | `2000 + p.Discipline * 30` (disciplined → 5000, low → 2000) |

Дополнительно для Military: если `receiver.CrimeRating > 30` — отказ всегда (вендетта).

Если offer ≥ threshold:
```
ratio = offer / threshold
chance = clamp01(0.3 + ratio * 0.4)
return Random.value < chance
```

### `OrderRequestCeasefire`

При приближении (`NegotiateRange=2.0`):
- Если Greed агрессора > 60 → принять. Передать 30% всех стеков. `ApplyAllyBonus(ship.Uid)`. `CeasefireSolidarity.ApplyHomePlanetBonus`.
- Если Greed агрессора < 40 → отказ.
- В обоих случаях `_aggressor.CrimeRating += 5`.

### `OrderLand` — двухфазная посадка

Атомарный ордер обёрнут вокруг ShipVisualController-флага `LandingPhase`:

```
Фаза 1 (подлёт):
  ship.LandingPlanetUid = planet.Uid
  target = PlanetGeometry.PredictNpcLandingTarget(ship, planet)  # jitter ±20° по UID-seed
  SetMoveTarget(ship, star, target)
  return false

Между ходами:
  если игрок в этой звезде → ShipVisualController.PrepareForTurn ставит LandingPhase=Fading,
    когда хвост анимации этого хода попадает внутрь R_land
  иначе (NPC в чужой звезде) → проверка distSq <= R_land² напрямую в фазе 2

Фаза 2 (финализация):
  если LandingPhase==Fading или offscreenArrival → 
    ship.LandedPlanetUid = planet.Uid
    ship.LandingPlanetUid = null
    ship.LandingPhase = None
    FreezeRoute (TargetQueue/Waypoints/TargetPosition)
    _done = true
```

Геометрия R_land/R_app и точки прицеливания — `landing.md`.

---

## Константы и настройки

### Балансовые (in-code, по решению пользователя НЕ переносить в JSON):

| Имя | Файл | Значение | Смысл |
|---|---|---|---|
| `ALLY_HELP_TRIGGER_RADIUS_SQ` | NpcBrain | 16 (=4²) | Радиус² «союзник в беде — лечу помогать» |
| `ESCORT_THREAT_RADIUS_SQ` | ActionEscort | 25 (=5²) | Радиус² «угроза рядом с подопечным» (больше, чем help — эскорт обязан реагировать раньше) |
| `MaxEngageRange` | ActionPursueAndAttack | 10 | Не используется напрямую, но эталон |
| `FrustrationCheckInterval` | ActionPursueAndAttack | 5 ходов | Период проверки прогресса по HP цели |
| `RepairTurns` | ActionRetreatAndRepair | 5 | Сколько стоять на дружественной планете |
| `MaxCycles, MineTurns` | ActionMine | 3, 4 | Циклов добычи, idle между ними |
| `DockDuration` | ActionTrade | 3 | Idle у планеты |
| `IdleWaitTurnsConst` | TraderAI | 8 | Ожидание после неудачного PlanRoute (используется и ActionGoodsTrader) |
| `MinProfitPerUnit` | TraderAI | 2 | Минимальная прибыль/единица для рассмотрения рейса |
| `MaxJumpHops` | TraderAI | 1 | Глубина поиска по соседним системам |
| `CandidatePruneLimit` | TraderAI | 6 | Топ-N кандидатов для глубокой оценки |
| `ChaseRadius` | OrderAttack | 8 | Цель считается «достигнутой» |
| `FollowDistance` | OrderFollow | 1.5 | Позади leader-а |
| `FleeDistance` | OrderFlee | 10 | Цель побега |
| `NegotiateRange` | OrderRequestCeasefire / Ransom | 2.0 | На какое расстояние подлететь |
| `OfferFraction` | OrderRequestCeasefire | 0.3 | Доля стека в качестве «выкупа» |
| `ScanRange` | OrderScan | 2.0 | На какое расстояние подлететь |
| `TransferRange` | OrderTransfer | 1.0 | На какое расстояние подлететь |
| `ArrivalThresholdSq` | OrderMoveTo, OrderPatrol | 0.25 (=0.5²) | Цель достигнута |
| `PickupRadiusSq` | OrderLoot | 0.36 (=0.6²) | Лут собран |

Личностные пороги (`ShipPersonality`):

| Имя | Источник | Использование |
|---|---|---|
| Aggression > 60 | NpcBrain | Триггер `ActionPursueAndAttack` при ally-helping |
| Caution > 70 | NpcBrain | Триггер `ActionFlee` при превосходстве врага |
| Frustration > 70 | NpcBrain | Триггер `ActionHyperJumpTo` (свалить из системы) |
| Greed > 60 | NpcBrain | Триггер `ActionGoodsTrader` для торговцев |
| Greed > 60 / < 40 | OrderRequestCeasefire | Принять/отказаться от ceasefire |
| FleeHullPercent | ShipPersonality | Порог HP для побега (зависит от типа корабля) |

---

## Внутренняя структура данных

### `ShipPersonality` (Ships/ShipPersonality.cs)

```
Aggression : float [0..100]   // склонность атаковать
Caution : float [0..100]      // склонность отступать
Greed : float [0..100]        // склонность к наживе/торговле
Discipline : float [0..100]   // зависит от типа (Military — выше)
Frustration : float [0..100]  // растёт при безуспешных атаках, падает после успеха
FleeHullPercent : float       // динамически от Caution и типа

AddFrustration(amount) / ReduceFrustration(amount)
ApplyAllyBonus(uid)           // персональный буст отношений (личностный, не фракционный)
```

### `CombatClass` (enum)

`Civilian / Pirate / Mercenary / Military`.

### `FactionDirective` (abstract)

```
TurnsRemaining : int          // ходов до автоэкспирации
bool Applicable(ShipData)     // подходит ли это кораблю
NpcAction Activate(ShipData)  // запустить активность
```

Хранится в `ship.ActiveDirectives` (List).

### `TradeRoute` (struct в `TraderAI`)

```
Valid : bool
GoodId / BuyPlanetUid / BuyStarUid / BuyPrice
SellPlanetUid / SellStarUid / SellPrice
Amount / FuelLeg1 / FuelLeg2 / ProfitTotal
```

---

## Известные ограничения / TODO

1. **`NpcController : ShipVisualController` через RequireComponent** — AI жёстко привязан к рендеру.
   Нельзя тестировать AI без Visual-слоя; нельзя гонять AI «отключённой» звезды без визуала. Перенос
   в Тир C (отложено).
2. **Балансовые числа NpcAI остаются in-code** (по явному решению пользователя). Если потребуется
   live-балансировка, нужен JSON-конфиг + перепарсинг.
3. **`NpcAction.cs` / `NpcOrder.cs` базовые классы дублируют helpers** `FindShip`/`FindAliveShip` —
   потенциально можно вынести в `NpcAiHelpers.cs`, но базы редко меняются и дубль маленький (8 строк).
4. **`TraderAI._reachableCache` инвалидируется только по смене `_cacheTurn`** — если у корабля поменялся
   JumpRange внутри хода (например, потерял топливный бак), кэш отдаст устаревшие данные. На практике
   редко, но возможен баг с «фантомным маршрутом».
5. **`NpcOrder.SetMoveTarget` строит путь через `ShipTrajectory.BuildPath` КАЖДЫЙ ход** даже если цель не
   изменилась (например, `OrderHold` стоит на месте). Накладные расходы минимальны (пустой путь), но
   можно кэшировать по «нового нет».
6. **CeasefireSolidarity применяется только если beneficiary — Military** (см. условие в коде).
   Это намеренно: «солидарность планеты» имеет смысл только для военных представителей. Если позже
   потребуется похожая механика для гражданских — копировать с переменой условия.
