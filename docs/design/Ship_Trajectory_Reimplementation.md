# SHIP TRAJECTORY — DETAILED RE-IMPLEMENTATION GUIDE — Rangers.exe (SR2 HD)

> Диздок перенесён из `Ship_Trajectory_Reimplementation.md` без правок текста: разделы оформлены заголовками,
> содержимое — дословно, моноширинным блоком (в исходнике — ASCII-вёрстка).

```text
Дата: 2026-06-10 (rev 2 — с правками по FOLLOW_OBJ / FOLLOW_SHIP)
Источник: декомпиляция через IDA MCP (ida_mcp/zeromcp на 127.0.0.1:13337)
          + структуры из Rangers.exe.h
          + Delphi-обёртки из aScriptFun.pas
          + Main.txt (data-конфиг)
          + memory/rangers_*.md
Цель: достаточно деталей чтобы воссоздать систему расчёта траектории с нуля.

История правок:
  rev 1 (2026-06-09): первая версия по результатам декомпила
  rev 2 (2026-06-10): уточнены ветки Order==2 (FOLLOW к TShip / TPlanet) и
                     Order==6 (FOLLOW_SHIP). DestPos для Follow-веток —
                     это смещение, а не абсолют. sub_710854 переименован из
                     TShip_UpdateEngine в TShip.CalcFollowRadius. Добавлены
                     helper'ы Vec2_Add, Dist2_0, GetNodeAtIndex,
                     PlanetFutureOrbitPos, PlanetCurrentOrbitPos,
                     PolarToCartesian, Atan2_PointToPoint.
```

## СОДЕРЖАНИЕ

```text
  §1. Глоссарий и обозначения
  §2. Структуры данных
  §3. Векторно-угловой helper-набор
  §4. Высокий уровень: от клика до отрисовки
  §5. CalcRoute — выбор цели по Order
  §6. PushDestination — внешняя обёртка планировщика
  §7. PlanPath — A* с обходом препятствия (Sun)
  §8. GenerateMovementSpline — генератор waypoints вдоль дуги
  §9. Безье-сглаживание для видимых кораблей
  §10. MoveAlongRoute — анимация
  §11. Полный псевдокод reference-implementation
  §12. Численные параметры
  §13. Краевые случаи и тонкости
  §14. Карта адресов
  §15. Открытые вопросы
```

## §1. ГЛОССАРИЙ И ОБОЗНАЧЕНИЯ

```text
  Pos        : (x, y) — позиция в локальных координатах ЗВЕЗДЫ.
               Sun находится в (0, 0). Y растёт вниз (game coords).
  Angle      : градусы. 0° — "север" (-Y), 90° — "восток" (+X).
               (Подтверждено через sub_8718C8 PolarToCartesian.)
  Waypoint   : запись плёнки { x, y, angle } + связи prev/next.
  RouteFilm  : двусвязный список waypoint'ов с пулом свободных узлов.
               Хранится в ship[+0x440].
  Frame/step : один кадр анимации между ходами. Один игровой ход
               длится a4 кадров (a4 = star.DayFrames = 200).
               Скорость корабля задана в "юнитах за полный ход"
               (ship.Speed). За один кадр корабль проходит ship.Speed/a4.
  star_factor: star.TimeFactor @ star[+0x108] (double, обычно ≈1.0).
               Корректирует длительность хода для звёзд с разной
               "плотностью" событий.

Запись `ship[+0xNNN]` означает offset от начала TShip; `obj.field` —
именованное поле (если IDA-имя известно).
```

## §2. СТРУКТУРЫ ДАННЫХ

## 2.1  TShip (фрагмент)

```text
  Offset    Type           Name              Назначение
  +0x000    VMT*           cls               Delphi vtable
  +0x004    int32          Id                уникальный id
  +0x014    _pair_float    Pos               (x, y) текущая позиция
  +0x024    TStar*         CurStar           текущая звезда
  +0x028    TStar*         PrevStar          предыдущая (для CalcAfterJumpPoint)
  +0x0E0    int32          SeedFactor        seed для Galaxy_RandInt
  +0x410    double         Speed             скорость, юнит/ход
  +0x418    double         TurnSpeed         max угол поворота, град/ход
  +0x420    double         Angle             текущий угол, градусы
  +0x428    uint8          Order             текущий приказ (см. §5)
  +0x42C    uint32         OrderData         параметры приказа
                                              ★ для FOLLOW_SHIP — low byte
                                                задаёт тактический режим
                                                CalcFollowRadius
                                              ★ для JUMP_HOLE — HIWORD выбирает
                                                Pos1/Pos2 у THole; значение
                                                0xFFFF0000 — особый «без дыры»
  +0x430    TObject*       DestObj           цель (TShip / TPlanet / THole / ...)
  +0x434    _pair_float    DestPos           (x, y) — назначение:
                                              ★ для MOVE/FORSAGE — АБСОЛЮТНАЯ
                                                точка
                                              ★ для FOLLOW_OBJ (Order==2) —
                                                СМЕЩЕНИЕ относительно DestObj.Pos
                                                (или future_planet_pos)
                                              ★ для FOLLOW_SHIP (Order==6) —
                                                после планирования здесь
                                                пишется фактический последний
                                                waypoint из ship.RouteFilm
  +0x440    TFilmList*     RouteFilm         плёнка waypoints (см. 2.2)
  +0x444    uint8          ArrivedFlag       =1 при завершении гипер-выхода
  +0x44C    uint8          ScriptOrderAbs    "не сбрасывать приказ из AI"
  +0x450    void*          GraphShip         TObjectSE — graphical instance
  +0x488    float          Alpha             прозрачность (для fade)
  +0x48C    float          AlphaDelta        скорость фейда
  +0x4AC    void*          GraphSecondary    второй графический объект
  +0x115C   float          ?                 reserve-range, используется в
                                              fallback CalcFollowRadius
                                              (открытый вопрос: точное имя)

  Visibility flag: HIBYTE байт верхнего слова Angle (ship[+0x423])
  используется как bit-флаг IsVisibleOnFilm. При реализации с нуля —
  завести отдельный uint8.

  Для TPlayer (наследует через TRanger : TNormalShip):
  +0x5CC    TStar*         dest_star          ★ звезда межзвёздной цели
                                              (для jump-from-dominion);
                                              читается/пишется
                                              SE_ShipDestination
```

## 2.2  TFilmList (head структуры RouteFilm)

```text
  Offset    Type      Name        Назначение
  +0x00     void*     ?           (vmt или allocator-helper)
  +0x04     Node*     Head        первый активный waypoint
  +0x08     Node*     Tail        последний активный waypoint (★ append идёт сюда)
  +0x0C     Node*     FreeHead    голова free-pool (переиспользование узлов)
  +0x10     Node*     FreeTail    хвост free-pool
  +0x14     int32     Count       число активных waypoint'ов
```

## 2.3  TFilmNode (waypoint)

```text
  Offset    Type      Name        Назначение
  +0x00     Node*     prev        предыдущий узел (или free-list next)
  +0x04     Node*     next        следующий узел
  +0x08     float     pos.x
  +0x0C     float     pos.y
  +0x10     float     angle       угол курса в этой точке (градусы)

  На уровне доступа в декомпилях читаются только pos.x/pos.y/angle.
  Размер узла, скорее всего, 24 байта — там может быть ещё одно
  doubleword для step-number, но в коде он не читается.
```

## 2.4  TStar (фрагменты, нужные траектории)

```text
  Offset    Type      Name                Назначение
  +0x05C    float     PlanetAvoidRadius   радиус Sun-зоны (для check intersect)
  +0x104    int32     DayFrames           = 200, число кадров в одном ходе
  +0x108    double    TimeFactor          поправка длительности хода
```

## 2.5  TPlanet (фрагменты для расчёта орбиты)

```text
  Offset    Type      Name                  Назначение
  +0x18     TStar*    parent_star
  +0x20     double    CurrentOrbitAngle     текущий угол на орбите (deg)
  +0x28     double    OrbitRadius           радиус орбиты (для PolarToCartesian)
  +0x40     double    OrbitalSpeed          угол/кадр (для упреждения)
  +0x50     int32     RadiusInt             = int(OrbitRadius);
                                            используется для approach_zone =
                                            RadiusInt + 150 и landing_zone =
                                            RadiusInt + 50
```

## 2.6  THole — две точки выхода

