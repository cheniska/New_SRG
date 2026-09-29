# Graphics Pipeline

Единая точка загрузки и кэширования графики. Всё, что рисует спрайт, обязано ходить через `GraphicsManager` — прямые `Resources.Load<Sprite>` в бизнес-коде запрещены (кроме шейдеров и TextAsset-конфигов).

Модули: `Presentation/Common/GraphicsManager.cs`, `Presentation/Effects/{SheetHandle, FrameStepper, AnimatedSpriteRenderer, ExplosionPlayback}`.

---

## Зачем это нужно

До унификации в проекте было **35+ прямых вызовов** `Resources.Load*`, три разных API для загрузки листов (`GetSpriteSheet` / `GetAsteroidVisuals` / `Resources.LoadAll<Sprite>` fallback), и три семантики путей в конфигах (папка / прямой лист / папка-с-вариантами). Из-за этого:

- Одна и та же иконка планеты грузилась заново на каждом кадре карты (Minimap: 7 разных `Resources.Load` без кэша).
- Взрыв ракеты был битый: пробегал 79 кадров листа в лексикографическом порядке `0,1,10,11,…,19,2,20,…` — мельчил в течение 6 секунд.
- Прогрев кэша при старте был раскидан по трём методам (`ExplosionPlayback.Preload`, `PreloadCombatSprites`, ad-hoc `Resources.Load` в спавне).

После сведения: **один менеджер, один pipeline загрузки, один прогрев, одна семантика путей.**

---

## Обзор

```
                       ┌──────────────────────────────────────────┐
    Callers ──────────►│ GraphicsManager (singleton, DontDestroy) │
    (Visual/UI/Combat) └──────────────────────────────────────────┘
                                       │
        ┌──────────────┬────────────────┼─────────────────┬──────────────┐
        ▼              ▼                ▼                 ▼              ▼
   GetSprite      TryGetSprite     GetSpriteSheet    GetSheetHandle   PreloadSheets
   (loud miss)    (silent miss)    (Sprite[])        (SheetHandle)    (batch warm-up)
        │              │                │                 │              │
        └───┬──────────┘                └────────┬────────┘              │
            ▼                                    ▼                       │
   _singleSpriteCache                   _spriteSheetCache                │
   _missingSprites                      _missingSheets                   │
   (positive + negative)                (positive + negative)            │
            │                                    │                       │
            └────────────► Resources.Load ◄──────┴───────────────────────┘
                          (main-thread, sync)
```

Потребители-анимации получают из GraphicsManager `SheetHandle` — пакет `(Sprite[] Frames, float SecPerFrame)` — и крутят его через `FrameStepper`:

```
   GraphicsManager.GetSheetHandle ──► SheetHandle ──┬──► AnimatedSpriteRenderer (loop)
                                                    └──► ExplosionPlayback     (one-shot)
                                                          └── FrameStepper.Advance(dt)
```

---

## Формат ассетов

Каждый спрайт-лист = **пара файлов** в `Resources/`:

| Файл                            | Что                                                                        |
| ------------------------------- | -------------------------------------------------------------------------- |
| `.../foo.png`                   | Sliced-текстура (Multiple sprite mode).                                    |
| `.../foo.png.json`              | Сопроводительная мета: `{ "FrameCount": N, "Cols": C, "Rows": R }`.        |

Unity видит `.json` как `TextAsset`, а `.png` как `Texture2D` — оба доступны через `Resources.Load`. `GraphicsManager.LoadSpriteSheet` читает мету, режет `Texture2D` на `Sprite.Create` построчно (row-major, `SpriteMeshType.FullRect`), возвращает `Sprite[]` в правильном индексном порядке.

**Путь в коде — всегда Resources-относительный БЕЗ расширения**: `"Graphics/Effects/Explosions/Asteroids/Default/001"`, а не `"…/001.png"` и не `"…/Default"`.

Если пары нет — `LoadSpriteSheet` пишет `LogError` с указанием, каких файлов не хватает, и возвращает `null`. **Silent fallback был удалён намеренно** — раньше он маскировал битую мету + давал лексикографическую сортировку.

---

## API

### Одиночные спрайты

```csharp
// Обязательный — при промахе LogError (один раз на путь).
Sprite GetSprite(string path);

// Опциональный — при промахе тишина. Для fallback-цепочек и опциональных иконок.
Sprite TryGetSprite(string path);
```

Правило: **`GetSprite`, если путь обязан быть валиден** (крашится дизайн); **`TryGetSprite`, если путь опционален** (можно и без картинки).

### Листы

