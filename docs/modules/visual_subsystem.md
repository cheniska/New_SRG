# Visual Subsystem

Модули: `Visual/*.cs` + `Core/SystemViewManager.cs` + `Core/GraphicsManager.cs` +
`Ships/ShipVisualController.cs` + `Ships/ShipGraphicsResolver.cs`.

Полный список файлов: `BaseVisualController`, `PlanetVisualController`, `SatelliteVisualController`,
`AsteroidVisualController`, `ShipVisualController` (формально в Ships/), `BackgroundVisualController`,
`HyperjumpPortalVisualController`, `DroppedItemVisualController`, `TowBeamVisualController`,
`SystemMinimapController`, `MinimapGraphics`, `GalaxyMapGraphics`, `StarLabelRenderer`,
`StarVisualController`, `WeaponVisualSystem`, `WeaponRangeIndicator`, `RadarRangeIndicator`,
`ExplosionPlayback`, `AnimatedSpriteRenderer`, `SortingLayerRegistry`, `CameraController`.

Связанные документы: [`Ship_Trajectory_*.txt`](..), [`Ship_Landing_Pipeline.txt`](..).

---

## Назначение

Слой рендера, отделённый от симуляции. Принимает данные из `GalaxyData`/`TurnAnimationData` и
рисует мир: планеты с шейдером вращения, орбитальные объекты, корабли с анимацией спрайтов,
ракеты, астероиды, выстрелы, контейнеры, лучи буксира, гиперпорталы, фоновые параллакс-звёзды,
индикаторы радаров/оружия, экраны минимапы и галакарты.

Центральный диспетчер — `SystemViewManager` (singleton), который собирает все Visual-контроллеры,
анимирует их каждый кадр Simulation-фазы, спавнит/уничтожает по событиям из `TurnAnimationData`.

---

## Публичный интерфейс

### `SystemViewManager` (singleton)

