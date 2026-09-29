# Характеристики планет и пригодность для заселения

Источник:
- `Assets/Scripts/Generation/GalaxyGenerator.Planets.cs` — генерация размера, плотности, спутников, орбит, площадь поверхности
- `Assets/Scripts/Generation/GalaxyGenerator.StarPhysics.cs` — `RollStellarPhysics`, `RollSurfaceTypes`, `RecomputeHydrologyState`, `CheckRaceConditions`
- `Assets/Scripts/Generation/GalaxyGenerator.Expansion.cs` — `RunExpansion`, `ExpansionIsHabitable`, `ExpansionIsTerraformable`, `ExpansionApplyOptimal`
- `Assets/Scripts/Config/GalaxyConfigurationModels.cs` — `RacePlanetConditionsConfig`, `PlanetSizeData`, `StarColorData`, `StarTypeData`
- `Assets/Scripts/Generation/GalaxyDataModels.cs` — поля `PlanetData`

---

## 1. Конвейер создания планеты

```
CreatePlanet
 ├─ ApplyRandomPlanetProps / ApplyFixedPlanetProps  → раса, владелец, имя, графика, скорости
 └─ FinalizePlanetVisuals                            → размер, спутники, эксцентриситет, наклон
     └─ RollGravityDensity                           → SurfaceGravity, Density, GeoActivity, TotalSurfaceArea

После того как все планеты звезды собраны:
 FinalizeStarProps
 ├─ HZ_SizeMult растягивает все OrbitRadius          (если у типа звезды есть множитель)
 └─ для каждой планеты:
     RollStellarPhysics
      ├─ SolarFlux, baseTemp
      ├─ MagneticField   (ветвление по Density)
      ├─ AtmPressure     (ветвление по Density)      ← перезаписывается AtmPressureFixed
      ├─ SurfaceRadiation
      ├─ WaterAbundance                             ← перезаписывается WaterAbundanceFixed
      ├─ SurfaceTemp = baseTemp + парниковый эффект ← перезаписывается SurfaceTempFixed
      ├─ OxygenPercent                              ← перезаписывается OxygenPercentFixed
      ├─ RecomputeHydrologyState
      └─ RollSurfaceTypes (% жидкости/равнин/гор)
     CheckRaceConditions                             → habitable / terraformable / сбросить расу
```

Если в `Config.Settings.Expansion == 1`, запускается отдельный `RunExpansion` — он стирает
расу у непремейдовых звёзд и заново колонизирует их историческими волнами (см. §10).

---

## 2. Промежуточные величины

```
lStar  = MassSolar^1.267 × RadiationMult     // относительная светимость звезды
d      = OrbitRadius × HZ_SizeMult           // расстояние от звезды (игр. ед.)
dRatio = d / 2800                            // StellarD0 = 2800
g      = SurfaceGravity (м/с²)
gRatio = g / 9.81
```

`MassSolar` берётся из `Stars.Types[Type].MassSolar`.
`RadiationMult` берётся из `Stars.Colors[Color].RadiationMult`.
`HZ_SizeMult` — множитель размера системы для данного типа звезды
(`Stars.Types[Type].HZ_SizeMult`); масштабирует все `OrbitRadius` после того, как они
расставлены, и используется в `RollStellarPhysics` для пересчёта `d`.

---

## 3. Размер, гравитация, плотность, спутники, площадь

`Size` выбирается равномерно из `Planets.Sizes` (для fixed-планет — берётся из конфига).
Параметры из `PlanetSizeRules[Size]` (`PlanetSizeData`):

| Поле                | Назначение                                                   |
|---------------------|--------------------------------------------------------------|
| `GravityRange`      | диапазон `SurfaceGravity` (м/с²)                             |
| `DensityRange`      | диапазон `Density` (г/см³)                                   |
| `GeoK`              | коэффициент геоактивности по размеру                         |
| `SurfaceArea`       | диапазон `TotalSurfaceArea` (усл. млн км²)                  |
| `MaxSatellites`     | максимум спутников                                            |
| `Population`        | диапазон начального населения для обитаемых планет           |
| `AtmosphereChance`  | шанс наличия слоя атмосферы (графика)                        |
| `BaseScale`         | визуальный масштаб                                            |