```text
  +0x0C/+0x10  Pos1.x / Pos1.y    первый "конец" дыры
  +0x18/+0x1C  Pos2.x / Pos2.y    второй "конец" дыры
```

## §3. ВЕКТОРНО-УГЛОВОЙ HELPER-НАБОР

```text
Все формулы взяты из декомпиляции; обозначены адресами в EXE.

  sub_40354C(x)            = sqrt(x)              float→float
  sub_403518(a)            = cos(a)               FPU-stack helper
  sub_403508(a)            = sin(a)               FPU-stack helper
                              (Y инвертирована — в коде везде "-sin")
  sub_41DCCC(t)            = arcsin(t)            FPU-stack, deg
  sub_41DD1C(y, x)         = atan2(y, x)          FPU-stack, deg
  sub_8719B8(a)            = normalize_360(a)     приведение к [0, 360)
  sub_871A04(a)            = "DegToRad" wrapper   (внутри передаётся в cos/sin)
  sub_871AA0(a, b)         = normalize_diff(a-b)  в (-180, +180]
  sub_871B10(a)            = normalize_360(a)
  sub_871BC4(a, b, c)      = bool: лежит ли угол c между a и b
                              по короткой дуге (для выбора стороны обхода)
  CalcDistanceSquared(p,q) = (px-qx)² + (py-qy)²  ★ Dist²
  Dist2(p, q)              = sqrt(Dist²)          ★ Euclidean distance
  MakePairFloat(out, x, y) = создать (out.x, out.y) → (x, y)

──── (★ rev 2 — добавлено) ────

  sub_45EDAC(a, b, out)    = Vec2_Add
      out.x := a.x + b.x
      out.y := a.y + b.y
      Используется в CalcRoute для:
        future_planet_pos + ship.DestPos      → target (для player+planet)
        target_ship.Pos    + ship.DestPos     → target (для player+ship)

  sub_872818(src, raw_seed, out, dist, base_angle_deg)  = ProjectBackwardWithJitter
      jitter := (signed(raw_seed) % 180) - 90       ; ±90°
      final  := base_angle + 180 + jitter
      final  := normalize_360(final)
      out.x  := src.x + cos(final) * dist
      out.y  := src.y - sin(final) * dist
      То есть «точка ПОЗАДИ source (180° от base_angle) с jitter ±90°,
      на дистанции `dist` от source». ★ Ключевая функция FOLLOW_SHIP.

  sub_4D7F98(N, head) = FilmList_GetNodeAtIndex
      node := head.next
      while N > 0:
          N := N - 1
          node := node.next
          if node == NULL: return NULL
      return node
      Линейный обход от head, возвращает узел номер N+1.

  sub_7774F4(planet, days, out) = PlanetFutureOrbitPos
      a := star.TimeFactor[+0x108] * planet.OrbitalSpeed[+0x40] * days
           + planet.CurrentOrbitAngle[+0x20]
      out := PolarToCartesian(a, planet.OrbitRadius[+0x28])
      Возвращает координаты планеты через `days` кадров.

  sub_779D1C(planet, out) = PlanetCurrentOrbitPos
      out := PolarToCartesian(planet.OrbitAngle[+0x20],
                              planet.OrbitRadius[+0x28])

  sub_8718C8(angle_ptr, radius) = PolarToCartesian
      rad := angle_ptr[0] * (π/180)   ; flt_8820B4 = DEG_TO_RAD
      out.x :=  sin(rad) * radius
      out.y := -cos(rad) * radius     ; Y инверсия
      0° = -Y (север), 90° = +X (восток).

  sub_871A50(P0, P1) = Atan2_PointToPoint
      dx :=  P1.x - P0.x
      dy := -(P1.y - P0.y)
      return normalize_360(atan2(dy, dx) * (180/π))
      Угол направления от P0 к P1 в [0..360).

  sub_710854 = TShip.CalcFollowRadius()
      ★ Раньше ошибочно назвал TShip_UpdateEngine; Exception-string
      в коде явно говорит "TShip.CalcFollowRadius()".
      Если ship.Order != 6 → исключение.

      mode := ship.OrderData & 0xFF
      leader := AsClass(DestObj, TShip)

      switch mode:
          1 ("close-combat"):
              r := +∞
              for w in leader.weapons[1..weapon_cnt]:
                  if w.is_active and w.effective_range < r:
                      r := w.effective_range
          2 ("long-range"):
              r := 0
              for w in leader.weapons:
                  if w.is_active and w.effective_range > r:
                      r := w.effective_range
          0 ("free/auto"):
              r := 999999

      if r ≤ 0 or r ≥ 999999:
          return TRUNC(leader[+0x115C float] + leader.weapon[0].something)
                 + 15
      else:
          return ROUND(r * <pos. constant>)
                  ; в декомпиле константа показывается как Extended-trash;
                  ; реальный коэффициент порядка 0.7..1.0 (требует
                  ; отдельной проверки)
```

## §4. ВЫСОКИЙ УРОВЕНЬ: ОТ КЛИКА ДО ОТРИСОВКИ

```text
Шаг 1. Игрок кликает мышью на космической карте.

       MB_TMainForm_OnSpaceClick (0x7AF0E8) определяет clicked_target
       (UI hit-test). Switch по типу:
         - TShip:   ship.Order=2 (FOLLOW_OBJ), DestObj=enemy,
                                  DestPos=(0,0)   ← OFFSET!
         - TPlanet: ship.Order=2, DestObj=planet,  DestPos=(0,0)
         - THole:   ship.Order=4 (JUMP_HOLE), DestObj=hole,
                                  OrderData=… (выбор стороны)
         - пустое место: ship.Order=1 (MOVE), DestPos=(x,y клика)
       и сразу вызывает TShip_CalcRoute.

Шаг 2. CalcRoute (см. §5):
       Switch по Order. Для каждой ветки выбирается реальная целевая
       точка target (с учётом offset DestPos для Follow, радиусов
       планет, точек дыры). target → PushDestination(ship, target, a4).

Шаг 3. PushDestination (§6) → PlanPath (§7) → GenerateMovementSpline (§8):
       Плёнка ship.RouteFilm заполняется waypoint'ами. Для видимого
       игроку корабля — до 200 узлов с Безье-resample (см. §9).

Шаг 4. На каждый кадр между ходами:
       TShip_StepDay_MoveProcessor (0x720090) → MoveAlongRoute (§10).
       Корабль "поедает" по одному waypoint'у за кадр.
       TFilm_OrderMove обновляет графический объект.

Шаг 5. Когда плёнка пуста и Order требует прибытия:
       TShip_OnArriveAtStar (0x7240C0) меняет CurStar/PrevStar,
       сбрасывает Order (если не ScriptOrderAbsolute).

Заметка про "линию курса игрока": ОТДЕЛЬНОЙ функции рисования линии нет.
Видимая на экране "пунктирная линия будущего пути" — это сама плёнка
RouteFilm, которую GFilm-аниматор рендерит как трассу. То что после
клика игрок сразу видит новый курс — заслуга многократного вызова
CalcRoute из OnSpaceClick (он перестраивает плёнку до конца хода).
```

## §5. CalcRoute (sub_7219B0) — ВЫБОР ЦЕЛИ ПО Order

```text
Сигнатура:
  void TShip_CalcRoute(TShip *ship, int a4 /* кадров на ход = 200 */);

Шаги общие:
  1. ship.ArrivedFlag = 0
  2. Если ship — Player и ship.Speed < 0.5 → принудительно ship.Speed := 0.5
  3. ship.Angle := normalize_360(ship.Angle)
  4. TShip_OnArriveAtStar(ship)
  5. Спец-ветка для босса Terron: если ship.Order==1 и
     Galaxy.TerronToStar нулевая — Terron управляется отдельно,
     return (TStar_ProcessCombatRound_0).
  6. ★ ПРЕФИКС перед switch:
       если ship.Order == 2 И IsClass(DestObj, TShip) — отдельный блок
       FOLLOW к чужому кораблю (см. §5.2.A ниже), и return.
  7. Иначе — switch(ship.Order):
```

## 5.1  Order == 0 (NONE)

```text
  Нечего делать. Плёнка не пополняется, корабль стоит.
```

## 5.2  Order == 2 (FOLLOW_OBJ)

