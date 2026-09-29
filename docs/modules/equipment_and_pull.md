# Equipment, Inventory, Shop & Pull Mechanics

Модули: `Equipment/EquipmentSystem.cs`, `Equipment/InventoryService.cs`, `Equipment/ItemFactory.cs`,
`Equipment/EquipmentTemplate.cs`, `Equipment/EquipmentSlotKeys.cs`, `Equipment/ItemsConfigModels.cs`,
`Systems/ShopService.cs`, `Systems/PickupSystem.cs`, `Systems/TowSystem.cs`, `Systems/BoardingSystem.cs`,
`Systems/HullAnchorMath.cs`, `Systems/PullKind.cs`, `Systems/HyperjumpController.cs`,
`Systems/FuelService.cs`, `Ships/ShipFactory.cs`, `Ships/ContainerFactory.cs`,
`Ships/CargoUtils.cs`.

Связанные документы: `equipment_tiers (2).md`, `Dominator_Equipment_Consolidated.txt`,
`Ship_Landing_Pipeline.txt`.

---

## Назначение

Управление оборудованием корабля, инвентарём, торговым взаимодействием с планетами и тремя
механиками «захвата другого объекта»: подбор контейнеров (Pickup), буксир кораблей и тяжёлых
предметов (Tow), абордаж кораблей (Boarding). Плюс гиперпрыжок (Hyperjump) — двух-ходовой
межсистемный переход.

Сервисный слой (`InventoryService`, `ShopService`) создан на этапе C рефакторинга, чтобы UI и
NPC-логика более не лазили в коллекции корабля напрямую.

---

## Публичный интерфейс

### `EquipmentSystem` (static — ядро установки/износа)

```csharp
// Установить предмет в слот из инвентаря. allowInSpace должен быть true для смены в космосе.
public static EquipResult Install(
    ShipData ship, string slotKey, string itemUid,
    EquipmentConfig equipConfig, bool allowInSpace = false);

// Получить предмет в слоте (или null).
public static ItemInstance GetEquipped(ShipData ship, string slotKey);

// Поиск первого работающего предмета указанной категории во всех слотах.
// Используется PickupSystem (CargoGrabber), TowSystem (TowingRig), BoardingSystem (BoardingHook).
public static ItemInstance FindEquipmentByCategory(
    ShipData ship, string category, bool requireWorking = true);

// Случайный работающий предмет, который можно повредить (CanBeDamagedByWeapon). Использует
// общий буфер. Вызывается из WeaponSystem.ProcessShot и MissileSystem.ApplyMissileImpact.
internal static (ItemInstance item, string slotKey) GetRandomDamageableSlot(
    ShipData ship, EquipmentConfig equipConfig);

// Категория слота по имени слота (всё что до последнего '_').
public static string SlotCategory(string slotKey);

// Чтение параметров корпуса.
public static float GetHullParam(ShipData ship, string paramKey);
public static float GetHullVulnerability(ShipData ship, string damageType);
public static float GetHullSusceptibility(ShipData ship, DamageType type);

// Топливо.
public static int GetCurrentFuel(ShipData ship);
public static int GetFuelCapacity(ShipData ship);
public static void ConsumeFuel(ShipData ship, int amount);

// Износ.
public static void ApplyWeaponShotWear(ShipData ship, string weaponSlotKey, EquipmentConfig);
public static void ApplyShieldHitWear(ShipData ship, EquipmentConfig);
public static void ApplyMovementWear(ShipData ship, float distanceUnits, EquipmentConfig);
public static void ApplyTurnEndWear(ShipData ship, EquipmentConfig);          // WearOnTurn

// Активация (форсаж, артефакты).
public static bool ActivateItem(ShipData ship, ItemInstance item);

// Скоростные расчёты.
public static float CalculateOwnEngineSpeed(ShipData ship);                   // unit/turn
public static float CalculateSpeed(ShipData ship);                            // с учётом буксира

// Корпус / план слотов.
public static Dictionary<string, int> GetHullSlotPlan(ItemInstance hull, EquipmentConfig);
public static void ApplySeriesToHull(ItemInstance hull, EquipmentConfig);

// Boost-эффекты от оборудования (Repair Droid, накопления).
public static void AccumulateDroidHeal(ItemInstance droid, float healAmount);
public static void AccumulateCargoGrab(ItemInstance grabber, float cargoWeight);
public static int GetEquippedWeight(ShipData ship);
```