```
SurfaceGravity   = rand(sizeData.GravityMin .. sizeData.GravityMax)
SurfaceGravity  ← fixedGravity      (если задано в FixedPlanetData)

с вероятностью HIGH_DENSITY_CHANCE (5%):
    Density     = rand(1 .. 2)                    // редкая «газовая» аномалия
иначе:
    Density     = rand(sizeData.DensityMin .. sizeData.DensityMax)
Density         ← fixedDensity      (если задано)

TotalSurfaceArea = rand(SurfaceAreaMin .. SurfaceAreaMax)   // по умолчанию 500
SatellitesCount  — пересчитывается в GenerateSatellites (≤ MaxSatellites)
```

### Геоактивность (GeoActivity), [0..1]

```
geoBase     = Clamp(GeoK × Density^0.6,  0, 1)
GeoActivity = Clamp(geoBase × rand[0.4 .. 1.6]  +  SatellitesCount × 0.05,  0, 1)
GeoActivity ← fixedGeoActivity                                 (если задано)
```

`GeoK` берётся из `PlanetSizeRules[Size]`. Каждый спутник добавляет +0.05 геоактивности.

---

## 4. Звёздный поток (SolarFlux)

```
SolarFlux = lStar / dRatio²
```

> Земля: `lStar = 1`, `dRatio = 1` → `SolarFlux = 1`.

---

## 5. Магнитное поле (MagneticField), [0..3]

Ветвление по **Density** (порядок: газовый → ледяной → каменный):

| Тип            | Density | Формула                                                                      |
|----------------|---------|------------------------------------------------------------------------------|
| Газовый        | < 2     | `Clamp( gRatio^1.2 / 1.4, 0, 3 )`                                            |
| Ледяной        | 2–3     | `Clamp( √gRatio × GeoActivity × (1 + 0.2 × Sats) / 0.77, 0, 3 )`             |
| Каменный       | ≥ 3     | `Clamp( √gRatio × GeoActivity³ × (1 + 0.15 × Sats) / 1.15, 0, 3 )`           |

---

## 6. Атмосферное давление (AtmPressure), атм

Ветвление по **Density**:

| Тип            | Density | Формула                                                                             |
|----------------|---------|-------------------------------------------------------------------------------------|
| Газовый        | < 2     | `Min(10,  gRatio^1.5 × 5)`                                                          |
| Ледяной        | 2–3     | `Clamp( gRatio^0.8 × (1/SolarFlux)^0.3 × 0.5,  0, 3 )`                              |
| Каменный       | ≥ 3     | `gRatio × GeoActivity² × √(1 + MagField) / (√SolarFlux × √2)`                       |

> Калибровка каменных: Земля (geo=1, g=9.81, B≈1, flux=1) → `AtmPressure ≈ 1.0` атм.

Перезаписывается `AtmPressureFixed`, если задано в `FixedPlanetData.AtmPressure`.

---

## 7. Радиация поверхности (SurfaceRadiation)

```
SurfaceRadiation = SolarFlux × 1000 × 1.361
                   ─────────────────────────────────────────
                   (1 + 0.36 × AtmPressure) × (1 + 0.36 × MagField)
```

Атмосфера и магнитное поле — два независимых защитных множителя
(коэффициенты одинаковые, 0.36).

---

## 8. Вода, температура, кислород, гидрология

### 8.1 Вода (WaterAbundance), [0..1]

```
Газовые (Density < 2):
    WaterAbundance = 0

Остальные:
    satBonus       = Clamp(SatellitesCount × 0.08,  0, 0.2)
    WaterAbundance = Clamp(
        (GeoActivity + satBonus) × MagField × (1 − SolarFlux × 0.343),
        0, 1)

WaterAbundance ← WaterAbundanceFixed              (если задано)
```

### 8.2 Температура поверхности (SurfaceTemp), К

```
baseTemp         = 278 × lStar^0.25 / √dRatio
greenhouseFactor = ln(1 + AtmPressure) × (1 + 0.5 × WaterAbundance)
SurfaceTemp      = baseTemp + 10 × greenhouseFactor
SurfaceTemp     ← SurfaceTempFixed                (если задано)
```