```csharp
// Raw массив кадров. Годится, когда caller сам знает fps.
Sprite[] GetSpriteSheet(string path);

// Пакет frames+secPerFrame. Каноничный вариант для анимаций.
SheetHandle GetSheetHandle(string path, float fps = 12f);
```

`SheetHandle`:
```csharp
public readonly struct SheetHandle {
    public readonly Sprite[] Frames;
    public readonly float SecPerFrame;
    public int FrameCount { get; }
    public bool IsValid { get; }
    public static readonly SheetHandle Empty;
}
```

Проверка: `if (!sheet.IsValid) return;` — короче, чем `frames != null && frames.Length > 0`.

### Прогрев

```csharp
// Загружает пачку листов в кэш до первого их показа. Обычно вызывается один раз
// из GalaxyManager.PreloadAllVisuals по HashSet<string> всех известных путей.
void PreloadSheets(IEnumerable<string> paths);
```

Единичные спрайты (`GetSprite`) прогревать не нужно — `Resources.Load<Sprite>` для несклеенного спрайта дёшев.

### Специфичные вызовы

- `GetStarVisuals(color, graphVariant)` — резолвит путь через `GalaxyConfig.Stars.Colors[color].Path + "/Var" + variant` и вызывает `LoadSpriteSheet`. Живёт отдельно, потому что путь конструируется из конфига.
- `GetStarScale(typeName)` — не грузит графику, читает `GraphicSizeMult` из конфига.
- `EnumerateSheetPathsInFolder(folderPath)` — сканирует папку и возвращает Resources-пути ко всем найденным листам (по `.png.json`-метам). **Единственный легитимный случай папочной семантики графики** — категория с многими равнозначными вариантами (астероиды: Rocky/00..14, Metallic/Blue00..Blue13). Не создавайте свой folder-scan; используйте этот метод.
- `ClearCache()` — вайпает всё (позитив/негатив/оба кэша + folder-scan кэш) + `Resources.UnloadUnusedAssets()`. Используется в редких случаях (переход между сессиями).

### Кэши

Четыре словаря на инстансе:

| Кэш                    | Что хранит                    | Когда чистится                         |
| ---------------------- | ----------------------------- | -------------------------------------- |
| `_singleSpriteCache`   | `Sprite` по path (позитив)    | `ClearCache()`                         |
| `_spriteSheetCache`    | `Sprite[]` по path (позитив)  | `ClearCache()`                         |
| `_missingSprites`      | путь → нет спрайта (негатив)  | `ClearCache()`                         |
| `_missingSheets`       | путь → нет листа (негатив)    | `ClearCache()`                         |

Негативный кэш КРИТИЧЕН для hot-path (WeaponVisualSystem, миникарта): без него опциональный не-найденный путь бил бы по `Resources.Load` на каждом кадре и логировал `LogError` бесконечно.

---

## Проигрывание анимаций

`FrameStepper` — общий покадровый счётчик:

```csharp
public struct FrameStepper {
    public Sprite[] Frames;
    public float SecPerFrame;
    public bool Loop;            // true = зациклить, false = один раз
    public int CurrentFrame;
    public bool Finished;        // взводится в конце oneShot
    // ...
    public FrameStepper(SheetHandle sheet, bool loop);
    public Sprite CurrentSprite { get; }
    public bool Advance(float dt);   // true = CurrentFrame изменился
}
```

Используют:

- **`AnimatedSpriteRenderer`** (loop): циклит лист по кругу. Прикрепляется к любому GameObject с SpriteRenderer.
  ```csharp
  var sheet = GraphicsManager.Instance.GetSheetHandle(path, fps);
  go.AddComponent<AnimatedSpriteRenderer>().Init(sheet, sr);
  ```

- **`ExplosionPlayback`** (one-shot): доигрывает лист до последнего кадра и сносит `gameObject`. Умеет два режима запуска:
  ```csharp
  // In-place: забирает SpriteRenderer у существующего объекта.
  ExplosionPlayback.PlayOn(missileGO, explosionPath);

  // Standalone: создаёт временный GO под parent (для игрока — он DontDestroyOnLoad).
  ExplosionPlayback.SpawnAt(SystemContainer, position, explosionPath);
  ```

`FrameStepper.Advance` использует `floor(timer / secPerFrame)` — при низком framerate не пропускает кадры.

---

## Прогрев при старте

