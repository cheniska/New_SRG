InFear (флаг «в панике») — система принятия страха у NPC
==========================================================

Что это, где хранится
---------------------
InFear — булев байт `TShip+0x3B4` (=948 dec). В памяти Delphi он же встречается
как «_3B4» / «_948». В структуре IDA сейчас безымянный (uint8 `TShip._3B4`).

Он используется как «сегодня я в панике, ищу как унести ноги». Никакой
таймер/градация не привязаны — только 0/1.

Скриптам движка байт открывается через:
- `SE_ShipInFear` @ 0x64E7BC — регистрируется в `MB_about_Scripts` под именем
  `"ShipInFear"` (0x6683BC). Скрипт `SE_ShipInFear(ship)` возвращает `bool =
  ship._3B4 != 0`. Используется в DEF/DIA/сюжетных скриптах.
- В таблице greetings (`ShipGreetings_ParseAll` @ 0x809360) поле-фильтр
  `InFear=Yes|No|Any` (строка @ 0x80E7D4) — см. ниже.

Как вычисляется (per-day)
-------------------------
Один VMT-слот на класс — `AI_ShouldFlee_ChanceBased` (в TNormalShip и всех
трёх наследниках). Функция ЯВЛЯЕТСЯ и решением «беги», и записывающей
`_3B4` (единственный способ его записи в игре):

| Класс                  | Адрес       | VMT-slot(ы)                     |
|------------------------|-------------|---------------------------------|
| TNormalShip / TPirate  | 0x005AEE90  | data-xref @ 0x5AB468            |
| TWarrior               | 0x005B8EA0  | data-xref @ 0x5B5F78            |
| TTransport             | 0x005C1D9C  | data-xref @ 0x5BF7EC            |
| TRanger                | 0x00757188  | data-xref @ 0x74FF2C и 0x78BEE0 |

Все 4 версии заканчиваются одним и тем же паттерном записи:
```
mov byte ptr [self+3B4h], <result>    ; C6 80 B4 03 00 00 XX
```
Найдено find_bytes: 0x5AEECC, 0x5B8EDC, 0x5C1DD8, 0x757253.

Каждый вызов ShouldFlee **перезаписывает** _3B4 (устанавливает 0 или 1) —
поэтому «страх» переоценивается каждый AI-тик и не «залипает» между днями.

Общая формула (TPirate/TNormalShip base — 0x5AEE90)
---------------------------------------------------
```
if TShip_AllWeaponsDisabled(self)                        // все пушки сбиты
   && self.CombatTarget != nil
   && self.CombatTarget.CurStar == self.CurStar:         // цель в той же звезде
   self._3B4 := 1; return true                           // ← гарантированный страх

else if self.Type == 7 && self._1300 != 0:               // спец-ветка (лайнер)
   /* Порог пропорционален размеру. Если он выше моего hull_size ИЛИ
      преследуемый именно я ИЛИ ChanceToWin(self, target) - k < 0 → 1, иначе 0 */

else:
   /* Общий аггрегированный подсчёт */
   total := 0; cnt := 0
   for ship in self.CurStar.Ships:
     if ship == self: continue
     if ship == self.CombatTarget                                            // моя цель
     OR (ship.CombatTarget == self && ship.DestObj == self)                  // меня преследует
     OR (TShip_GetAttitude(ship, self) < 10 && dist²(self, ship) < 250000):  // враждебен и рядом (радиус 500)
       total += TShip_ChanceToWin(ship, self)                                 // ЕГО шанс победить МЕНЯ
       cnt++
       // и если моя текущая CombatTarget уже мёртвая/чужая — переключаемся на этого
   threshold := PortionInDiapason(3.0, 0.0, hull_size, 50.0, my_hp)          // ≈50..3× размера
   effective := total + (cnt-1) * total * ~0                                  // масштабирование множественной угрозы
   result := (effective > threshold)
   self._3B4 := result

if self._3B4:                                                                 // ТОЛЬКО у base/TPirate
   self._1304 *= 0.5                                                          // «морал/агрессия» —2×
```

