# ОСНАЩЕНИЕ ДОМИНАТОРОВ И ТАБЛИЦЫ ИХ ПАРАМЕТРОВ — КОНСОЛИДИРОВАННЫЙ ДОКУМЕНТ

> Диздок перенесён из `Dominator_Equipment_Consolidated.md` без правок текста: разделы оформлены заголовками,
> содержимое — дословно, моноширинным блоком (в исходнике — ASCII-вёрстка).

```text
Дата: 2026-06-04
Объединяет:
  • Ship_Equipment_Layout.txt              (04.06)
  • Dominator_Equipment_Article.txt        (04.06, v1)
  • Dominator_Equipment_Article_v2.txt     (04.06, v2 ← приоритет)
  • Off_882670_Structure.txt               (04.06)
  • Off_882670_Verified_Fields.txt         (04.06, ← приоритет)
  • Dominator_Stats_Table.txt              (04.06, dword_87B390)

Источники: TKling_CreateMinionsForStar, TKling_CreateMinion (0x5C6E94),
TKling_InitFromGalaxyConfig (0x5C5FD4), TKling_SetupBossStats (0x5C6490),
TShip_RegisterEquipment (0x70E980), TShip_SlotGetEquipment (0x724824).
```

## ЧАСТЬ I. ОБЩАЯ РАЗМЕТКА ОБОРУДОВАНИЯ КОРАБЛЯ

```text
Каждый корабль (TShip) имеет ДВА ВИДА оборудования:

  1. SINGLE-SLOT (одиночные) — 8 предметов, каждый ровно в одном экземпляре:
       Hull, FuelTanks, Engine, Radar, Scanner, RepairRobot, CargoHook,
       DefGenerator
     Хранятся в массиве FEquip[] — прямые указатели в TShip.

  2. MULTI-SLOT — переменное количество:
       Weapons (типы 50..68) — до 5 слотов
       Artefacts (типы 8..41) — до 4 слотов
     Хранятся в TList'ах (EquipmentItems, ArtefactItems) + быстрый
     доступ для weapons через массив указателей в TShip.

КАРТА ItemType (TItemType ENUM):
  ItemType  Категория          Где хранится
  ----      ---------          ------------
  8..41     Artefacts          TShip.ArtefactItems @ +0x3BC (TList)
  42        Hull               TShip.FEquip_Hull          @ +0xF8
  43        FuelTanks          TShip.FEquip_FuelTanks     @ +0xFC
  44        Engine             TShip.FEquip_Engine        @ +0x100
  45        Radar              TShip.FEquip_Radar         @ +0x104
  46        Scanner            TShip.FEquip_Scanner       @ +0x108
  47        RepairRobot        TShip.FEquip_RepairRobot   @ +0x10C
  48        CargoHook          TShip.FEquip_CargoHook     @ +0x110
  49        DefGenerator       TShip.FEquip_DefGenerator  @ +0x114
  50..68    Weapons (19 типов) TShip.weapons[5] @ +0x118..+0x12C

Формула offset для single-slot: ship + (type + 20) × 4 = ship + 80 + type×4
  Например: type=42 → 80 + 168 = 248 = 0xF8 ✓

РАСКЛАДКА TShip:
  Offset    Имя поля                Тип       Назначение
  +0xF8     FEquip_Hull             DWORD ptr type 42
  +0xFC     FEquip_FuelTanks        DWORD ptr type 43
  +0x100    FEquip_Engine           DWORD ptr type 44
  +0x104    FEquip_Radar            DWORD ptr type 45
  +0x108    FEquip_Scanner          DWORD ptr type 46
  +0x10C    FEquip_RepairRobot      DWORD ptr type 47
  +0x110    FEquip_CargoHook        DWORD ptr type 48
  +0x114    FEquip_DefGenerator     DWORD ptr type 49
  +0x118    weapons[0..4]           5×DWORD   weapons
  +0x12C    weapon_cnt              BYTE
  +0x3B8    EquipmentItems          TList*    общий список
  +0x3BC    ArtefactItems           TList*    4 слота
  +0x3C0    DropItems               TList*
```

## ЧАСТЬ II. ЛОГИКА УСТАНОВКИ

