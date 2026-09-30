# Galaxy Generation

Модули: `Generation/*.cs` (9 файлов) + `Config/Models/*.cs` +
`Config/GalaxyConfigLoader.cs` + `Config/GalaxyConstants.cs` +
`Galaxy/Models/*.cs` (data) + `Utils/VoronoiHelper.cs` + `Utils/OrbitMath.cs`.

Файлы `GalaxyGenerator` — partial class, разбит на:
`GalaxyGenerator.cs` (orchestration), `.SectorStars.cs`, `.Placement.cs`, `.Owners.cs`,
`.Expansion.cs`, `.Planets.cs`, `.Satellites.cs`, `.StarPhysics.cs`. Утилитные классы в
`GalaxyUtils.cs` (5 классов): `OwnerResolver`, `CustomPropertyResolver`, `GalaxyLogger`,
`GenerationHelpers`, `OwnershipDisplayResolver`.

Связанные документы: [`world_generation.md`](../design/world_generation.md), [`planet_formulas.md`](../design/planet_formulas.md).

---

## Назначение

Процедурная генерация всей галактики из seed: секторы → звёзды → планеты → спутники.
Расставляет владельцев фракций (через расширение метрополий по соседним системам),
вычисляет физические параметры звёзд (масса/спектр), визуальные параметры планет
(размер/орбита/атмосфера/спутники/орбитальные объекты), формирует базу для экономики
(размер/тип/правительство планеты).

Один deterministic запуск: одинаковый seed → одинаковая галактика (модулу-внешние
эффекты вроде времени системы исключены).

---

## Публичный интерфейс

### `GalaxyGenerator` (partial class)

```csharp
public GalaxyGenerator(GalaxyGenerationContext context);
public GalaxyData Generate(int seed, string galaxyKey = "MilkyWay");

// Внутренние helpers, доступные другим модулям (через internal):
internal void FinalizeStar(StarData star);
internal void FinalizePlanet(PlanetData planet, string size);
internal void GeneratePlanetSatellites(PlanetData planet);
internal void AssignOrbitRadiiToList(List<PlanetData> list);
```

### `GalaxyGenerationContext`

Контекст генерации со всеми загруженными конфигами и накопленным state:
```csharp
public GalaxyConfig Config;
public TextConfig TextConfig;
public PremadeConfig PremadeConfig;
public EquipmentConfig Equipment;
public ItemsConfig ItemsConfig;
public GalaxyConfigEntry ActiveGalaxyConfig;     // активная галактика (MilkyWay и т.п.)
public List<string> AvailableRaces;
public Dictionary<string, SystemSizeRule> SystemSizeRules;
public HashSet<string> ReservedNames;             // имена premade-звёзд

public void Reset();      // очистка между попытками генерации
```

### `GalaxyConfigLoader` (static)

```csharp
public static bool TryLoadConfigs(
    TextAsset galaxyJson, TextAsset textJson, TextAsset premadeJson, TextAsset equipmentJson,
    out GalaxyConfig galaxyCfg, out TextConfig textCfg, out PremadeConfig premadeCfg,
    out EquipmentConfig equipCfg);
```

Десериализует JSON-конфиги через Newtonsoft.Json. Возвращает `false` при любой ошибке парсинга
(логирует через `Debug.LogError`).

### `GalaxyData`

```csharp
public Dictionary<string, StarData> StarsMap;
public Dictionary<string, PlanetData> PlanetsMap;       // быстрый lookup планет по UID
public List<SectorData> Sectors;
public Dictionary<string, RaceInfo> Races;
public int Width, Height;
public string StartDateString;
public int CurrentTurn;
public float InflationFactor = 1.0f;
public HashSet<string> VisitedStarUids;                 // для галакарты
public Dictionary<string, List<string>> PendingShipMigrations;  // ship.Uid → target star uid

public void InitSimulation();          // подготовить runtime-структуры после Generate или Load
public void GalaxyNextDay(GalaxyGenerationContext ctx);    // главный тик симуляции
public void MigrateShipsBetweenStars(); // обработать ship.CurrentStarUid != currentStar.Uid

public DateTime GetStartDate();
public DateTime GetCurrentDate();

public const int SubTurnsPerTurn = 10;
```