```text
  ★ rev 2: DestPos в этой ветке — это СМЕЩЕНИЕ относительно цели,
  а не абсолютная координата. UI/AI задают желаемое смещение в
  ship.DestPos (для клика «лети к этому кораблю/планете» — (0,0)).

  5.2.A  DestObj — TShip      (отдельный префикс ДО switch)
  ─────────────────────────────────────────────────────────────────
    target_ship := AsClass(DestObj, TShip)
    target_pos  := target_ship.Pos
    d²          := Dist²(target_pos, ship.Pos)

    if d² < HUGE:                  ; HUGE = Extended-max² → почти всегда true
        point := target_pos + ship.DestPos        ; Vec2_Add (sub_45EDAC)
        PushDestination(ship, point, a4)

        ; Если плёнка пуста (DestPos был (0,0) и точка совпала с ship.Pos):
        if !ship.RouteFilm.head.next:
            ship.DestPos.x := Galaxy_RandInt(-5, +5, seed)
            ship.DestPos.y := Galaxy_RandInt(-5, +5, seed)
            point := target_pos + ship.DestPos
            PushDestination(ship, point, a4)
    else:                          ; цель в немыслимой дали — резерв
        angle := Rnd(0, 360, ship.SeedFactor + target_ship.Id)
        point.x := target_pos.x + cos(angle) * 200
        point.y := target_pos.y - sin(angle) * 200
        PushDestination(ship, point, a4)
    return

  5.2.B  DestObj — TPlanet, ship == Player
  ─────────────────────────────────────────────────────────────────
    planet      := AsClass(DestObj, TPlanet)
    R_app       := planet.RadiusInt + 150
    R_land      := planet.RadiusInt + 50
    day_frames  := ship.CurStar.DayFrames   ; = a4 = 200

    future_pp   := PlanetFutureOrbitPos(planet, day_frames)
    target      := future_pp + ship.DestPos       ; Vec2_Add → target

    cur_pp      := PlanetCurrentOrbitPos(planet)
    d²_now      := Dist²(ship.Pos, cur_pp)

    if d²_now > R_app²:                ; ship вне approach zone
        d²_target := Dist²(target, future_pp)
        if d²_target <= R_land²:       ; target внутри landing zone будущей планеты
            ; нужно НЕ въезжать в посадочную зону —
            ; ищем точку на луче (ship → target), которая ВНЕ R_land

            full_dist := Dist(target, ship.Pos)
            dir_angle := Atan2_PointToPoint(ship.Pos, target)
            dir_angle := normalize_360(dir_angle)

            ; ─── ШАГ 1: GREEDY HALVING ───
            step := full_dist
            loop:
                step := step * 0.5
                candidate.x := ship.Pos.x + cos(dir_angle) * step
                candidate.y := ship.Pos.y - sin(dir_angle) * step
                future_pp := PlanetFutureOrbitPos(planet, day_frames)
                if Dist²(candidate, future_pp) < R_land² and step > 30:
                    continue
                else: break

            ; ─── ШАГ 2: BINARY SEARCH (точность 10) ───
            hi := step * 2.0    ; точка внутри landing zone
            lo := step          ; точка вне landing zone
            while hi - lo > 10:
                mid := (hi + lo) * 0.5
                candidate.x := ship.Pos.x + cos(dir_angle) * mid
                candidate.y := ship.Pos.y - sin(dir_angle) * mid
                future_pp := PlanetFutureOrbitPos(planet, day_frames)
                if Dist²(candidate, future_pp) <= R_land²:
                    hi := mid
                else:
                    lo := mid
            ; берём LO (точка ВНЕ зоны)
            step := lo
            target.x := ship.Pos.x + cos(dir_angle) * step
            target.y := ship.Pos.y - sin(dir_angle) * step
        ; else: target = future_pp + DestPos, без корректировки
    ; else (ship уже в approach zone): target = future_pp + DestPos

    if ship.Pos != target:
        PushDestination(ship, target, a4)
    return

    НАМЕРЕНИЕ: при approach игрок целится в (future_planet + DestPos).
    Если эта точка окажется внутри посадочной зоны будущей планеты,
    бинарный поиск находит ближайшую точку на луче, которая всё ещё
    ВНЕ landing-зоны. Корабль зависает снаружи, не врезается.

  5.2.C  DestObj — TPlanet, ship != Player  (NPC)
  ─────────────────────────────────────────────────────────────────
    planet      := AsClass(DestObj, TPlanet)
    R_app       := planet.RadiusInt + 150
    R_land      := planet.RadiusInt + 50
    day_frames  := ship.CurStar.DayFrames

    future_pp       := PlanetFutureOrbitPos(planet, day_frames)
    jitter          := Rnd(-20, +20, ship.SeedFactor + planet.Id*2)
    cur_pp          := PlanetCurrentOrbitPos(planet)
    approach_angle  := Atan2_PointToPoint(cur_pp, ship.Pos) + jitter
    approach_angle  := normalize_360(approach_angle)

    cur_pp := PlanetCurrentOrbitPos(planet)   ; пересчёт (в коде так)
    d²_now := Dist²(cur_pp, ship.Pos)

    ; Дистанция захода:
    if d²_now <= R_app²:                ; уже близко к планете
        seed_dist := signed(star_high + ship.SeedFactor + planet.Id*2)
        step := abs(seed_dist) mod planet.RadiusInt * <small_const>
                                        ; <small_const> — Extended-trash в IDA,
                                        ; реально ~1.0; даёт случайную дистанцию
                                        ; чтобы корабли не залипали в точке
    else:
        step := R_land                  ; default landing zone

    target.x := future_pp.x + cos(approach_angle) * step
    target.y := future_pp.y - sin(approach_angle) * step

    PushDestination(ship, target, a4)
    return

    НАМЕРЕНИЕ: NPC «упреждает» позицию планеты (будущая на ход вперёд),
    подходит сбоку с jitter ±20° от линии «планета→корабль».
```

## 5.3  Order == 4 (JUMP_HOLE)

```text
  if ship.OrderData == 0xFFFF0000:
      LABEL_84 (просто MOVE к DestPos, без подсчёта дыры)
  else:
      hole := AsClass(DestObj, THole)
      if HIWORD(ship.OrderData) != 0:
          end_pt := hole.Pos2
      else:
          end_pt := hole.Pos1
      d² := Dist²(end_pt, ship.Pos)
      if d² > HUGE:
          PushDestination(ship, ship.DestPos, a4)    ; запасной путь
      else:
          jitter := Rnd(-20, +20, ship.SeedFactor + hole.Id)
          ang    := Atan2_PointToPoint(end_pt, ship.Pos) + jitter
          ang    := normalize_360(ang)
          target.x := end_pt.x + cos(ang) * 100
          target.y := end_pt.y - sin(ang) * 100
          PushDestination(ship, target, a4)
```

## 5.4  Order == 6 (FOLLOW_SHIP)       ★ rev 2 — переписано

```text
  leader        := AsClass(DestObj, TShip)
  follow_radius := TShip.CalcFollowRadius(ship)
                   ; зависит от ship.OrderData & 0xFF:
                   ;   0 = auto/free   → 999999 (фактически "прямо на leader")
                   ;   1 = close-combat→ min(weapon.range_i) leader’а
                   ;   2 = long-range  → max(weapon.range_i) leader’а

  if leader.RouteFilm.head:                          ; у leader есть waypoints
      if leader.RouteFilm.count > a4:                ; плёнка длиннее одного хода
          ref_node := GetNodeAtIndex(a4 - 1, leader.RouteFilm.head)
                      ; «куда leader будет к концу хода»
          jitter_seed := signed(ship.SeedFactor + leader.SeedFactor)
          target := ProjectBackwardWithJitter(
                       ref_node.pos,           ; source
                       jitter_seed,            ; jitter seed
                       follow_radius,          ; дистанция позади
                       ref_node.angle)         ; base_angle = курс leader’а
                       ; → точка позади ref_node по углу 180°+jitter,
                       ;   на дистанции follow_radius
      else:                                          ; плёнка ≤ a4 кадров
          jitter_seed := signed(ship.SeedFactor + leader.SeedFactor)
          target := ProjectBackwardWithJitter(
                       leader.RouteFilm.tail.pos,
                       jitter_seed,
                       follow_radius,
                       leader.RouteFilm.tail.angle)
  else:                                              ; у leader нет waypoints
      jitter_seed := signed(ship.SeedFactor + leader.SeedFactor)
      target := ProjectBackwardWithJitter(
                   leader.Pos,
                   jitter_seed,
                   follow_radius,
                   leader.Angle)                     ; double @ +0x420

  PushDestination(ship, target, a4)

  ; ★ ОБНОВЛЕНИЕ ship.DestPos после планирования —
  ;   обязательное для всех веток FOLLOW_SHIP:
  if ship.RouteFilm.tail:
      ship.DestPos.x := ship.RouteFilm.tail.pos.x
      ship.DestPos.y := ship.RouteFilm.tail.pos.y
  else:
      ship.DestPos := ship.Pos
  return

  Намерение:
    • Партнёр держится ЗА leader’ом на дистанции follow_radius.
    • Jitter ±90° от «строго сзади» — чтобы не пересекаться с другими
      ведомыми и не двигаться по прямой за leader’ом.
    • OrderData_low_byte задаёт тактику (близко/далеко/прямо на leader’а).
      Это пишет UI (диалог Talk_Partner_*) или AI.
    • После планирования ship.DestPos обновлён фактическим хвостом плёнки
      — это нужно для SE_ShipTurnBeforeEndOrder и отрисовки.
```

