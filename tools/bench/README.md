# bench

Headless-бенчмарк хода симуляции без Unity: генерирует мир по сиду из реальных конфигов
(`Assets/Config`) и прогоняет N ходов через `HeadlessWorldHost`, как `tools/headless-tests`.

```
dotnet run -c Release --project tools/bench -- [days=100] [seed=20260930] [warmup=5]
```

Выводит время генерации, среднее/p50/p95/max времени хода, аллокации на ход, число сборок GC
и хэш состояния. Хэш должен совпадать до и после оптимизации, если она не меняет правила игры.

Профиль (CPU-сэмплы) без Unity:

```
dotnet tool install -g dotnet-trace
dotnet build -c Release tools/bench
dotnet-trace collect --format Speedscope -- tools/bench/bin/Release/net8.0/Bench 100
```

Полученный `*.speedscope.json` открывается на https://www.speedscope.app.