`ln(1+P)` нелинейно растёт с давлением; вода усиливает парниковый эффект.

### 8.3 Кислород (OxygenPercent), %, [0..35]

```
oxyBase       = WaterAbundance × Clamp01(MagField) × Clamp01(AtmPressure)
fluxFactor    = 1 / (1 + 0.6 × max(0, SolarFlux − 1))   // УФ-расщепление при flux > 1
geoFactor     = 1 / (1 + 0.8 × GeoActivity²)            // вулканические газы вытесняют O₂
OxygenPercent = Clamp(oxyBase × fluxFactor × geoFactor × 52,  0, 35)
OxygenPercent ← OxygenPercentFixed                      (если задано)
```

> Требует одновременно воды, магнитного поля и достаточного давления — нулевое значение
> любого множителя обнуляет O₂.

### 8.4 Парциальное давление O₂ (pO₂)

Используется в проверках пригодности для рас:

```
pO2 = (OxygenPercent / 100) × AtmPressure
```

### 8.5 Состояние воды (HydrologyState)

| Значение | Состояние               | Условие                                            |
|----------|-------------------------|----------------------------------------------------|
| 0        | нет / лёд (сухо)        | `WaterAbundance < 0.05` или `SurfaceTemp < 200 К` |
| 1        | лёд                     | `SurfaceTemp < 273 К`                              |
| 2        | жидкость                | `SurfaceTemp < 647 К` **и** `AtmPressure > 0.05`   |
| 3        | пар / сверхкритическая  | всё остальное                                       |

Состояния 4 (CH₄) и 5 (NH₃) зарезервированы, не реализованы.

---

## 9. Поверхность: жидкость / равнины / горы

`RollSurfaceTypes` распределяет 100% площади между тремя категориями. Сумма всегда = 100.

### Сырые значения

```
rawLiquid =
    HydrologyState == 2 → WaterAbundance × 75    // жидкая вода (Земля ~71%)
    HydrologyState == 1 → WaterAbundance × 8     // преимущественно лёд
    HydrologyState == 3 → WaterAbundance × 15    // сверхкрит./лавовое
    иначе              → 0                       // безводная пустошь
rawLiquid    = Clamp(rawLiquid, 0, 80)

rawMountains = Clamp(GeoActivity × 40 + 5, 5, 55)
```

### Перезапись и нормализация

```
liquid    = SurfaceLiquidFixed    ?? rawLiquid
mountains = SurfaceMountainsFixed ?? rawMountains

if (liquid + mountains > 100)
    масштаб = 100 / (liquid + mountains)
    liquid    ×= масштаб
    mountains ×= масштаб

SurfaceLiquid    = round(liquid)
SurfaceMountains = round(mountains)
SurfacePlains    = 100 − SurfaceLiquid − SurfaceMountains
```

### Реальные площади (млн км²)

```
AreaLiquid    = TotalSurfaceArea × SurfaceLiquid    / 100
AreaPlains    = TotalSurfaceArea × SurfacePlains    / 100
AreaMountains = TotalSurfaceArea × SurfaceMountains / 100
```

---

## 10. Орбиты

### 10.1 Эксцентриситет и наклон

```
OrbitEccentricity = rand(Planets.OrbitsEccentricity[0] .. Planets.OrbitsEccentricity[1])
OrbitTiltDeg      = rand(0 .. 360)
AxialTilt         = rand(0 .. 90)
```

### 10.2 Радиусы орбит

Орбиты раздаются по возрастанию `OrbitIndex`. Хранимое значение `OrbitRadius` — это
**апоцентр**.

```
peri      = prevApocenter + rand(ORBIT_GAP_MIN .. ORBIT_GAP_MAX) × SystemSizeMult
для i = 0:
    minPeri = rand(FIRST_ORBIT_MIN .. FIRST_ORBIT_MAX) × SystemSizeMult
    peri    = max(peri, minPeri)

OrbitRadius   = peri / max(1 − e, 0.01)        // апоцентр = peri / (1 − e)
prevApocenter = OrbitRadius × (1 + e)
```

Если `OrbitRadius` задан вручную (`FixedPlanetData.OrbitRadius`) и не помечен как
`HasFixedOrbitRadius`, он трактуется как **перицентр** и пересчитывается в апоцентр
тем же делением `peri / (1 − e)`.