### `InventoryService` (static — фасад инвентаря/слотов)

Создан на этапе C. UI и системы НЕ лазят в `_ship.Equipment.Set/Clear`, `_ship.AllItems[]`,
`_ship.Inventory.*` напрямую — только через эти методы.

```csharp
// Inventory
public static ItemInstance TakeFromInventory(ShipData ship, string uid);
public static bool AddToInventory(ShipData ship, ItemInstance item);
public static bool RemoveFromInventory(ShipData ship, string uid);
public static bool InventoryContains(ShipData ship, string uid);

// Slots
public static ItemInstance UnequipFromSlot(ShipData ship, string slotKey);
   // → clear slot + AllItems.Remove + RebuildShieldState + InvalidateWeaponSlotsCache
public static ItemInstance EquipToSlot(ShipData ship, string slotKey, ItemInstance item);
   // → set slot + AllItems[uid] = item + side-effects, возвращает displaced

// Полное удаление (для ContainerFactory.SpawnContainerWithItem)
public static void RemoveCompletely(ShipData ship, ItemInstance item);

// Кросс-корабль (абордаж, обмен)
public static bool TransferInventoryItem(ShipData from, ShipData to, string itemUid);
```

### `ShopService` (static — фасад магазина планеты)

Создан на этапе C. UI и `TraderAI` НЕ правят `_planet.Shop.Goods[id].Stock/BuyPrice/SellPrice` напрямую.

```csharp
public static ShopGoodEntry GetEntry(PlanetData planet, string goodId);

public static bool TryBuy(
    ShipData buyer, PlanetData planet, string goodId, int amount,
    GalaxyConfig galaxyCfg, out int actualAmount, out int totalCost);
   // → списать деньги, уменьшить Stock, пересчитать цены, AddStack в инвентарь покупателя

public static ItemStack TrySell(
    ShipData seller, PlanetData planet, string goodId, int amount,
    GalaxyConfig galaxyCfg, out int incomeMoney);
   // → изъять стек, начислить деньги, увеличить Stock, пересчитать цены

public static void RecalculatePrices(
    ShopGoodEntry entry, PlanetData planet, string goodId, GalaxyConfig galaxyCfg);
```

UI-сообщения остаются в UI (для разных контекстов разные тексты).

### `PickupSystem` (static)

```csharp
public static ItemInstance FindCargoGrabber(ShipData ship);   // → EquipmentSystem.FindEquipmentByCategory

// Очередь.
public static bool EnqueuePull(ShipData tug, string targetUid);
public static int EnqueueAllItemsInRadius(ShipData tug, IEnumerable<ShipData> candidates);
public static int EnqueueAllItemsInSystem(ShipData tug, IEnumerable<ShipData> candidates);
public static void CleanPullQueue(ShipData tug, Dictionary<string, ShipData> uidLookup);

// Тики.
public static void ProcessPullQueueSubturn(ShipData tug, EquipmentConfig, Dictionary<string, ShipData> uidLookup);
public static void UpdatePullStep(ShipData target, Dictionary<string, ShipData>, int subTurnsPerTurn);

// Завершение.
public static void EndPull(ShipData tug, ShipData target);
public static void ClearPullState(ShipData target);

// Распознавание разрыва связи (растущее расстояние).
public static bool TickPullDistanceCheck(ShipData target, Dictionary<string, ShipData>, EquipmentConfig);
```

### `TowSystem` (static)

