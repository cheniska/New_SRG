# Архитектура: слои, сборки, правила

Документ описывает, как код разделён на слои, как слои общаются и какие правила
проверяются автоматически. Детали отдельных подсистем — в остальных файлах `modules/`.

---

## Сборки (asmdef)

```
SRG.Simulation   ← SRG.Presentation ← SRG.Game
      ↑        ↖                          ↙
      │         SRG.UI.Logic ←───────────
SRG.Tests.EditMode (→ SRG.Simulation, SRG.UI.Logic)
```

| Сборка | Папки `Assets/Scripts` | Что внутри | Может ссылаться на |
|---|---|---|---|
| **SRG.Simulation** | `Simulation` (asmdef), `Combat`, `Config`, `Economy`, `Equipment`, `Galaxy`, `NpcAI`, `Ships`, `Science`, `Dialog`, `Scripting`, `Utils` (через `.asmref`) | Данные мира, генерация, расчёт хода, ИИ, экономика, диалоговая логика, Lua | UnityEngine (математика, `Debug`), Newtonsoft, MoonSharp. **Не** UGUI, не Presentation/Game |
| **SRG.Presentation** | `Presentation` | Визуальные контроллеры объектов, эффекты, камера, миникарты, графика | SRG.Simulation, UGUI |
| **SRG.UI.Logic** | `UI/Logic` | Презентеры экранов: строки магазинов, цены, проверки, тексты и цвета — без MonoBehaviour и UGUI | SRG.Simulation |
| **SRG.Game** | `Core` (asmdef), `Controllers`, `UI` (через `.asmref`) | Менеджеры сцены, игрок/NPC-контроллеры, все экраны | всё выше |
| **SRG.Tests.EditMode** | `Assets/Tests/EditMode` | EditMode-тесты | SRG.Simulation, SRG.UI.Logic, NUnit |

Папки подключены к сборкам через `.asmref`, поэтому пути файлов и namespace'ы не менялись.
Нарушение слоя — ошибка компиляции, а не замечание на ревью.

UI, Core и Controllers живут в одной сборке, потому что ссылаются друг на друга
(экраны читают `PlayerShip`, а `PlayerShip` показывает HUD). Их разделение — следующий шаг,
если понадобится.

### Корабль игрока: `PlayerPilot` и `PlayerShip`

Команды игрока исполняет `Ships/Player/PlayerPilot` — обычный класс в сборке симуляции, без
MonoBehaviour: маршрут из точек (с отслеживанием движущейся цели), следование за кораблём в 7
режимах (`PlayerFollowMode`) с автоогнём, абордажем, буксиром и стыковкой, посадка, червоточины,
очередь захвата, ручной выстрел, форсаж и доведение маршрута по итогам хода (`OnTurnCompleted`).
Мир — через `GameWorld`, поэтому пилот покрыт headless-тестами (`PlayerPilotTests`).

`Controllers/PlayerShip` — компонент в сцене: читает ввод (клавиши, режимы наведения и связи,
объект под курсором), переводит клики в команды `Pilot`, анимирует ход и реализует
`IPlayerPilotView` — то, что пилот просит показать: маршрут, посадку/взлёт, сообщения HUD.
Внешний код обращается к командам через `PlayerShip.Instance.Pilot`.