`GalaxyManager.PreloadAllVisuals()` — единая точка, вызывается после `GalaxyNextDay` при генерации/загрузке. Собирает все известные sheet-пути в `HashSet<string>` (взрывы астероидов и корабельные, корпуса всех hull-типов × всех рас, ракеты и sprite-снаряды оружия, астероиды-варианты, гиперпорталы), потом одним `gm.PreloadSheets(sheets)` прогревает. Отдельно вызывает `GetStarVisuals` для уникальных `(Color, GraphVar)` из уже сгенерированной галактики.

Без прогрева первый показ каждого типа даёт **spike 30–700 мс** на главном потоке (парсинг JSON + `Sprite.Create` × N кадров).

---

## Пути в конфигах

Три места конфигурируют графику:

1. **JSON**: `AsteroidTypeConfig.GraphicPath` (папка вариантов), `ExplosionPath` (лист), `PlanetData.MaskGraphic` (спрайт), `ShipData.MinimapIconPath` (спрайт), и т.п. — определены в `GalaxyConfigurationModels.cs`.
2. **`GameSettingsConfig`** (`.asset` в Inspector): `ExplosionSpritePath`, `AsteroidMinimapIconPath`, `Wormhole_IconPath` и т.д. — Unity Inspector.
3. **`GalaxyConstants`** — код-константы для fallback (`EXPLOSION_SPRITE_PATH`, `ASTEROID_SPRITES_PATH`).

**Договор**: путь = Resources-относительный БЕЗ расширения. Для листов — путь к самому листу, не к папке. Для одиночных спрайтов — путь к спрайту.

**Исключение — папка-с-вариантами**: `AsteroidTypeConfig.GraphicPath = "Graphics/SpaceObjects/Asteroids/Rocky"` — папка, внутри которой 15 листов `00.png`..`14.png` с мета-JSONами. Сканирование делает `GraphicsManager.EnumerateSheetPathsInFolder` — единая точка для этого паттерна (`AsteroidSystem` только зовёт её, никакого локального folder-scan). Для новой графики предпочтительно указывать явные пути к листам; папку — только когда вариантов много и они равнозначны.

---

## Как добавить новый визуал (пошагово)

### Случай A: одиночный спрайт (иконка, статичный декор)

1. Положить `foo.png` в `Assets/Resources/Graphics/<категория>/foo.png`.
2. В конфиге/коде указать путь `"Graphics/<категория>/foo"` (без расширения).
3. В коде: `var sprite = GraphicsManager.Instance.GetSprite(path);` (или `TryGetSprite` если опционально).

### Случай B: анимированный лист (взрыв, эффект, hull-анимация)

1. Положить `foo.png` (multiple-sprite mode, sliced) в `Assets/Resources/Graphics/<категория>/foo.png`.
2. Рядом создать `foo.png.json` с содержимым `{"FrameCount": N, "Cols": C, "Rows": R}` (N ≤ C*R).
3. Убедиться, что в `.png.meta` стоит `spriteMode: 2` (Multiple) и `textureType: 8` (Sprite). Проверить в Inspector Unity.
4. Прогрев: добавить путь в сборщик `GalaxyManager.PreloadAllVisuals` (в подходящую секцию — взрывы/ракеты/корпуса/астероиды/эффекты).
5. Использование:
   ```csharp
   var sheet = GraphicsManager.Instance.GetSheetHandle(path, fps);
   if (!sheet.IsValid) return;   // GraphicsManager уже залогировал ошибку
   // Loop:
   go.AddComponent<AnimatedSpriteRenderer>().Init(sheet, sr);
   // Один раз до конца:
   ExplosionPlayback.PlayOn(go, path);
   ```

### Случай C: категория с многими вариантами (типа Rocky/00..14)

Не изобретайте свой folder-scan. Либо (a) сложите варианты в отдельную папку и используйте `GraphicsManager.EnumerateSheetPathsInFolder(folder)`, либо (b) в JSON перечислите список путей явно (`GraphicPaths: string[]`). Вариант (b) предпочтителен — папка без сканирования не даёт визуальной неопределённости «какие варианты сейчас в билде».

### Случай D: нужен новый тип анимации (не loop, не one-shot)

1. Создать компонент, взять `FrameStepper` полем.
2. `Init(SheetHandle sheet, bool loop)`.
3. В `Update`: `if (_step.Advance(Time.deltaTime)) _sr.sprite = _step.CurrentSprite;`.
4. Если нужен произвольный порядок кадров (ping-pong, случайная выборка) — не расширяйте `FrameStepper`, а сделайте отдельный компонент. `FrameStepper` заведомо простой.

---

## Правила расширения (что НЕ ломать)