```text
TShip_RegisterEquipment (0x70E980):
  itemType := item.+0C (байт типа)

  Если itemType ∈ [42..49] (Single-slot):
    slot := ship + (itemType + 20)×4
    Если slot занят: destroy_existing
    slot := item

  Если itemType ∈ [50..68] (Weapons):
    Если ship.weapon_cnt < 5: ++ship.weapon_cnt
    slot := ship + (weapon_cnt × 4) + 0x114
    Если slot занят: destroy_existing
    slot := item

  Иначе (Artefacts 8..41): не обрабатывается, добавляются в TList отдельно.

  TEquipment_CalcStats(item) — пересчёт характеристик.

TShip_SlotGetEquipment (0x724824, для скриптов ship.SlotGetEquipment):
  category := sub_7DE6A4(itemType)
    43→0, 44→1, ..., 49→6 (single-slot)
    50..68→7 (Weapons)
    8..41→8 (Artefacts)
    прочее→10
  Если category ∈ [0..7]: list := ship.EquipmentItems
  Иначе:                   list := ship.ArtefactItems
  Перебор: item с (item.+0x49 активный) и (item.+0x4C & 0x7F)==slotIdx → return.

ПЕРЕИМЕНОВАННЫЕ ФУНКЦИИ:
  Старое       Новое                       Назначение
  sub_71AED0   TShip_InstallHull           Hull
  sub_71AFCC   TShip_InstallFuelTanks      FuelTanks
  sub_71B028   TShip_InstallEngine         Engine
  sub_71B084   TShip_InstallRadar          Radar
  sub_71B0E0   TShip_InstallScanner        Scanner
  sub_71B13C   TShip_InstallRepairRobot    RepairRobot
  sub_71B198   TShip_InstallCargoHook      CargoHook
  sub_71B1F4   TShip_InstallDefGenerator   DefGenerator
  sub_71B250   TShip_InstallWeapon         Weapon (для каждого slot)
```

## ЧАСТЬ III. POWER LEVEL ДОМИНАТОРА — главная метрика мощи (1..7)

```text
ФОРМУЛА (TKling_CreateMinionsForStar):

  difficultyBase := 125 × Galaxy_GetDifficultyIndex(galaxy)
                    ; DifficultyIndex обычно 0..4 (Лёгкая..Невозможная)
                    ; +0, +125, +250, +375, +500

  dayBonus := PortionInDiapason(Galaxy.Day, 300, 22200, 0, 3000)
              ; Линейно от 0 (Day=301) до 3000 (Day=22200)

  base := difficultyBase + dayBonus

  deltaWinAdjust := -150 × Galaxy.FWarDeltaWin[Klings]
                    ; самобалансирующая коррекция (см. часть IV)

  nearbyBonus := 40 × count_neighbor_kling_stars_same_series / 4
                 ; +10 за каждого ближнего Клинга той же серии

  total := base + deltaWinAdjust + nearbyBonus

  power := total / 1000 + 1
  Если RandRange(0, 1000) < total mod 1000: power += 1
  Если KlingType == 6 (Bertor):  power += 1
  Если KlingType == 7 (Klig):    power -= 1
  power := clamp(power, 1, 7)

CheatKlingStrength override (при Day ≥ 666):
  Cheat=1: power = max(power, 5)
  Cheat=2: power = max(power, 6)
  Cheat=3: power = 7 (всегда максимум)

ПРИМЕРЫ:
  Day=301, сложность=0, нет соседних Klings:
    base=0, total=0, power=1
  Day=11250, сложность=2, 5 соседних той же серии:
    base = 250 + 1500 = 1750
    nearby = 200/4 = 50
    total = 1800 → power = 2..3
  Day=22200, сложность=3, 10 соседних той же серии:
    base = 375 + 3000 = 3375
    nearby = 400/4 = 100
    total = 3475 → power = 4..5
  Day=5000, сложность=2, Klings выиграли 5 систем (DeltaWin=5):
    base = 250 + 660 = 910
    delta = -150*5 = -750
    total = 160 → power = 1 (НЕРФ)
  Day=5000, сложность=2, Klings потеряли 3 системы (DeltaWin=-3):
    base = 910, delta = +450
    total = 1360 → power = 2 (БАФФ)
```

## ЧАСТЬ IV. Galaxy.FWarDeltaWin[3] — счётчики хода войны