```csharp
public static ItemInstance FindTowingRig(ShipData ship);

public static bool CanTow(ShipData tug, ShipData target, EquipmentConfig, out string reason);
public static bool TryTow(ShipData tug, ShipData target, EquipmentConfig, Dictionary<string, ShipData>);

// Тики.
public static void UpdateTowPullStep(ShipData target, EquipmentConfig, Dictionary<string, ShipData>, int subTurnsPerTurn);
public static void UpdateTowChain(ShipData tug, EquipmentConfig, Dictionary<string, ShipData>, int subTurnsPerTurn);

// Дерево буксира.
public static (ShipData parent, string anchorKey) FindFreeAnchor(
    ShipData node, EquipmentConfig, Dictionary<string, ShipData> uidLookup, bool forShip);
public static int CountTowedSubtree(ShipData root, Dictionary<string, ShipData>);
public static IEnumerable<ShipData> EnumerateTowedDescendants(ShipData tug, Dictionary<string, ShipData>);
public static void DetachSubtree(ShipData root, Dictionary<string, ShipData>);

// Отцепить.
public static void ReleaseTow(
    ShipData tug, ShipData towed,
    Dictionary<string, ShipData> uidLookup = null,
    EquipmentConfig equipConfig = null);

// Якоря / геометрия.
public static Vector2 ComputeChainAttachPos(ShipData prev, ShipData towed, Vector2 anchor);
public static Vector2 GetTowAnchorWorld(ShipData tug, int index, EquipmentConfig);

// Плотный рендер-путь после симуляции.
public static void RebuildTowedRenderPaths(
    ShipData tug, EquipmentConfig, Dictionary<string, ShipData>, TurnAnimationData);
```

### `BoardingSystem` (static)

```csharp
public static ItemInstance FindGrapplingHook(ShipData ship);

public static bool CanBoard(ShipData attacker, ShipData target, EquipmentConfig, out string reason);
public static bool TryBoard(ShipData attacker, ShipData target, EquipmentConfig);
public static void EndBoarding(ShipData attacker, ShipData target);

// Каждый сабтёрн пристёгивает цель к точке крепления абордажника.
public static void SnapBoardedTarget(ShipData attacker, ShipData target, EquipmentConfig);

// Обмен предмета между кораблями (для UI абордажа).
public static bool TransferItem(ShipData from, ShipData to, string itemUid);
```

### `HyperjumpController` (static)

```csharp
public static bool RequestJump(ShipData ship, StarData target, GalaxyData galaxy);
public static bool CancelJump(ShipData ship);
public static void CancelOnRouteChange(ShipData ship);
public static void FinalizeTurn(ShipData ship, StarData star, GalaxyData galaxy);
public static int CalcFuelCost(ShipData ship, StarData from, StarData to);
public static bool CanJump(ShipData ship, StarData from, StarData to, out string reason);
```

### `FuelService` (static)

```csharp
public static void RefillToFull(ShipData ship, PlanetData planet);
public static int CalculateRefillCost(ShipData ship, PlanetData planet);
```

---

## Зависимости

```
EquipmentSystem
   ├─► EquipmentConfig                   (GameSettingsConfig → GalaxyManager.Context)
   ├─► WeaponSystem.RebuildShieldState
   ├─► GalaxyManager.Instance.GeneratedGalaxy   (для FindShipByUid в CalcSpeed)
   ├─► OrbitMath.GetPlanetWorldPosition         (для LandingRange-проверок)
   └─► PlanetGeometry.GetLandingRadius          (то же)

InventoryService
   ├─► EquipmentSystem.GetEquipped/SlotCategory
   ├─► WeaponSystem.RebuildShieldState           (заглушка, но контракт сохранён)
   └─► ShipData.{Equipment, AllItems, Inventory, InvalidateWeaponSlotsCache}

ShopService
   ├─► TradeSystem.CalculateBuyPrice / CalculateSellPrice / GetDisplayName
   └─► ShipData.Money, ShipData.Inventory.AddStack/TakeStack

PickupSystem
   ├─► EquipmentSystem.FindEquipmentByCategory (CargoGrabber)
   ├─► ContainerFactory.UnloadContainerInto
   ├─► ShipData.FreezeRoute
   └─► GameConsoleController.AddEntry

TowSystem
   ├─► EquipmentSystem.FindEquipmentByCategory (TowingRig)
   ├─► EquipmentSystem.CalculateOwnEngineSpeed (для скорости буксируемого корабля)
   ├─► HullAnchorMath.LocalToWorld / LocalToWorldAt / GetHullTypeDef / ComputeHeadingAt
   ├─► PickupSystem.ClearPullState
   └─► ShipData.FreezeRoute

BoardingSystem
   ├─► EquipmentSystem.FindEquipmentByCategory (BoardingHook)
   ├─► HullAnchorMath.GetHullTypeDef
   └─► ShipData.FreezeRoute

HyperjumpController
   ├─► EquipmentSystem.GetEquipped (FuelTank, Engine)
   ├─► EquipmentSystem.ConsumeFuel
   ├─► GalaxyManager.CalcDistanceParsecs
   └─► ShipData.{HyperjumpPhase, HyperjumpEdge, HyperjumpArrivalEdge, …}
```

