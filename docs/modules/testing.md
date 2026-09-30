# Тестирование

EditMode-тесты — `Assets/Tests/EditMode`, сборка `SRG.Tests.EditMode` (ссылается только на
`SRG.Simulation`). Запуск: Unity → Window → General → Test Runner → EditMode → Run All,
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
| `SimulationDeterminismTests` | **golden**: генерация и N ходов при одном сиде дают одинаковое состояние (SHA-256 всего мира); сохранение → загрузка сохраняет состояние |
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