После генерации всех планет звезды все `OrbitRadius` ещё раз умножаются на
`Stars.Types[Type].HZ_SizeMult` — это «растягивает» систему под яркую/тусклую звезду.

### 10.3 Скорости

```
OrbitSpeed  = rand(PLANET_ORBIT_SPEED_MIN .. PLANET_ORBIT_SPEED_MAX) × (±1)
DaySpeed    = rand(PLANET_DAY_SPEED_MIN .. PLANET_DAY_SPEED_MAX)
CloudsSpeed = round(DaySpeed × rand(CLOUD_SPEED_FACTOR_MIN .. CLOUD_SPEED_FACTOR_MAX))
```

Константы по умолчанию: `50..300`, `20..60`, `0.6..0.8`. Все три могут быть
переопределены в `GameSettingsConfig`.

---

## 11. Обитаемая зона звезды

Каждая звезда тянет из `Stars.Colors[Color]` четыре параметра:

| Поле                 | Назначение                                            |
|----------------------|-------------------------------------------------------|
| `HabitableZoneMin_u` | внутренняя граница HZ (внутр. единицы)                |
| `HabitableZoneMax_u` | внешняя граница HZ                                    |
| `HabitableZoneMid_u` | оптимум                                                |
| `FlareRisk`          | риск вспышек, влияет на UI/события                    |

HZ напрямую **не используется** в формулах температуры или O₂ — фактическая обитаемость
выводится из физики (`SolarFlux`, `SurfaceTemp`, `AtmPressure`...). HZ нужна только для
визуализации на миникарте системы (`SystemMinimapController.showHabitableZone`).

---

## 12. Пригодность для конкретной расы

`CheckRaceConditions(planet)` запускается для каждой планеты, у которой стоит конкретная
раса (`Race != "None"`). Берёт `Races[Race].PlanetConditions`
(класс `RacePlanetConditionsConfig`).

### 12.1 Конфиг условий

Для каждого параметра задаётся два диапазона:

| Параметр          | «Habitable»                        | «Terraformable» (более широкий) |
|-------------------|------------------------------------|---------------------------------|
| Вода              | `WaterAbundance`                   | `WaterAbundanceTerraformable`   |
| pO₂               | `OxygenPartialPressure`            | `OxygenPartialPressureTerraformable` |
| g                 | `g`                                | `gTerraformable`                |
| Давление          | `AtmPressure`                      | `AtmPressureTerraformable`      |
| Радиация          | `SurfaceRadiation`                 | `SurfaceRadiationTerraformable` |
| Температура       | `SurfaceTemp.Acceptable[0..1]`     | `SurfaceTemp.Terraformable[0..1]` |

У температуры дополнительно есть `Optimal` (целевой диапазон для терраформирования) и
`Limit` (внешняя граница выживания — зарезервировано для штрафов).

### 12.2 Алгоритм

```
1. partialO2 = (OxygenPercent / 100) × AtmPressure
2. Если все min/max из «habitable» выдержаны:
       IsTerraformable = false                       → планета остаётся как есть
       выход
3. Иначе проверяется «terraformable»:
       если все Tf-диапазоны выдержаны:
           IsTerraformable = false
           ExpansionApplyOptimal(planet, raceConfig) → параметры подтягиваются к Optimal
           IsTerraformed   = true
           выход
4. Иначе раса покидает планету:
       Race  = "None"
       Owner = "None"
       OrbitalObjects = ""
       IsTerraformable = false
       IsTerraformed   = false
```

Каждый параметр проверяется независимо: достаточно одного выхода за min/max,
чтобы планета перестала быть «habitable» (или соответственно «terraformable»).

> Один и тот же набор условий используется в `ExpansionIsHabitable` /
> `ExpansionIsTerraformable` в режиме исторической колонизации (§14).

### 12.3 Что показывает «✓ / ✗» в UI

`GalaxyLogger.FormatRaceHabitability` строит таблицу пригодности для всех рас по
**текущим** значениям планеты. Для каждой расы выводит «`Имя ✓`», если все шесть
условий выдержаны, иначе «`Имя ✗(метка↑/↓, …)`». Метки совпадают с параметрами:
`Вода`, `pO₂`, `P`, `Rad`, `g`, `T`.

