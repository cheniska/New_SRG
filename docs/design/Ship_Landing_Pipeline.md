# SHIP LANDING PIPELINE — Rangers.exe (SR2 HD)

> Диздок перенесён из `Ship_Landing_Pipeline.md` без правок текста: разделы оформлены заголовками,
> содержимое — дословно, моноширинным блоком (в исходнике — ASCII-вёрстка).

```text
Дата: 2026-06-25 (rev 2 — 2026-06-25 правки по проверке)
Источник: декомпиляция через IDA MCP (ida_mcp/zeromcp на 127.0.0.1:13337)
          + Rangers.exe.i64 структуры TShip / TPlanet / THole / TRuins
            (cross-checked через type_inspect)
          + Articles/Hyperjump_Pipeline.txt
          + Articles/Ship_Trajectory_Reimplementation.md (rev 2)
          + Articles/Ship_Trajectory_FollowModes_Recheck.md

История правок:
  rev 1 (2026-06-25 первая): полный разбор по результатам IDA-сессии.
  rev 2 (2026-06-25 пост-проверка):
    • Исправлены смещения THole: Star1 +0x08 (был ошибочно +0x2C),
      Pos1 +0x0C, Star2 +0x14 (был +0x30), Pos2 +0x18, OpenTurn +0x20,
      Status +0x24, Graph +0x28.
    • Удалена ошибочная пометка «OrbitRadius на самом деле в +0x140
      GraphRadius». Реально: OrbitRadius @ +0x28 (часть PolarPos),
      GraphRadius @ +0x140 — два РАЗНЫХ поля.
    • Уточнена семантика OrderData=0xFFFF0000 для JUMP_HOLE:
      HIWORD=0xFFFF + LOWORD=0, режим «без дыры»; HIWORD выбирает
      Pos1/Pos2 в обычной ветке.
    • Подтверждено через type_inspect: TRuins наследует TShip
      (1232 первых байта), поэтому IsClass(ruins, TShip)==true.

Цель статьи: дать полное описание механики посадки корабля — на планету,
на станцию (TRuins) и «посадки в чёрную дыру» (THole) — с акцентом на
построение маршрута, учёт скорости движения цели и точку финализации.

★ ВАЖНОЕ ИСПРАВЛЕНИЕ ПРЕДЫДУЩИХ СТАТЕЙ:
   В Ship_Trajectory_Reimplementation/Recheck ветка Order==2 называлась
   «FOLLOW_OBJ». На самом деле State==2 = LANDING (см. §1). Названо так
   потому что внутри CalcRoute эта ветка строит approach-плёнку, но
   реальное назначение — посадка с финализацией в StepDay case 2.
```

## СОДЕРЖАНИЕ

```text
  §1.  Терминология и таксономия состояний/приказов
  §2.  Цели посадки: TPlanet, TRuins, TShip-carrier, THole
  §3.  Пайплайн посадки на планету (player + NPC)
  §4.  Пайплайн посадки на станцию (TRuins)
  §5.  Пайплайн прохода через чёрную дыру (THole)
  §6.  Учёт скорости движения цели (упреждение)
  §7.  Двухфазный landing trigger (StepDayStart vs StepDay_MoveProcessor)
  §8.  Финализация: TShip_ProcessUndock + CurPlanet/CurRuins binding
  §9.  Кто инициирует посадку (UI, AI, скрипты)
  §10. Карта функций и адресов
  §11. Псевдокод reference-implementation
  §12. Краевые случаи и тонкости
  §13. Открытые вопросы
```

## §1. ТЕРМИНОЛОГИЯ И ТАКСОНОМИЯ

```text
Поле TShip[+0x428] (1 байт) одновременно используется как State и Order.
Возможные значения:
  0  Idle / на орбите
  1  TakeOff      — взлёт с планеты/станции
  2  Landing      — посадка ★ ВСЯ ЭТА СТАТЬЯ
  3  Move         — движение к точке в системе
  4  ArriveFromHiperspace — выход из дыры в системе-назначения
  5  Hiperspace   — в гиперпрыжке (½ скорости, корабль уже в новой системе)
  6  Special move (follow / wingman)
  7  Teleport     — артефакт-перенос с эффектом частиц

Поле TShip[+0x42C] (DWORD) называется OrderData и в State=2 содержит:
  0 — фаза approach (корабль ещё далеко, плёнка длинная)
  1 — фаза final landing (запущен fade-out, ждём истощения плёнки)
  ★ Этот переход 0→1 происходит в StepDayStart_Main, §7.

Поле TShip[+0x430] = DestObj. В State=2 это:
  - TPlanet  → классическая посадка на планету
  - TRuins   → стыковка со станцией (TRuins наследует от TShip!)
  - TShip    → стыковка на корабль-носитель (Tranclucator → Player и т.п.)

Поле TShip[+0x434] = DestPos. В State=2 это OFFSET относительно DestObj.Pos
(а не абсолют). Для большинства приказов (клик игрока) DestPos = (0, 0),
что значит «лети к точке самой цели».

Поле TShip[+0x43C] = ScriptOrderAbsolute (1 байт). Если 1 — приказ от
скрипта, нельзя сбросить через ChangeState.

Поле TShip[+0x44C] = TempBlocked. Если 1 — все Order*-функции блокируются.
```

## §2. ЦЕЛИ ПОСАДКИ

```text
2.1  TPlanet — обычная планета.
     • Pos НЕ ХРАНИТСЯ напрямую: вычисляется через PolarToCartesian из
       (CurrentOrbitAngle, OrbitRadius). См. §6.
     • Радиус для landing trigger — TPlanet.GraphRadius @ +0x140 (int).
     • Радиус для approach/halt zone — также GraphRadius (см. §3).
     • После посадки: ship.CurPlanet = planet; ship[+0x445] = 0.

2.2  TRuins — станция (RC/PB/WB/SB/BK/MC/CB/UB).
     • Pos хранится «как есть» в TRuins[+0x14] (унаследовано из TShip,
       т.к. TRuins : TShip). Станция МОЖЕТ двигаться (CB-мобильная база
       мигрирует, см. memory/rangers_station_ai.md).
     • IsClass(DestObj, TShip) возвращает TRUE для TRuins.
       ★ В коде StepDay case 2 нет специальной ветки для TRuins —
       обрабатывается как «посадка на TShip-цель».
     • Радиус для landing trigger = 0.0 (см. §3) — требует ТОЧНОГО
       совпадения хвоста плёнки с target.Pos+DestPos.
     • После посадки: ship.CurRuins = ruins.

2.3  TShip-carrier — посадка корабля на другой корабль.
     • Используется для Tranclucator: ReturnAsArtefact (превращение
       компаньона в артефакт у Owner-корабля игрока).
     • Используется при TKling_Boss_SpawnFollowers: миньоны Бертора
       и т.п. часто появляются «у CurRuins=Босс».
     • Логически то же что TRuins: ship.CurRuins = target_ship.

2.4  THole — чёрная дыра (двусторонний портал).
     • Хранит две точки выхода: Pos1 @ +0x0C, Pos2 @ +0x18 (оба _pair_float).
     • Привязана к двум звёздам: Star1 @ +0x08, Star2 @ +0x14.
     • Имеет OpenTurn @ +0x20 и Status @ +0x24: 1=открыта-новая, 3=использована, 4=закрыта.
     • Graph @ +0x28 — указатель на TFilm-анимацию портала.
     • «Посадка в дыру» — это две разные вещи (см. §5):
         (a) ВХОД  — корабль летит к дыре в текущей системе (Order=4);
                     когда близко, состояние переключается на 5 (Hiperspace)
                     и BuildTrajectory телепортирует в новую систему.
         (b) ВЫХОД — корабль уже в системе-назначения; State=4 (ArriveFromHiperspace);
                     летит от точки выхода дыры, при достижении DestPos
                     hole.Status 1→3, OnArriveAtStar.
```