### `StarData`

```csharp
public string Uid, Name;
public Vector2 Position;             // позиция в галактической сетке
public string SectorUid;
public StarPhysics Physics;          // спектр, масса, температура
public string ColorKey;              // для отрисовки
public float SystemSize;             // радиус всей звёздной системы в пикселях
public List<PlanetData> Planets;
public List<AsteroidData> Asteroids;
public List<ShipData> Ships;
public List<ActiveMissile> ActiveMissiles;
public string Owner;                 // эффективный владелец (most_common из планет)
public int MaxAsteroids;             // лимит астероидов
public List<Vector2> CircleObstacles, PolygonObstacles;  // для ship trajectory planning

public void StarNextDay(GalaxyData galaxy, GalaxyGenerationContext ctx, TurnAnimationData anim);
```

### `PlanetData`

```csharp
public string Uid, Name;
public string StarUid;
public int OrbitIndex;
public float OrbitRadius;            // в пикселях
public float OrbitSpeed, DaySpeed, CloudsSpeed, AxialTilt;
public float OrbitEccentricity, OrbitTiltDeg;
public float CurrentAngle, PreviousAngle;     // текущая позиция на орбите

public string Race, Owner;
public string Size;                  // Small/Mid/Large
public string EconomyType;           // Agricultural/Industrial/Mining/Trade/Research
public string Government;
public long Population;
public int TechLevel;

public string Graphic, Atmosphere, MaskGraphic, OrbitalObjects;
public List<SatelliteData> Satellites;

public PlanetShop Shop;
public PlanetEquipmentShop EquipmentShop;
public Dictionary<string, int> BaseStockCache;       // ленивый кэш TradeSystem.GetBaseStock
```

### `OwnerResolver` (static, в GalaxyUtils.cs)

```csharp
public static string Resolve(StarData star, GalaxyGenerationContext ctx);
// Возвращает эффективного владельца звезды по правилу «модальный owner среди планет»
// (None/Mixed если не доминирует никто).

public static string Resolve(PlanetData planet, GalaxyGenerationContext ctx);
```

### `VoronoiHelper` (static, в Utils/)

```csharp
public static List<VoronoiCell> Compute(List<Vector2> sites, Rect bounds);
// Простая реализация Вороной — для отрисовки границ владений на галакарте.
```

### `OrbitMath` (static, в Utils/)

```csharp
public static Vector2 GetEllipticPosition(float radius, float angle, float ecc, float tiltDeg);
public static Vector2 GetEllipticPositionLerp(float radius, float fromAngle, float toAngle,
                                              float progress, float ecc, float tiltDeg);
public static Vector2 GetPlanetWorldPosition(PlanetData planet);
public static Vector2 GetPlanetPositionAtSubTurn(PlanetData planet, int subTurn);
public static Vector2 GetPlanetPositionSimple(PlanetData planet);  // упрощённая (для AI)
```

### `GalaxyLogger` (static, в GalaxyUtils.cs)

```csharp
public static void LogGalaxy(GalaxyData galaxy, int seed,
                             GalaxyConfig cfg, List<string> availableRaces);
```

Печатает в `Debug.Log` сводку: число секторов / звёзд / планет / распределение владельцев / рас.

---

## Зависимости