---

## Алгоритмы и формулы

### `EquipmentSystem.Install` — пайплайн установки

```
если !allowInSpace → return Fail("Смена в космосе запрещена")
item = ship.Inventory.TakeByUid(itemUid)
если item == null → Fail("не найден")

expectedCategory = SlotCategory(slotKey)
если !item.CanFitInSlotCategory(expectedCategory):
    ship.Inventory.Add(item)  # вернуть
    return Fail("неподходящая категория")

если !ship.Equipment.Slots.ContainsKey(slotKey):
    ship.Inventory.Add(item)
    return Fail("слот недоступен для текущего корпуса")

если item.Category == Hull → InstallHull(...)         # отдельный путь (rebuild slot plan, evict)

prevUid = ship.Equipment.Set(slotKey, item.Uid)
ship.AllItems[item.Uid] = item

если prevUid не null:
    ship.AllItems.Remove(prevUid)
    ship.Inventory.Add(prev)                          # вернуть старый в инвентарь

если item.Category == Shield → WeaponSystem.RebuildShieldState(ship)
ship.InvalidateWeaponSlotsCache()
return Ok
```

### `EquipmentSystem.InstallHull` (для слота Hull)

Особенность: корпус определяет план слотов. Меняешь корпус → пересоставляешь слоты,
лишние предметы выпадают в инвентарь (`evicted`).

```
slotPlan = GetHullSlotPlan(hull, equipConfig)
если slotPlan == null → return Fail
ship.Equipment.Clear(Hull) → oldHull в Inventory
ship.Equipment.Set(Hull, hull.Uid); ship.AllItems[hull.Uid] = hull
ApplySeriesToHull(hull, equipConfig)
evicted = ship.Equipment.RebuildForHullSlots(slotPlan, equipConfig)
foreach evictedUid:
    ship.AllItems.Remove(uid)
    ship.Inventory.Add(item)
ship.MaxHull = round(hull.GetParam("HP", maxDurability или 100))
ship.CurrentHull = min(CurrentHull, MaxHull)
```

### `EquipmentSystem.CalculateOwnEngineSpeed` (68 LOC, требует разбиения — TODO)

Один из самых сложных методов. Учитывает: двигатель, бак, форсаж, вес оборудования, вес груза,
буксируемые объекты, личностные модификаторы. См. исходник для деталей формулы.

Общая структура:
```
engine = GetEquipped(Engine)
если engine.IsBroken → speed = 100 (постоянная при сломанном двигателе)
если нет fuel или fuel == 0 → speed = 0
baseSpeed = engine.GetParam("Speed")
если ship.ForsageActive → baseSpeed *= forsage.SpeedMult (default 2)
weight = SumEquipmentWeight + Inventory.TotalWeight
если ship — корень буксирного дерева → weight += SumTowedSubtreeWeight
loadFactor = ... # формула с порогами
тяга-модификатор = ... # коэффициент 0.00045 в зависимости от capacity
return baseSpeed * loadFactor * thrustMod
```

### `PickupSystem.UpdatePullStep` — притяжение груза