## §3. ПАЙПЛАЙН ПОСАДКИ НА ПЛАНЕТУ

```text
Полный путь от инициации до bind на CurPlanet, для одного хода = a4 кадров
(a4 = ship.CurStar.DayFrames, обычно 200):

  ┌──────────────────────────────────────────────────────────────────────┐
  │ Шаг A. ИНИЦИАЦИЯ                                                     │
  │   UI: MB_TMainForm_OnSpaceClick (0x7AF0E8), клик по TPlanet:          │
  │        ship.Order      = 2  (Landing)                                 │
  │        ship.DestObj    = planet                                       │
  │        ship.DestPos    = (0, 0)         ← offset relative to planet   │
  │        ship.ScriptOrderAbsolute = 0                                   │
  │      → вызов TShip_CalcRoute(ship, a4) сразу для отображения курса.   │
  │   Эквивалент: TShip_OrderLandToTarget(ship, planet, abs) @ 0x71C870.  │
  │   AI: TRanger_AI_DailyDecision (0x751B00) и др. AI-функции тоже       │
  │       вызывают OrderLandToTarget когда решают сесть.                  │
  └──────────────────────────────────────────────────────────────────────┘
  ┌──────────────────────────────────────────────────────────────────────┐
  │ Шаг B. ПЛАНИРОВАНИЕ МАРШРУТА (TShip_CalcRoute case 2 @ 0x7219B0)     │
  │   1. AsClass(DestObj, TPlanet) → planet                               │
  │   2. R_app  := planet.GraphRadius + 150   ← approach zone             │
  │   3. R_land := planet.GraphRadius + 50    ← landing zone              │
  │   4. df     := ship.CurStar.DayFrames                                 │
  │   5. future_pp := TPlanet_GetFutureOrbitPos(planet, df)               │
  │      target    := future_pp + ship.DestPos    (Vec2_Add)              │
  │   ───── ИГРОК ─────                                                   │
  │   6.  cur_pp  := TPlanet_GetCurrentOrbitPos(planet)                   │
  │       d²_now  := |ship.Pos − cur_pp|²                                 │
  │   7.  IF d²_now > R_app²  AND  |target − future_pp|² ≤ R_land²:       │
  │           // Игрок ещё далеко, но прицелился в landing-зону           │
  │           // — НЕ хотим врезаться в орбиту планеты!                   │
  │           dirA   := AngleFromPoints(ship.Pos, target)                 │
  │           step   := |target − ship.Pos|                               │
  │           // Greedy halving — найти точку ВНЕ R_land:                 │
  │           DO                                                          │
  │             step *= 0.5                                               │
  │             cand   := ship.Pos + dir(dirA)*step                       │
  │             future := TPlanet_GetFutureOrbitPos(planet, df)           │
  │           WHILE (|cand − future|² < R_land²  AND  step > 30)          │
  │           // Бинарный поиск точности 10:                              │
  │           hi := step*2; lo := step                                    │
  │           WHILE hi − lo > 10:                                         │
  │             mid := (hi+lo)*0.5                                        │
  │             cand := ship.Pos + dir(dirA)*mid                          │
  │             future := TPlanet_GetFutureOrbitPos(planet, df)           │
  │             IF |cand − future|² ≤ R_land²: hi := mid                  │
  │             ELSE                          : lo := mid                 │
  │           target := ship.Pos + dir(dirA)*lo  ; точка ВНЕ зоны         │
  │   8.  IF ship.Pos ≠ target: TShip_PushDestination(ship, target, df)   │
  │   ───── NPC ─────                                                     │
  │   6'. future_pp  := TPlanet_GetFutureOrbitPos(planet, df)             │
  │       jitter     := Rnd(−20, +20, ship.SeedFactor + planet.Id*2)      │
  │       cur_pp     := TPlanet_GetCurrentOrbitPos(planet)                │
  │       approachA  := AngleFromPoints(cur_pp, ship.Pos) + jitter        │
  │       d²_now     := |cur_pp − ship.Pos|²                              │
  │       IF d²_now ≤ R_app²:    step := (|seed|% planet.GraphRadius) * c │
  │       ELSE                 : step := R_land                           │
  │       target.x := future_pp.x + cos(approachA)*step                   │
  │       target.y := future_pp.y − sin(approachA)*step                   │
  │   7'. TShip_PushDestination(ship, target, df)                         │
  └──────────────────────────────────────────────────────────────────────┘
  ┌──────────────────────────────────────────────────────────────────────┐
  │ Шаг C. A*-ОБХОД И ПЛЁНКА (PushDestination → PlanPath @ 0x723A6C)     │
  │   PlanPath обходит Sun (центр (0,0), радиус CurStar.PlanetAvoidRadius)│
  │   и заполняет ship.RouteFilm[+0x440] последовательностью waypoint'ов.│
  │   Для видимого игроку корабля Bezier-resample до 200 waypoint'ов.    │
  │   (Подробности — Articles/Ship_Trajectory_Reimplementation.md §6-9.) │
  └──────────────────────────────────────────────────────────────────────┘
  ┌──────────────────────────────────────────────────────────────────────┐
  │ Шаг D. ЦИКЛ NextDay  ─  каждый ход выполняется заново                │
  │   TStar_NextDay (0x863998) для каждого корабля в системе:            │
  │     1) TShip_StepDayStart_Main(ship, tick, …)  @ 0x71D054            │
  │        ─ для state=2 запускает landing-trigger check (см. §7)        │
  │        ─ выставляет AlphaDelta (fade-out) и OrderData=1, ЕСЛИ        │
  │          tail плёнки попал внутрь GraphRadius планеты.               │
  │     2) AI-наследник (TRanger_AI_*, TPirate_AI_* и т.п.) может        │
  │        перезаписать Order, но обычно не вмешивается в state=2.       │
  │     3) TShip_StepDay_MoveProcessor(ship, tick, …) @ 0x720090         │
  │        case 2: TShip_MoveAlongRoute → продвинуть на 1 waypoint.      │
  │     В конце хода TShip_CalcRoute(ship, df) перепланирует плёнку      │
  │     с НОВОЙ позицией ship.Pos.                                       │
  └──────────────────────────────────────────────────────────────────────┘
  ┌──────────────────────────────────────────────────────────────────────┐
  │ Шаг E. ФИНАЛИЗАЦИЯ (StepDay_MoveProcessor case 2 @ 0x720226)        │
  │   При каждом тике в state=2:                                        │
  │   if (RouteFilm.head != null):                                       │
  │       TShip_MoveAlongRoute()                                         │
  │       if (sound_flag && Player==ship && OrderData==0):               │
  │           // approach-звук (заход на посадку) для игрока              │
  │           if IsClass(DestObj,TShip): AsClass(TShip)                  │
  │           else                     : AsClass(TPlanet);               │
  │                                       TPlanet_GetCurrentOrbitPos()   │
  │           sub_7CFF94(1)   ; «landing sound»                          │
  │                                                                      │
  │       if ((RouteFilm.head == null                                    │
  │            || RouteFilm.head.next == null)                            │
  │           && OrderData == 1):                                         │
  │           // ★ ВЫПОЛНИТЬ ПОСАДКУ                                     │
  │           if Player == ship:                                          │
  │               TRanger_NextDay_GrantEminentBonus()                    │
  │           TShip_ProcessUndock()                                      │
  │           if IsClass(DestObj, TShip):                                 │
  │               ship.CurRuins  = AsClass(DestObj, TShip)               │
  │           else:                                                       │
  │               ship.CurPlanet = AsClass(DestObj, TPlanet)              │
  │               ship[+0x445]   = 0                                      │
  │               if Player==ship && !planet[+0x139]:                     │
  │                   planet.VisitedByPlayer = true                       │
  │                   if planet.Owner == 6 (None):                        │
  │                       sub_7FF210();  Achievement_Trigger()           │
  │                       Player.statsScreen.discoveries++                │
  │           TShip_ChangeState(ship, 0)                                  │
  │           ship[+0x3B5] = 0                                            │
  │           TShip_OnLanding_NotifyEquipmentIssues() @ 0x6FDFA8         │
  │           // Reset engine cooldowns:                                  │
  │           for each engine in ship.weapons[1..]:                       │
  │               if IsClass(engine, TEngine):                            │
  │                   engine[+0x69] = 100   ; cooldown timer              │
  │           if sound_flag:                                              │
  │               sub_7CFE58(ship.f293, tick) ; «приземление» звук         │
  │               if ship.f299:                                           │
  │                   sub_7CFE58(ship.f299, tick) ; secondary             │
  └──────────────────────────────────────────────────────────────────────┘

После шага E корабль сидит на планете (state=0, CurPlanet=planet) — на
следующий ход StepDayStart запустит TShip_StepDay_Inherited (0x6FE39C)
для per-day-on-planet логики.
```