```
GalaxyGenerator.Generate
   ├─► GalaxyConfig (Galaxies, Sectors, Stars, Planets, Owners, Races)
   ├─► PremadeConfig (фиксированные имена звёзд)
   ├─► GalaxyGenerationContext (state, SystemSizeRules, AvailableRaces, …)
   ├─► .SectorStars partial   (GenerateSectors, GenerateStars)
   ├─► .Placement partial     (TryPlaceSectorPositions, проверка коллизий)
   ├─► .Owners partial        (ResolveAllOwners — каждой планете и звезде назначить owner)
   ├─► .Expansion partial     (расширение фракций по соседним системам)
   ├─► .Planets partial       (GeneratePlanetsForStar, AssignOrbitRadii)
   ├─► .Satellites partial    (GenerateSatellites)
   ├─► .StarPhysics partial   (FinalizeStarProps, расчёт массы/спектра)
   └─► GalaxyUtils helpers

GalaxyConfigLoader
   └─► Newtonsoft.Json (parse)

OrbitMath
   └─► чистая математика (без зависимостей)

VoronoiHelper
   └─► чистая математика
```

---

## Алгоритмы и формулы

### Главный пайплайн `Generate(seed)`

```
foreach attempt in [0..MaxPlacementAttempts):
    actualSeed = attempt == 0 ? seed : seed XOR (attempt * 0x9E3779B9)
    Random.InitState(actualSeed)
    _ctx.Reset()
    
    PreReserveFixedNames(galaxyKey)          # резерв имён из PremadeConfig
    galaxy = new GalaxyData { Width, Height, StartDateString }
    ComputeSpacingParams(galaxyCfg, galaxy)  # _minStarDist, _sectorRadius
    
    GenerateSectors(galaxy, galaxyKey, galaxyCfg)
    GenerateStars(galaxy, galaxyCfg)
    ResolveAllOwners(galaxy)                 # первый проход
    
    если !TryPlaceSectorPositions(galaxy):   # коллизии — рестарт с новым seed
        continue
    
    ApplyExpansion(galaxy, galaxyCfg)        # фракции расширяют влияние на соседей
    ResolveAllOwners(galaxy)                 # второй проход после экспансии
    GeneratePlanetsForAllStars(galaxy, ctx)
    GenerateSatellitesForAllPlanets(galaxy)
    FinalizeStarPhysics(galaxy)              # масса, спектр
    
    return galaxy

если все попытки провалились → throw
```

### `GenerateSectors` (в .SectorStars partial)

Размещает несколько секторов (расовых регионов) в сетке `Width × Height`:
```
foreach sectorTemplate in galaxyCfg.SectorTemplates:
    center = random внутри bounds (с минимальной дистанцией до других секторов)
    radius = sectorTemplate.RadiusFromConfig * _sectorRadius
    добавить в galaxy.Sectors
```

### `GenerateStars` (в .SectorStars partial)

В каждом секторе:
```
starCount = random(sector.MinStars, sector.MaxStars)
foreach starIdx in [0..starCount):
    name = if первая → premade; else generate(SectorTemplate.NamePool)
    position = random внутри сектора (с _minStarDist между звёздами)
    star = new StarData { Uid, Name, Position, SectorUid }
    добавить в galaxy.StarsMap
```

### `ResolveAllOwners`

```
foreach star in galaxy.StarsMap:
    foreach planet in star.Planets:
        planet.Owner = sample(cfg.Owners weighted by sector.OwnerWeights)
        planet.Race  = cfg.Owners[planet.Owner].HomeRace
    star.Owner = OwnerResolver.Resolve(star, ctx)   # модальный owner
```

### `ApplyExpansion` (в .Expansion partial)

Метрополии (звёзды с `OwnerWeight > threshold`) расширяют влияние на соседние звёзды
в радиусе `cfg.GalaxyExpansion.MaxRange`. Каждая звезда после экспансии содержит
смесь владельцев в `Owner` (либо `Mixed` если конфликт).

### `GeneratePlanetsForAllStars` (в .Planets partial)

```
foreach star in galaxy.StarsMap:
    maxPlanets = ResolveMaxPlanets(star)   # SystemSizeRule по типу звезды
    planetCount = random(1, maxPlanets + 1)
    
    foreach planetIdx in [0..planetCount):
        planet = new PlanetData
        AssignOrbitRadii(planet)           # _firstOrbitRadius .. + (gap_min..gap_max)
        AssignOrbitalParams(planet)        # OrbitSpeed, DaySpeed, AxialTilt
        AssignVisualParams(planet)         # Size, Graphic, Atmosphere, MaskGraphic
        AssignEconomicParams(planet)       # EconomyType, Government, Population, TechLevel
        TradeSystem.InitializeShop(planet, cfg)
        star.Planets.Add(planet)
        galaxy.PlanetsMap[planet.Uid] = planet
```