1. **Никаких прямых `Resources.Load<Sprite>` / `Resources.LoadAll<Sprite>` в бизнес-коде.** Разрешено только в:
   - `GraphicsManager` (сам менеджер).
   - Специфичных folder-scan (`AsteroidSystem.GetVariantNames`, `ShipGraphicsResolver`, `GalaxyUtils` для генерации).
   - Шейдеров (`Resources.Load<Shader>`).
   - TextAsset конфигов (`Resources.Load<TextAsset>`).

   Проверить: `git grep 'Resources\.Load' Assets/Scripts/` — новых `Sprite`-загрузок вне списка выше быть не должно.

2. **Никаких локальных Sprite-кэшей в компонентах.** Они дублируют `GraphicsManager` и рассинхронизируются при `ClearCache`. Если хочется закэшировать промах — GraphicsManager уже делает это через `_missingSprites`/`_missingSheets`.

3. **Пути — всегда без расширения**, всегда Resources-относительные. `NormalizePath` в GraphicsManager стрипает `.png` если случайно передали — но не рассчитывайте на это в новом коде.

4. **Мета-JSON обязателен для листов.** Silent fallback удалён — если поставите лист без `.png.json`, получите `LogError` при первой загрузке. Это фича, не баг: раньше отсутствие меты приводило к сортировке спрайтов лексикографически и битой анимации.

5. **`GetSprite` vs `TryGetSprite` — выбирайте осознанно.** `GetSprite` = «путь обязан быть валиден, если нет — это баг конфига». `TryGetSprite` = «путь может отсутствовать, это норма». Не глушите LogError оборачиванием `GetSprite` в try — это заглушит настоящие баги.

6. **Прогрев — только в `PreloadAllVisuals`.** Не делайте свои Preload-методы. Добавляйте пути в общий `HashSet<string>` в подходящей секции.

7. **Не расширяйте `SheetHandle` полями сверх `(Frames, SecPerFrame)`.** Это value-type — рост поля стоит копирования на каждом `Init`. Если нужны доп. данные о листе (кол-во колонок и т.п.) — читайте их из JSON отдельно.

8. **Не мутируйте `FrameStepper` вне `Advance`.** `CurrentFrame`/`Finished`/`Loop` — public для читабельности, но менять их в середине воспроизведения — путь к сюрпризам.

---

## Диагностика

| Симптом                                                       | Причина                                                              | Куда смотреть                                                                    |
| ------------------------------------------------------------- | -------------------------------------------------------------------- | -------------------------------------------------------------------------------- |
| `[GraphicsManager] Sheet load failed for '…': meta MISSING`   | Нет `.png.json` рядом с листом.                                      | Создать мету, проверить путь.                                                    |
| `[GraphicsManager] Sheet load failed for '…': texture MISSING` | Путь указывает не туда или PNG забыт.                                | Grep путь в конфиге; проверить `Assets/Resources/…`.                             |
| `[GraphicsManager] Resource not found: …` (одиночный)          | `GetSprite` вернул null; сработает 1 раз потом молча.                | Проверить путь; если промах нормален — заменить на `TryGetSprite`.               |
| Анимация мельчит, порядок кадров битый                        | Уже нельзя (fallback удалён), но если увидите — грепайте `LoadAll`. | В `graphics_pipeline` LoadAll быть не должно.                                    |
| Первый показ типа лагает на 100+ мс                           | Пропущен прогрев.                                                    | Добавить путь в `GalaxyManager.PreloadAllVisuals`.                               |
| Промах повторно логируется                                    | Кто-то нашёл дыру в missing-кэше.                                    | Проверить, не проходит ли путь через нестандартный вход (напрямую `Resources.Load`). |

`PerfLog` пишет `[GfxLoad] path=… meta=X tex=Y build=Z frames=N` для листов, чья загрузка заняла ≥ 20 мс. Смотрите `logs/perf_log.txt` — это индикатор пропущенных прогревов.

---

## История

- Апрель 2026 (первый рефакторинг): выделен `ExplosionPlayback` и `AnimatedSpriteRenderer`, введён `.png.json` для листов через `TryBuildFromSpriteSheet`.
- Июль 2026: полная унификация — снесены дубли-обёртки в GraphicsManager, введены `SheetHandle`/`FrameStepper`, все callers переведены на GraphicsManager, удалён `Resources.LoadAll<Sprite>` fallback, введено negative-caching, все пути к листам приведены к «прямой путь без расширения». Причина — баг с мерцающими взрывами ракет (лексикографическая сортировка 79-кадрового листа) вскрыл беспорядок в графической подсистеме.