## §4. ПАЙПЛАЙН ПОСАДКИ НА СТАНЦИЮ (TRuins)

```text
Идентичен §3 за следующими отличиями:

  • В Шаге A: DestObj = ruins. (IsClass(ruins, TShip) == true.)
  • В Шаге B (CalcRoute case 2): ПРЕФИКС перед switch:
        if state == 2 && IsClass(DestObj, TShip):
            target_pos := DestObj.Pos          (то есть ruins.Pos)
            point      := target_pos + ship.DestPos
            PushDestination(ship, point, df)
            // Если плёнка пустая (DestPos=(0,0) и точка совпала со ship.Pos):
            //   DestPos := Rnd(-5,+5)×2;  повторно PushDestination
            return
        // только потом switch (с веткой для TPlanet)
    То есть для станции:
      - НЕ используется PlanetFutureOrbitPos (станция статична либо
        двигается медленно, и для упреждения нет формулы);
      - НЕ используется approach/landing zone по радиусу.
        Trigger landing — точное совпадение (см. §7).
      - target = ruins.Pos + DestPos.

  • В Шаге D (StepDayStart_Main, loc_71D540):
        target := ruins.Pos + ship.DestPos          (Vec2_Add)
        threshold := flt_71FC60 = 0.0  ★ КОНСТАНТА В EXE
        trigger   := (|RouteFilm.tail.pos − target|² ≤ 0.0)
                     ⇔ tail ТОЧНО совпадает с target.
    Поскольку PushDestination snap-ит последний waypoint к target если
    он ближе stepDist*200*TimeFactor (см. Reimplementation §6), это
    условие достигается естественным образом за O(1) хода когда корабль
    «подходит вплотную».

  • В Шаге E (StepDay_MoveProcessor case 2):
        if IsClass(DestObj, TShip):
            ship.CurRuins = AsClass(DestObj, TShip)
        Внимание: ship.CurPlanet НЕ обнуляется отдельно (но раньше при
        State=2 → 3 transition обычно CurPlanet=null).

  • Особенности для CB-мобильной базы и Tranclucator-у-Player:
        Если станция движется (например, CB), то Pos меняется каждый
        ход, но landing-trigger всё равно работает: на следующем
        CalcRoute target пересчитается с новой ruins.Pos.
```

## §5. ПАЙПЛАЙН ПРОХОДА ЧЕРЕЗ ЧЁРНУЮ ДЫРУ

```text
Это НЕ посадка в обычном смысле — корабль не привязывается к дыре. Это
двусторонний переход между системами. Полный конвейер описан в
Articles/Hyperjump_Pipeline.txt, здесь — только «механика подхода».

5.1  ВХОД (Order=4 / JUMP_HOLE)
─────────────────────────────────────────────────────────────────────────
Игрок: MB_TMainForm_OnSpaceClick по THole:
       ship.Order      = 4 (JUMP_HOLE)
       ship.DestObj    = hole
       ship.OrderData:
         HIWORD != 0 → подход к hole.Pos2; HIWORD == 0 → подход к hole.Pos1.
         Значение ровно 0xFFFF0000 (HIWORD=0xFFFF, LOWORD=0) — особая
         ветка: режим «без дыры», подход не строится через геометрию
         hole, а fallback'ом просто MOVE к DestPos.
       → TShip_CalcRoute case 4

CalcRoute case 4:
       end_pt := (HIWORD(OrderData) != 0) ? hole.Pos2 : hole.Pos1
       d²     := |end_pt − ship.Pos|²
       jitter := Rnd(−20, +20, ship.SeedFactor + hole.Id)
       ang    := AngleFromPoints(end_pt, ship.Pos) + jitter
       target.x := end_pt.x + cos(ang) * 100        ← подход на 100 ед.
       target.y := end_pt.y − sin(ang) * 100        ← снаружи дыры
       PushDestination(ship, target, df)

Корабль летит к точке СНАРУЖИ дыры (отступ 100 единиц по случайному углу
±20° от направления «дыра→корабль»). Это естественное «торможение» —
ship не входит в дыру в первом же ходу.

Переход в гипер (state=4 → 5) случается через диспетчер
TShip_ProcessScriptOrder (0x72A7A0) или прямой вызов
TShip_OrderJump_BuildTrajectory (0x71C8FC). По достижении дыры:
       ship.Pos      → старт в новой системе (orbit(planet или ruins))
       ship.State    := 5  (Hiperspace)
       ship.HasArrived := 0
       ship.HyperFlag  := 0

5.2  ВЫХОД (state=4 / ArriveFromHiperspace)
─────────────────────────────────────────────────────────────────────────
В системе-назначения корабль уже стоит у дыры. Вызывается:
  TShip_OrderArriveFromHiperspace(ship, hole, abs) @ 0x71C75C:
       state := 4
       DestObj := hole
       if (ship.CurStar == hole.Star1 @+0x08):
           DestPos := hole.Pos1 @+0x0C; OrderData := 2
       else:
           DestPos := hole.Pos2 @+0x18; OrderData := 0x10002 (HIWORD=1)

CalcRoute case 4 для state=4: НЕ обрабатывает специально (там код
для подхода к дыре, не от неё). Реально корабль движется по плёнке,
которую сгенерировал ChangeState/BuildTrajectory. Часто плёнка делает
«arc» — дугу к итоговой DestPos с ½ скорости.

StepDay_MoveProcessor case 4:
       if (OrderData != 0xFFFF0000 || !RouteFilm.head):    ; обычная ветка
           if (!HasArrived && RouteFilm.head):
               TShip_MoveAlongRoute()
               if (Player==ship && sound):
                   sub_7CFF94(1)   ; «выходной» звук
               if (ship.Pos == DestPos):
                   ; ★ Дошёл до точки выхода
                   if (hole.Status == 1):
                       hole.Status := 3
                   if (hole.Status == 4 && Player==ship && Keller_check):
                       Galaxy.Day -= 11   ; особый «Keller-timewarp»
                   HasArrived := 1
                   ship[+0x445] = 0
                   if sound:
                       sub_7CFE58(ship.f293, tick)
                       if ship.f299: sub_7CFE58(ship.f299, tick)
                   TShip_OnArriveAtStar()
                   Star.Ships.Add(ship)
                   TStar_RegisterShip(star, ship)

ВНИМАНИЕ: state=4 это **окончательная посадка в системе-назначения через
дыру** — она финализируется одинаково с обычным прибытием по гиперу
(state=3 + DestPos=(0,0)). После этого ship.State=0, корабль в системе.

5.3  НИЧЕГО НЕ НАЗЫВАЕТСЯ «landing на дыру»
─────────────────────────────────────────────────────────────────────────
В коде нет специального landing-trigger handler для дыры. Дыра — НЕ
имеет «GraphRadius» как у TPlanet и НЕ имеет ship-Pos как у TRuins.
Дыра обрабатывается через сравнение ship.Pos с DestPos (=Pos1 или
Pos2 hole) ВНУТРИ MoveProcessor case 4 — без отдельной trigger-фазы.

Это значит:
  • Нет fade-out анимации при «посадке» в дыру (она происходит только
    через scripting THyperObject — особая анимация портала).
  • Нет State→OrderData=1 перехода в StepDayStart для state=4.
  • Финализация мгновенная: ship.Pos==DestPos → HasArrived=1.
```