Различия по классам
-------------------
- **TWarrior (0x5B8EA0)** — упрощённая формула БЕЗ агрегирования: только
  проверка размера-к-размеру и одного ChanceToWin против CombatTarget с
  множителем `0.5 × g_OwnerInfo[owner*8+6]`. Более жёсткие пороги: игнорирует
  бой, если Hull < 130 или Hull ≥ 200 (тяжёлый броневик не боится).
  Не халвит `_1304`.

- **TTransport (0x5C1D9C)** — та же агрегирующая схема, что и у TPirate, НО
  масштабирование множественной угрозы иное: `(cnt-1) * total * 0.5` (пираты
  используют ~0). Дополнительно на входе проверяется VMT+136 (какая-то
  «is-panicking» дополнительная реакция). Не халвит `_1304`.

- **TRanger (0x757188)** — самая сложная версия, четыре ветки:
  1. Если глобальный флаг `Galaxy[1].VMT[0]` (пауза AI?) — `_3B4 := 0`
     всегда.
  2. Если ship в гиперпространстве (`PrevStar||CurStar`, поля +0x1C/+0x20)
     — `_3B4 := (CombatTarget in same star) && (target.Weapons > self.Weapons)`.
     То есть в гипере паникует, только если преследователь ЕЩЁ И сильнее.
  3. Если оружие полностью сбито + CombatTarget в системе → `_3B4 := 1`
     (стандартно).
  4. Иначе — агрегирующая формула (аналогично TTransport), но:
     - радиус «hostile-and-near» больше: dist² < 360000 (радиус 600) вместо 500
     - если враг — Owner==5 (Kling) и dist² < 1440000 (радиус 1200) — тоже в
       счёт (доминаторы страшны на большей дистанции)
     - порог модифицируется полем `+0x51D` (unsigned byte 0..100) через
       `PortionInDiapason` — это «храбрость»/навык рейнджера (Skill_Leader?)
  5. **Пост-фильтр**: если Owner (=+0x3FC → hire/partner) == Player, HP >
     35 %, Player в моей звезде и `_1084` установлен — принудительно
     `_3B4 := 0` («партнёр рейнджера у сильного лидера не паникует»).

Кто вообще вызывает ShouldFlee_ChanceBased
------------------------------------------
- TTransport явно вызывает через VMT в начале `TTransport_AI_DailyDecision`:
  `call [edx+0A0h]` @ 0x5C0750.
- TRanger — то же (через VMT), затем `mov byte [+3B5h], 0`, затем читает `_3B4`.
- Для TWarrior/TPirate функция обычно вызывается косвенно из
  `TNormalShip_AI_FleeIfOutgunned` (0x5B3904) или напрямую как альтернатива.
- В BigWarriorBranch / DailyDecision TWarrior только читают уже установленный
  флаг (`cmp byte [+3B4h], 0`); установка происходит перед этим где-то в
  `sub_5B8EA0` или в стороннем VMT-вызове.

Как InFear ВЛИЯЕТ (что делает NPC)
----------------------------------

**1) Полёт: `TNormalShip_AI_FleeIfOutgunned` (0x5B3904)** — глобальный
    системный аудит. InFear используется как СИЛЬНЫЙ мультипликатор боязни:

    v33 = Rnd(0.85..1.15) × дополнительные модификаторы
    if self._3B4 && !in_pirate_system:  v33 *= ~0    (около нуля — порог обвалится)
    ...
    if |coalition - 2.5*dominator| * v33  ≥  pirate_power  OR  все пушки сбиты:
       — если сейчас в полёте (Order=3): VMT+156 (перевызов task-swap)
       — иначе если рядом мобильная база CB: OrderLandToTarget(CB)
       — иначе: искать ближайшую планету/звезду по углу от угрозы
         и запаниковавшим (InFear && превосходящая сила пиратов &&
         RandRange(0..9)<5): взять просто ближайшую любую (без фильтра
         «не в направлении пиратов»)