## 5.5  Order == 1, 3, 5  (MOVE / HYPER-EXIT / FORSAGE-MOVE)

```text
  LABEL_84:
      PushDestination(ship, ship.DestPos, a4)
      return

  Для Order == 3 (HYPER-EXIT) есть отдельная пред-обработка:
      radius² := (TStar_CreateArrivalAnimation(CurStar)/2 - 400)²
      if |ship.Pos|² <= radius²:
          goto LABEL_84               ; недостаточно вышли — просто MOVE
      else:
          ; угол ship.Pos→центр vs ship.Angle:
          dlt_pos_origin := Atan2_PointToPoint(CurStar.Pos, ship.Pos)
          dlt_zero_pos   := Atan2_PointToPoint((0,0), ship.Pos)
          if |normalize_diff(dlt_zero_pos - dlt_pos_origin)| > 5°:
              goto LABEL_84            ; не туда смотрим
          else:
              ; угол курса:
              cur_to_origin := atan2(-ship.Pos)  (deg, normalized)
              v79 := |normalize_diff(cur_to_origin - ship.Angle)|

              ; ★ Если корабль уже почти "за горизонт":
              if (v79 < 125° or ship[+0xC4] <= 200) and v79 < 175°:
                  inv_len := 1 / |ship.Pos|
                  dist_out := max(ship[+0xC4] * 0.25, 250.0)
                  ship.DestPos.x := ship.Pos.x + ship.Pos.x*inv_len*dist_out
                  ship.DestPos.y := ship.Pos.y + ship.Pos.y*inv_len*dist_out
                  PushDestination(ship, ship.DestPos, a4)
              else:
                  ; реальный гипер-выход (большой arc)
                  if v79 < 175° and ship[+0xC4] >= 200 and ship.CurStar == PlayerStar:
                      inv_len := 1 / |ship.Pos|
                      fake.x := ship.Pos.x + ship.Pos.x*inv_len*10000
                      fake.y := ship.Pos.y + ship.Pos.y*inv_len*10000
                      GenerateMovementSpline(ship, fake, false, a4)
                  TStar_ProcessCombatResults(ship, 1.0)
                  ; снимок последнего waypoint'а:
                  if ship.RouteFilm.tail:
                      ship.DestPos.x := ship.RouteFilm.tail.pos.x
                      ship.DestPos.y := ship.RouteFilm.tail.pos.y
                  else:
                      ship.DestPos := ship.Pos
                  ship.ArrivedFlag := 1
      return
```

## §6. PushDestination (sub_722A10)

```text
Сигнатура:
  void TShip_PushDestination(TShip *ship, _pair_float *target, int a4);

Реализация:
  PlanPath(ship, target, a4);

  ; Если корабль видим игроку — гарантируем минимум 200 кадров в плёнке
  if (ship.CurStar == PlayerStar
      && ship.RouteFilm.Count < 200
      && (!Player.CurPlanet || Player.Order == 5))
  {
      BezierResampleFilm(
          ship.RouteFilm,
          ship.RouteFilm.Head,
          ship.RouteFilm.Tail,
          200);
  }

  ; Сглаживание: если последний узел очень близко к target — скрутить
  if (ship.RouteFilm.Head) {
      threshold := ship.Speed * 200 * ship.CurStar.TimeFactor;
      if (threshold² > Dist²(ship.RouteFilm.Tail.pos, target)) {
          ship.RouteFilm.Tail.pos := target;
      }
  }

Зачем "200 кадров"? Это длина одного игрового хода в анимационных кадрах.
Если у NPC-корабля в системе игрока плёнка короче — будет «телепортация»
скачками, видимая игроку. Безье-сэмплинг (см. §9) сглаживает.
```

## §7. PlanPath (sub_723A6C) — A* С ОБХОДОМ ОДНОГО ПРЕПЯТСТВИЯ

```text
Препятствие — это центр звезды (Sun) в точке (0, 0) с радиусом
ship.CurStar.PlanetAvoidRadius. Алгоритм не делает мульти-обход.

Сигнатура:
  void PlanPath(TShip *ship, _pair_float *target, int a4);

Реализация:

  R := ship.CurStar.PlanetAvoidRadius;

  ; 1. Стартовая точка: последний waypoint, либо ship.Pos
  if (ship.RouteFilm.Tail)
      start := ship.RouteFilm.Tail.pos;
  else
      start := ship.Pos;

  ; 2. Если start внутри R — вытолкнуть на R+2 по радиусу
  PushOutsideCircle(start, &start, 2.0, R);

  ; 3. Проверить пересечение прямого сегмента start→target с R
  blocked := SegmentIntersectsCircle(start, target, R);   ; sub_872390

  if (!blocked) {
      if (a4 < 20)
          AppendSimpleSegment(ship, target, a4);    ; sub_722964
      else
          AppendSmoothedSegment(ship, target, a4);  ; sub_722848 (+ Bezier)
      return;
  }

  ; 4. Пересекает → касательные ОТ start и ОТ target
  ComputeTangentsFromExternalPoint(start,  &tangA1, &tangB1, R);
  ComputeTangentsFromExternalPoint(target, &tangA2, &tangB2, R);

  ; 5. Выбрать ближнюю пару (start-tangent ↔ target-tangent)
  if (Dist²(tangA1, tangA2) < Dist²(tangB1, tangB2)) {
      t_start := tangA1;  t_target := tangA2;
  } else {
      t_start := tangB1;  t_target := tangB2;
  }

  ; 6. Если start "очень далеко" от центра — длинная траектория
  if (2*R * 2*R < |start|²) {
      AppendSimpleSegment(ship, t_start, a4);
      if (ship.RouteFilm.Count >= a4) return;
      goto LABEL_28;
  }

  ; 7. Обычная дуга: спайн от start к t_start
  GenerateMovementSpline(ship, t_start, /*smart*/ true, a4);
  if (ship.RouteFilm.Count >= a4) return;

  ; 8. После касания — повторная проверка start2 → target
  start2 := ship.RouteFilm.Tail.pos;
  PushOutsideCircle(start2, &start2, 2.0, R);
  blocked2 := SegmentIntersectsCircle(start2, target, R);

  if (!blocked2) {
      if (a4 < 20)
          AppendSimpleSegment(ship, target, a4);
      else
          AppendSmoothedSegment(ship, target, a4);
      return;
  }

  ; 9. Финальный сегмент к цели (с smart=false)
  LABEL_28:
      GenerateMovementSpline(ship, target, /*smart*/ false, a4);
      if (ship.RouteFilm.Count < a4)
          TStar_ProcessCombatResults_0(ship, target, a4);
```

## §8. GenerateMovementSpline (sub_722CC0)

