# headless-tests

Прогон EditMode-тестов симуляции без Unity:

```
dotnet test tools/headless-tests
```

Нужен .NET SDK 8. Проект компилирует исходники сборки `SRG.Simulation`
(`Assets/Scripts/{Simulation,Combat,Config,Economy,Equipment,Galaxy,NpcAI,Ships,Science,Dialog,Scripting,Utils}`)
и тесты `Assets/Tests/EditMode` вместе с `UnityShim.cs` — управляемым заменителем той части
`UnityEngine`, которой пользуется симуляция (`Vector2`, `Mathf`, `Debug`, атрибуты инспектора и т.п.).

- Используется в CI (`.github/workflows/ci.yml`, job `headless-tests`) — работает без лицензии Unity.
- Эталон — Unity Test Runner (job `unity-editmode`); этот прогон его дополняет. Математика shim
  повторяет семантику Unity, но это не движок: тесты, завязанные на рендер/сцену, сюда не подходят.
- Если симуляция начала использовать новый API `UnityEngine` и сборка упала — добавьте его в
  `UnityShim.cs` (или, лучше, подумайте, нужен ли он симуляции).
- Подробный лог симуляции: `SRG_TEST_VERBOSE=1 dotnet test tools/headless-tests`.