Итог: **InFear в _1× режиме означает почти-гарантированный вылет из системы**,
причём не обязательно в безопасном направлении.

**2) TPirate_AI_DailyDecision (0x5AC5C0)** — 2 использования:
    - `5AC5D2`: если `_3B4=0` → пропустить весь «беги в friendly-станцию»
      блок. Если `_3B4=1` → искать TShip_FindFriendlyStation_Same и
      OrderLand на неё (если не в пиратской системе).
    - `5AC9C1`: если `!_428 && !_3B4` → `TPirate_AI_JumpToFreshSystem` .
      **В страхе пират не улетает в новую систему** — он локально ищет
      прикрытие.

**3) TWarrior_AI_DailyDecision (0x5B7190)** и `TWarrior_AI_BigWarriorBranch`
    (0x5B7DA0) — идентичный паттерн:
    - `mov byte [+3B5h], 0` (сброс какого-то соседнего флага)
    - если `_3B4=0` → нормальный патруль/атака
    - если `_3B4=1` → var=0Ah, вызвать VMT+9Ch (наверно PickBaseTarget),
      затем VMT+64h, TShip_FindFriendlyStation_Same, OrderLandToTarget
      (сажаемся на дружественную станцию).

    Дополнительно `sub_5B7560` (5B7581) — «BigWarrior авто-ремонт»: если
    Warrior landed AND `_3B4=1` AND Player_GetBaseProgramCost>0 → потратить
    деньги на репэйр/апгрейд оружия прямо на базе. То есть **страх у
    Big-Warrior монетизируется в апгрейд**.

**4) TTransport_AI_DailyDecision (0x5C0750..)**:
    - VMT+0xA0 (ShouldFlee) вызвана
    - VMT+0x20 вызвана (наверно roam pick)
    - если `_3B4=0`: var_14=7, VMT+0x6C и +0x70 (нормальная торговля/приход)
    - если `_3B4=1`: var_14=8, попытаться OrderLand на dest-станцию, иначе
      искать любую пригодную станцию для приземления

**5) TRanger_AI_DailyDecision (0x751E00..)** — три использования, разные
    ситуации:
    - `751E94`: если ship — партнёр (`_3FC != nil` = OwnerPartner) И
      `_3B4=1` → пропустить «follow-leader» логику (не гонится за лидером
      в чужую звезду).
    - `751FCC`: после VMT+A0 (ShouldFlee), сбросить `_3B5`. Если `_3B4=1`
      → ветка var_C=8 (искать станцию для посадки, аналогично Warrior).
    - `7527CA`: если ранджер не приземлён И `_3B4=0` → нормальный выбор
      целевой звезды `TRanger_AI_PickTargetStar_AvoidBlazer` и лучшей
      торговой планеты. **В страхе рейнджер отказывается от торгового
      плана**.

**6) Диалоги / greetings**:
    - `NormalShip_GetGreetingKey` (0x788330, читает `_3B4` на 789D7C и
      789DA3): для каждой greeting-строки поле InFear имеет значение
      0=Yes-only, 1=No-only, 2=Any. Строка проходит фильтр только если
      её InFear-значение согласуется с текущим _3B4.
      Из `SR_2_HD_Lang_vanilla.txt` видны примеры фраз:
      * `ComputerInFear0..4` — общие ответы
      * `DiplomatFear0..4` — для Type=8 Diplomat
      * `LinerFear0..4` — для Type=7 Liner
    - `sub_5B2950` (комбат-реплика для TPirate, 5B2BEC), `sub_5C41C4` (для
      TTransport, 5C4498), `sub_75BE84` (для TRanger, 75C22C) — в каждом
      если `_3B4=1` или VMT-getter возвращает «flee», выдаётся реплика
      «I'm scared / отвяжись»: подставляется `off_5B2DA0` / аналог, ключ
      `off_5B2E38` (`lk.Attack.ComputerInFear` / `lk.DropGoodsInFear.Drop`
      и пр.).

