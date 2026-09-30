# Spawn System

Модули: `Systems/Spawning/*.cs` — `SpawnSystem`, `ISpawnPolicy`, `SpawnConfig`, `SpawnFormulas`,
`SpawnTickContext`, `SubtypePicker`, `DominationCalculator`, `GalaxyShipCounters`,
плюс политики `Policies/*SpawnPolicy.cs` (Civilian, Warrior, Ranger, Pirate, Linkor, Dominator).
Также: `NpcAI/NpcSpawner.cs` (механика собственно создания NPC при генерации галактики).



---

## Назначение

«Top-up» система, поддерживающая населённость галактики кораблями: следит за глобальными
квотами по типам/расам/владельцам, периодически досоздаёт корабли в подходящих звёздах,
учитывает кулдауны спавна на звезду и эффект «доминирования» (если фракция захватила
большинство — снижает спавн её противников). Политики (`ISpawnPolicy`) реализуют конкретное
поведение для каждого типа: трейдеры спавнятся у торговых планет, пираты у нейтральных,
синтеты — по специальным правилам и т.д.

Начальный спавн при генерации галактики — отдельная вещь в `NpcSpawner` (вызывается из
`GalaxyGenerator`); `SpawnSystem` отвечает только за поддержание популяции после старта.

---

## Публичный интерфейс

### `SpawnSystem` (static)

```csharp
public static GalaxyShipCounters Counters { get; }
public static DominationFlags Domination { get; }

public static void Register(ISpawnPolicy policy);
public static void ClearPolicies();
public static void RegisterDefaultPolicies();        // вызывается из GalaxyManager.LoadGame + Init
public static void RecountFromGalaxy(GalaxyData, GalaxyConfig);   // полный пересчёт после загрузки

public static void DailyTick(GalaxyData galaxy, GalaxyGenerationContext ctx);
   // 1) star.SpawnsToday = 0 для каждой звезды (per-day квота, лимит в GameSettingsConfig.MaxShipsSpawnedPerStarPerDay)
   // 2) Counters.RecalculateFromScratch + Domination.Recalculate
   // 3) Прогон всех политик через ISpawnPolicy.DailyTick(SpawnTickContext)

// Хуки жизни корабля — для инкрементального счёта (вызываются из ShipFactory / DeathRegistration).
public static void RegisterSpawn(ShipData ship, StarData star);
public static void RegisterDeath(ShipData ship);

// Общая точка создания корабля — используется политиками.
public static ShipData SpawnShipAtPlanet(
    string shipTypeId, string ownerId, string raceId,
    PlanetData planet, StarData star, GalaxyGenerationContext ctx,
    string reason = null);

public static void ApplyStartingMoney(ShipData ship, string shipTypeId, GalaxyGenerationContext);
   // Базовая сумма из SpawnConfig.Policies[shipTypeId].StartingMoney, умноженная на InflationFactor
```

### `ISpawnPolicy`

```csharp
public interface ISpawnPolicy
{
    string ShipTypeId { get; }           // имя для логов и SpawnConfig.GetPolicy
    void DailyTick(SpawnTickContext ctx);
}
```

### `SpawnTickContext`

```csharp
public GalaxyData Galaxy;
public GalaxyGenerationContext Gen;
public GalaxyConfig Config;
public GalaxyShipCounters Counters;
public DominationFlags Domination;
public int CurrentTurn;
```

### `GalaxyShipCounters`

```csharp
public Dictionary<string, int> ByShipType;              // тип → count
public Dictionary<string, int> ByOwner;
public Dictionary<string, int> ByRace;
public Dictionary<(string owner, string shipType), int> ByOwnerAndType;

public void RecalculateFromScratch(GalaxyData galaxy);
public void OnShipSpawned(ShipData ship);
public void OnShipDied(ShipData ship);
```

### `DominationCalculator` (static)

```csharp
public static DominationFlags Recalculate(GalaxyShipCounters counters, GalaxyConfig cfg);
   // Считает, какие фракции «доминируют» (по StarCount или ShipCount), и заполняет
   // DominationFlags для текущего тика.
```