См. также [`turn_pipeline.md`](turn_pipeline.md#systemviewmanager-singleton).

Ключевые методы для других модулей:
```csharp
public void BeginTurnAnimation(TurnAnimationData anim);
public void AnimateSystem(float progress, int currentSubTurn, TurnAnimationData anim);
   // Композирует: UpdatePlanetVisuals + UpdateShipVisuals + UpdateTowBeams
   //            + AnimateAsteroidPositions + WeaponVfx.Tick + missile-блок
public void UpdatePlanetPositions();
public void RenderSystem(StarData star);                 // полный re-render при смене звезды
public void EnsureShipVisual(ShipData ship);             // для свежеспавненного объекта
public bool TryGetAsteroidVisualPosition(string uid, out Vector2 position);
public void SpawnExplosionEffect(Vector2 pos, AsteroidCollisionType, string explosionPath = null);
public void SpawnDroppedItems(Vector2 pos, List<ItemStack> stacks, List<ItemInstance> items);

// Доступ к подсистемам:
public WeaponVisualSystem GetWeaponVfx();
public HyperjumpPortalManager GetHyperPortals();
public SystemMinimapController MinimapController;
public Transform SystemContainer;
```

### `BaseVisualController` (abstract — Visual/BaseVisualController.cs)

Создан на этапе A10 рефакторинга. Общий код для Planet/Satellite (Asteroid слишком отличается).

```csharp
public SpriteMask ShapeMask;
protected void EnsureMask(Sprite mask);
protected SpriteRenderer EnsureRenderer(SpriteRenderer r, string slotName, int sortOrder, bool useMask);
```

Подклассы:
- `PlanetVisualController` — добавляет surface/atmosphere/orbital слои, MaterialPropertyBlock,
  PlanetRotation-shader, спутники.
- `SatelliteVisualController` — добавляет surface/atmosphere слои, расчёт эллиптической орбиты
  вокруг родительской планеты, динамические depth-факторы (масштаб + sortingOrder через
  `SortingLayerRegistry.SatelliteUp/Down`).

### `ShipVisualController` (Ships/ShipVisualController.cs)

```csharp
public void Setup(ShipData ship);
public void PrepareForTurn(TurnAnimationData anim, StarData star);
   // Считает геометрию посадки заранее: если конец анимации этого хода попадает в R_land
   // целевой планеты — выставляет ship.LandingPhase = Fading. См. Ship_Landing_Pipeline.txt.
public void AnimateTurn(float progress, int currentSubTurn, TurnAnimationData anim);
public void EndTurn();
public void PlayExplosion();
public void Setup(...);  // загрузка spritesheet, init аниматора
```

### `WeaponVisualSystem` (Visual/WeaponVisualSystem.cs)

См. также раздел в [`combat.md`](combat.md).

```csharp
public void Init(Transform root);
public void BeginSimulation(TurnAnimationData anim);
public void Tick(float progress, TurnAnimationData anim);
public void EndSimulation();
```

Хранит пул `SpriteRenderer`/`LineRenderer` (prewarm 100+30), ленивый билд VFX-объектов
(GameObject'ы создаются при первом тике, когда `progress` достигает `T0`).

### `ExplosionPlayback` (Visual/ExplosionPlayback.cs)

```csharp
public static void Preload(IEnumerable<string> paths);     // прогрев кэша
public static void PlayOn(GameObject go, string path = null);  // in-place: забирает SpriteRenderer
public static void SpawnAt(Transform parent, Vector2 pos, string path = null);
```

Унифицирован на этапе perf-оптимизации июня 2026 (см. memory `project_perf_explosions_vfx_jun2026`).

### `GraphicsManager` (singleton)

Единая точка загрузки/кэширования графики. Подробное руководство и правила расширения:
[`graphics_pipeline.md`](graphics_pipeline.md).

```csharp
public void Init(GalaxyGenerationContext ctx);
// Одиночные спрайты
public Sprite GetSprite(string path);        // LogError один раз на промах
public Sprite TryGetSprite(string path);     // silent — для fallback-цепочек
// Листы
public Sprite[]   GetSpriteSheet(string path);
public SheetHandle GetSheetHandle(string path, float fps = 12f);
// Прогрев
public void PreloadSheets(IEnumerable<string> paths);
// Специфичное
public (Sprite[] frames, float animSpeed) GetStarVisuals(string color, int variant);
public float GetStarScale(string typeName);
public string[] EnumerateSheetPathsInFolder(string folderPath);
public GalaxyConfig GetConfig();
public void ClearCache();
```

### `ShipGraphicsResolver` (static)

```csharp
public static ShipGraphicsResolution Resolve(ShipData ship, EquipmentConfig equipConfig);
   // Возвращает spritesheet path + размер + параметры анимации для текущего корпуса/типа
public static void ClearCache();
```

### `CameraController` (singleton)

```csharp
public Camera Camera;
public void SetPosition(Vector2 worldPos);
public void Tick(float dt);     // обработка ввода (move/zoom/edge scroll)
```

### `SortingLayerRegistry` (static)

```csharp
public static int Get(SortLayer layer);

public enum SortLayer {
    PlanetSurface, PlanetAtmosphere, PlanetOrbital,
    SatelliteUp, SatelliteDown,
    Asteroid, Ship, Missile, Projectile, Explosion,
    DroppedItem, TowBeam, RangeIndicator,
    UI, Background, ...
}
```

---

## Зависимости

```
SystemViewManager
   ├─► GalaxyManager.Instance.CurrentStar
   ├─► PlayerShip.Instance + ShipData
   ├─► все Visual-контроллеры (Planet, Satellite, Asteroid, Ship, …)
   ├─► WeaponVisualSystem, HyperjumpPortalManager, TowBeamRenderer
   ├─► ExplosionPlayback
   ├─► MinimapController
   └─► TurnAnimationData (читает поля)

ShipVisualController
   ├─► ShipGraphicsResolver
   ├─► GraphicsManager
   ├─► PlanetGeometry (R_land/R_app для PrepareForTurn)
   ├─► AnimatedSpriteRenderer
   └─► ExplosionPlayback (PlayExplosion)

PlanetVisualController / SatelliteVisualController
   ├─► BaseVisualController
   ├─► GraphicsManager
   ├─► SortingLayerRegistry
   └─► Custom/PlanetRotation shader

WeaponVisualSystem
   ├─► SpriteUtility.CreateCircleSprite
   ├─► ShipFrames / MissileFrames (через ShipPos/ShipPosAt)
   └─► GalaxyManager.Instance.CurrentStar (BuildLocalShipUidSet)
```

---

## Алгоритмы и формулы

### `PlanetVisualController.Setup` — порядок инициализации

```
maskSprite = GraphicsManager.GetSprite(data.MaskGraphic)
EnsureMask(maskSprite)              # из BaseVisualController
EnsureMaterial()                    # создать planetMaterial (Custom/PlanetRotation shader)

SetupSurfaceLayer(data.Graphic, maskSprite)
   # EnsureRenderer + MaterialPropertyBlock с MainTex/AxialTilt
SetupAtmosphereLayer(data.Atmosphere, maskSprite)
   # тот же шейдер с другой текстурой, прозрачность 0.9
SetupOrbitalLayer(data.OrbitalObjects)
   # обычный Sprites/Default шейдер, без маски

ApplyScale(data.Size)               # config.Planets.Sizes[size].BaseScale
SpawnSatellites(data, satellitePrefab)
```

Каждый кадр `Update`:
```
TickLayerRotation(ref _currentSurfaceRotation, _surfaceRotationSpeed, SurfaceRenderer, dt)
TickLayerRotation(ref _currentCloudsRotation, _cloudsRotationSpeed, AtmosphereRenderer, dt)
foreach satellite: ctrl.UpdateVisualOrbit(dt)
```

Скорости из `data.DaySpeed`/`data.CloudsSpeed` (большее число = медленнее: `speed = 1 / X`).

### `SatelliteVisualController.UpdateWorldPosition`

Эллиптическая орбита с наклоном плоскости:
```
rad = angleDeg * π/180
r = OrbitRadius / 100
incl = OrbitInclination * π/180

# Эллипс: y-полуось сжата в 0.4 раза, потом наклон вокруг Z
xBase = cos(rad) * r
yBase = sin(rad) * r * 0.4

local.x = xBase * cos(incl) - yBase * sin(incl)
local.y = xBase * sin(incl) + yBase * cos(incl)

# Depth-фактор для масштаба и порядка отрисовки
depthFactor = sin(rad)
scaleMult = 1 - depthFactor * 0.25
localScale = baseScale * scaleMult
sortingOrder = depthFactor > 0 ? SatelliteUp : SatelliteDown
```

### `ShipVisualController.AnimateTurn` (упрощённо)

```
если ship.TowedByUid задан и IsTowSettled → use buffered path from TurnAnimationData.ShipRenderPaths
иначе:
    путь = TurnAnimationData.ShipRenderPaths[uid] или fallback на ShipFrames[uid]
    позиция = Lerp(путь[Floor(progress * (N-1))], путь[Ceil(...)], frac)
направление = (нынешняя - предыдущая).normalized → CurrentHeading
ApplyFadeOverlay(progress)        # для Landing/Takeoff/HyperEnter/HyperExit
тяги (ThrusterRenderer)
```

Fade-альфа в `ComputeFadeAlpha`:
- **Landing.Fading**: alpha = clamp(1 - (progress - fadeStart) / fadeDur, 0, 1) — постепенное исчезновение
- **Takeoff**: alpha = clamp((progress - takeoffStart) / takeoffDur, 0, 1) — появление
- **HyperEnter**: исчезание к концу хода
- **HyperExit**: появление с начала хода

### `ExplosionPlayback` — in-place vs spawn

**In-place** (`PlayOn(go)`) — забирает существующий `SpriteRenderer` (например, у корабля или
астероида), подменяет его на кадры взрыва, по окончании уничтожает `GameObject`. Используется
для гибели цели — взрыв «продолжает» рендер на месте.

**Spawn** (`SpawnAt(parent, pos)`) — создаёт отдельный `GameObject` под взрыв (например, для
гибели игрока, чья основа `DontDestroyOnLoad`). По окончании анимации сам уничтожается.

Кадры грузятся через `GraphicsManager.GetSheetHandle` (общий sheet-кэш; отдельного кэша
в ExplosionPlayback нет). Прогрев — единый sink `GalaxyManager.PreloadAllVisuals`. Подробнее —
[`graphics_pipeline.md`](graphics_pipeline.md).

### `SystemMinimapController` — обновление позиций

```csharp
UpdatePlanetPosition(planet, worldPos)    // вызывается из UpdatePlanetVisuals
UpdateNpcShipPosition(ship, worldPos)     // вызывается из UpdateShipVisuals
UpdatePlayerPosition()                    // каждый кадр в LateUpdate (читает PlayerShip.Instance)
```

Цвета иконок берутся через `Relations.GetLevelToPlayer(ship)` — hostile=red,
friendly=green, etc.

### `TowBeamVisualController.Refresh(pairs)`

Для каждой пары `(ship, t)` в текущей системе:
- Если ship буксирует кого-то (есть TowedObjectUids или PullKind == Tow) — нарисовать `LineRenderer`
  от ship до якоря/цели.
- Если корабль использует CargoGrabber на лету (PullKind == Pickup) — другой стиль луча.
- Если идёт абордаж — третий стиль (тонкая красная связь).

Цвета и толщина — из `TowBeamConfig` или per-equipment override.

---

## Константы и настройки

### `WeaponVisualSystem`

См. [`combat.md` — настройки WeaponVisualSystem](combat.md#weaponvisualsystem-наблюдения).

Ключевые: `kSub=10`, `kZ=35`, `kImpactDur=0.065`, `kDotPrewarm=100`, `kLinePrewarm=30`.

### `BackgroundVisualController`

Все настройки в `GalaxyConstants` (инициализированы из `GameSettingsConfig`):
- `BG_STAR_COUNT_FAR/MID/MID_NEAR/NEAR` — кол-во звёзд по слоям
- `BG_TEX_SIZE_FAR/NEAR` — размер процедурной текстуры
- `BG_PARALLAX_*` — скорости параллакса
- `BG_LAYER_TILE_SIZE`, `BG_VOID_SCALE_FACTOR`, `BG_PARALLAX_STAR_BIG`, `BG_STAR_BIG_GRID`

### `SystemMinimapController`

| Имя | Значение | Назначение |
|---|---|---|
| `ShipOutlineThickness` | 1.2 | Толщина обводки иконки корабля |
| `TrailDotCount` | 12 | Кол-во точек траектории |
| `MinimapSize` / `MinimapRange` | конфиг | Размер и зум миникарты |

### Цвета иконок миникарты

Берутся из `GalaxyConstants.DEFAULT_COLOR`/`DefaultMinimapColor` и через `Relations.GetLevelToPlayer`.

---

## Внутренняя структура данных

### `TurnAnimationData` (см. `turn_pipeline.md`)

Поля, релевантные Visual:
- `ShipFrames`, `ShipRenderPaths` — позиции и плотный путь
- `AsteroidFrames`, `AsteroidSpawns`, `AsteroidDestroys`
- `MissileFrames`, `MissileDeathUids`, `MissileSilentDeathUids`
- `Shots: List<ShotEvent>`
- `DeathUids`, `PlanetAngles`

### `Vfx` (внутренний класс в `WeaponVisualSystem`)

```
Shot : ShotEvent
From, To : Vector2
T0, T1, TFade : float
Dots : SpriteRenderer[]
Lines : LineRenderer[]
ChainPts : Vector2[]
All : List<GameObject>
ImpactDotsStart, ImpactDotsCount : int
Built : bool                       # lazy-build flag
```

### `GraphicsConfig` (через `GraphicsManager.GetConfig`)

```
Planets : { Sizes : Dictionary<string, SizeData> }
   // SizeData { BaseScale : float, ... }
Satellites : { Sizes : Dictionary<string, SizeData> }
... shader paths, default mask sprites, etc.
```

### `ShipSubTurnFrames`, `MissileSubTurnFrames`

`SubTurns: Vector2[SubTurnsPerTurn + 1]` — позиции на каждом сабтёрне.

---

## Известные ограничения / TODO

1. **`SystemViewManager.AnimateSystem` уже сокращён** (этап B2), но **missile-блок** (~150 строк
   спавн/смерть/анимация) ещё не выделен — слишком переплетён с `_firedDestroyEvents` и флагом
   `animating`. Разбор оставлен на следующий заход.
2. **`ShipVisualController.AnimateTurn` (59 строк)** — на границе «можно ли разбить». Подметоды
   `ComputeAnimatedPosition` и `ApplyFadeOverlay` — кандидаты для извлечения. Не сделано из-за
   риска изменить timing fade-out при посадке (SR2HD-чувствительно).
3. **Visual-цвета цели** на миникарте через `Relations.GetLevelToPlayer` каждый кадр — потенциальный
   hotspot при сотнях NPC. Кэширование (раз в N кадров) — на потом.
4. **`AsteroidVisualController` не наследует `BaseVisualController`** — рендер сильно отличается
   (без маски, без PropertyBlock, со spritesheet-анимацией). Унификация не оправдана.
5. **`HyperjumpPortalVisualController`** имеет свою машину состояний (открытие/закрытие портала)
   синхронизированную с `HyperjumpPhase` корабля. Тонкий момент — переход HyperEnter→HyperExit
   занимает 2 хода, портал «остаётся» в исходной системе один лишний тик. Сглажено через
   `_pendingArrivalTransition` (см. `turn_pipeline.md`).
6. **WeaponVisualSystem дублирует фильтр по локальной системе** через HashSet каждый
   BeginSimulation — переиспользование поля сэкономит малую аллокацию (см. наблюдения в Этапе D).