```text
МАССИВ ИЗ 3 ЭЛЕМЕНТОВ:
  Galaxy.FWarDeltaWin[0] = Normals @ +0xF4
  Galaxy.FWarDeltaWin[1] = Klings  @ +0xF8
  Galaxy.FWarDeltaWin[2] = Pirates @ +0xFC

Подтверждено source-кодом (new_func.txt):
  procedure SF_DeltaWin(av:array of TVarEC; code:TCodeEC);
  var owner: TStarOwners;
  begin
    if(High(av) < 1) then raise Exception.Create('Error.Script DeltaWin');
    owner := TStarOwners(av[1].VInt);
    av[0].VInt := Galaxy.FWarDeltaWin[owner];
    if(High(av) > 1) then Galaxy.FWarDeltaWin[owner] := av[2].VInt;
  end;

СЕМАНТИКА: «преимущество фракции X в войне за галактику».
  Положительное = фракция захватывает системы (выигрывает)
  Отрицательное = фракция теряет
  Близкое к 0 = баланс

КАК МЕНЯЕТСЯ:

(A) При смене владения звезды (TStar_UpdateBattleState_And_PlanetOwners):
    DecWarDeltaWin(старый_владелец)
    IncWarDeltaWin(новый_владелец)
    Каждый захват системы трогает ДВЕ ячейки.

(B) В TGalaxy_NextDay PHASE 0 (ежедневная балансировка):
    При экстремальном дисбалансе:
      Перевес против коалиции (v50 > 2.0): шанс 30/1000 → --FWarDeltaWin[Pirates]
      ... (10/1000 при v50>1.5, 3/1000 при v50>1.25)
      Перевес против Klings: аналогично → ++FWarDeltaWin[Pirates],
                                          --FWarDeltaWin[Normals]
    Phase 0 НЕ меняет FWarDeltaWin[Klings] напрямую — только через
    смену владения звёзд.

(C) Через скрипты — SF_DeltaWin позволяет читать и писать.

САМОБАЛАНСИРУЮЩАЯ ЛОГИКА:
  TGalaxy_IncWarDeltaWin(faction):
    Если FWarDeltaWin[faction] < -1:
      FWarDeltaWin[faction] := FWarDeltaWin[faction] / 2   (а не +1!)
    Иначе:
      FWarDeltaWin[faction] += 1

  TGalaxy_DecWarDeltaWin(faction):
    Если FWarDeltaWin[faction] > 1:
      FWarDeltaWin[faction] := FWarDeltaWin[faction] / 2   (а не -1!)
    Иначе:
      FWarDeltaWin[faction] -= 1

Когда фракция уже сильно впереди (>1) или сильно отстаёт (<-1), следующее
изменение НЕ просто ±1, а ПОЛОВИНИТ значение в сторону 0. Это «смягчающий»
механизм, не позволяющий счётчику убегать.

ВЛИЯНИЕ НА POWER LEVEL КЛИНГА:
  -150 × Galaxy.FWarDeltaWin[Klings]
    Klings выигрывают войну → -150*positive → СНИЖАЕТ power новых Клингов
    Klings проигрывают       → -150*negative → ПОВЫШАЕТ power новых Клингов
Игра нерфит «слишком успешных» доминаторов и баффит «отстающих».

ПРИМЕРЫ:
  FWarDeltaWin[Klings] = 0:    0      (нет влияния)
  FWarDeltaWin[Klings] = 5:    -750   (≈ -1 ранг power)
  FWarDeltaWin[Klings] = -5:   +750   (≈ +1 ранг power)
  FWarDeltaWin[Klings] = 10:   -1500  (-1.5 ранга)
```

## ЧАСТЬ V. ТАБЛИЦА dword_87B390 (стат-параметры по power)