```text
Имя в IDA — "TShip_ProcessHyperspaceArc". Реальное содержимое — генератор
waypoint'ов с дуговым turning-манёвром.

Сигнатура:
  void GenerateMovementSpline(
      TShip *ship,
      _pair_float *target,
      bool smart_mode,
      int a4);

Алгоритм:

  ; 1. Стартовая точка и угол
  if (ship.RouteFilm.Tail) {
      cur := ship.RouteFilm.Tail.pos;
      curA := ship.RouteFilm.Tail.angle;
  } else {
      cur := ship.Pos;
      curA := ship.Angle;
  }

  ; 2. Если стартовая точка ≡ target — особый случай Order=4 (JUMP_HOLE):
  if (cur == target) {
      if (ship.RouteFilm.Head == NULL && ship.Order == 4) {
          ; ORBIT pattern: круг радиусом 30 вокруг target за a4 кадров
          while (ship.RouteFilm.Count < a4) {
              i := ship.RouteFilm.Count;
              ratio_deg := 360.0 * i / a4;
              wx := target.x + (sin(curA) - sin(curA + ratio_deg)) * 30;
              wy := target.y + (cos(curA) - cos(curA + ratio_deg)) * 30;
              wa := curA + ratio_deg;
              n := AppendNode(ship.RouteFilm);
              n.pos = (wx, wy);  n.angle = wa;
          }
      }
      return;
  }

  ; 3. Скоростные параметры на один кадр
  stepD := ship.Speed     * 200 * ship.CurStar.TimeFactor;
  stepA := ship.TurnSpeed * 200 * ship.CurStar.TimeFactor;
  if (ship.Order == 5
      || (ship.Order == 4 && ship.OrderData == 0xFFFF0000)) {
      stepD := stepD / 2;
      stepA := stepA / 2;
  }

  ; 4. Если до цели меньше одного stepD — один waypoint в текущей позиции
  if (Dist²(cur, target) < stepD²) {
      n := AppendNode(ship.RouteFilm);
      n.pos = cur;
      n.angle = curA;
      return;
  }

  ; 5. Угол к цели и diff
  target_angle := atan2(target.y - cur.y, target.x - cur.x) [→ deg]
  if (|normalize_diff(target_angle - curA)| < stepA) {
      n := AppendNode(ship.RouteFilm);
      n.pos = cur;
      n.angle = curA;
      return;
  }

  ; 6. Выбрать сторону обхода
  if (smart_mode) {
      side := pick_turn_direction_via_angle_test(...);    ; ±1; sub_871BC4
  } else {
      side := +1;
  }

  ; 7. ЦИКЛ генерации
  prev1 := -1;  prev2 := -1;
  while (ship.RouteFilm.Count < a4) {
      if (cur == target) break;

      if (smart_mode) {
          cur_origin² := cur.x² + cur.y²;
          if (prev2 != -1 && prev2 < prev1 && cur_origin² < prev1)
              break;        ; пошли удаляться, выход
          prev2 := prev1;  prev1 := cur_origin²;
      }

      tA := atan2(target.y - cur.y, target.x - cur.x);   [deg, normalized]
      if (|normalize_diff(tA - curA)| <= stepA) break;

      curA := normalize_360(curA + side * stepA);
      cur.x += cos(curA) * stepD;            ; (sin/cos с инверсией Y)
      cur.y -= sin(curA) * stepD;

      ; Snap к target если перепрыгнем
      if (stepD² >= Dist²(cur, target)) cur := target;

      n := AppendNode(ship.RouteFilm);
      n.pos = cur;  n.angle = curA;
  }
```

## §9. Безье-сглаживание (sub_4D813C) — ТОЛЬКО ДЛЯ ВИДИМЫХ

```text
Применяется в PushDestination когда ship.CurStar == PlayerStar и плёнка
короче 200 узлов. Преобразует N точек в ровно target_count точек,
скользящих вдоль кривой Безье степени N-1.

  void BezierResampleFilm(
      TFilmList *list,
      TFilmNode *src_head,
      TFilmNode *src_tail,
      int target_count /* = 200 */);

  N := count_nodes_from_head_to_tail(src_head, src_tail);
  if (N < 2 || target_count < 2) return;

  ; 1. Биномиальные коэффициенты C(N-1, k) для k=0..N-1
  binom[0]   := 1.0;
  binom[N-1] := 1.0;
  for k := 1 .. (N-1)/2:
      binom[k]     := (N - k) * binom[k-1] / k;
      binom[N-1-k] := binom[k];                 ; симметрия

  ; 2. Нормализовать углы: накопить разности
  for each node i (head..tail):
      diff := normalize_diff(i.angle - i.next.angle);
      i.angle := diff;
  src_tail.angle := 0;
  acc := 0;
  for each node i (tail..head reverse):
      acc += i.angle;
      i.angle := acc;

  ; 3. Сэмплинг
  for step := 0..target_count-1:
      t := step / (target_count - 1);
      one_minus_t := 1.0 - t;
      acc_x := 0;  acc_y := 0;  acc_a := 0;
      tk := 1.0;
      tnk := pow(one_minus_t, N-1);
      k := 0;
      for each node i (tail..head reverse):
          coef := tk * binom[k] * tnk;
          acc_x += coef * i.pos.x;
          acc_y += coef * i.pos.y;
          acc_a += coef * i.angle;
          k := k + 1;
          tk  := tk * t;
          tnk := tnk / one_minus_t;
      acc_x += <последний терм для k=N-1>;
      ; ... аналогично для y и a

      new := AllocateOrReuse(list);
      new.pos := (acc_x, acc_y);
      new.angle := normalize_360(acc_a);

  ; 4. Удалить исходные узлы (head..tail)
  RemoveRange(list, src_tail, src_head);
```

## §10. MoveAlongRoute (sub_71FE10) — АНИМАЦИЯ ОДНОГО КАДРА

```text
  void TShip_MoveAlongRoute(TShip *ship) {
      if (ship.RouteFilm.Head && ship.RouteFilm.Head.next) {
          ship.RouteFilm.Tail := ship.RouteFilm.Head.next;
          if (ship.IsVisibleOnFilm) {
              new_a256 := AngleGraTo256(ship.RouteFilm.Tail.angle);
              if (AngleGraTo256(ship.Angle) != new_a256)
                  TFilm_OrderRotate(ship.GraphShip, ship.Id, new_a256);
          }
          ship.Pos.x  := ship.RouteFilm.Tail.pos.x;
          ship.Pos.y  := ship.RouteFilm.Tail.pos.y;
          ship.Angle  := ship.RouteFilm.Tail.angle;
          DeleteNode(ship.RouteFilm.Head, ship.RouteFilm.Head.next);
      }
      if (ship.IsVisibleOnFilm) {
          TFilm_OrderMove(ship.GraphShip, ship.Id, &ship.Pos);
          if (ship.GraphSecondary)
              TFilm_OrderMove(ship.GraphSecondary, ship.Id, &ship.Pos);
          if (ship == Player)
              SetCameraTarget(ship.Id, &ship.Pos);
          if (ship.AlphaDelta != 0) {
              ship.Alpha += ship.AlphaDelta;
              a := round(clamp(ship.Alpha, 0.0, 255.0));
              TFilm_OrderAlpha(ship.GraphShip, ship.Id, a);
          }
      }
  }
```

## §11. ПОЛНЫЙ ПСЕВДОКОД REFERENCE-IMPLEMENTATION

