# Тестирование

EditMode-тесты — `Assets/Tests/EditMode`, сборка `SRG.Tests.EditMode` (ссылается только на
`SRG.Simulation` и `SRG.UI.Logic`). Запуск: Unity → Window → General → Test Runner → EditMode → Run All,
или из командной строки:

```
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml
```

Без Unity (нужен только .NET SDK 8) — тот же набор тестов поверх управляемого заменителя
UnityEngine, см. [tools/headless-tests](../../tools/headless-tests/README.md):

```
dotnet test tools/headless-tests
```

CI (`.github/workflows/ci.yml`): job `headless-tests` на каждый push/PR; job `unity-editmode`
(GameCI, настоящий Unity) — когда в секретах репозитория заданы `UNITY_LICENSE`,
`UNITY_EMAIL`, `UNITY_PASSWORD`.

---

## Что покрыто

| Файл | Что проверяет |
|---|---|
| `GameRngTests` | детерминизм потока, семантика `Range`/`Value` как у `UnityEngine.Random`, сохранение состояния, UID |
| `StableHashTests` | эталонные значения FNV-1a |
| `SaveTypeBinderTests` | чтение старых имён типов, отказ чужим типам в `$type` |
| `SaveFormatTests` | миграции v0→v2, v1→v2, отказ на будущей версии и битом файле |
| `EconomyTests` | инфляция, легальность товаров, базовый сток |
| `NewsServiceTests` | Id новостей после загрузки |
| `SimulationDeterminismTests` | **golden**: генерация и N ходов при одном сиде дают одинаковое состояние (SHA-256 всего мира); сохранение → загрузка сохраняет состояние; `SaveLoad_ContinuesIdentically` — игра, загруженная из сейва, дальше идёт день в день так же, как без сохранения |
| `AiStateSerializationTests` | решения ИИ (`NpcBrain`, действия, приказы) переживают сохранение; ссылки на объекты мира в состоянии ИИ помечены `[NonSerialized]`; инкрементальные счётчики спавна совпадают с полным пересчётом |
| `SaveSchemaTests` | формат сохранения совпадает с эталоном `SaveSchema.txt` (см. [saves.md](saves.md)) |
| `UiPresenterTests` | логика экранов без UI: магазины товаров и оборудования, ангар, описание предмета, цвета карты галактики |
| `ArchitectureTests` | в симуляции нет `UnityEngine.Random`, `Guid.NewGuid`, `new System.Random()`, `Uid.GetHashCode()` |

`SimulationDeterminismTests` помечены категорией `Slow` (генерируют настоящую галактику из
`Assets/Config`, ~10–20 с).

## Инструменты

- `TestWorld` — headless-мир: реальные конфиги, генерация по сиду, `RunDays(n)`, снимок в
  формате сейва, хэш состояния.
- `FakeWorldHost` — минимальный `IWorldHost` для юнит-тестов.
- Путь к `Assets` определяется автоматически; можно задать явно переменной `SRG_ASSETS_DIR`.

## Если golden-тест упал

1. `Generation_IsReproducible` / `Simulation_IsReproducible` — появился источник недетерминизма:
   `UnityEngine.Random`/`Guid`/`GetHashCode` в симуляции, зависимость от времени кадра, статическое
   состояние, переживающее сессию, обход `HashSet`/`Dictionary` с ключами-объектами.
2. `SaveLoad_RoundTrip_PreservesState` — новое поле, влияющее на игру, не сериализуется
   (`[JsonIgnore]`/`static`) или пересчитывается при загрузке.
3. `SaveLoad_ContinuesIdentically` — после загрузки мир расходится с исходным: не сохраняется
   состояние, влияющее на следующие ходы (решения ИИ, поля кораблей), кэш после загрузки
   расходует общий RNG, или случайный выбор идёт в порядке обхода `Dictionary`
   (после удалений порядок у загруженного словаря другой — сортируйте ключи).
4. `SaveSchemaTests` — поменялся формат сохранения. Если так и задумано — добавьте миграцию
   (если старые сейвы иначе не прочитаются) и обновите эталон: `SRG_UPDATE_SAVE_SCHEMA=1 dotnet test tools/headless-tests`.