```text
Адрес: 0x0087B390 в .data, размер 532 байта = 133 dword.
Layout: 7 power_level × 19 параметров.
Индексация: `dword_87B390[19 * power_level - 20 + idx]` для idx=1..19.

ВСЕ значения — параметры PortionInDiapason(out_min, out_max, day_max, day_min, current_day):
интерполяция по Galaxy.Day между out_min (раннее) и out_max (позднее).
Day-диапазон обычно 300..11250.

ПАРНЫЕ СЛОТЫ (idx_a, idx_b) = (значение в начале, значение в late-game):
  (2, 3)   Hull equipment quality              SetupEquip
  (4, 5)   RepairRobot equipment quality       SetupEquip
  (6, 7)   DefGenerator equipment quality      SetupEquip
  (8, 9)   Weapon equipment quality            SetupEquip
  (10, 11) Engine equipment quality            SetupEquip
  (12, 13) Weapon count (число оружия)         SetupEquip
  (14, 15) RepairRobot install probability %   Finalize
  (16, 17) DefGenerator install probability %  Finalize
  (18, 19) Equipment downgrade chance %        SetupEquip
  idx 1    (агрегат — TBD)

ЗНАЧЕНИЯ ПО POWER_LEVEL:

Power | idx 1 | 2,3   | 4,5   | 6,7   | 8,9   | 10,11 | 12,13 | 14,15  | 16,17  | 18,19
------+-------+-------+-------+-------+-------+-------+-------+--------+--------+--------
P1    | 65    | (1,3) | (1,3) | (1,3) | (1,3) | (1,5) | (1,2) | (5,25) | (5,20) | (0,10)
P2    | 75    | (1,5) | (1,5) | (1,5) | (1,5) | (2,6) | (1,3) | (15,50)| (10,40)| (0,20)
P3    | 85    | (2,7) | (2,7) | (2,7) | (2,7) | (3,7) | (2,4) | (30,75)| (20,60)| (15,40)
P4    | 95    | (3,8) | (3,8) | (3,8) | (3,8) | (4,8) | (2,5) | (45,95)| (30,80)| (30,80)
P5    | 95    | (4,8) | (4,8) | (4,8) | (4,8) | (6,8) | (3,5) | (50,95)| (50,95)| (40,95)
P6    | 95    | (6,8) | (6,8) | (6,8) | (6,8) | (7,8) | (4,5) | (75,95)| (75,95)| (70,95)
P7    | 95    | (8,8) | (8,8) | (8,8) | (8,8) | (8,8) | (5,5) | (95,95)| (95,95)| (95,95)

ИНТЕРПРЕТАЦИЯ:
  • Quality (idx 2..11): уровень предмета (1=мусор, 8=максимум)
  • WpnCount (idx 12,13): 1..5 пушек
  • Install% (idx 14..17): вероятность установки опционального
  • Downgrade% (idx 18,19): шанс УДАЛЕНИЯ оборудования (компенсация мощи у P7)

ВАЖНО (исправлено из v1): «5%..25%» — это НЕ диапазон вероятностей в смысле
«случайно 5-25%», а пара out_min/out_max для интерполяции по Day:
  install_chance = PortionInDiapason(Day, Day_start, Day_max, table[a], table[b])

ПРИМЕР для RepairRobot, power=1:
  Day=301:   install_chance ≈ 5%
  Day=2500:  ≈ 11%
  Day=6000:  ≈ 17%
  Day=11250: ≈ 25%
  Day=22200: 25% (за пределами шкалы, на максимуме)

ПРИМЕР для RepairRobot, power=7:
  table[14] = table[15] = 95 → MIN = MAX = 95% (всегда)

ПЕРЕРАСЧЁТ install chance (Day 301 → 11250):
  RepairRobot:                 DefGenerator:
  P1: 5%..25%                  P1: 5%..20%
  P2: 15%..50%                 P2: 10%..40%
  P3: 30%..75%                 P3: 20%..60%
  P4: 45%..95%                 P4: 30%..80%
  P5: 50%..95%                 P5: 50%..95%
  P6: 75%..95%                 P6: 75%..95%
  P7: 95% (всегда)             P7: 95% (всегда)
```

## ЧАСТЬ VI. КАК ОСНАЩАЕТСЯ ДОМИНАТОР (TKling_CreateMinionsForStar)