```text
Ниже — минимальная программа, воспроизводящая всю систему.

// ─── Структуры ─────────────────────────────────────────────────────
struct Vec2 { float x, y; };
enum Order { NONE=0, MOVE=1, FOLLOW_OBJ=2, HYPER=3, JUMP_HOLE=4, FORSAGE=5, FOLLOW_SHIP=6 };

struct FilmNode {
    FilmNode *prev, *next;
    Vec2 pos;
    float angle;     // deg
};

struct FilmList {
    FilmNode *head, *tail;
    FilmNode *freeHead, *freeTail;
    int count;
    FilmNode* append() {
        FilmNode *n = popFree() ?: new FilmNode;
        n->prev = tail;  n->next = nullptr;
        if (tail) tail->next = n;
        tail = n;
        if (!head) head = n;
        ++count;
        return n;
    }
    void removeHead() {
        if (!head) return;
        FilmNode *h = head;
        head = h->next;
        if (head) head->prev = nullptr; else tail = nullptr;
        pushFree(h);  --count;
    }
    FilmNode* getNodeAtIndex(int n) const {
        FilmNode* x = head ? head->next : nullptr;
        while (x && n-- > 0) x = x->next;
        return x;
    }
};

struct Star {
    float planetAvoidRadius;
    int   dayFrames;          // = 200
    double timeFactor;
};

struct Planet {
    Star* star;
    double orbitAngle;        // current, deg
    double orbitRadius;
    double orbitalSpeed;      // deg per frame
    int    radiusInt;         // = round(orbitRadius), для approach/landing
    int    id;
};

struct Ship {
    int   id;
    Vec2  pos;
    double speed;             // unit per turn
    double turnSpeed;         // deg per turn
    double angle;             // deg
    Order order;
    uint32_t orderData;
    void*  destObj;           // Ship*, Planet*, Hole*
    Vec2   destPos;           // ★ для FOLLOW_OBJ — это OFFSET
    FilmList routeFilm;
    Star*  curStar;
    bool   isVisibleOnFilm;
    int    seedFactor;
};

// ─── Геометрия ─────────────────────────────────────────────────────
static const float DEG = (float)(M_PI/180);

float dist2(Vec2 a, Vec2 b) { float dx=a.x-b.x, dy=a.y-b.y; return dx*dx+dy*dy; }
float dist(Vec2 a, Vec2 b)  { return sqrtf(dist2(a,b)); }
float normDeg(float a) { a = fmodf(a, 360.0f); if (a<0) a+=360; return a; }
float normDiff(float a) { a = fmodf(a+180, 360); if (a<0) a+=360; return a-180; }

Vec2  vec_add(Vec2 a, Vec2 b) { return { a.x+b.x, a.y+b.y }; }

Vec2 polarToCartesian(float angleDeg, float radius) {
    float r = angleDeg * DEG;
    return { sinf(r) * radius, -cosf(r) * radius };
}

float atan2PtToPt(Vec2 p0, Vec2 p1) {
    float dx = p1.x - p0.x, dy = -(p1.y - p0.y);
    return normDeg(atan2f(dy, dx) / DEG);
}

Vec2 planetFutureOrbit(const Planet* p, int days) {
    double a = p->star->timeFactor * p->orbitalSpeed * days + p->orbitAngle;
    return polarToCartesian((float)a, (float)p->orbitRadius);
}

Vec2 planetCurrentOrbit(const Planet* p) {
    return polarToCartesian((float)p->orbitAngle, (float)p->orbitRadius);
}

bool segmentIntersectsCircle(Vec2 p0, Vec2 p1, float R) {
    if (p0.x*p0.x+p0.y*p0.y < R*R) return false;
    if (p1.x*p1.x+p1.y*p1.y < R*R) return false;
    Vec2 d = { p1.x-p0.x, p1.y-p0.y };
    float seg2 = d.x*d.x + d.y*d.y;
    float p02  = p0.x*p0.x + p0.y*p0.y;
    if (seg2 < p02) return false;
    float t = (-p0.x*d.x - p0.y*d.y) / sqrtf(seg2);
    if (t < 0) return false;
    return R*R > p02 - t*t;
}

void tangentsFromExternalPoint(Vec2 P, Vec2 *A, Vec2 *B, float R) {
    float r = sqrtf(P.x*P.x + P.y*P.y);
    float psi = asinf(R / r) / DEG;
    float base = atan2f(-P.y, P.x) / DEG;
    float a1 = base + psi, a2 = base - psi;
    *A = { R*cosf(a1*DEG), -R*sinf(a1*DEG) };
    *B = { R*cosf(a2*DEG), -R*sinf(a2*DEG) };
}

void pushOutsideCircle(Vec2 P, Vec2 *out, float margin, float R) {
    float r = sqrtf(P.x*P.x + P.y*P.y);
    if (fabsf(R - r) > margin) { *out = P; return; }
    out->x = P.x/r * (R+margin);
    out->y = P.y/r * (R+margin);
}

// ★ Dist2_0 (sub_872818): "точка позади source"
Vec2 projectBackwardWithJitter(Vec2 src, int rawSeed, float dist, float baseAngleDeg) {
    int j = (rawSeed % 180);
    if (j < 0) j += 180;
    j -= 90;                                       // ±90°
    float fa = normDeg(baseAngleDeg + 180.0f + j);
    return { src.x + cosf(fa*DEG)*dist,
             src.y - sinf(fa*DEG)*dist };
}

// ─── CalcFollowRadius ──────────────────────────────────────────────
int calcFollowRadius(const Ship* ship, const Ship* leader) {
    if (ship->order != FOLLOW_SHIP) abort();
    int mode = ship->orderData & 0xFF;
    int r;
    if (mode == 1) {
        r = 999999;
        for (auto& w : leader->weapons) {
            if (w.isActive() && w.effRange() < r) r = w.effRange();
        }
    } else if (mode == 2) {
        r = 0;
        for (auto& w : leader->weapons) {
            if (w.isActive() && w.effRange() > r) r = w.effRange();
        }
    } else {
        r = 999999;
    }
    if (r <= 0 || r >= 999999)
        return (int)(leader->fallbackRange + leader->weapon0.bonus) + 15;
    return (int)(r * 0.85f);  // коэффициент-предположение (см. §15)
}

// ─── Спайн-генератор ───────────────────────────────────────────────
void generateMovementSpline(Ship* s, Vec2 target, bool smart, int frames) {
    Vec2 cur; float curA;
    if (s->routeFilm.tail) {
        cur = s->routeFilm.tail->pos;
        curA = s->routeFilm.tail->angle;
    } else {
        cur = s->pos; curA = (float)s->angle;
    }
    if (cur.x == target.x && cur.y == target.y) return;

    double stepD = s->speed     * 200 * s->curStar->timeFactor;
    double stepA = s->turnSpeed * 200 * s->curStar->timeFactor;
    if (s->order == FORSAGE
        || (s->order == JUMP_HOLE && s->orderData == 0xFFFF0000)) {
        stepD /= 2; stepA /= 2;
    }
    if (dist2(cur, target) < stepD*stepD) {
        auto* n = s->routeFilm.append();
        n->pos = cur; n->angle = curA;
        return;
    }
    int side = +1;
    if (smart) {
        float a2 = atan2PtToPt(cur, target);
        side = (normDiff(a2 - curA) > 0) ? +1 : -1;
    }
    double prev1 = -1, prev2 = -1;
    while (s->routeFilm.count < frames) {
        if (cur.x == target.x && cur.y == target.y) break;
        if (smart) {
            double c2 = cur.x*cur.x + cur.y*cur.y;
            if (prev2 != -1 && prev2 < prev1 && c2 < prev1) break;
            prev2 = prev1; prev1 = c2;
        }
        float tA = atan2PtToPt(cur, target);
        if (fabsf(normDiff(tA - curA)) <= stepA) break;
        curA = normDeg(curA + side*(float)stepA);
        cur.x += cosf(curA*DEG)*(float)stepD;
        cur.y -= sinf(curA*DEG)*(float)stepD;
        if (stepD*stepD >= dist2(cur, target)) cur = target;
        auto* n = s->routeFilm.append();
        n->pos = cur; n->angle = curA;
    }
}

// ─── Планировщик ───────────────────────────────────────────────────
void planPath(Ship* s, Vec2 target, int frames) {
    float R = s->curStar->planetAvoidRadius;
    Vec2 start = s->routeFilm.tail ? s->routeFilm.tail->pos : s->pos;
    pushOutsideCircle(start, &start, 2.0f, R);
    if (!segmentIntersectsCircle(start, target, R)) {
        generateMovementSpline(s, target, false, frames);
        return;
    }
    Vec2 a1, b1, a2, b2, t1, t2;
    tangentsFromExternalPoint(start,  &a1, &b1, R);
    tangentsFromExternalPoint(target, &a2, &b2, R);
    if (dist2(a1,a2) < dist2(b1,b2)) { t1=a1; t2=a2; } else { t1=b1; t2=b2; }

    generateMovementSpline(s, t1, true, frames);
    if (s->routeFilm.count >= frames) return;

    Vec2 start2 = s->routeFilm.tail->pos;
    pushOutsideCircle(start2, &start2, 2.0f, R);
    if (!segmentIntersectsCircle(start2, target, R)) {
        generateMovementSpline(s, target, false, frames);
        return;
    }
    generateMovementSpline(s, target, false, frames);
}

void pushDestination(Ship* s, Vec2 target, int frames) {
    planPath(s, target, frames);
    if (s->isVisibleOnFilm && s->routeFilm.count < frames) {
        bezierResample(&s->routeFilm, s->routeFilm.head, s->routeFilm.tail, frames);
    }
    if (s->routeFilm.tail) {
        double thr = s->speed * 200 * s->curStar->timeFactor;
        if (thr*thr > dist2(s->routeFilm.tail->pos, target))
            s->routeFilm.tail->pos = target;
    }
}

// ─── CalcRoute ─────────────────────────────────────────────────────
void calcRoute(Ship* s, int frames) {
    s->angle = normDeg((float)s->angle);

    // Префикс: Follow к TShip
    if (s->order == FOLLOW_OBJ && isShip(s->destObj)) {
        Ship* tgt = (Ship*)s->destObj;
        Vec2 point = vec_add(tgt->pos, s->destPos);
        pushDestination(s, point, frames);
        if (!s->routeFilm.head || !s->routeFilm.head->next) {
            s->destPos.x = (float)rndInt(-5, 5, /*seed*/0);
            s->destPos.y = (float)rndInt(-5, 5, /*seed*/0);
            point = vec_add(tgt->pos, s->destPos);
            pushDestination(s, point, frames);
        }
        return;
    }

    switch (s->order) {
    case NONE: break;
    case MOVE:
    case FORSAGE:
    case HYPER:
        pushDestination(s, s->destPos, frames);
        break;

    case FOLLOW_OBJ: {                    // planet here
        Planet* p = (Planet*)s->destObj;
        float Rapp  = (float)p->radiusInt + 150;
        float Rland = (float)p->radiusInt + 50;
        int   df    = s->curStar->dayFrames;
        Vec2  future = planetFutureOrbit(p, df);
        Vec2  cur    = planetCurrentOrbit(p);

        if (s == playerShip) {
            Vec2 tgt = vec_add(future, s->destPos);
            if (dist2(s->pos, cur) > Rapp*Rapp
                && dist2(tgt, future) <= Rland*Rland) {
                // avoidance
                float fullDist = dist(s->pos, tgt);
                float dirA = atan2PtToPt(s->pos, tgt);
                // halving
                float step = fullDist;
                while (true) {
                    step *= 0.5f;
                    Vec2 c = { s->pos.x + cosf(dirA*DEG)*step,
                               s->pos.y - sinf(dirA*DEG)*step };
                    Vec2 fp = planetFutureOrbit(p, df);
                    if (dist2(c, fp) < Rland*Rland && step > 30) continue;
                    break;
                }
                float hi = step*2.0f, lo = step;
                while (hi - lo > 10) {
                    float mid = (hi+lo)*0.5f;
                    Vec2 c = { s->pos.x + cosf(dirA*DEG)*mid,
                               s->pos.y - sinf(dirA*DEG)*mid };
                    Vec2 fp = planetFutureOrbit(p, df);
                    if (dist2(c, fp) <= Rland*Rland) hi = mid; else lo = mid;
                }
                tgt = { s->pos.x + cosf(dirA*DEG)*lo,
                        s->pos.y - sinf(dirA*DEG)*lo };
            }
            if (s->pos.x != tgt.x || s->pos.y != tgt.y)
                pushDestination(s, tgt, frames);
        } else {
            // NPC
            float jitter = (float)rndInt(-20, 20,
                                         s->seedFactor + p->id*2);
            float appAng = atan2PtToPt(cur, s->pos) + jitter;
            appAng = normDeg(appAng);
            float step;
            if (dist2(cur, s->pos) <= Rapp*Rapp) {
                int sd = (int)(s->seedFactor + p->id*2);
                step = (float)(std::abs(sd) % p->radiusInt);
            } else {
                step = Rland;
            }
            Vec2 tgt = { future.x + cosf(appAng*DEG)*step,
                         future.y - sinf(appAng*DEG)*step };
            pushDestination(s, tgt, frames);
        }
        break;
    }

    case JUMP_HOLE: {
        if (s->orderData == 0xFFFF0000u) {
            pushDestination(s, s->destPos, frames);
            break;
        }
        Hole* h = (Hole*)s->destObj;
        Vec2 end = (HIWORD(s->orderData) != 0) ? h->pos2 : h->pos1;
        float jitter = (float)rndInt(-20, 20, s->seedFactor + h->id);
        float ang = normDeg(atan2PtToPt(end, s->pos) + jitter);
        Vec2 tgt = { end.x + cosf(ang*DEG)*100,
                     end.y - sinf(ang*DEG)*100 };
        pushDestination(s, tgt, frames);
        break;
    }

    case FOLLOW_SHIP: {
        Ship* leader = (Ship*)s->destObj;
        int   fr = calcFollowRadius(s, leader);
        int   js = s->seedFactor + leader->seedFactor;
        Vec2  tgt;
        if (leader->routeFilm.head) {
            if (leader->routeFilm.count > frames) {
                FilmNode* n = leader->routeFilm.getNodeAtIndex(frames - 1);
                tgt = projectBackwardWithJitter(n->pos, js, (float)fr, n->angle);
            } else {
                FilmNode* n = leader->routeFilm.tail;
                tgt = projectBackwardWithJitter(n->pos, js, (float)fr, n->angle);
            }
        } else {
            tgt = projectBackwardWithJitter(leader->pos, js, (float)fr,
                                            (float)leader->angle);
        }
        pushDestination(s, tgt, frames);
        if (s->routeFilm.tail) s->destPos = s->routeFilm.tail->pos;
        else                   s->destPos = s->pos;
        break;
    }
    }
}

// ─── MoveAlongRoute (один кадр) ────────────────────────────────────
void moveAlongRoute(Ship* s) {
    if (s->routeFilm.head && s->routeFilm.head->next) {
        auto* next = s->routeFilm.head->next;
        s->pos   = next->pos;
        s->angle = next->angle;
        s->routeFilm.removeHead();
    }
    if (s->isVisibleOnFilm) {
        film_orderMove(s->graphShip, s->id, s->pos);
        if (s == playerShip) setCameraTarget(s->pos);
    }
}
```