```
если target.PullMode != Pickup или нет PulledByUid → return
если tug.IsBroken или вне радиуса → ClearPullState

dist = (tug.Position - target.Position).magnitude
если dist <= PickupArrivalThreshold=0.20:
    moved = ContainerFactory.UnloadContainerInto(target, tug)
    ClearPullState
    лог
    return

# Hard-cap по массе:
weight = max(1, target.Inventory.TotalWeight())
если grabber.Power > 0 и weight > grabber.Power:
    ClearPullState; лог; return

# Скорость линейная по тиру:
tier = clamp(grabber.TechLevel, 1, 10)
subturnsFor500AtMaxRange = lerp(10, 2, (tier-1)/9)     # T1 → 10 сабтёрнов на 500 веса с MaxRange
perSubturnWorld = (pullR / subturnsFor500AtMaxRange) * (500 / weight)

# «Притягивание на лету»: к собственной скорости добавляем проекцию шага tug на направление к нему,
# иначе при быстром tug груз отстаёт и связь рвётся:
tugChaseAdd = max(0, Dot(tug.Position - tug.PreviousPosition, dir))
step = min(perSubturnWorld + tugChaseAdd, dist)
target.Position += dir * step
target.CurrentHeading = atan2(delta)
```

### `TowSystem.UpdateTowPullStep` — притяжение к якорю

Аналогично Pickup, но для буксира (корабль/тяжёлый предмет):
- Для **предметов** — та же формула (line по тиру TowingRig + ReferenceWeight=500).
- Для **кораблей** — скорость = собственный engine / 2:
```
perTurnWorld = (EquipmentSystem.CalculateOwnEngineSpeed(target) * 0.5) / 100
perSubturnWorld = perTurnWorld / SubTurnsPerTurn
```

При достижении `TowSettleThreshold=0.08` → `AttachToAnchor` (присоединяет к якорю и переводит в IsTowSettled).

### `TowSystem.UpdateTowNode` — PD-цепочка

После присоединения цель «висит» на пружинке (PD-controller, критическое демпфирование):
```
dt = 1 / SubTurnsPerTurn
delta = desired - towed.Position           # desired = ComputeChainAttachPos(prev, towed, anchorWorld)
v = towed.TowVelocity
a = TowSpringOmega² * delta - 2*TowSpringOmega * v       # TowSpringOmega = 5.0
v += a * dt
towed.Position += v * dt
towed.TowVelocity = v
towed.IsTowSettled = true
towed.CurrentHeading = prev.CurrentHeading
```

Для корабля `desired = anchor - normalize(prev.Position - anchor) * TowedShipFollowDistance` (точка чуть позади якоря по линии «prev → anchor»), для предмета — `desired = anchor`.

### `TowSystem.FindFreeAnchor` — поиск свободного якоря в дереве

Рекурсивно по дереву (parent → дети по TowOrder):
- **Корабль (forShip=true)**: только Back-якорь на каждом узле. Если Back занят — спуск в Back-ребёнка.
- **Предмет (forShip=false)**: любой свободный по TowOrder; при полной занятости — спуск в Back.

Возвращает `(parent, anchorKey)` либо `(null, null)`.

### `BoardingSystem.SnapBoardedTarget`

Цель не двигается самостоятельно (`FreezeRoute()` при `TryBoard`), Position каждый сабтёрн ставится
рядом с атакующим:
```
def = HullAnchorMath.GetHullTypeDef(attacker, equipConfig)
если def.BoardAnchor:
    localDir = (BoardAnchor[0], BoardAnchor[1]).normalized
    ang = attacker.CurrentHeading - π/2
    worldDir = rotate(localDir, ang)
    gap = 0.5 * attacker.SpriteWorldSize + 0.5 * target.SpriteWorldSize
    target.Position = attacker.Position + worldDir * gap
иначе target.Position = attacker.Position
target.CurrentHeading = attacker.CurrentHeading
```

### `PickupSystem.TickPullDistanceCheck` — разрыв связи

Применяется и к Pickup, и к Tow:
- Если расстояние от target до референсной точки (tug или ближайшего родителя в цепи) растёт **2 хода подряд** → связь рвётся, лог. Эпсилон `Eps = 0.001`.