---

## 13. Терраформирование

Когда `CheckRaceConditions` (или фаза Expansion) решает, что планета попадает только в
«terraformable», вызывается `ExpansionApplyOptimal(planet, raceConfig)`:

```
для каждого параметра, у которого есть «оптимум»:
    TerraformOriginals[имя] = текущее_значение
    значение                = середина оптимального диапазона
```

Оптимум считается так:

| Параметр       | Источник «оптимума»                                            |
|----------------|----------------------------------------------------------------|
| SurfaceTemp    | середина `SurfaceTemp.Optimal[0..1]`, иначе середина `Acceptable` |
| AtmPressure    | середина `AtmPressure[min..max]`                               |
| WaterAbundance | середина `WaterAbundance[min..max]`                            |
| OxygenPercent  | пересчитан из середины `OxygenPartialPressure[min..max]`: `pO₂ / AtmPressure × 100`, Clamp(0..100) |
| SurfaceRadiation | середина `SurfaceRadiation[min..max]`                        |

После этого вызывается `RecomputeHydrologyState` — `HydrologyState` пересчитывается под
новые температуру/воду/давление.

`SurfaceLiquid/Mountains/Plains` **не пересчитываются** — карта поверхности остаётся
прежней, меняются только климатические показатели.

`PlanetData` хранит:
- `IsTerraformed` — флаг, что параметры были подтянуты к оптимуму;
- `TerraformOriginals` — исходные значения, индекс по имени поля (`"SurfaceTemp"`,
  `"AtmPressure"`, `"WaterAbundance"`, `"OxygenPercent"`, `"SurfaceRadiation"`).
- `IsTerraformable` — рабочий промежуточный флаг внутри генерации; после `CheckRaceConditions`
  всегда сбрасывается в `false` (терраформирование уже произошло, либо раса ушла).

UI (`PlanetUIController.RefreshOverview`, `ObjectInfoPopup.DrawPlanet`,
`GalaxyLogger.FormatPlanet`) рисует стрелки ↑/↓ рядом с теми параметрами, у которых
`current ≠ original`.

---

## 14. Режим исторической колонизации (Expansion)

Включается `Settings.Expansion == 1`. Не зависит от `CheckRaceConditions` — наоборот,
сначала сносит всю расовую привязку у непремейдовых звёзд.

```
1. Снимаем расу/владельца со всех планет НЕпремейдовых звёзд.
2. Для каждой расы:
     ColonizationStartDate / ColonizationSpeed → бюджет шагов
         years   = (InitialDate − StartDate).Years
         budget  = floor(years × ColonizationSpeed)
3. Расы сортируются по StartDate (кто раньше — тот раньше расселяется).
4. Frontier  = звёзды, на которых раса присутствует на premade-планетах.
5. Шаг колонизации (повторить budget раз):
     ищем ближайшую к фронтиру звезду в радиусе 12 → 16 → 20 → … → 40
       приоритет: своя «секторная» зона выше, чем чужая;
                  внутри зоны — habitable планета приоритетнее terraformable.
     если найдена → ExpansionColonizeStar:
         все планеты, проходящие habitable или terraformable, помечаются расой;
         для terraformable вызывается ExpansionApplyOptimal + IsTerraformed = true.
     добавляем звезду в фронтир и в ExpansionEdges (для отрисовки линии колонизации).
6. После всех рас:
     раса звезды = свёртка рас её планет;
     раса сектора = свёртка рас звёзд (если сектор не «fixed»).
```

Расы с флагом `NonPlanetary != 0` пропускаются — они не претендуют на планеты.

Линии колонизации (`ExpansionEdge`) хранят `RaceKey`, точки `From → To` и
`ColonizationYear` (вычисляется из `step / speed` + `StartDate`), используются галактической
картой для рендера истории расселения.

---

## 15. Фиксированные планеты (premade)

В `FixedPlanetData` (внутри `FixedStarData.Planets`) можно жёстко задать любую величину;
её JSON-поле кладётся в *Fixed-«двойник» внутри `PlanetData`:

| JSON поле              | Поле PlanetData                           | Используется в                                  |
|------------------------|-------------------------------------------|-------------------------------------------------|
| `SurfaceGravity`       | сразу в `SurfaceGravity`                  | `RollGravityDensity`                            |
| `Density`              | сразу в `Density`                         | `RollGravityDensity`                            |
| `GeoActivity`          | сразу в `GeoActivity`                     | `RollGravityDensity`                            |
| `TotalSurfaceArea`     | сразу в `TotalSurfaceArea`                | `RollGravityDensity`                            |
| `AtmPressure`          | `AtmPressureFixed`                        | перезаписывает результат формулы                |
| `OxygenPercent`        | `OxygenPercentFixed`                      | перезаписывает результат формулы                |
| `WaterAbundance`       | `WaterAbundanceFixed`                     | перезаписывает результат формулы                |
| `SurfaceTemp`          | `SurfaceTempFixed`                        | перезаписывает результат формулы                |
| `SurfaceLiquid`        | `SurfaceLiquidFixed`                      | подменяет `rawLiquid` в `RollSurfaceTypes`      |
| `SurfaceMountains`     | `SurfaceMountainsFixed`                   | подменяет `rawMountains` в `RollSurfaceTypes`   |
| `OrbitRadius`          | `OrbitRadius` + `HasFixedOrbitRadius`     | блокирует пересчёт в `AssignOrbitRadii`         |
| `OrbitIndex`, `Size`, `Race`, `Owner`, `OrbitSpeed`, `DaySpeed`, `CloudsSpeed`, `AxialTilt`, `SatellitesCount`, `Graphic`, `OrbitalObjects`, `Atmosphere` | как есть | задают исходное состояние                       |

> `*Fixed` поля помечены `[JsonIgnore]` — они не сохраняются в SaveGame, а только
> участвуют в одноразовой генерации. После генерации значения уже сидят в обычных полях
> `PlanetData`.

---

## 16. Инициализация населения и экономики

Не относится к физике, но завершает «жизненный портрет» планеты
(`ItemFactory.InitializeDynamicPlanetData`):

```
inhabited = (Race ≠ "None")

Government     = inhabited ? rand(Planets.GovernmentTypes.Keys) : GovernmentTypes.Keys[0]
                  (или "Anarchy" / "None", если конфига нет)

если inhabited:
    EconomyType = rand(Planets.EconomyTypes.Keys)  (или "Mixed")
    techCoef    = EconomyTypes[EconomyType].TechGrowthCoef
    TechLevel   = Clamp( round(rand(1..5) × techCoef), 1, 8 )
    Population  = rand(PlanetSizeRules[Size].Population[0 .. 1])
    Shop / EquipmentShop ← инициализируются торговой системой
```

Эти величины **не возвращают расу** на необитаемую планету — наличие/отсутствие расы
определено заранее в `CheckRaceConditions` либо в `RunExpansion`.

---

## 17. Граф зависимостей физики

```
MassSolar, RadiationMult ──┐
OrbitRadius × HZ_SizeMult ─┤
                           ▼
                       lStar, dRatio
                           │
                   ┌───────┴────────┐
                   ▼                ▼
               SolarFlux        baseTemp
                   │                │
     ┌─────────────┼────────┐       │
     ▼             ▼        │       │
 MagField     AtmPressure   │       │
     │              │       │       │
     │         ┌────┘       │       │
     ▼         ▼            ▼       ▼
 SurfaceRadiation       WaterAbundance
                              │
                    ┌─────────┴──────────┐
                    ▼                    ▼
               SurfaceTemp          OxygenPercent
                    │                    │
                    ▼                    ▼
               HydrologyState        partialO2
                    │
                    ▼
               SurfaceLiquid / Mountains / Plains

Density, SurfaceGravity, SatellitesCount ──► GeoActivity
                                            └─► MagField, AtmPressure, WaterAbundance

PlanetConditions(Race)  +  {WaterAbundance, partialO2, AtmPressure, SurfaceRadiation,
                            SurfaceGravity, SurfaceTemp}  ──►  Habitable / Terraformable
                                                                     │
                                                                     ▼
                                                       ExpansionApplyOptimal
                                                                     │
                                                                     ▼
                                                       IsTerraformed,
                                                       TerraformOriginals
```