### `DominationFlags`

```csharp
public HashSet<string> DominatingOwners;       // фракции, превысившие порог доминации
public bool IsDominator1Active;                // активирован первый эшелон синтетов
public bool IsDominator2Active;
public bool IsDominator3Active;
public Dictionary<string, int> Pressure;       // давление каждой фракции (для UI/AI)
```

### Политики (`Systems/Spawning/Policies/*.cs`)

- `CivilianSpawnPolicy(shipTypeId)` — Transport/Liner/Diplomat. Спавнятся у планет с подходящим
  EconomyType (например, Transport у торговых, Diplomat у больших). Квота на фракцию.
- `WarriorSpawnPolicy` — военные. Спавнятся у военных планет своей фракции, квота от Domination.
- `RangerSpawnPolicy` — вольные пилоты, специальные «свободные охотники» с квотой по галактике.
- `PirateSpawnPolicy` — пираты, спавнятся у нейтральных/слабых планет.
- `LinkorSpawnPolicy` — линкоры, только в системах с высокой враждебной активностью.
- `DominatorSpawnPolicy` — синтеты (Blazer/Keller/Terron → Race Dominators 1/2/3, Dom1..7);
  спавнятся при `DominationFlags.IsDominatorNActive == true`. См. memory
  `project_dominators_rename_jun2026`.

### `SubtypePicker` (static)

```csharp
public static string PickSubtype(
    string baseShipType, string race, string owner,
    SpawnConfig cfg, System.Random rng);
   // Выбирает конкретный шаблон из категории по весам (race-specific overrides → fallback)
```

### `SpawnFormulas` (static)

Вспомогательные формулы для лимитов:
```csharp
public static int ComputeQuota(int baseQuota, int starsOwned, float perStarMul);
public static int ApplyDominationScaling(int quota, DominationFlags dom, string owner);
```

### `NpcSpawner` (static) + `NpcSystemSpawner` (static)

```csharp
public static void SpawnInitialNpcsForGalaxy(GalaxyData galaxy, GalaxyGenerationContext ctx);
   // Полный спавн при генерации галактики. Раскладывает корабли по планетам исходя из квот,
   // характеристик звезды и владельцев. Вызывается из GalaxyGenerator после расстановки планет.
```

---

## Зависимости

```
SpawnSystem.DailyTick
   ├─► GalaxyShipCounters.RecalculateFromScratch
   ├─► DominationCalculator.Recalculate
   └─► foreach policy in _policies: ISpawnPolicy.DailyTick(ctx)
         ├─► SubtypePicker (для выбора конкретного шаблона)
         ├─► SpawnFormulas (для квот)
         └─► SpawnSystem.SpawnShipAtPlanet
               ├─► ShipFactory.BuildShipData
               ├─► ItemFactory.EquipStarterKit
               ├─► InflationSystem.GetFactor (для денег)
               └─► EconomicLog.Spawn

NpcSpawner.SpawnInitialNpcsForGalaxy
   ├─► SpawnConfig (квоты, шаблоны)
   ├─► ShipFactory.BuildShipData
   └─► ItemFactory.EquipStarterKit
```

Хуки вызываются:
- `RegisterSpawn` — из `SpawnSystem.SpawnShipAtPlanet` и `NpcSpawner` при создании.
- `RegisterDeath` — должен вызываться при `RegisterTargetDeath` (TODO: проверить, что это
  действительно так — death-репорт обрабатывается в `WeaponSystem.RegisterTargetDeath` через
  `anim.DeathUids.Add`, а инкрементальный счётчик `SpawnSystem.RegisterDeath` вызывается где-то
  в финализации хода, см. `StarData.StarNextDay` / `GalaxyData.GalaxyNextDay`).

---

## Алгоритмы и формулы

### `SpawnSystem.DailyTick`