### `AssignOrbitRadii` — расстановка орбит

```
prevR = FIRST_ORBIT_MIN + random * (FIRST_ORBIT_MAX - FIRST_ORBIT_MIN)
foreach planet in список:
    planet.OrbitRadius = prevR
    gap = random(ORBIT_GAP_MIN, ORBIT_GAP_MAX)
    prevR += gap
star.SystemSize = prevR + SYSTEM_PADDING   # для границы пустоты
```

Константы из `GalaxyConstants` (инициализированы из `GameSettingsConfig`).

### `OrbitMath.GetEllipticPositionLerp` — позиция планеты с интерполяцией

```
fromPos = ellipticAt(radius, fromAngle, ecc, tiltDeg)
toPos   = ellipticAt(radius, toAngle,   ecc, tiltDeg)
return Lerp(fromPos, toPos, progress)
```

где `ellipticAt`:
```
rad = angle * π/180
tilt = tiltDeg * π/180

xLocal = cos(rad) * radius
yLocal = sin(rad) * radius * sqrt(1 - ecc²)    # сжатие по короткой оси эллипса

# Поворот эллипса на tilt
worldX = xLocal * cos(tilt) - yLocal * sin(tilt)
worldY = xLocal * sin(tilt) + yLocal * cos(tilt)
```

### Спектр звезды (StarPhysics в `.StarPhysics` partial)

Эмпирическая формула, упрощённая main-sequence:
```
mass = sample(cfg.StarTypes[type].MinMass, MaxMass)
luminosity = mass^3.5             # L = M^3.5 main sequence approx
temperature = TableLookup(spectralClass)
color = SpectralClassToColor(spectralClass)
```

### `OwnerResolver.Resolve(star)` — модальный владелец

```
counts = Dictionary<string, int>()
foreach planet in star.Planets:
    counts[planet.Owner] += 1
если counts.Count == 0 → return None
если counts.Count == 1 → return единственный owner
mode = key с max count
если 2-й по count имеет ≥ 60% от mode → return Mixed
иначе → return mode
```

### Генерация имён (CustomPropertyResolver)

Имена планет и звёзд берутся из:
1. `PremadeConfig` (фиксированные имена для конкретных мест) — приоритет;
2. `NamePool` сектора — случайный выбор с проверкой `ReservedNames`;
3. Generic-генератор (если pool пуст) — комбинация префикс+суффикс+номер.

---

## Константы и настройки

### `GalaxyConstants` (mirror of `GameSettingsConfig`)