## §6. УЧЁТ СКОРОСТИ ДВИЖЕНИЯ ЦЕЛИ (УПРЕЖДЕНИЕ)

```text
6.1  TPlanet — ОРБИТАЛЬНОЕ УПРЕЖДЕНИЕ
─────────────────────────────────────────────────────────────────────────
Планета движется по окружности вокруг звезды. Её позиция в любой момент:
    Pos = PolarToCartesian(CurrentOrbitAngle, OrbitRadius)

При посадке используется ОЖИДАЕМАЯ позиция через df = DayFrames кадров:
    angle_future = star.TimeFactor   ; TStar[+0x108] (double)
                 * planet.OrbitalSpeed ; TPlanet[+0x40] (double)
                 * df                  ; обычно 200
                 + planet.CurrentOrbitAngle ; TPlanet[+0x20]
    Pos_future   = PolarToCartesian(angle_future, OrbitRadius)

  TPlanet_GetFutureOrbitPos @ 0x7774F4(planet, df, &out)
  TPlanet_GetCurrentOrbitPos @ 0x779D1C(planet, &out)

★ Use cases:
  - CalcRoute case 2 (планета): target = future_pp + DestPos.
    Гарантирует что корабль идёт к месту, где планета будет К КОНЦУ хода.
  - StepDayStart_Main loc_71D540 (landing-trigger): сравнивается tail
    плёнки с future_pp — учитывая что планета сдвинется за время полёта
    плёнки.
  - Для NPC дополнительно: jitter ±20° от линии «cur_pp → ship», чтобы
    подходить «по дуге», а не строго в лоб.

6.2  TRuins — БЕЗ УПРЕЖДЕНИЯ
─────────────────────────────────────────────────────────────────────────
Станция имеет постоянное Pos в TRuins[+0x14]. Большинство станций
неподвижны. CB-мобильная база перемещается, но дискретно — раз в
TRuins_NextDay (0x6CC6C0), и обычно НЕ во время посадки игрока.

CalcRoute case 2 (TShip-цель — включая TRuins) использует target =
ruins.Pos + DestPos НАПРЯМУЮ. Если ruins переместится между ходами,
landing-trigger пересчитает на следующем CalcRoute.

★ Это значит: если CB-станция «убегает» от вас на максимальной скорости,
вы её можете и не догнать — но в реальной игре скорость CB << скорости
обычного корабля, так что этого не случается.

6.3  TShip-цель — БЕЗ УПРЕЖДЕНИЯ (но динамичная)
─────────────────────────────────────────────────────────────────────────
Аналогично TRuins: target = target_ship.Pos + DestPos. Каждый ход
позиция target_ship меняется (он сам летит по плёнке), но landing-
trigger не требует упреждения — точное совпадение работает потому что
target_ship может «дождаться» (зависнуть на орбите) и потому что
плёнка корабля-преследователя пересчитывается каждый ход.

★ Для FOLLOW_SHIP (Order=6) есть ИНОЕ упреждение: target берётся из
плёнки лидера, на индексе (df-1) — то есть «куда лидер ПРИДЁТ через
df кадров». Но это уже не landing, а wingman-логика
(см. Ship_Trajectory_FollowModes_Recheck.md §3).

6.4  THole — БЕЗ УПРЕЖДЕНИЯ
─────────────────────────────────────────────────────────────────────────
Дыра неподвижна. Pos1/Pos2 не меняются после генерации.
```

## §7. ДВУХФАЗНЫЙ LANDING TRIGGER (★ ВАЖНОЕ ОТКРЫТИЕ)