```text
ВСЕГДА устанавливается (8 слотов):
  1. Hull (корпус)            — всегда
  2. Engine                   — всегда
  3. FuelTanks                — всегда
  4. Radar                    — всегда
  5. Scanner                  — всегда
  6. CargoHook                — всегда

ОПЦИОНАЛЬНО:
  7. RepairRobot:
     RandRange(1,100) ≤ install_chance[power] (см. часть V)
  8. DefGenerator:
     RandRange(1,100) ≤ install_chance[power]

ПОСЛЕДОВАТЕЛЬНОСТЬ УСТАНОВКИ:

(1) HULL — @ ship+0xF8
    SetupEquip(2, 3)     ; уровень из dword_87B390
    SetupHull(base_hull_min, base_hull_max)
           ; per-KlingType базовые из off_882670[14*KlingType+3..4]
    TShip_InstallHull (sub_71AED0)

(2) ENGINE — @ ship+0x100
    SetupEquip(10, 11)
    SetupWeapons(gvar_008829BC[0])
    TShip_InstallEngine

(3) REPAIR ROBOT — @ ship+0x10C [УСЛОВНО]
    Условие: RandRange(1,100) ≤ SetupEquip(14, 15)
    SetupEquip(4, 5)
    SetupWeapons(gvar_008829A0[0])
    TShip_InstallRepairRobot

(4) DEF GENERATOR — @ ship+0x114 [УСЛОВНО]
    Условие: RandRange(1,100) ≤ SetupEquip(16, 17)
    SetupEquip(6, 7)
    SetupWeapons(gvar_008829E0)
    TShip_InstallDefGenerator

(5) FUEL TANKS — @ ship+0xFC [GTL-CLAMP]
    GTL := Galaxy.GTL
    SetupHull(1, GTL)
    SetupWeapons(gvar_008822FC)
    TShip_InstallFuelTanks

(6) RADAR — @ ship+0x104 [GTL-CLAMP]
    SetupHull(1, GTL)
    SetupWeapons(gvar_00882B74[0])
    TShip_InstallRadar

(7) SCANNER — @ ship+0x108 [GTL-CLAMP]
    SetupHull(1, GTL)
    SetupWeapons(gvar_00882174[0])
    TShip_InstallScanner

(8) CARGO HOOK — @ ship+0x110 [GTL-CLAMP до 7]
    GTL_clamped := min(GTL, 7)
    SetupHull(1, GTL_clamped)
    SetupWeapons(gvar_00882978)
    TShip_InstallCargoHook

WEAPONS (циклически):
  weapon_count := SetupEquip(12, 13)
  Для слота 1..weapon_count:
    weapon_id выбирается weighted random из dword_87AC10
      Перебор itemType 50..64 (15 типов; 65..68 — спец-серии)
      Вес: dword_87AC10[120*table_level + 15*KlingType + (weapon_id-50)]
      table_level из dword_87ABF0[power] (см. ниже)
    SetupEquip(8, 9) для weapon уровня
    SetupEngine(weapon_template, params: v92=dword_87ABB4[2*KT], v91=...+1)
    TShip_InstallWeapon

ARTEFACTS:
  artefact_count := SetupEquip(18, 19)
  Сложная система с ограничениями по KlingType.+1233 (не разобрано полностью).

DOWNGRADE PHASE (по Series, idx 18,19):
  Blazeroid → удаляет DefGenerator
  Kelleroid → удаляет sub_70E6A4 эквип
  Terronoid → удаляет RepairRobot

После всего TShip_CalcParam пересчитывает derived stats.
```

## ЧАСТЬ VII. ВЛИЯНИЕ GTL И ДРУГИХ ФАКТОРОВ

```text
ВЛИЯНИЕ GTL — минимальное, ТОЛЬКО 4 вспомогательных предмета:
  • FuelTanks: уровень = RandRange(1, GTL)
  • Radar:     RandRange(1, GTL)
  • Scanner:   RandRange(1, GTL)
  • CargoHook: RandRange(1, min(GTL, 7))

GTL НЕ влияет на:
  Hull (по KlingType), Engine (по power), RepairRobot/DefGenerator (по power),
  Weapons (по power + KlingType + Series).

ИТОГ: GTL — «технологичность датчиков и инфраструктуры». Боевая опасность
Клинга определяется power_level.
```

## ЧАСТЬ VIII. РОЛЬ Серии (Blazeroid/Kelleroid/Terronoid)

```text
ОБЫЧНО — серия НЕ влияет на оборудование (только на финальный downgrade).

ПРИ AA_KlingRacialWeapons (флаг Anti-Aging моде):
  Применяются ограничения weapon_type по Series:
    Blazeroid (0): запрещены weapon_type 62, 63
    Kelleroid (1): запрещены weapon_type 62, 64
    Terronoid (2): запрещены weapon_type 63, 64
  Каждая серия теряет 2 weapon_type, оставляя 13 из 15.

Внутри игры это «своё» расовое оружие у каждой серии (тип 62 = Blazer-spec
и т.д.). Вне AA-режима ограничения сняты.
```