### `HyperjumpController.RequestJump`

Двух-ходовая модель (см. memory `project_hyperjump_2turn`):
```
если ship.HyperjumpPhase != None → return false        # уже прыгает
если !CanJump(ship, from, target, out reason) → return false  # нет JumpRange/fuel/engine/distance
ship.HyperjumpTargetStarUid = target.Uid
ship.HyperjumpEdge = точка на краю исходной системы в направлении target
ship.HyperjumpHeading = atan2(edge - center)
ship.HyperjumpPhase = Travel
# фактический ConsumeFuel — при входе в портал (StarNextDay)
return true
```

`StarNextDay` каждый ход двигает корабль к `HyperjumpEdge`, при достижении меняет фазу на `HyperEnter`,
финализация фазы → `HyperExit` (CurrentStarUid меняется, корабль на ArrivalEdge) — делает
`HyperjumpController.FinalizeTurn`, вызываемый из `GalaxyManager.FinalizeHyperjumpPhases`.

---

## Константы и настройки

### `EquipmentSystem`

| Имя | Значение | Назначение |
|---|---|---|
| `0.6f, 2.0f` | в `CalculateWeightRandom` | Диапазон веса (BaseWeight × [0.6..2.0]) |
| `0.4f` | в `CalculateOwnEngineSpeed` | Forsage durability threshold (отказ при < 40% хитов) |
| `2f` | default `SpeedMult` Forsage | Множитель скорости при активации |
| `100` | константа сломанного двигателя | Минимальная скорость при `IsBroken` |
| `0.00045f` | в формуле тяги | Коэффициент тяги-без-capacity |
| `Hull HP default` | 100 (если нет MaxDurability) | Дефолт HP корпуса |

### `PickupSystem`

| Имя | Значение | Назначение |
|---|---|---|
| `PickupArrivalThreshold` | 0.20 | Дистанция «корабль успевает зацепить» |
| `ReferenceWeight` | 500 | Эталон в формуле скорости (perSubturn = pullR / subturnsFor500 * (500/weight)) |
| `subturnsFor500AtMaxRange` | lerp(10, 2, (tier-1)/9) | T1 → 10 сабтёрнов, T10 → 2 |
| `Eps` (в TickPullDistanceCheck) | 0.001 | Эпсилон роста расстояния |
| 2 хода роста | в TickPullDistanceCheck | Связь рвётся после 2 ходов подряд |

### `TowSystem`

| Имя | Значение | Назначение |
|---|---|---|
| `TowedShipFollowDistance` | 0.6 | Отступ корабля от якоря (по линии prev→anchor) |
| `TowAttachRadius` | 1.5 | Дистанция «крепится сразу» в TryTow |
| `TowSettleThreshold` | 0.08 | Дистанция «сел на якорь» в UpdateTowPullStep |
| `TowSpringOmega` | 5.0 | Частота PD-демпфера (ω·dt = 0.5 для устойчивости) |
| `0.5f` | в UpdateTowPullStep | Собственная скорость буксируемого корабля = engine/2 |

### `BoardingSystem`

| Имя | Значение | Назначение |
|---|---|---|
| `1e-6f` | в SnapBoardedTarget | Эпсилон «BoardAnchor нулевой» |
| `Vector2.right` | fallback | Если BoardAnchor нулевой |

### `HyperjumpController`

Все настройки внутри `EquipmentConfig.HyperJump` (для типов двигателей) и `Engine.GetParam("JumpRange")`.

---

## Внутренняя структура данных

### `ItemInstance` (в `ItemFactory` / `GalaxyDataModels`)

```
Uid : string
ItemId : string                  # ключ из конфига
Category : string                # Weapons, Engine, Hull, Shield, FuelTank, CargoGrabber, …
Name : string
Description : string
Weight : int
ManufacturerRace : string
TechLevel : int                  # 1..10 для тира
MaxDurability, Durability : int
Params : Dictionary<string, float>      # уровневые параметры (BlockPercent, Speed, Power, …)
ParamsString : Dictionary<string, string> # строковые параметры (DamageType, ShotPattern, …)
IsBroken : bool                  # Durability <= 0
IsWorking : bool                 # !IsBroken && не отключён эффектом
Activatable : bool               # есть слот «активация» (Forsage, Artefact)
```