```text
Посадка не выполняется одной функцией. Она разделена на:

  ФАЗА 1 (StepDayStart_Main):
    Проверка «достигла ли плёнка зоны посадки». Запуск fade-out alpha-
    анимации (корабль постепенно исчезает с космической карты).
    Маркер фазы: ship.OrderData = 1.

  ФАЗА 2 (StepDay_MoveProcessor):
    Дождаться истощения плёнки (alpha завершит затухание синхронно).
    Когда плёнка пуста → выполнить ProcessUndock + bind CurPlanet/CurRuins.

7.1  Псевдокод TShip_StepDayStart_Main для state=2 (loc_71D540)
─────────────────────────────────────────────────────────────────────────
  if state != 2: return  ; (диспетчер на 71D7BE проверяет case 7, и т.д.)

  if IsClass(DestObj, TShip):                          ; TRuins/TShip
      target.xy := DestObj.Pos                         ; [DestObj+0x14]
      if (!RouteFilm.tail || RouteFilm.tail.next):
          var61 := 0; goto loc_71D6AA
      Vec2_Add(&target, &ship.DestPos, &final_target)
      d² := CalcDistanceSquared(&final_target, RouteFilm.tail.pos)
      var61 := (d² <= flt_71FC60)   ; flt_71FC60 = 0.0
                                    ; → требует ТОЧНОГО совпадения

  else (TPlanet):
      planet := AsClass(DestObj, TPlanet)
      TPlanet_GetFutureOrbitPos(planet, ship.CurStar.DayFrames, &future_pp)
      TPlanet_GetCurrentOrbitPos(planet, &cur_pp)
      if (!RouteFilm.tail || ship.f3E0 != 0):
          var61 := 0; goto loc_71D6AA
      d² := CalcDistanceSquared(RouteFilm.tail.pos, &future_pp)
      threshold² := planet.GraphRadius * planet.GraphRadius + 1
      var61 := (threshold² >= d²)   ; tail внутри диска планеты

  loc_71D6AA:
      ship.OrderData := 0          ; reset (фаза approach)
      if (!RouteFilm.tail || ship.f3E0 != 0):
          goto loc_71D770          ; альтернативная ветка
      if (var61 == 0):
          return                   ; ещё не в зоне, продолжать подход
      if (var9 == 0):              ; sound/animation параметр
          goto loc_71D749          ; skip alpha calc

      ; ВКЛЮЧИТЬ FADE-OUT АНИМАЦИЮ:
      ship.AlphaDelta := -(ship.Alpha / RouteFilm.Count)
      ; AlphaDelta даёт fade-out за оставшиеся кадры плёнки.
      sub_7CF9C0(g_FilmList, tick, ship.GraphSecondary, 255)  ; set alpha
      if (Player == ship):
          sub_7CFF94(...0)         ; camera/sound

  loc_71D749:
      ship.Alpha := 0              ; +0x488
  loc_71D754:
      ship[+0x3B5] := 0            ; некий landing-state byte
  loc_71D761:
      ship.OrderData := 1          ; ★★★ TRIGGER LANDING

  loc_71FB93:
      return

7.2  Псевдокод TShip_StepDay_MoveProcessor case 2 (loc_720226)
─────────────────────────────────────────────────────────────────────────
  if state != 2: <other cases>

  if (RouteFilm.head):
      TShip_MoveAlongRoute()
      if (sound_flag && Player==ship && OrderData==0):
          ; approach-фаза, корабль ещё на карте — «landing sound»
          if IsClass(DestObj, TShip):
              AsClass(DestObj, TShip)
          else:
              AsClass(DestObj, TPlanet)
              TPlanet_GetCurrentOrbitPos(...)
          sub_7CFF94(1)
      if ((!RouteFilm.head || !RouteFilm.head.next)  ; плёнка истощена
          && OrderData == 1):                         ; alpha=0, ФАЗА 2
          ; ★ ВЫПОЛНИТЬ ПОСАДКУ:
          if Player==ship:
              Player(); TRanger_NextDay_GrantEminentBonus()
          TShip_ProcessUndock() @ 0x71BDE4
          if IsClass(DestObj, TShip):
              ship.CurRuins  := AsClass(DestObj, TShip)      ; +0x20
          else:
              ship.CurPlanet := AsClass(DestObj, TPlanet)    ; +0x1C
              ship[+0x445]   := 0
              if Player==ship && !planet.VisitedByPlayer:
                  planet.VisitedByPlayer := true
                  if planet.Owner == 6:        ; никем не занята
                      sub_7FF210()             ; «открыли планету»
                      Achievement_Trigger()
                      Player.MB_player_ui[+44]++
          TShip_ChangeState(ship, 0)   ; state=0, DestObj=0
          ship[+0x3B5] := 0
          TShip_OnLanding_NotifyEquipmentIssues() @ 0x6FDFA8
          ; Сброс cooldown'ов всех TEngine:
          for i in 1..ship.EquipList.count:
              eq := ship.EquipList[i]
              if IsClass(eq, TEngine):
                  eq[+0x69] := 100        ; cooldown_full
          if sound_flag:
              sub_7CFE58(ship.f293, tick)
              if ship.f299:
                  sub_7CFE58(ship.f299, tick)

  return  (LABEL_132 — alpha-fade update для GraphSecondary)

7.3  ПОЧЕМУ ИМЕННО ТАК
─────────────────────────────────────────────────────────────────────────
Двухфазная схема позволяет:

  (a) Анимация исчезновения корабля синхронизирована с истощением
      плёнки. AlphaDelta вычисляется так, чтобы за оставшиеся
      RouteFilm.Count кадров Alpha упал с текущего значения до 0.

  (b) Корабль не «телепортируется» при пересечении границы планеты —
      он плавно входит в её диск и одновременно затухает.

  (c) Финализация (CurPlanet) откладывается до самого конца, что
      устраняет race condition между планетной/станционной логикой и
      продолжающейся анимацией.

  (d) Если CalcRoute переплнирует плёнку (например, AI решит сменить
      цель в момент посадки), OrderData=1 «застрянет» и не вызовет
      ложного landing — пока плёнка не достигнет нового хвоста с
      target внутри GraphRadius.
```

## §8. ФИНАЛИЗАЦИЯ: TShip_ProcessUndock + BIND CurPlanet/CurRuins

```text
TShip_ProcessUndock @ 0x71BDE4 — вызывается при выполнении посадки
(а также при стыковке и взлёте). Внутри:
  • Снимает корабль с активного боевого списка системы (Star.Ships).
  • Останавливает все weapon-cooldown'ы.
  • Сбрасывает CombatTarget.
  • Перерасчёт Skill_Charm для AI-наблюдателей.
  • Уведомляет AI-друзей о ship «вне зоны».

После ProcessUndock в case 2 вычисляется:
  if DestObj is TShip:  ship.CurRuins  = AsClass(DestObj, TShip)
  else:                  ship.CurPlanet = AsClass(DestObj, TPlanet)

Затем:
  TShip_ChangeState(ship, 0) @ 0x71C3F0:
       ship.ScriptOrderAbsolute := 0     ; +0x43C
       ship.State                := 0    ; +0x428
       ship.DestObj              := 0    ; +0x430
       return TShip_OnArriveAtStar(ship) ; @ 0x7240C0

TShip_OnArriveAtStar @ 0x7240C0:
       просто очищает оставшуюся плёнку (sub_4D7E14 = TFilmList_Clear).

TShip_OnLanding_NotifyEquipmentIssues @ 0x6FDFA8:
       проверяет ship.equipment на повреждения и для Player создаёт
       сообщение «нужен ремонт» если нужно.

★ ВАЖНО: ship.Pos в момент посадки совпадает с последним waypoint
плёнки. Для TPlanet это означает что ship.Pos = future_pp (будущая
точка планеты). На следующем кадре render-цикла GFilm уже не рисует
корабль (alpha=0), но если игрок откроет окно планеты (Form Planet),
её внутренние координаты соответствуют именно тому моменту времени.
```

## §9. КТО ИНИЦИИРУЕТ ПОСАДКУ

```text
9.1  UI
─────────────────────────────────────────────────────────────────────────
  MB_TMainForm_OnSpaceClick @ 0x7AF0E8:
       switch по типу clicked_target:
         TPlanet → OrderLandToTarget(ship, planet)    state=2
         TRuins  → OrderLandToTarget(ship, ruins)      state=2
         TShip   → OrderLandToTarget? или FOLLOW (зависит от UI режима)
         THole   → OrderArriveFromHiperspace или Order=4 для подхода

  Кнопочные обработчики Bridge/Land:
       sub_7B16CC, sub_7B1FA4 — содержат вызовы TShip_ChangeState и
       OrderLandToTarget. Это UI-обработчики кнопок «Land» и т.п.

9.2  AI
─────────────────────────────────────────────────────────────────────────
Все AI-классы могут вызвать OrderLandToTarget:

  TRanger_AI_DailyDecision @ 0x751B00 — рейнджер выбрал планету.
  TPirate_AI_DailyDecision @ 0x5AC1CC — пират в засаде (AmbushTick).
  TTransport_AI_DailyDecision @ 0x5C0550 — торговец прилетел.
  TWarrior_AI_DailyDecision @ 0x5B6ED0 — воин Big-Warrior на базе.
  TKling_Minion_NextDay @ 0x5C81C4 — миньон Klig на станции.
  TKling_ProcessLanding @ 0x5CD2E8 — Bertor/Босс приземление.
  TTranclucator_AI_FlyToOwner @ 0x5CF200 — компаньон возвращается.
  TRuins_NextDay (CB-станция) @ 0x6CC6C0 — мобильная база приземлилась.

9.3  Скрипты
─────────────────────────────────────────────────────────────────────────
  SE_OrderLand — нет такой явной функции, но есть универсальная
                 SE_ShipOrder @ 0x64469C для Get/Set ship.Order.
  SE_ShipConnect @ 0x650138 — особая стыковка по скрипту:
                  вызывает StepDayStart_Main с принудительным state
                  и AlphaDelta=255, потом CalcRoute. Используется
                  при респавне миньонов «у CurRuins».
  Talk_Partner_OrderLandOnObject @ 0x6111E4 — диалог с партнёром
                  «лети к этому объекту и сядь».
```