## ЧАСТЬ IX. ТАБЛИЦА off_882670 (per-KlingType data)

```text
Адрес: 0x00882670 в .rdata
Размер: 448 байт = 112 dwords
Layout: 8 KlingType × 56 байт (14 dwords) = 112 dwords

⚠ ЭТО НЕ массив указателей, как изначально показывает IDA.
Это PACKED MIXED-TYPE TABLE. IDA рендерит dwords как «offset xxx» потому что
значения попадают в диапазон .data адресов, но это совпадение. Байты
интерпретируются как разные типы (byte/word/dword/qword/double) в зависимости
от того, как их читает код.

ИНДЕКСАЦИЯ (подтверждено в TKling_CreateMinion @ 0x5C6E94):
  STRIDE = 56 байт = 14 dwords на запись.
  8 KlingType × 14 dwords = 112 dwords.

Аргументы TKling_CreateMinion:
  a1 (eax) = TKling self
  a2 (edx) = KlingType byte (0..7) — записывается в kling[+0x4D0]
  a3       = parent star
  a4       = eDominatorSeries (0..2) — записывается в kling[+0x4D1]

ПОДТВЕРЖДЁННЫЕ СЛОТЫ:
  Slot   Offset  Тип           Назначение
  0..2   0..8    ?             Не используются напрямую
  3      0x0C    int           SetupHull(min, ...) - 1-й arg для RandRange
                                (передаётся как Hull min)
  4      0x10    int           SetupHull(..., max) - 2-й arg
                                (Hull max; результат × gvar_008829F0 → Hull size)
  5      0x14    BSTR ptr      Имя серии (UNICODE string), доступ в WStrAsg
                                в TKling_InitFromGalaxyConfig
  6+7    0x18    qword/double  Множитель денег миньона:
                                money = ROUND(Galaxy[+0x64] × this)
  8      0x20    word          Scale для стата Kling[+0x3A4] = NodePayload:
                                stat = ROUND((RndOut01() + 0.5) × this)
                                (flt_5C7178 = 0.5)
  9      0x24    ?             Не идентифицирован
  10     0x28    int           IntToStr-нумерация в FormRating UI (rating-id)
  11..13 0x2C+   ?             Не идентифицированы напрямую; основной
                                источник статов миньона — dword_87B390

ВАЖНО (поправка из Off_882670_Verified_Fields.txt):
  На уровне СТАТИЧЕСКИХ байт в .rdata большинство «pointer-like» значений —
  это указатели на 8-байтовые структуры формата
  {pointer_VMT_0x008867F4, dword_field_id_0xFFxx} (Delphi property accessor).
  ОДНАКО:
    Адрес 0x008867F4 в .rdata = 32 байта НУЛЕЙ.
    gvar_008829F0 ведёт на 0x00889F4C = 4 байта НУЛЕЙ.
  Таблица заполняется в runtime при инициализации Delphi RTL — статически
  содержит только placeholder-структуры. Без отладки реальные значения slots
  9, 11..13 не известны.

ПРИМЕР Boss (KT=0) slot 6+7:
  Байты `B8 B7 87 00 24 BF 88 00`
  Как little-endian double: near-zero denormal ≈ 0
  → Galaxy[+0x64] × ≈0 = 0 → money = 0
  Значит: миньоны Boss-доминатора рождаются без денег.

ИСПОЛЬЗОВАНИЕ В РАЗНЫХ ФУНКЦИЯХ:
  (A) TKling_CreateMinion @ 0x5C6E94
      [off_882670 + KT*56 + 0x18] — qword/double для money
      [off_882670 + KT*56 + 0x20] — word для +0x3A4 стата
  (B) sub_51BBBC @ 0x51C645 (FormRating UI)
      [off_882670 + KT*56 + 0x28] — int → IntToStr (rating-id)
  (C) TKling_InitFromGalaxyConfig @ 0x5C5FD4
      off_882670[14*KlingType + Series], Series=5 hardcoded
      Используется как BSTR-указатель в WStrAsg (slot 5 = имя варианта).
  (D) TKling_SetupBossStats @ 0x5C6490 (xrefs 0x5C64FD, 0x5C658D)
      Это **второй конструктор** для Boss другой Series, НЕ override таблицы.
      Различия sub_5C5FD4 vs sub_5C6490 (оба — Boss с KT=0):
        param         5C5FD4              5C6490
        Series        5                   1
        Hull mul      ×6000               ×4000
        Primary wpn   64×2,62,60          64,63,62,58
        Extra wpn     61                  59
      У боссов есть разные варианты по сериям, для каждого свой hardcoded конструктор.
  (E) TKling_SpawnInStar @ 0x5C68D8
  (F) WB_NextRank_ShowDialog @ 0x57BBD0 (UI в WB-меню)

ТОЧНЫЕ ВОПРОСЫ:
  ✓ TKling_SetupBossStats — не override, а отдельный конструктор Boss-Kelleroid
  ✓ Galaxy[+0x64] — int-поле в money-формуле (точное имя неизвестно — кандидаты
    Day, DifficultyIndex, TechLevel, NodeFactor)
  ✓ Kling[+0x3A4] = NodePayload — базовый «рейтинг/мощь» миньона:
    Boss: задаётся напрямую через Galaxy_GetNodeFactor
    Минион: ROUND((RndOut01() + 0.5) × off_882670[KT*14+8 word])
  ✗ Slots 9, 11..13 — без runtime-отладки не определены
```