Побочный эффект самой ShouldFlee (только TPirate/base)
------------------------------------------------------
После записи `_3B4 := v25` (в общей ветке, не early-exit):
```
if (self._3B4)
    self._1304 *= 0.5     // ← поле float, что-то вроде "aggression morale"
```
Т. е. каждый вход в страх ополовинивает `+0x518` — вероятно это либо
`AttackChanceMul`, либо `AggroBoldness`. Это единственное место в игре,
где `_1304` пишется. TWarrior/TTransport/TRanger такого делают ХОТЯТ.

Резюме потоков вызовов
----------------------
```
[per-day AI]
   ├─ TTransport_AI_DailyDecision → VMT[0xA0]=ShouldFlee → пишет _3B4 → читает _3B4 → маршрут
   ├─ TRanger_AI_DailyDecision   → VMT[0xA0]=ShouldFlee → пишет _3B4 → читает _3B4 → маршрут
   ├─ TWarrior_AI_DailyDecision  → (ShouldFlee вызван раньше по цепочке?) → читает _3B4
   ├─ TPirate_AI_DailyDecision   → (ShouldFlee вызван из FleeIfOutgunned) → читает _3B4
   └─ TNormalShip_AI_FleeIfOutgunned → v33 *= ~0 если _3B4 → почти всегда бежит

[скрипт]
   SE_ShipInFear(ship) = ship._3B4 != 0

[greetings/dialog]
   NormalShip_GetGreetingKey ⇒ фильтр строк по InFear-полю
   sub_5B2950 / sub_5C41C4 / sub_75BE84 ⇒ комбат-реплика "In Fear"
```

Точки для мода
--------------
- Хотите, чтобы NPC вообще не паниковал → в его ShouldFlee_ChanceBased
  на первой инструкции записывать `mov byte [eax+3B4h], 0; ret`
  (или занулять xor eax, eax + ret).
- Хотите, чтобы в страхе NPC был АГРЕССИВНЕЕ (не бежал) → изменить
  `v33 *= ~0` в `TNormalShip_AI_FleeIfOutgunned` на `v33 *= 1.0`.
- Хотите, чтобы рейнджер-партнёр стал трусом с лидером — снять
  пост-фильтр в 0x7575B0 (условие `_1084`).
- Хотите изменить порог агрегирующей формулы — константа 250000 (dist²) в
  0x5AEF14 area (TPirate) / 360000 в TRanger.
- Хотите триггерить InFear скриптом — прямо `WriteMem ship+0x3B4 = 1`;
  проживёт до следующего AI-тика.

Что мне НЕ удалось верифицировать
---------------------------------
- Точное имя VMT-слота 0xA0 (называю его условно «AI_ShouldFlee_ChanceBased»).
  Явных data-xref-ов на 4 функции достаточно для идентификации, но
  реального словесного тэга нет.

Соседние поля: TShip._3B5 (=Forsage) и _1304 (=stress accumulator)
=================================================================

_3B5 = TShip.Forsage (форсаж двигателя, boolean byte)
-----------------------------------------------------
**Не «второй флаг паники»**. Это стандартный ускоритель, найдена цепочка:
- `SE_OrderForsage` (0x6406B8) — Delphi-скрипт `OrderForsage(ship [, on])`,
  явный комментарий "TShip.Forsage @ +0x3B5 (boolean)". При включении
  проверяет `SlotCount(st_Forsage)>0` и `AbleToUse(Engine)`, вызывает
  `TShip_CalcParam` (пересчёт скорости), для Player обновляет маршрут на
  звёздной карте (`TShip_CalcRoute`).
- `sub_6ED224` (0x6ED224) — UI-toggle кнопки форсажа: играет
  `Sound.ForsageOn` / `Sound.ForsageOff` (строки @ 0x6ED354/0x6ED378),
  запускает анимацию `FormShip.ForsageActivate`.