## §10. КАРТА ФУНКЦИЙ И АДРЕСОВ

```text
Game-logic core:
  0x71C3F0   TShip_ChangeState               smooth finalize (state=0)
  0x71C43C   TShip_OrderMove_Internal        state=1 (TakeOff/Move)
  0x71C6B4   TShip_OrderApproachTarget       state=3 (полёт к звезде)
  0x71C634   TShip_CalcOrderDataApproach     помощник: dist² * coef + 1
  0x71C75C   TShip_OrderArriveFromHiperspace state=4 (выход из дыры)
  0x71C870   TShip_OrderLandToTarget         ★ state=2 entry
  0x71C8FC   TShip_OrderJump_BuildTrajectory state=5 (телепорт в гипер)
  0x71CE50   TShip_OrderFollowShip           state=6
  0x7219B0   TShip_CalcRoute                 switch по state, выбор target
  0x71D054   TShip_StepDayStart_Main         ★ landing-trigger handler
                                              (loc_71D540 — state=2 dispatch)
                                              (0x71D761 — OrderData=1 set)
  0x720090   TShip_StepDay_MoveProcessor     ★ landing finalize
                                              (0x720226 — switch dispatch)
  0x71FE10   TShip_MoveAlongRoute            один шаг по плёнке
  0x7240C0   TShip_OnArriveAtStar            очистка плёнки

Финализация посадки:
  0x71BDE4   TShip_ProcessUndock             снять с боевого списка
  0x6FDFA8   TShip_OnLanding_NotifyEquipmentIssues
                                              сообщение о ремонте
  0x7546E0   TRanger_NextDay_GrantEminentBonus
                                              бонус для игрока

Орбитальная геометрия:
  0x7774F4   TPlanet_GetFutureOrbitPos       упреждение через df кадров
  0x779D1C   TPlanet_GetCurrentOrbitPos      текущая
  0x8718C8   PolarToCartesian
  0x45EDAC   Vec2_Add                        target = base + offset

Path planning (см. Reimplementation.txt):
  0x722A10   TShip_PushDestination           обёртка PlanPath + Bezier
  0x723A6C   TShip_PlanPath                  A*-обход Sun
  0x722CC0   TShip_GenerateMovementSpline    arc-turning генератор
  0x4D813C   BezierResampleFilm              сглаживание плёнки до 200
  0x4D7E14   TFilmList_Clear (sub_4D7E14)    used by OnArriveAtStar

UI dispatch:
  0x7AF0E8   MB_TMainForm_OnSpaceClick       клик по карте
  0x7B16CC   sub_7B16CC                      Bridge-кнопка
  0x7B1FA4   sub_7B1FA4                      Land-кнопка

Глобалы:
  0x71FC60   flt_71FC60 = 0.0 (float)        landing threshold для TShip-цели
  0x882B38   g_PlayerStar
  0x882D74   g_FilmList (GFilm anim sys)
  0x88263C   Galaxy

Поля TShip (для state=2):
  +0x14   Pos (x,y)         float[2]
  +0x1C   CurPlanet         TPlanet*  (пишется при посадке)
  +0x20   CurRuins          TRuins*   (пишется при стыковке)
  +0x24   CurStar           TStar*    (для df = DayFrames)
  +0x420  Angle             double    (курс)
  +0x428  State (Order)     uint8     2 = Landing
  +0x42C  OrderData         uint32    0=approach, 1=landing trigger
  +0x430  DestObj           void*     TPlanet/TRuins/TShip
  +0x434  DestPos           float[2]  OFFSET от DestObj.Pos
  +0x43C  ScriptOrderAbs    uint8     1 = не сбрасывать
  +0x440  RouteFilm         TFilmList*
  +0x445  ?? (+0x445)       uint8     обнуляется при посадке на планету
  +0x488  Alpha             float     прозрачность (для fade-out)
  +0x48C  AlphaDelta        float     delta/кадр
  +0x494  GraphSecondary    void*     второй TFilm

Поля TPlanet (подтверждены через type_inspect TPlanet):
  +0x18   Star              TStar*
  +0x20   PolarPos[0]       double  CurrentOrbitAngle (deg)
  +0x28   PolarPos[1]       double  OrbitRadius (расстояние от звезды)
  +0x30   Mass              int
  +0x38   Radius            int (физический «размер» планеты)
  +0x40   Angle             double — в IDB-имени «Angle», но семантически
                            OrbitalSpeed (deg/кадр), см. PlanetFutureOrbitPos
  +0x74   Owner             uint8 (0..4=normals, 5=Kling, 6=None, 7=Pirate)
  +0x139  VisitedByPlayer   bool
  +0x140  GraphRadius       int (★ landing trigger threshold для tail плёнки;
                            ОТЛИЧАЕТСЯ от OrbitRadius — это размер диска
                            планеты на экране, а не орбитальный радиус)

Поля TRuins (size=1384, наследует TShip 1232 байта):
  +0x00..+0x4CF  TShip-base (включая Pos @ +0x14, Id @ +0x04)
  +0x4D0  EquipmentShop     TList* (отдельный магазин станции)
  ★ IsClass(ruins, TShip) == TRUE — поэтому в коде state=2 ветка
  «DestObj — TShip» обслуживает И корабли, И станции.

Поля THole (size=52, подтверждено через type_inspect THole):
  +0x00   VMT
  +0x04   Id                uint32
  +0x08   Star1             TStar*       ← первая сторона
  +0x0C   Pos1              _pair_float
  +0x14   Star2             TStar*       ← вторая сторона
  +0x18   Pos2              _pair_float
  +0x20   OpenTurn          int
  +0x24   Status            int (1=open, 3=used, 4=closed)
  +0x28   Graph             void* (TFilm-портал)
  +0x30   Name              wchar_t*

Поля TStar:
  +0x5C   PlanetAvoidRadius float (для PlanPath)
  +0x104  DayFrames         int (=200)
  +0x108  TimeFactor        double (для упреждения)
```

## §11. ПСЕВДОКОД REFERENCE-IMPLEMENTATION