## ЧАСТЬ X. ДОПОЛНИТЕЛЬНЫЕ ТАБЛИЦЫ ОРУЖЕЙНОЙ СИСТЕМЫ

```text
dword_87ABF0 @ 0x0087ABF0 (32 байта = 8 dwords)
  Маппинг power_level → weapon_tier_index:
    power 0 → 5 (unused placeholder)
    power 1 → 1
    power 2 → 1
    power 3 → 2
    power 4 → 2
    power 5 → 3
    power 6 → 3
    power 7 → 4
  Используется как v86 в индексации dword_87AC10.

dword_87ABB4/dword_87ABB8 @ 0x0087ABB4 (64 байта)
  Layout: 8 KlingType × 2 dwords = (param_a, param_b) на каждый KT.
  Передаётся как (v92, v91) в SetupEngine(weapon_data, v92, v91, ...) — per-KT
  качество engine оружия:
    KlingType         (a, b)   Комментарий
    Boss     (KT=0)   (1, 1)   бедное engine — компенсация мощи
    Equentor (KT=1)   (1, 3)   стандартный middle-tier
    Urgant   (KT=2)   (2, 4)   средний
    Smersh   (KT=3)   (3, 4)   хорошее, узкий диапазон
    Menok    (KT=4)   (3, 5)   хорошее, шире
    Shtip    (KT=5)   (4, 5)   лучшее среди обычных
    Bertor   (KT=6)   (1, 1)   «странный» — отдельный AI
    Klig     (KT=7)   (4, 5)   топ

  При v74=58..61 (item type=4) — hardcoded override (v92=2, v91=1).

dword_87AC10 @ 0x0087AC10 (~1920 байт)
  Layout: 4 weapon_tier × 8 KlingType × 15 weapon_id = 480 dwords
  Индексация: dword_87AC10[120 * weapon_tier - 170 + 15 * KT + (weapon_id-50)]
  Вероятностные веса для weapon roll. Суммируются в running total (v79) и
  сравниваются с rolled HP (v84 = 1..100). Когда сумма превышает порог —
  выбирается weapon_id (диапазон 50..64).

dword_87C0FC @ 0x0087C0FC (160 байт)
  Выбор подтипа KlingType при спавне.
  8 KlingTypes × 5 density-levels = 40 dword.
  См. Spawn_Rules_Consolidated.md часть IV для полной таблицы.
```

## ЧАСТЬ XI. КАК POWER ОПРЕДЕЛЯЕТ ОСНАЩЕНИЕ — ИНТЕРПРЕТАЦИЯ

```text
Power 1 (самый слабый):
  Quality 1..3, 1..2 пушки, 5..25% RepairBot (по Day), 5..20% DefGen.
  Это «Shtip-пехота» — слабое массовое мясо.

Power 4 (средне-высокий):
  Quality 3..8, 2..5 пушек, 45..95% RepairBot, 30..80% DefGen.

Power 7 (максимум, late-game):
  Всё quality 8, ровно 5 пушек, всё опц. 95%, downgrade тоже 95%.
  Это элитный финальный босс.
```