### `ShipEquipment` (в `ShipData`)

```
Slots : Dictionary<string, string>      # slotKey → itemUid (или null)
string Set(slotKey, itemUid)            # возвращает предыдущий uid
string Clear(slotKey)
string GetItemUid(slotKey)
List<string> RebuildForHullSlots(plan, equipConfig)   # возвращает выпавшие uid'ы
IEnumerable<string> GetOccupiedSlotsOfCategory(category)
```

### `ShipInventory` (в `ShipData`)

```
Items : List<ItemInstance>            # отдельные предметы по UID
Stacks : Dictionary<string, StackEntry>  # ItemId → накопительные стеки (Goods/минералы)
int Count, int TotalWeight
ItemStack TakeStack(itemId, amount)
void AddStack(stack)
ItemInstance TakeByUid(uid)
bool Remove(uid)
void Add(item)
bool Contains(uid)
```

### `PullKind` (enum)

`None / Pickup / Tow`.

### `PlanetShop` (`PlanetData.Shop`)

```
Goods : Dictionary<string, ShopGoodEntry>
```

`ShopGoodEntry`:
```
Stock, MaxStock : int
BuyPrice, SellPrice : int
BasePrice : int
Legal : bool                # для контрабандных товаров
```

### `HullTypeDef` (в `EquipmentConfig`)

```
TowAnchors : Dictionary<string, Vector2>   # key → локальный offset якоря
TowOrder : List<string>                    # порядок занятия якорей (первый = Back)
BoardAnchor : float[]                      # [x, y] локальный вектор для абордажа
```

---

## Известные ограничения / TODO

1. **`EquipmentSystem.CalculateOwnEngineSpeed`** (68 строк) совмещает 4 разных расчёта — кандидат
   на разбиение на `CalculateEngineBase / CalculateLoadFactor / CalculateThrustMod`. Не сделано:
   риск изменить точку расчёта, поведение чувствительно к балансу.
2. **`InstallHull` дублирует логику** между `EquipmentSystem.InstallHull` (внутренний путь из Install)
   и `ShipFormView.PlaceHull` (UI-путь). Оба корректны, но evict-логика дублируется. Стоит вынести
   в `EquipmentSystem.ReplaceHull(ship, hull, equipCfg, Func<ItemInstance, void> onEvicted)`.
3. **`InventoryService.UnequipFromSlot` всегда вызывает `RebuildShieldState`**, даже если категория
   не Shield — лишний вызов (хоть и заглушка после рефактора щитов). Можно проверять категорию
   ДО clear-а, но это микро-оптимизация.
4. **`PickupSystem` и `TowSystem` имеют параллельные функции `UpdatePullStep`/`UpdateTowPullStep`**
   с похожей структурой (поиск ссылки → проверка валидности → расчёт скорости → шаг). Объединить
   через `PullKind`-switch может усложнить, лучше держать раздельно. Дублирование в проверке tug
   потенциально можно вынести в `ValidatePullSource(target, uidLookup, out tug)`.
5. **`HullAnchorMath` и `BoardAnchor`** используют разные конвенции (один — словарь по ключу, другой —
   float[2]). Унификация (HullTypeDef.BoardAnchorKey?) — задача отдельного дизайн-этапа.
6. **`ShopService.TryBuy` не проверяет контрабанду** — это в `PlanetUIController.BuyGood` перед вызовом.
   Если кто-то ещё (NPC?) начнёт вызывать TryBuy — контрабандный штраф не применится. Стоит либо
   перенести проверку внутрь сервиса, либо явно задокументировать ответственность caller'а.
7. **`PlaceHull` в `ShipFormView`** напрямую вызывает `ShipFactory.RecalculateSpriteWorldSize`.
   Если `EquipmentSystem.InstallHull` тоже потребует пересчёт спрайта — это side-effect, который
   надо синхронизировать.