- `TShip_StepDayStart_Main` (несколько мест) — проигрывает film-effect
  каждый тик, пока Forsage активен.
- `TShip_StepDay_MoveProcessor` @ 0x720474 — сбрасывает Forsage=0 сразу
  после `TShip_ChangeState` (то есть после посадки/стыковки/прибытия).
- `TShip_StepDay_Inherited` @ 0x6FECA7 — сброс Forsage=0 в общем per-step
  входе.
- `TGalaxy_ProcessDayEvents` @ 0x840FB9, `TStar_NextDay_1` @ 0x86C8C5 —
  сброс в глобальных tick-обработчиках.
- `TTranclucator_AI_ReturnAsArtefact` @ 0x5CF57C — сброс при превращении
  компаньона в артефакт.

**AI-функции классов сбрасывают Forsage в начале DailyDecision** — это те 4
записи, которые «сопровождают» проверку InFear:
| Функция                      | Адрес    | Комментарий              |
|------------------------------|----------|--------------------------|
| TPirate_AI_DailyDecision     | 0x5AC580 | сброс перед решениями    |
| TWarrior_AI_DailyDecision    | 0x5B7191 | + повтор на 0x5B733C     |
| TWarrior_AI_BigWarriorBranch | 0x5B7DA1 | + повтор на 0x5B8099     |
| TRanger_AI_DailyDecision     | 0x751FCC | сброс перед AI-логикой   |

Смысл: **AI не оперирует форсажом**. Форсаж — это тактический per-frame
эффект (плата: топливо + возможный износ), не стратегическое решение AI-тика.
Поэтому AI обнуляет флаг перед принятием дневных решений, чтобы не
унаследовать «залипшее» состояние с прошлого фрейма боя. NPC могут
получить форсаж только через *явный* скриптовый вызов (SE_OrderForsage)
или через UI (только Player).

Как байт-паттерн `mov byte [reg+3B5h], 0` соседствует с проверкой `_3B4`
— это две **несвязанные операции** в одной функции: AI сбрасывает форсаж
и потом читает флаг паники. Смешивать их в один «двойной флаг» нельзя.

_1304 = TNormalShip._518: float "stress/hesitation accumulator"
---------------------------------------------------------------
Плавающее число, персистируется в save (добавлено в save-версии 79 — см.
loader `sub_5ABF1C`). Загружается вместе с `_1296` (int, добавлено в
v60) и `_1300` (byte). Не имя-именован.

**Семантика**: аккумулятор боевого стресса / нерешительности.

Writers (все — `+= const` или `*=`, кроме одного reset):
| Функция                              | Адрес    | Действие         |
|--------------------------------------|----------|------------------|
| CB_WarPlans_WarOperation_OnConfirm   | 0x599FFF | `+= flt_59A2C0`  |
| TPirate_AI_DailyDecision             | 0x5AC4FF | `+= flt_5ACE9C`  |
| TNormalShip_AI_ShouldFlee_ChanceBased| 0x5AF29B | `*= 0.5` (fear!) |
| TNormalShip_VCanAttackTarget         | 0x5AF6F9 | `:= 0` (reset)   |
| TNormalShip_AI_PickAggroTarget       | 0x5B1104 | `+= ratio*const` |
| TPlanet_PirateRaid_Sub3              | 0x776326 | `+= flt_7764B4`  |
| (loader)  sub_5ABF1C                 | 0x5ABF92 | загрузка из save |

Readers:
- `TNormalShip_VCanAttackTarget` (0x5AF64C) @ 5AF6AD — множитель для
  ChanceToWin в разрешении атаки:
  ```
  can_attack = threshold > ChanceToWin(self, target) * (_1304 + 1.0)
  ```
  Больше `_1304` → правая часть больше → меньше шанс `can_attack=true` →
  **выше _1304, реже атакую**. Причём при подтверждённой атаке своей
  CombatTarget поле обнуляется (0x5AF6F9) — «стресс сбросился боем».