## ЧАСТЬ XII. РОЛЬ KlingType В ОСНАЩЕНИИ

```text
KlingType влияет на:
  1. Hull (через off_882670 — per-KT границы; Boss/Bertor/Equentor имеют
     свои уникальные семейства корпусов)
  2. Engine quality (через dword_87ABB4/B8)
  3. Распределение weapons (через dword_87AC10 с индексом KlingType)
  4. Финальный power через бонусы: Bertor +1, Klig -1

Сам KlingType выбирается по таблице вероятностей (dword_87C0FC), которая
зависит от klings_density. КОСВЕННО плотность Klings влияет на их типы.

«Расового» weapon-pool по KlingType НЕТ — только по Series (и только в режиме
AA_KlingRacialWeapons).
```

## ЧАСТЬ XIII. ИТОГОВАЯ ТАБЛИЦА ВЛИЯНИЯ

```text
ЧТО влияет на КАЖДЫЙ ПРЕДМЕТ:
  Hull          power + off_882670[KlingType]  — НЕТ зависимости от GTL
  Engine        power (dword_87B390[power, 10..11]) + dword_87ABB4
  FuelTanks     GTL [1..GTL]
  Radar         GTL [1..GTL]
  Scanner       GTL [1..GTL]
  CargoHook     GTL clamped to 7 [1..min(GTL,7)]
  RepairRobot   power (опц., шанс 5..95% интерпол. по Day)
  DefGenerator  power (опц., шанс 5..95% интерпол. по Day)
  Weapon types  power + KlingType (через dword_87AC10)
                + Series (только при AA_KlingRacialWeapons)
  Weapon levels power (8..9 поля dword_87B390)
  Weapon count  power (12..13 поля): 1..5
  Artefact count power (18..19 поля): 0..1

ЧТО влияет на POWER:
  Сложность      +125 × DifficultyIndex (0..500)
  Время (Day)    +0..3000 (Day 301..22200)
  Окружение      +10 за каждого ближнего Klinga той же серии
  FWarDeltaWin[Klings]  -150 × значение (нерф при выигрыше / бафф при проигрыше)
  KlingType      Bertor +1, Klig -1
  Cheat          CheatKlingStrength 1/2/3 → min power 5/6/7
```

## ЧАСТЬ XIV. КРАТКИЕ ОТВЕТЫ

```text
(1) Что такое DeltaWin?
    Galaxy.FWarDeltaWin[3] — массив счётчиков для 3 фракций. Изменяется
    при захвате/потере систем (TStar_UpdateBattleState) и в Phase 0.
    С самобалансирующим halving при экстремуме. Для Клингов используется
    как нерф/бафф power (-150 × value).

(2) Что значит "5..25%" для install chance?
    Пара MIN/MAX, интерполируется по Galaxy.Day (Day=301 → 5%, Day=11250 → 25%).

(3) Влияет ли DeltaWin на оружие/оборудование?
    Косвенно через power_level: DeltaWin меняет power, power → оснащение.

(4) Off_882670 — что это?
    Packed mixed-type таблица для 8 KlingType × 14 dwords. Содержит имя
    серии (BSTR), Hull min/max, money-multiplier (double), per-KT stat scale,
    rating-id для UI.

(5) Зависит ли оборудование от Series?
    Обычно НЕТ. Исключение — AA_KlingRacialWeapons (запрещает 2 weapon_type
    каждой серии).

(6) Сколько пушек у доминатора?
    weaponCount = RandRange из dword_87B390[19*power + 11..12]:
      P1: 1..2  P2: 1..3  P3: 2..4  P4: 2..5  P5..P7: ≤5 (физ. предел)

(7) Связь off_882670 и dword_87B390:
    • off_882670[KT*14 + slot] — per-KlingType (имя, hull base, money, stat scale)
    • dword_87B390[power*19 + idx] — power-level-зависимые параметры
      (одинаковы для всех KT)
    Индивидуальность миньона = KlingType (имя+Hull+rating) × power (quality+count).
```

## КОНЕЦ ДОКУМЕНТА