См. подробный список в [`turn_pipeline.md` — таблица констант](turn_pipeline.md#константы-и-настройки)
и в исходнике `Config/GalaxyConstants.cs`. Краткая выжимка для генерации:

| Константа | Назначение |
|---|---|
| `SECTOR_RADIUS` | Базовый радиус сектора в сетке |
| `MIN_ORBIT_RADIUS, SYSTEM_PADDING` | Геометрия системы |
| `ORBIT_GAP_MIN/MAX` | Расстояние между орбитами планет |
| `FIRST_ORBIT_MIN/MAX` | Первая орбита от звезды |
| `PLANET_ORBIT_SPEED_MIN/MAX, PLANET_DAY_SPEED_MIN/MAX` | Скорости вращения |
| `CLOUD_SPEED_FACTOR_MIN/MAX, CLOUD_VARIANTS_COUNT` | Атмосфера |
| `ORBITAL_OBJ_CHANCE, HIGH_DENSITY_CHANCE, NONE_ORBIT_MAX_FRACTION` | Вероятности |
| `FALLBACK_PLANET_RADIUS, FALLBACK_MAX_PLANETS` | Дефолты |
| `SAT_ORBIT_*` | Параметры спутников |
| `MaxPlacementAttempts` (в GalaxyGenerator.cs) | Сколько попыток рестарта при коллизиях |

### `GalaxyConfig` (десериализуется из JSON)

Главные секции:
- `Galaxies` — определения галактик (MilkyWay, …)
- `Sectors` — шаблоны секторов
- `Stars` — типы звёзд (по спектру) с диапазонами массы
- `Planets` — `Sizes`, `EconomyTypes`, `GovernmentTypes`, `TypeDefinitions`
- `Owners` — фракции (HomeRace, ColorHex, Personality, начальные владения)
- `Races` — расы (HomePlanets, Personality, Trade.BannedGoodsByGovernment, …)
- `Goods` — товары для торговли
- `Trade` — глобальные торговые параметры
- `Inflation` — параметры инфляции
- `Events` — шок-события и их вероятности
- `Equipment` — категории оборудования и шаблоны
- `Settings` — `SystemSizeMult`, `InitialDate`, спавн-параметры

---

## Внутренняя структура данных

### `SectorData`

```
Uid : string
Name : string
Center : Vector2
Radius : float
DominantRace : string
DominantOwners : List<string>
Stars : List<string>           # UIDs звёзд внутри сектора
NamePool : List<string>
OwnerWeights : Dictionary<string, float>
```

### `StarPhysics`

```
SpectralClass : string         # O / B / A / F / G / K / M
Mass : float                   # солнечных масс
Luminosity : float             # солнечных
Temperature : float            # K
Color : Color                  # для рендера
```

### `SatelliteData`

```
Uid, Name : string
ParentPlanetUid : string
OrbitRadius : float            # вокруг планеты
OrbitSpeed, DaySpeed : float
OrbitInclination : float       # для эллиптической плоскости
CurrentAngle : float
Size : string
Graphic : string
```

### `RaceInfo`

```
Uid, Name : string
HomePlanetUids : List<string>
Personality : RacePersonalityTemplate    # для NPC дефолтов
... всё что в cfg.Races
```

---

## Известные ограничения / TODO

1. Модели разнесены по `Galaxy/Models/*.cs` (сентябрь 2026). Оркестрация дня вынесена в
   `Galaxy/Simulation/GalaxySimulator` (`GalaxyData.GalaxyNextDay` — делегат), день звезды —
   в `StarSimulator`. В моделях ещё остаются `ShipData.ShipNextDay`/`ShipMoveStep` (движение
   завязано на приватные поля) — кандидат на следующий шаг.
2. **Конфиг-модели** разнесены по `Config/Models/*.cs` по темам; в `ItemsConfig` остаются методы парсинга
   (`EnsureItemsCache`, `EnsureTemplatesCache`). Парсинг внутри конфиг-модели — нарушение SRP,
   но миграция в отдельный `EquipmentConfigParser` отложена (изменения схемы конфигов будут
   массовые).
3. **`MaxPlacementAttempts` зашит в коде** — `0x9E3779B9` (golden ratio constant) для seed-shake.
   Если генерация не сходится за N попыток — exception. На практике сходится за 1–3.
4. **`VoronoiHelper`** — простая O(N²) реализация. Для текущих ~100 центров (звёзд с unique owner)
   это OK; для крупных галактик стоит заменить на Fortune's algorithm.
5. **`OrbitMath.GetEllipticPositionLerp` интерполирует ДВЕ позиции по progress** — это правильно для
   небольших шагов, но при большом fromAngle→toAngle (например, если планета прошла > 180°
   за один ход) интерполяция «срезает угол» вместо обхода. На практике планеты движутся
   медленно, скачков нет.
6. **`OwnerResolver.Resolve` для звезды** возвращает `Mixed` если 2-й по count owner ≥ 60% от 1-го.
   Это эвристика без чёткого обоснования — балансится при тестировании.