```text
// Структуры — упрощённые, см. §10 для полного списка полей.
struct Vec2 { float x, y; };

enum State { IDLE=0, TAKE_OFF=1, LANDING=2, MOVE=3, ARRIVE_FROM_HOLE=4,
             HYPERSPACE=5, SPECIAL_MOVE=6, TELEPORT=7 };

enum LandingPhase { APPROACH=0, FADING=1 };

struct FilmNode { FilmNode *prev, *next; Vec2 pos; float angle; };
struct FilmList { FilmNode *head, *tail; FilmNode *freeHead, *freeTail; int count; };

struct Planet { Star* star; double orbitAngle, orbitRadius, orbitalSpeed;
                int radius, graphRadius; int owner; bool visited; };
struct Ruins  { /* наследует TShip, Pos на +0x14 */ Star* star; Vec2 pos; int id; };
struct Hole   { /* layout: Star1 +0x08, Pos1 +0x0C, Star2 +0x14, Pos2 +0x18 */
                Star *star1; Vec2 pos1; Star *star2; Vec2 pos2;
                int openTurn, status; };

struct Star   { float planetAvoidRadius; int dayFrames; double timeFactor; };

struct Ship {
    Vec2 pos; double speed, angle;
    State state;
    uint32_t orderData;
    void* destObj;
    Vec2 destPos;              // offset от destObj.Pos
    FilmList routeFilm;
    Star* curStar; Planet* curPlanet; Ruins* curRuins;
    float alpha, alphaDelta;
    bool isVisibleOnFilm; int seedFactor;
    int subFlag3E0;             // +0x3E0 — special-skip
};

// ───── Гео-помощники (см. §6) ─────────────────────────────────────────
Vec2 polarToCartesian(double angDeg, double r) {
    double a = angDeg * M_PI / 180.0;
    return { (float)( sin(a)*r), (float)(-cos(a)*r) };
}
Vec2 planetFutureOrbit(const Planet* p, int df) {
    double a = p->star->timeFactor * p->orbitalSpeed * df + p->orbitAngle;
    return polarToCartesian(a, p->orbitRadius);
}
Vec2 planetCurrentOrbit(const Planet* p) {
    return polarToCartesian(p->orbitAngle, p->orbitRadius);
}
float dist2(Vec2 a, Vec2 b) { float dx=a.x-b.x, dy=a.y-b.y; return dx*dx+dy*dy; }

// ───── 1. ИНИЦИАЦИЯ (TShip_OrderLandToTarget) ─────────────────────────
void orderLandToTarget(Ship* s, void* target, bool scriptAbs) {
    if (s->tempBlocked || target == nullptr) return;
    if (!preOrderViabilityCheck(s)) { changeState(s, 0); return; }
    s->state              = LANDING;
    s->destObj            = target;
    s->destPos            = {0, 0};        // offset = 0
    s->scriptOrderAbsolute = scriptAbs;
    s->orderData          = APPROACH;      // будет 1 когда подойдём
}

// ───── 2. CalcRoute case 2 (см. §3 для игрока + NPC) ──────────────────
void calcRouteLanding(Ship* s, int df) {
    if (isClass(s->destObj, TShip)) {
        // TShip-цель (включая TRuins):
        Ship* target = (Ship*)s->destObj;
        Vec2 point = { target->pos.x + s->destPos.x,
                       target->pos.y + s->destPos.y };
        pushDestination(s, point, df);
        if (!s->routeFilm.head || !s->routeFilm.head->next) {
            s->destPos.x = randInt(-5, 5, s->seedFactor);
            s->destPos.y = randInt(-5, 5, s->seedFactor);
            point = { target->pos.x + s->destPos.x,
                      target->pos.y + s->destPos.y };
            pushDestination(s, point, df);
        }
        return;
    }
    Planet* p = (Planet*)s->destObj;
    float Rapp  = p->graphRadius + 150.0f;
    float Rland = p->graphRadius + 50.0f;
    Vec2 future = planetFutureOrbit(p, df);
    Vec2 cur    = planetCurrentOrbit(p);
    Vec2 tgt    = { future.x + s->destPos.x, future.y + s->destPos.y };
    if (s == playerShip) {
        if (dist2(s->pos, cur) > Rapp*Rapp
            && dist2(tgt, future) <= Rland*Rland) {
            // avoidance (greedy halving + binary search) — см. §3 step 7
            tgt = avoidLandingZone(s, p, tgt, Rland, df);
        }
        if (s->pos.x != tgt.x || s->pos.y != tgt.y)
            pushDestination(s, tgt, df);
    } else {
        float jitter = randInt(-20, 20, s->seedFactor + p->id*2);
        float approachAng = atan2deg(cur, s->pos) + jitter;
        float step = (dist2(cur, s->pos) <= Rapp*Rapp)
                     ? (abs(seed) % p->graphRadius)
                     : Rland;
        tgt.x = future.x + cosf(approachAng*DEG)*step;
        tgt.y = future.y - sinf(approachAng*DEG)*step;
        pushDestination(s, tgt, df);
    }
}

// ───── 3. StepDayStart_Main для state=2 (см. §7.1) ────────────────────
void stepDayStart_Landing(Ship* s, int tick, bool soundFlag) {
    Vec2 target;
    bool inZone = false;
    if (isClass(s->destObj, TShip)) {
        Ship* tgt = (Ship*)s->destObj;
        if (!s->routeFilm.tail || s->routeFilm.tail->next || s->subFlag3E0) {
            inZone = false;
        } else {
            target = { tgt->pos.x + s->destPos.x, tgt->pos.y + s->destPos.y };
            inZone = (dist2(s->routeFilm.tail->pos, target) <= 0.0f);
        }
    } else {
        Planet* p = (Planet*)s->destObj;
        Vec2 future = planetFutureOrbit(p, s->curStar->dayFrames);
        if (!s->routeFilm.tail || s->subFlag3E0) {
            inZone = false;
        } else {
            float thr2 = (float)(p->graphRadius * p->graphRadius + 1);
            float d2   = dist2(s->routeFilm.tail->pos, future);
            inZone = (thr2 >= d2);
        }
    }
    s->orderData = APPROACH;          // reset перед решением
    if (!s->routeFilm.tail || s->subFlag3E0) {
        landing_alternativeBranch(s);
        return;
    }
    if (!inZone) return;             // ещё далеко, продолжать
    if (soundFlag) {
        // Запустить fade-out:
        s->alphaDelta = -(s->alpha / s->routeFilm.count);
        film_orderAlpha(s->graphSecondary, tick, 255);
        if (s == playerShip) film_setCameraFlag(0);
    }
    s->alpha   = 0;
    s->subFlag3B5 = 0;
    s->orderData = FADING;            // ★ landing trigger SET
}

// ───── 4. StepDay_MoveProcessor для state=2 (см. §7.2) ────────────────
void stepDay_MoveProc_Landing(Ship* s, int tick, bool soundFlag) {
    if (s->routeFilm.head) {
        moveAlongRoute(s);
        if (soundFlag && s == playerShip && s->orderData == APPROACH) {
            // approach-звук (запуск двигателя ландинг-режима)
            sub_7CFF94(1);
        }
        bool filmEmpty = !s->routeFilm.head || !s->routeFilm.head->next;
        if (filmEmpty && s->orderData == FADING) {
            // ★ ВЫПОЛНИТЬ ПОСАДКУ
            if (s == playerShip) grantEminentBonus();
            processUndock(s);
            if (isClass(s->destObj, TShip)) {
                s->curRuins = (Ruins*)s->destObj;
            } else {
                s->curPlanet = (Planet*)s->destObj;
                s->subFlag445 = 0;
                if (s == playerShip && !s->curPlanet->visited) {
                    s->curPlanet->visited = true;
                    if (s->curPlanet->owner == 6) {
                        triggerDiscoveryAchievement();
                    }
                }
            }
            changeState(s, 0);
            s->subFlag3B5 = 0;
            notifyEquipmentIssues();
            for (auto& eq : s->equipment) {
                if (isClass(eq, TEngine)) eq->cooldown = 100;
            }
            if (soundFlag) playLandingSound(s);
        }
    }
}
```