## §12. ЧИСЛЕННЫЕ ПАРАМЕТРЫ

```text
  frames per turn (a4)      : 200     (везде в коде литералом)
  ship.Speed (player min)   : 0.5
  Pushout margin            : 2.0
  Target snap dist          : 30      (внутр. параметр generateSpline)
  Binary search final tol   : 10      (avoidance к планете для player)
  Planet "approach zone"    : RadiusInt + 150
  Planet "landing zone"     : RadiusInt + 50
  Hole "approach offset"    : 100
  Hyperspace radius scale   : ArrivalAnimRadius/2 - 400
  FORSAGE divisor           : 2 для stepD/stepA
  JUMP_HOLE 0xFFFF0000      : "без дыры" — поведение как MOVE +
                              halving stepD/stepA в spline
  Hover-orbit radius        : 30 (Order==4 когда at target)
  Search jitter ranges      : Rnd(-20,20), Rnd(-5,5), Rnd(0,360)
  Bezier resample target    : 200 кадров
  Player approach binsearch : ±90° jitter (Dist2_0)
  FollowRadius "auto"       : 999999 → fallback к (leader[+0x115C]+w0)+15
```

## §13. КРАЕВЫЕ СЛУЧАИ И ТОНКОСТИ

```text
  • Стартовая точка ВНУТРИ Sun — выталкивается на R+2 PushOutsideCircle.
  • Цель ВНУТРИ Sun не обрабатывается специально — алгоритм будет
    кружить по касательной бесконечно. В игре цель внутри Sun
    невозможна (UI hit-test исключает).
  • Если плёнка достигла лимита a4 кадров, generateSpline обрывается.
    Корабль на следующем ходу продолжит CalcRoute с того же start.
  • smart_mode = true даёт защиту от кружения мимо цели по последним
    трём расстояниям от центра. На финальном сегменте всегда false.
  • Безье-resample применяется ТОЛЬКО для видимых игроку кораблей.
  • Order=4 (JUMP_HOLE) с OrderData == 0xFFFF0000 ведёт себя как MOVE
    + spline в half-speed mode.
  • MoveAlongRoute съедает по одному узлу за кадр — плёнка длиной 200
    проходится за один ход.
  • Игрок дополнительно центрирует камеру через TFilm_SetCameraTarget
    (0x7CFF54).
  • TFilm_OrderMove не двигает корабль сразу — добавляет приказ в
    очередь плёнки, интерполируется на следующем кадре Render-цикла.

  ★ rev 2 — FOLLOW-специфичные тонкости:

  • DestPos для FOLLOW_OBJ — это СМЕЩЕНИЕ от target. Кликом по объекту
    UI ставит DestPos := (0,0); другие команды (UI «стой рядом») могут
    задать конкретный offset (например, (100, 0) для "правее").

  • Для player → planet алгоритм гарантирует что выбранная цель НЕ
    окажется внутри посадочной зоны будущей позиции планеты. Корабль
    остановится снаружи, чтобы не въехать в орбиту.

  • Для NPC → planet всегда работает «упреждающий» полёт: подход
    рассчитывается относительно БУДУЩЕЙ позиции планеты, с jitter
    ±20° от линии «текущая позиция планеты → корабль».

  • Для FOLLOW_SHIP всегда используется ProjectBackwardWithJitter
    (Dist2_0) — точка позади leader’а с ±90° jitter. Так wingman
    не идёт строго в кильватер.

  • OrderData.lo_byte для FOLLOW_SHIP даёт тактический режим:
        0 → ~999999 (на leader’а)
        1 → close-combat (min weapon range)
        2 → long-range  (max weapon range)
    Этот режим переключается, скорее всего, диалогом
    Talk_Partner_OrderFlyToMe или подобными.

  • После любого FOLLOW (Order==2 к TShip/TPlanet или Order==6)
    ship.DestPos обновляется фактическим хвостом плёнки. Это нужно
    для (а) корректной отрисовки UI «куда летит», (б) подсчёта
    оставшихся ходов в SE_ShipTurnBeforeEndOrder.

  • CalcFollowRadius (sub_710854) кидает исключение если Order != 6 —
    при реализации хорошо бы воспроизвести (контроль инвариантов).
```