```
foreach star in galaxy.StarsMap.Values:
    star.SpawnsToday = 0                  # per-day квота (лимит в GameSettingsConfig)

Counters.RecalculateFromScratch(galaxy)   # O(N): пересчёт всех счётчиков
Domination = DominationCalculator.Recalculate(Counters, cfg)

foreach policy in _policies:
    try:
        policy.DailyTick(ctx)
    catch:
        Debug.LogError("Policy X threw: ...")
```

### `DominationCalculator.Recalculate` (примерная логика)

```
totalStars = galaxy.StarsMap.Count
flags = new DominationFlags()
foreach owner, ownerCfg in cfg.Owners:
    starsOwned = подсчёт звёзд с эффективным owner == owner
    fraction = starsOwned / totalStars
    если fraction >= cfg.DominationThreshold (например 0.5):
        flags.DominatingOwners.Add(owner)

# Синтеты (Dominator1/2/3) активируются эскалационно: 1 при появлении любой
# домин-фракции > X, 2 при превышении более жёсткого порога, 3 — критический.
flags.IsDominator1Active = (max_fraction >= cfg.Spawn.Dominator1Threshold)
flags.IsDominator2Active = ...
flags.IsDominator3Active = ...

return flags
```

### Типовой `ISpawnPolicy.DailyTick`

```
quota = SpawnFormulas.ComputeQuota(basePolicy.Quota, starsByOwner, perStarMul)
quota = SpawnFormulas.ApplyDominationScaling(quota, ctx.Domination, owner)

current = ctx.Counters.ByOwnerAndType[(owner, ShipTypeId)]
если current >= quota → return     # квота заполнена

# Найти подходящую планету
foreach star in ctx.Galaxy.StarsMap.Values:
    если !SpawnFormulas.StarCanSpawnMore(star, ctx.Settings) → continue   # per-day квота
    foreach planet in star.Planets:
        если !SpawnFormulas.StarCanSpawnMore(star, ctx.Settings) → break  # квота исчерпана in-loop
        если PlanetIsSuitable(planet) и quota не заполнена:
            subtype = SubtypePicker.PickSubtype(ShipTypeId, planet.Race, planet.Owner, cfg, rng)
            ship = SpawnSystem.SpawnShipAtPlanet(subtype, planet.Owner, planet.Race, planet, star, ctx)
            current++
            если current >= quota → return
```

### `SpawnSystem.SpawnShipAtPlanet`

```
ship = ShipFactory.BuildShipData(shipTypeId, ownerId, raceId, ctx.AvailableShipTypes, ctx)
если ship == null → return null

# Расположение: позиция планеты + лёгкий разброс
pos = OrbitMath.GetPlanetWorldPosition(planet) + Random.insideUnitCircle * (star.SystemSize / 100 * 0.05)

ship.SpawnedFromUid = ship.HomePlanetUid = planet.Uid
ship.HomeStarUid = ship.CurrentStarUid = ship.PreviousStarUid = star.Uid
ship.CurrentStar = star
ship.Position = ship.PreviousPosition = ship.TargetPosition = pos

EquipStarterKitIfAvailable(ship, shipTypeId, ctx)   # вызов ItemFactory или fallback HP=100
ApplyStartingMoney(ship, shipTypeId, ctx)           # money * InflationFactor

star.Ships.Add(ship)
RegisterSpawn(ship, star)                            # ++star.SpawnsToday
EconomicLog.Spawn(0, ship.Name, "TOP_UP_SPAWN", "type=... owner=... planet=... reason=...")
return ship
```

### `ApplyStartingMoney`

```
если ship.Money > 0 → return     # не перезаписываем
policy = cfg.Spawn.GetPolicy(shipTypeId)
baseMoney = policy?.StartingMoney ?? 0
если baseMoney <= 0 → return
inflation = galaxy.InflationFactor ?? 1
ship.Money = round(baseMoney * inflation)
```

---

## Константы и настройки

### В `SpawnConfig` (`cfg.Spawn`)