## §12. КРАЕВЫЕ СЛУЧАИ И ТОНКОСТИ

```text
12.1  Что если планета успела «уйти» из-под корабля?
─────────────────────────────────────────────────────────────────────────
CalcRoute использует future_pp для построения плёнки. Плёнка ведёт
корабль в точку, где планета БУДЕТ. На каждом ходу плёнка пересчитывается:
если планета сдвинулась дальше, чем предполагалось (например, корабль
не успел за один ход), на следующем ходу target пересчитается заново.

Однако: если ship.Speed мала, а planet.OrbitalSpeed велика, посадка
может занять много ходов или вовсе быть невозможной. В коде нет
fallback — корабль будет «гоняться» за планетой бесконечно.

12.2  Что если плёнка короче 200 кадров?
─────────────────────────────────────────────────────────────────────────
В StepDayStart fade-out расчёт: AlphaDelta = -Alpha/Count. Если Count=10,
fade пройдёт за 10 кадров. Если Count=200, за 200. Это означает что
fade-out длительность ВСЕГДА синхронизирована с истощением плёнки.

12.3  Что если ship.Pos уже в зоне посадки?
─────────────────────────────────────────────────────────────────────────
CalcRoute для игрока: блок avoidance не сработает (d²_now <= R_app²).
target = future + DestPos. Плёнка короткая (несколько кадров). На
следующем StepDayStart inZone=true → OrderData=1 → посадка.

12.4  Двойной trigger (планета+TShip-цель одновременно)
─────────────────────────────────────────────────────────────────────────
Невозможно: DestObj только один. IsClass проверяется один раз.

12.5  Прерывание посадки во время fade-out
─────────────────────────────────────────────────────────────────────────
Если игрок ставит новый Order (Move/Attack) во время fade-out:
  OnSpaceClick → ChangeState(ship, ... new state) → SE_ChangeState или
  прямой Order*-вызов.
  ChangeState обнуляет DestObj и state. AlphaDelta остаётся, но при
  возврате к state=3+ StepDay_MoveProcessor case 3 не выполняет
  finalization. Alpha продолжит падать до 0 — корабль ИСЧЕЗНЕТ с карты
  до тех пор, пока не будет принудительно восстановлен (например, при
  взлёте с планеты — TakeOff устанавливает alpha=255).
  ★ Это потенциальный баг в игре («корабль-призрак») при отмене посадки
  в последний момент.

12.6  Посадка на станцию в момент её перемещения (CB)
─────────────────────────────────────────────────────────────────────────
CB-станция мигрирует через TRuins_NextDay. Если корабль уже в state=2,
StepDayStart пересчитывает target.Pos каждый ход, и если CB ушла —
landing-trigger не сработает (dist² > 0). В этот момент CalcRoute
заново построит плёнку к НОВОЙ позиции, и в следующем ходу корабль
полетит за станцией.

★ Это означает: посадка на CB ведёт себя как «chase mode».

12.7  Threshold = 0.0 для TShip-цели
─────────────────────────────────────────────────────────────────────────
Кажется чрезмерно строгим. На практике работает благодаря snap-логике
в PushDestination (§6 Reimplementation.txt):
    if (ship.Speed*200*timeFactor)² > dist²(tail.pos, target):
        tail.pos := target
То есть последний waypoint автоматически snap-ится к точному target.
В итоге dist²(tail, target) == 0.0 за O(1) хода для разумных скоростей.

12.8  Камера игрока во время посадки
─────────────────────────────────────────────────────────────────────────
sub_7CFF94(1) — устанавливает камера-флаг для GFilm. Точная семантика:
  - 1 = «следовать за player.ship.Pos» (центрирование).
  - 0 = «не следовать» (передаётся при выполнении finalization).
Когда ship становится state=0 на планете, камера перестаёт следить.

12.9  TPlanet.OrbitRadius vs GraphRadius
─────────────────────────────────────────────────────────────────────────
В коде:
  • OrbitRadius (расстояние планеты от звезды) — используется для
    PolarToCartesian (где находится планета).
  • GraphRadius (TPlanet[+0x140]) — РАЗМЕР планеты на экране = trigger
    для landing.
Это разные величины!

12.10 Что если RouteFilm пуст с самого начала (state=2 immediately)
─────────────────────────────────────────────────────────────────────────
Пример: SE_ShipConnect — скриптовая стыковка. CalcRoute строит плёнку,
но если корабль УЖЕ в точке цели (ship.Pos == target), GenerateMovementSpline
не добавляет waypoint'ов. StepDayStart loc_71D6AA попадает в "tail.next==null"
branch → loc_71D770 → возможно прямой landing.
```

## §13. ОТКРЫТЫЕ ВОПРОСЫ

```text
[ ] var_9 в StepDayStart_Main — третий аргумент (cl). По логике —
    «надо ли запускать fade-out / звук» (likely sound_flag).
    Проверить через xrefs callers (TStar_NextDay).

[ ] loc_71D770 — альтернативная ветка landing-handler когда плёнка
    пуста ИЛИ ship.f3E0 != 0. Похоже на «landing уже произошёл — что-то
    наводящее на повтор». Декомпилировать полностью.

[ ] Точная семантика ship[+0x3E0] — какой-то skip-флаг (сетится в чём?).
    Не описан в Hyperjump_Pipeline.

[ ] Точная семантика ship[+0x3B5] и ship[+0x445] — landing-related
    байты. Сбрасываются в конце посадки.

[ ] sub_7FF210 — «Achievement_Trigger» с какой стороны? Похоже
    на отметку планеты как «открытой игроком впервые».

[ ] TPlanet.OrbitRadius — точное смещение. В Reimplementation.txt rev2
    написано +0x28 (double), но в TPlanet struct +0x28 это часть
    PolarPos (_pair_double). Возможно OrbitRadius — это +0x28 double
    второй элемент полярки.

[ ] Поведение в state=4 для НЕ-игрока: что если NPC-корабль прилетает
    в систему через дыру? Видимо тоже state=4 → дыра.Status переход.

[ ] sub_7CFF94 vs sub_7CFE58 — оба «landing sound» функции, но
    разные. sub_7CFE58 получает ship.f293/.f299 — видимо двигатели
    (engine sound channels).
```

## §14. ССЫЛКИ ВНУТРИ ПРОЕКТА

```text
• Articles/Ship_Trajectory_Code_Map.md
• Articles/Ship_Trajectory_Reimplementation.md (rev 2)
• Articles/Ship_Trajectory_FollowModes_Recheck.md
• Articles/Hyperjump_Pipeline.txt — пайплайн прыжка через дыру
• Articles/Planet_Daily_Cycle.txt — что происходит на планете
• memory/rangers_ship_nextday.md
• memory/rangers_ship_ai.md
• memory/rangers_station_ai.md
```

## КОНЕЦ СТАТЬИ