## §14. КАРТА АДРЕСОВ

```text
  Game-logic:
  0x7219B0   TShip_CalcRoute              (switch by Order)
  0x722A10   TShip_PushDestination
  0x723A6C   TShip_PlanPath
  0x722CC0   TShip_GenerateMovementSpline (IDA-имя: ProcessHyperspaceArc)
  0x722964   TShip_AppendSimpleSegment    (sub_722964)
  0x722848   TShip_AppendSmoothedSegment  (sub_722848, +Bezier)
  0x723DF4   TShip_PlanPath_AfterTangent  (sub_723DF4)
  0x71D054   TShip_StepDayStart_Main
  0x720090   TShip_StepDay_MoveProcessor
  0x71FE10   TShip_MoveAlongRoute
  0x7240C0   TShip_OnArriveAtStar

  Film:
  0x4D813C   BezierResampleFilm
  0x4D7E3C   FilmList_AppendNode
  0x4D7C9C   FilmList_DeleteNode
  0x4D7F98   FilmList_GetNodeAtIndex
  0x7CF8F4   TFilm_OrderMove
  0x7CFA28   TFilm_OrderRotate
  0x7CF9C0   TFilm_OrderAlpha
  0x7CFF54   TFilm_SetCameraTarget

  Геометрия:
  0x872390   SegmentIntersectsCircle
  0x872734   ComputeTangentsFromExternalPoint
  0x871CD8   PushOutsideCircle
  0x8728AC   CalcDistanceSquared
  0x8728F4   Dist2 (Euclidean)
  0x872818   ProjectBackwardWithJitter (Dist2_0)   ★ rev 2
  0x45EDAC   Vec2_Add                              ★ rev 2
  0x871A04   DegToRad helper
  0x871A50   Atan2_PointToPoint                    ★ rev 2
  0x871AA0   NormalizeAngleDiff
  0x871B10   NormalizeAngle360
  0x871BC4   AngleBetween (boolean predicate)
  0x8718C8   PolarToCartesian                      ★ rev 2
  0x8820B4   DEG_TO_RAD constant (flt_8820B4 = π/180)

  Planet/Star:
  0x7774F4   PlanetFutureOrbitPos                  ★ rev 2
  0x779D1C   PlanetCurrentOrbitPos                 ★ rev 2
  0x710854   TShip.CalcFollowRadius                ★ rev 2 (переименование)
  0x712A24   TShip.WeaponEffectiveRange
  0x70E934   TShip.WeaponGetSpeed

  UI:
  0x7AF0E8   MB_TMainForm_OnSpaceClick

  Globals:
  0x78C078   Player()
  0x8824AC   AutoBattleShip
  0x882B38   PlayerStar (gvar[0])
  0x882D74   GFilm (gvar[0])
  0x88263C   Galaxy
```

## §15. ОТКРЫТЫЕ ВОПРОСЫ

```text
  [ ] Точные значения TOrderType enum — должны быть в .pas или
      IDA Local Types. Подтвердить.
  [ ] Точная семантика side-decision в sub_871BC4 в spline-generator.
  [ ] sub_872600 — helper, используется в spline_generator для расчёта
      v65 (вероятно, длина дуги или сектор-проекция).
  [ ] sub_4D7F18 / sub_4D7D50 — AllocateOrReuse / RemoveRange в
      TFilmList. Подтвердить.
  [ ] Точный размер TFilmNode (24 байта без поля step, или 28 со step).
  [ ] Реальное значение Extended-константы в CalcFollowRadius для
      "r * coef" в успешной ветке (IDA показывает trash).
  [ ] Реальное значение Extended-константы для NPC random-step
      в FOLLOW к planet (в коде показывается trash).
  [ ] leader[+0x115C float] — назначение поля в CalcFollowRadius
      fallback. Возможно reserve_range или какой-то bonus.
  [ ] Кто пишет OrderData.lo_byte=0/1/2 для FOLLOW_SHIP? Скорее всего
      Talk_Partner_* функции. Проверить xrefs.
  [ ] Условие "d² < HUGE" в CalcRoute (Extended-trash в декомпиле):
      какая реальная константа? (по поведению — всегда true в пределах
      одной звезды).
================================================================================
```

## §16. RE-CHECK (2026-06-29) — что НЕ ДЕЛАЕТ корабль при чейзе движущейся цели

```text
Дополнение по запросу проверки «как строится курс к движущемуся объекту».

Существенно: **корабль НЕ предсказывает lead на корабль-цель**.

Проверка по декомпиляции 0x7219B0 (cm. §5.2.A):

  Order==2 (FOLLOW_OBJ к TShip):
      target_ship.Pos считывается ОДИН РАЗ в начале CalcRoute.
      Дальше A* + RouteFilm — никакого re-evaluation по ходу турна.
      MoveAlongRoute просто потребляет waypoint'ы.

  ⇒ Если враг тоже бежит, к концу хода chaser окажется в точке,
    где enemy был В НАЧАЛЕ хода, а enemy уже сдвинулся.
    Гонка «корабль за кораблём» в SR2 = «один кадр отстать на 1 ход».

  ИСКЛЮЧЕНИЕ: TPlanet-цель — там есть упреждение через
  TPlanet_GetFutureOrbitPos(planet, star.DayFrames). Корабль целится
  туда, КУДА планета прилетит за полный ход. Это работает,
  потому что орбита детерминирована.

  ИСКЛЮЧЕНИЕ-2: FOLLOW_SHIP (Order==6) — но это «формация», не chase.
  Цель здесь — leader.RouteFilm.node(a4-1), то есть точка, КУДА leader
  УЖЕ ЗАПЛАНИРОВАЛ дойти к концу турна. Это упреждение через
  ПОДГЛЯДЫВАНИЕ ЧУЖОГО PLAN'а, а не через лайв-tracking.

Сравнение с СНАРЯДАМИ:
  • Снаряд читает target.Pos каждый кадр (TMissile_StepDay 0x5E6D4C).
  • Поворот ограничен 3°/кадр.
  • Догоняет естественно, ибо AI-цель за кадр сдвинется
    на <= 1.0 единицу, а снаряд может развернуться на 3° и пройти
    несколько единиц вдогонку.

ВЫВОДЫ ДЛЯ AI:
  • Атакующий корабль таранит «вчерашнюю» позицию врага, но
    после каждого End-Turn пересчитывает план заново. Это работает
    «достаточно хорошо» благодаря коротким дистанциям между кораблями
    в одной звезде.
  • Если хотите имитировать lead для UI/AI без правки sub_7219B0 —
    можно перед write Order=2 выставить ship.DestPos = (target.Velocity*ahead),
    т.к. DestPos в этой ветке — это OFFSET от target.Pos (rev 2).
  • Снаряды тактически "более точны", чем dogfight ship-vs-ship,
    потому что у них есть continuous tracking.

ДЕТАЛЬНО ПО СНАРЯДАМ: см. Articles/Missile_Trajectory.md
================================================================================
```