- `TNormalShip_AI_FleeIfOutgunned` (0x5B3904) @ 5B3AAF — множитель для
  общего flee-порога:
  ```
  factor = PortionInDiapason(0.1, 1.0, 15.0, 0.0, _1304)
  v33 *= factor
  ...
  will_flee := |coalition - 2.5*dominator| * v33 >= pirate_power
  ```
  _1304=0 → factor=1.0 (no-op); _1304=15 → factor=0.1 (порог обрушен).
  **Выше _1304, охотнее бегу.**

- Loader и `CB_WarPlans_WarOperation_OnConfirm` — доступ ради save/load
  и первичной инициализации.

Как накапливается (примеры add-паттернов):
- **Player даёт CB приказ идти в бой** через War-Plans: _1304 у
  назначенного корабля растёт на константу (599FF6).
- **Пиратский рейд планеты** (TPlanet_PirateRaid_Sub3): каждый корабль,
  участвующий в рейде — прирост _1304.
- **TPirate.AI daily**: если CurStar != DestStar (пират-в-полёте) — прирост.
- **PickAggroTarget**: при выборе новой aggro-цели — прирост,
  пропорциональный какой-то величине (индекс приоритета цели).

Как сбрасывается / уменьшается:
- **Проведённая атака** своей CombatTarget → `_1304 := 0` (полный сброс).
- **Установка InFear** → `_1304 *= 0.5` (частичный сброс, разгрузка через
  переход в дискретное состояние «паника»).

Итоговая психологическая модель для NPC базы TNormalShip / TPirate:
```
[накопление стресса]  бой/атаки/долгие миссии  →  _1304 растёт
[последствия]         больше атак реже, больше бегу чаще
[дискретный триггер]  порог агрессии по чужим ChanceToWin  →  _3B4=1 (fear)
                      _1304 → _1304/2 (частичная разрядка)
[разрешение]          успешная атака (VCanAttackTarget=true, target=my CT)
                      _1304 := 0 (полный сброс)
```

Мод-точки для _1304:
- Обнулить накопление стресса → NOP add-инструкции.
- «Всегда атакует» → занулить множитель в VCanAttackTarget: `(_1304+1.0)`
  → `1.0` (0x5AF6D0 area).
- «Никогда не убегает от накопленного стресса» → занулить фактор в
  FleeIfOutgunned: заменить call PortionInDiapason на `fld1`.
- Обнуление при InFear → изменить `*= 0.5` → `*= 0.0` (сброс в 0) в
  0x5AF29B — тогда после паники pirate «забывает» весь накопленный
  стресс.

Классовая принадлежность полей _1300/_1304
------------------------------------------
Loader `sub_5ABF1C` — TPirate/TNormalShip-specific (загружает +1296, +1300,
+1304 после parent-load). `_1300` (byte) читается в
`TNormalShip_AI_ShouldFlee_ChanceBased` ветке Type==7 (лайнер) как
дополнительный гейт — вероятно «уязвимый пассажир на борту». `_1300` и
`_1304` находятся ЗА границей 1296-байтного TNormalShip → они принадлежат
уже дочернему классу (TPirate 0x?; TRanger 1376; TWarrior/TTransport
конкретной длины не знаю). Практически: доступ через offset единый в
базовых AI функциях, значит слоты выровнены во всех наследниках.

Файлы/адреса-ссылки
-------------------
- `SE_ShipInFear` @ 0x64E7BC (регистрация в 0x662890)
- Строка "InFear" @ 0x80E7D4 (парсер greetings @ 0x809360)
- Строка "ShipInFear" @ 0x6683BC
- 4 setter'а: 0x5AEECC, 0x5B8EDC, 0x5C1DD8, 0x757253
- 15+ reader'ов (`cmp byte [reg+3B4h], 0`) в AI функциях всех 4 классов и
  в greetings/dialogs