Логику экранов выносим в `SRG.UI.Logic` (презентеры): она тестируется без сцены, а экран
только строит виджеты и пишет результат в лог. Подробнее — [ui_subsystem.md](ui_subsystem.md#презентеры-srguilogic).

---

## Как симуляция общается с остальной игрой

Симуляция не знает о MonoBehaviour-менеджерах и UI. Всё, что ей нужно снаружи, описано
интерфейсами в `Simulation/HostInterfaces.cs` и доступно через статический фасад
`SRG.Simulation.GameWorld`:

| Интерфейс | Реализует | Что даёт симуляции |
|---|---|---|
| `IWorldHost` | `GalaxyManager` / `HeadlessWorldHost` | активная/тикающая галактика, контекст, текущая звезда, фаза, `RequestPlanning`, `ExecuteInstantTurn` |
| `IPlayerHost` | `PlayerManager` | корабль игрока, посадка/взлёт, смерть, «следую за» |
| `IViewHost` | `SystemViewManager` | создать визуал для корабля, появившегося посреди хода |
| `IDialogPresenter` | `DialogUIController` | входящий вызов на связь от NPC, окно улучшения |

- Хосты регистрируются сами: `GameWorld.Attach(this)` в `Awake`, `Detach` в `OnDestroy`.
- Любой хост может отсутствовать (тесты, headless) — фасад возвращает значения по умолчанию.
- События хода (`OnTurnCalculate`, `OnTurnAnimate`, `OnTurnComplete`) — в `GameWorld`;
  поднимает их хост через `GameWorld.Raise*`.
- Лог симуляции — `SRG.Utils.GameLog.Add(...)`; консоль подписана на `GameLog.OnEntry`.

Графику симуляция не читает вовсе (ни `Resources`, ни `Sprite`/`Texture2D`): всё, что ей нужно
знать об ассетах, лежит в данных — каталоге графики `Config/GraphicsCatalog.json`
(`SRG.Simulation.GraphicsCatalog`). См. раздел «Графика как данные» ниже.

Presentation так же не знает о Game: аватар игрока, запросы к визуалу системы и флаг
«ввод занят консолью» приходят через `SRG.Presentation.Common.PresentationContext`.

**Composition root** — `Core/GameBootstrap` (`RuntimeInitializeOnLoadMethod`): регистрирует то,
что не привязано к объекту сцены (резолвер графики корпусов, Lua-API верхнего слоя, флаг ввода).

---

## Состояние мира и ход

- `SimulationSetup.CreateContext(ConfigSources, settings)` — парсит JSON-конфиги, инициализирует
  константы и создаёт объекты сессии (`OwnerRaceRelationsManager`, `DirectiveManager`,
  `HighCommandRegistry` — обычные классы, по одному на сессию).
- `SimulationSession` — все галактики, генерация (`GenerateAll`), загрузка (`LoadFrom`),
  расчёт дня (`SimulateDay`), финализация гиперпрыжков. При новой игре и загрузке сбрасывает
  статические кэши, привязанные к объектам прошлого мира.
- `GalaxyManager` — фазы Planning/Simulation, тайминг анимации, ввод, сценовые гиперпереходы,
  оркестрация новой игры/загрузки. Прогрев графики — `Presentation.Common.VisualPreloader`.
- `HeadlessWorldHost` — тот же ход без сцены (тесты, утилиты).

---

## Графика как данные

Геймплей не зависит от того, чем и как рисуется мир (2D-спрайты, 3D-модели, headless без графики).
Там, где решение симуляции опирается на ассеты, она берёт их описание из каталога
`Config/GraphicsCatalog.json`, а не из самих ассетов:

| Что | API | Используется |
|---|---|---|
| Ширина кадра листа в мире | `GraphicsCatalog.FrameWorldSize(path)` | `ShipData.SpriteWorldSize` → дистанции стыковки, абордажа, остановки, выхода из гипера |
| Есть ли лист / картинка | `ContainsSheet` / `ContainsImage` | Owner/Race-варианты корпуса (`ShipFactory.ResolveHullSheetPath`), иконка звезды на миникарте |
| Листы / картинки папки | `SheetsInFolder` / `ImagesInFolder` | варианты атласов астероидов, случайные текстуры планет, спутников, орбиталок |

- Каталог генерирует `tools/graphics-catalog/gen_graphics_catalog.py` по `Assets/Resources/Graphics`.
  Ширина листа — **после импорта в Unity** (с учётом `maxTextureSize` из `.meta` и правил
  `Editor/MyTexturePostprocessor`), то есть ровно то, что раньше давал `Sprite.rect.width`.
- `GraphicsCatalogTests.Catalog_MatchesResources` падает, если каталог не перегенерировали после
  добавления, удаления или замены картинки.
- Каталог грузится в `SimulationSetup.CreateContext` (`ConfigSources.GraphicsCatalog`); в сцене —
  поле `graphicsCatalogJson` у `GalaxyManager`.
- Перечисление папки — только прямые потомки (без вложенных папок), по имени (Ordinal, IgnoreCase).

Следствие: расчёт хода не ждёт главный поток ради графики, а headless-прогон (тесты, бенчмарк)
генерирует ту же галактику и считает те же дистанции, что игра.

---

## Выбор объектов курсором

Клик, наведение и контекстное меню находят объект **по данным мира**, а не по коллайдерам визуала,
поэтому работают при любом рендере (2D-спрайты, 3D-модели):

1. `Presentation.Common.WorldPointer.TryScreenToWorld(cam, screenPos)` — луч камеры пересекается с
   плоскостью системы z = 0. Одинаково для ортографической и перспективной камеры.
2. `Galaxy.WorldPicker.Pick(star, point, config, settings)` — чистая функция над `StarData`:
   круг вокруг позиции из данных, радиус = 0.5 × масштаб объекта (звезда — `GraphicSizeMult` типа,
   планета — `BaseScale` размера, корабль — `HullSizeMultiplier`, астероид —
   `clamp(2·CollisionRadius, 0.2, 1.5)`, червоточина — `Wormhole_WorldSize`). Это те же размеры,
   что у визуала.
3. Результат — `WorldPick` (`Kind` + ссылка на данные: `Ship`/`Asteroid`/`Wormhole`(+`WormholeStar`)/`Planet`/`Star`).

- Перекрытие: выигрывает то, что рисуется поверх — корабль → астероид → червоточина → планета →
  звезда; внутри вида — ближайший к центру.
- Не выбираются: севшие на планету/носитель, погибшие, корабли в HyperArrive (невидимы),
  уничтоженные астероиды.
- Позиции — на конец хода: ввод игрока и попапы работают только в фазе планирования, когда
  визуал стоит ровно на позициях из данных.
- `Physics2D`/`Collider2D` в коде не используются (`ArchitectureTests.Scripts_DoNotUse2DPhysics`);
  поведение выбора покрыто `WorldPickerTests`.

---

## Детерминизм

Один и тот же сид и одни и те же действия игрока дают одно и то же состояние мира.

| Правило | Почему |
|---|---|
| Случайность в симуляции — только `GameRng` | `UnityEngine.Random` делится с визуалом: частицы и фон сдвигали последовательность |
| UID — `GameRng.NewUid()`, не `Guid.NewGuid()` | от UID зависят сиды, слоты и порядок словарей |
| Сид из строки — `StableHash.Of`, не `GetHashCode()` | `string.GetHashCode` рандомизирован в .NET Core и разный в Mono/IL2CPP |
| Состояние, влияющее на игру, — в `GalaxyData`/`ShipData`, не в `static` | статическое не сохраняется и протекает между сессиями |
| Порядок обхода — по структуре данных (сектора → звёзды) | одинаков после генерации и после загрузки |
| Данные об ассетах — из `GraphicsCatalog`, не из `Resources` | иначе выбор варианта и размеры зависят от наличия графики (headless ≠ игра) |

Визуал (`Presentation`) свободно пользуется `UnityEngine.Random`.

Правила проверяет `ArchitectureTests`, поведение — golden-тесты `SimulationDeterminismTests`
(см. [testing.md](testing.md)).

---

## Сохранения

Формат, версии и миграции — `Simulation/SaveSerializer`, файловый слой — `Core/GalaxySaveManager`.
Подробно — [saves.md](saves.md).