```
Policies : Dictionary<string, ShipTypePolicy>
   StartingMoney : int
   Quota : int                  # базовая квота на фракцию
   PerStarMultiplier : float    # +n кораблей за каждую владеемую звезду
   PlanetFilter : { allowedEconomyTypes, allowedGovernments, ... }
   SpawnCooldownDays : int
   StarterKitId : string
   ...

DominationThresholds : { Dominator1, Dominator2, Dominator3 : float (доля звёзд) }
```

### В коде

| Имя | Значение | Назначение |
|---|---|---|
| `0.05f` (в SpawnShipAtPlanet) | scatter factor | Разброс позиции спавна (% от SystemSize) |
| `100` fallback HP | в EquipStarterKitIfAvailable | Если нет StarterKit |

---

## Внутренняя структура данных

### `ShipTypePolicy` (в SpawnConfig)

```
ShipTypeId : string
Quota : int
PerStarMultiplier : float
StartingMoney : int
StarterKitId : string
PlanetFilter : PlanetFilter
SpawnCooldownDays : int
```

### `GalaxyShipCounters`

```
ByShipType : Dictionary<string, int>           // "Transport" → 47
ByOwner : Dictionary<string, int>              // "Race3_Main" → 234
ByRace : Dictionary<string, int>               // "Race3" → 312
ByOwnerAndType : Dictionary<(string, string), int>
```

Поддерживается инкрементально через `OnShipSpawned`/`OnShipDied`, плюс полный пересчёт раз в день
(дёшево для текущего масштаба, страховка от рассинхронизации).

### `DominationFlags`

```
DominatingOwners : HashSet<string>
IsDominator1Active, IsDominator2Active, IsDominator3Active : bool
Pressure : Dictionary<string, int>          // нагрузка каждой фракции (0..N)
```

### `StarData.SpawnsToday`

`int` (транзитный, `[JsonIgnore]`). Сбрасывается в `0` в начале каждого хода
(`SpawnSystem.DailyTick`), инкрементится в `RegisterSpawn`. Гейт `StarCanSpawnMore(star, settings)`
разрешает спавн, пока `SpawnsToday < GameSettingsConfig.MaxShipsSpawnedPerStarPerDay`. Учитывается
всеми политиками, применяется и внутри одного тика (пере-проверка перед каждой планетой в
`WarriorSpawnPolicy`/`CivilianSpawnPolicy`, чтобы одна политика не выпустила N кораблей в
системе с N планетами).

---

## Известные ограничения / TODO

1. **`Counters.RecalculateFromScratch` каждый день** — O(N) по всем кораблям. На текущем
   масштабе ~5к кораблей пренебрежимо. При росте до десятков тысяч стоит положиться только
   на инкрементальные `OnShipSpawned/OnShipDied` (и пересчёт раз в N дней как страховка).
2. **`RegisterDeath` должен вызываться где-то в death-pipeline**. После `WeaponSystem.RegisterTargetDeath`
   корабль попадает в `anim.DeathUids`, но фактическое удаление из `star.Ships` и вызов
   `SpawnSystem.RegisterDeath(ship)` — отдельный шаг. Проверить, что нет рассинхронизации
   (если корабль удалён из `star.Ships` без `RegisterDeath` — счётчик «зависнет»).
3. **Политики спавна не учитывают** «куда выгодно спавнить» — выбирают первую подходящую планету.
   Это даёт неравномерное распределение (часть планет «перенаселены», часть пусты). Эвристика
   «спавн где меньше всего своих» — на потом.
4. **`Dominator` политика жёстко привязана к 3 «эшелонам»** (memory `project_dominators_rename_jun2026`).
   Если потребуется 4-й — менять `DominationFlags` + `DominationCalculator` + `DominatorSpawnPolicy`.
5. **`SubtypePicker.PickSubtype` использует System.Random** — а не Unity.Random. Это специально
   (deterministic-spawn для тестов), но если кто-то ещё захочет deterministic от seed-а — нужно
   передавать seed-ed RNG через `SpawnTickContext`.
