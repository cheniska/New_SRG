# ПРАВИЛА СПАВНА КОРАБЛЕЙ В ГАЛАКТИКЕ — КОНСОЛИДИРОВАННЫЙ ДОКУМЕНТ

> Диздок перенесён из `Spawn_Rules_Consolidated.md` без правок текста: разделы оформлены заголовками,
> содержимое — дословно, моноширинным блоком (в исходнике — ASCII-вёрстка).

```text
Дата составления: 2026-06-04
Объединяет:
  • Spawn_Rules_Article.txt              (03.06, обзорная статья)
  • TPlanet_NextDay_analysis.txt         (03.06, детальный разбор функции)
  • Dominator_Spawn_Detailed.txt         (03.06, расчёт v25/v27/v29)
  • Coalition_Spawn_Article.txt + v2     (04.06, v2 имеет приоритет)
  • Dominator_Spawn_And_Drops.txt        (04.06, Bertor→Klig + drops)

Источники: TPlanet.NextDay (0x774348), TPlanet_NextDay_TrySpawnKling (0x773DF4),
TPlanet_BuyRandomKling (0x77BDD4), TThreadCreateNewGame_Execute_inner,
TKling_Boss_SpawnFollowers (0x5CAF9C), TShip_TakeWeaponDamage_FullCycle (0x704490).

ВРЕМЯ В ИГРЕ: игра стартует с Galaxy.Day = 301 (Day 0..300 — pre-game).
Все формулы используют этот сдвиг.
```

## ЧАСТЬ I. НАЧАЛЬНАЯ РАЗДАЧА КОРАБЛЕЙ (TThreadCreateNewGame_Execute_inner)

```text
Перебор планет ПОСЛЕДОВАТЕЛЬНЫЙ (`for i := 0 to Galaxy.Planets.Count-1`),
не случайный. Первые планеты в списке получают рейнджеров первыми.

ДВЕ ПРИНУДИТЕЛЬНЫЕ ПЕРЕЗАПИСИ ВЛАДЕЛЬЦА ПЕРЕД РАЗДАЧЕЙ:
  1. Если планета принадлежит коалиции/Пиратскому Клану, но звезда у Клингов —
     планета переводится Клингам.
  2. Если у планеты есть владелец (не None) и звезда у пиратов —
     планета переводится Пиратскому Клану.

ПО ВЛАДЕЛЬЦАМ:

Coalition (Owner 0..4, Maloc/Peleng/People/Fei/Gaal):
  • 2 транспорта (random subtype)
  • 1 воин
  • +1 рейнджер, если Galaxy.Rangers.Count < CntOwnersSystem(Normals) × 1.2

Klings (Owner=5):
  Цикл while count<10 на звезде, прерывается если count≥8 И мощь сбалансирована.

None (Owner=6): ничего.

PirateClan (Owner=7):
  • 2 пирата + 3 военных-пирата

Подтип транспорта (RandRange 0..100, в TTransport_InitShip @ 0x5BFC8C):
  v<46  → Cargo Transport (46%)
  46..79→ Liner (34%)
  80..99→ Diplomat (~20%)
Распределение фиксированное, не зависит от расы/сложности/времени.
```

## ЧАСТЬ II. ПОВСЕДНЕВНЫЙ ДОСПАВН: ОБЩИЕ ПРИНЦИПЫ

```text
КАЖДЫЙ ДЕНЬ TStar.NextDay (0x863998) для каждой не-игроковой звезды вызывает
TPlanet.NextDay (0x774348) для каждой планеты. TStar.NextDay САМ НИЧЕГО НЕ
СПАВНИТ — это оркестратор.

Спавн происходит исключительно через:
  1) TPlanet.NextDay (per-day, per-planet)
  2) TThreadCreateNewGame_Execute_inner (один раз при генерации)
  3) Script Engine (SE_Buy*) — на запрос скрипта
  4) TKling_Boss_SpawnFollowers (свита боссов, см. ниже)

ОБЩИЕ ФЛАГИ-ГЕЙТЫ:
  • Planet.Flag_NoBuyShips @ +0x15E — отключает все Buy*-вызовы
  • Planet.Flag_NoMilitaryUpdate @ +0x15F — отключает ProcessMilitaryUpdate
  • Planet._160 — отключает многие спавн-блоки

ДИСПЕТЧЕР ПО Planet.Owner @ +0x74:
  Owner < 5  → ветка Coalition          @ 0x77442C
  Owner == 5 → Klings                    @ 0x774D12
  Owner == 6 → None (uninhabited)        @ 0x774DF1 → TPlanet_CheckDominatorLiberation
  Owner == 7 → PirateClan                @ 0x774DFE
  Owner ≥ 8  → default skip              @ 0x775BFD

ВАЖНЫЙ ТРЮК В КОДЕ: проверки вида `*(int*)&Star.ShipTypeCnt_WB < 5` — это НЕ
"WB < 5". Компилятор Delphi генерирует `cmp dword ptr [eax+0xA0], 5` (читает
сразу 4 байта). В TStar по +0x98..+0xA5 лежит ShipTypeCnt[14]:
  +0x98 t_Kling  +0x99 Ranger  +0x9A Transport  +0x9B Pirate  +0x9C Warrior
  +0x9D Tranclucator  +0x9E RC  +0x9F PB
  +0xA0 WB  +0xA1 SB  +0xA2 BK  +0xA3 MC  +0xA4 CB  +0xA5 UB
Все базы кроме RC/PB имеют максимум 1 на систему.

  `*(int*)[WB] < 5`  ⟺  "В системе НЕТ Science Base, Black Knight и Medic Center"
                        (SB=BK=MC=0 И WB<5)
  `*(int*)[CB] < 2`  ⟺  "Нет UB/Ruins/_A7, CB ∈ {0,1}"
```

## ЧАСТЬ III. КОАЛИЦИОННЫЕ ПЛАНЕТЫ (Owner 0..4)

```text
PHASE 1: Рост населения и дохода
  Лимит населения зависит от радиуса (radius 60 → 100k, radius 100 → 1M).
  Если PeopleCnt < лимит: рост по 1% в день.
  Если PeopleCnt > лимит: +300/день (фиксированный).
  Planet.Money += PeopleCnt × мелкий коэффициент.

PHASE 2: Спавн кораблей (только если !Planet._160 И !Flag_NoBuyShips)
И ТОЛЬКО если Constellation.Id != 20 (НЕ PirateConstellation):

(1) BuyRanger — 4% в день, при отставании коалиции:
    target = min(N × 1.5, 63)
    R = число активных (не в тюрьме) рейнджеров
    Если R >= target + econ_offset (Galaxy+0x198, обычно 0): спавн НЕТ.
    Иначе если CalcPopulationDelta < CalcEconomicFactors + 1
         И RndOut01 < 0.04: → BuyRanger
    В начальной раздаче цель = N × 1.2 (мягче).

(2) BuyTransport — 5% в день, кап 2 на планете:
    Условия:
      • *(int*)[WB] < 5 (нет тяжёлых станций SB/BK/MC)
      • 9 × CntOwnersSystem(Normals) + 3 × CntOwnersSystem(Pirates)
        > Galaxy.TransportCount_Galactic
      • RndOut01 < 0.05
      • Planet.TransportCnt < 2
    → BuyTransport(mode=0)  // random subtype

    ⚠ Поле Galaxy.+0x16C исторически называлось "PirateSpawnTimer" — это
    misnomer. На самом деле это счётчик ЖИВЫХ транспортов в галактике
    (TransportCount_Galactic): растёт при TTransport_InitShip, падает при
    TTransport.Destroy. Это «спрос (9N+3P) против предложения (текущие
    транспорты)», а не таймер.

(3) BuyPirate — "местный пират" / vassal:
    Условия:
      • *(int*)[CB] < 2 (нет UB/Ruins)
      • CntOwnersSystem(Normals) > Galaxy.PirateCnt
      • (RaceMood/10)² > RndOut01, где RaceMood = off_882A80[42*Race+38]
    → TPlanet_BuyPirate

BLOCK B — Warrior spawn (работает ВСЕГДА, даже в PirateConstellation):
(4) k := if CntOwnersSystem(Normals) > 1 then 1 else 2
       (последняя выжившая Normals-система получает k=2)
    Если RndOut01 < 0.01 × k:
      threshold := k × PortionInDiapason(Planet.Radius, 60, 100, 1, 3)
      Если Warriors.Count < threshold:
        → BuyWarrior
      Иначе если Galaxy.FlagmansToBuild[Race] > 0 И RndOut01 < 0.1:
        → BuyBigWarrior(monKoef=200)
        Galaxy.FlagmansToBuild[Race] -= 1

    threshold по радиусу:
      Маленькая (r=60):    1 × k = 1 или 2
      Средняя   (r=80):    2 × k = 2 или 4
      Большая   (r=100):   3 × k = 3 или 6

    Galaxy.FlagmansToBuild[5] — массив byte 0..255 по расе. Заказы НЕ
    инкрементируются автоматически в найденном коде, ставятся при генерации
    или скриптами/квестами. Когда счётчик расы доходит до 0 — больше
    флагманов у этой расы не будет.

PHASE 3: Управление существующими Warriors
  Если HasHostileShips: warriors прыгают защищать систему.
  Иначе: с шансом 5% Refit; деньги при бедности 1-5k или 2-10k
  (clamp(RangersMaxCapital/15, 1k..5k) или /7); ImprovementItems (5%);
  при критических условиях (<5% Normals, <0.2 health) RandRange(500,1500)
  → IncPoints → FreePointsToSkill.
```

## ЧАСТЬ IV. KLING-ПЛАНЕТЫ (Owner=5)

```text
PHASE 1: Экономика обнуляется
  for i in 0..7: Planet.Goods[i].Count := 0
  Planet.Money := 0

PHASE 2: ApplyInventionBoost (~70%), UpdateEquipmentShop (~5%)

PHASE 3: TPlanet_NextDay_TrySpawnKling (если !Flag_NoBuyShips)

PHASE 4: HasHostileShips → warriors с homePlanet=self прыгают в звезду
```

## TPlanet_NextDay_TrySpawnKling (0x773DF4) — детальный разбор

```text
ПРЕДВАРИТЕЛЬНЫЙ GUARD:
  Series_Terronoid (=2): запрещён спавн если
    Terron мёртв ИЛИ Galaxy.TerronGrowLockTurn != 0
    ИЛИ Galaxy.TerronToStar >= 0x40000000
  Series_Blazeroid (=0): запрещён если
    Blazer мёртв ИЛИ Galaxy.BlazerLanding != 0 ИЛИ Blazer.+0x460 != 0
  Series_Kelleroid (=1) и default — без guard'ов.

РАСЧЁТ ЛИМИТА АКТИВНЫХ КЛИНГОВ НА ЗВЕЗДЕ (v29):
  Базовый кап по серии:
    Blazeroid: 13   Kelleroid: 15   Terronoid: 11   default: 10

  density_score = ROUND(count_same_series_in_galaxy / Galaxy.Stars.Count × ~100)
  random_jitter = Rnd(0, 5, planet.Rnd + Galaxy.Day/500)
  corr1 = PortionInDiapason(density_score, 0, 33, base_cap, 10)
          (низкая плотность → full cap; средняя → 10; линейно)
  corr2 = PortionInDiapason(density_score, 34, 100, 0, 3)
          (при перенаселении дополнительный срез)
  series_growth = sub_857588(galaxy, Star.Series)
  gtl_bonus = PortionInDiapason(series_growth, 0, 1, 0, 3)
  v29 = random_jitter + corr1 - corr2 + gtl_bonus
  Итог: base_cap ± 3-5.

  CheatKlingStrength override (Day≥666): v29 = 15 для всех уровней.

РАСЧЁТ ВЕРОЯТНОСТИ СПАВНА (v27, 0..100):
  Базовая «мощность серии» (peak_power):
    Blazeroid: 40   Kelleroid: 20   Terronoid: 60   default: 20

  klings_pct = TGalaxy_PercentOwnersSystem(galaxy, 1)
  peak_point = PortionInDiapason(Galaxy.Day, 300, 11250, 40, 80)
               (точка пика растёт с 40% до 80% по ходу игры)

  Треугольный профиль:
    Если klings_pct ≤ peak_point:
      v27_raw = lerp(klings_pct, 0..peak_point → 0..peak_power/2)
    Иначе:
      v27_raw = lerp(klings_pct, peak_point..100 → peak_power/2..1.0)

  +10 если Star.Battle (буст в активных боях)
  Если active_count == 0: v27 принудительно высокое (первый спавн гарантирован)

  CheatKlingStrength override (Day≥666):
    Cheat=1: v27=70  Cheat=2: v27=85  Cheat=3: v27=100

РАСЧЁТ COOLDOWN (v25, дни без спавна):
  v24_raw = sub_85E418(galaxy)         (галактический NPC spawn interval, GTL-shaped)
  v24 = ROUND(sub_85E230(galaxy, v24_raw, 0.333, 81.0))   (нормализация ~0..81)

  Профиль cooldown (треугольный):
    Если klings_pct ≤ peak_point:
      v25 = lerp(klings_pct, 0..peak_point → 1..v24/2)
    Иначе:
      v25 = lerp(klings_pct, peak_point..100 → v24/2..0)

  CheatKlingStrength override (Day≥666):
    Cheat=1: v25=3   Cheat=2: v25=2   Cheat=3: v25=1

ФИНАЛЬНОЕ УСЛОВИЕ СПАВНА (все три):
  1) Star.DayWithoutCreateShip > v25
  2) RandRange(1, 100) ≤ v27
  3) active_minions < v29
  → TPlanet_BuyRandomKling

Star.DayWithoutCreateShip автоматически инкрементируется каждый день и
сбрасывается в 0 при любом спавне любого ship в этой звезде.
```

## ТАБЛИЦА ПОДТИПОВ КЛИНГОВ dword_87C0FC (BuyRandomKling)

```text
Уровень в таблице (1..5 по плотности Klings в галактике):
  level = ROUND(PortionInDiapason(CntOwnersSystem(Klings), 0, 100, 5, 1))

Границы:
  klingsCount   level    название уровня
  0 .. 12       5        «Klings почти отсутствуют»
  13 .. 37      4        «Klings редки»
  38 .. 62      3        «Klings средне распространены»
  63 .. 87      2        «Klings широко распространены»
  88 .. ∞       1        «Klings подавляют галактику»

Каждый шаг ≈ 25 систем = -1 уровень.

ТАБЛИЦА (raw values, total ≈ 1000):
           Boss  Equentor  Urgant  Smersh  Menok  Shtip  Bertor  Klig  | Total
level 1:     0      50       50     100     200    600     0      0    | 1000
level 2:     0      50       50     150     250    500     2      0    | 1002
level 3:     0      50      100     200     250    400    10      0    | 1010
level 4:     0     100      100     250     250    300    20      0    | 1020
level 5:     0     100      150     300     250    200    30      0    | 1030

В ПРОЦЕНТАХ:
           Boss  Equentor  Urgant  Smersh  Menok   Shtip  Bertor  Klig
level 1:   0.0%    5.0%    5.0%   10.0%   20.0%   60.0%   0.0%   0.0%
level 5:   0.0%    9.7%   14.6%   29.1%   24.3%   19.4%   2.9%   0.0%

КЛЮЧЕВЫЕ НАБЛЮДЕНИЯ:
  • Boss (KT=0) и Klig (KT=7) НИКОГДА не появляются через random spawn.
    Boss = уникальный босс серии (создаётся при генерации галактики).
    Klig = спавнится только Bertor'ом (см. часть VII).
  • Shtip — самый распространённый при доминировании Klings (60% при lvl 1).
  • Bertor — шанс растёт когда Klings редеют (2.9% при lvl 5, 0% при lvl 1).
  • Доп. проверки в BuyRandomKling:
    - Bertor (KT=6) исключается если он уже есть в созвездии
      (sub_856D08(Constellation, Series)) ИЛИ это его родная планета
      (gvar_882498 == self).
    - Если выбор не удался → fallback = Shtip.

КОРРЕКЦИЯ СЕРИИ ПЕРЕД СПАВНОМ (в TPlanet_BuyKling, если Star.Owner != 1):
  1) Series==Terronoid И Terron жив И TerronToStar >= 0x40000000:
     Series → Kelleroid (Терронцы превращаются если Террон почти прибыл)
  2) Series==Blazeroid И Blazer самоуничтожился:
     Series → Kelleroid (после смерти Блейзера серия конвертируется)
После коррекций TKling_CreateMinionsForStar создаёт корабль.
```

## ЧАСТЬ V. ПИРАТСКИЕ ПЛАНЕТЫ (Owner=7)

```text
PHASE 1: Penalty к Rangers (если !Planet._160 И PirateWinType != 3)
  Если PercentOwnersSystem(Pirates) > 2× PercentOwnersSystem(Normals) И >10%
       И !CoalitionDefeatedTurn:
    для всех Rangers: GetRelationToRanger(-1)
  Если PercentOwnersSystem(Pirates) > 4× PercentOwnersSystem(Normals) И >20%
       И !CoalitionDefeatedTurn:
    ещё раз -1 для всех Rangers
  TPlanet_ProcessResearch
  Если !Flag_NoMilitaryUpdate: TPlanet_ProcessMilitaryUpdate
  Planet.Gov := 0, Planet.Economy := 2

PHASE 2: Рост населения (как Owner<5)

PHASE 3: Инициализация mod1..mod4 по Galaxy.PirateWinType
  v110 := if CntOwnersSystem(Pirates) > 0 then TGalaxy_PrepareDayData else 0.0
  По умолчанию mod1=mod2=mod3=mod4=1.0
  switch PirateWinType:
    0 (peace):    mod1=mod2 = 0.125*v110 + 1.0,  mod3=mod4 = 1.0
    1 (war):      mod1 = 1 - 0.125*v110,         mod2 = 0.375*v110+1, m3=m1, m4=m2
    2 (war):     mod1 = 0.375*v110+1,           mod2 = 1 - 0.125*v110, m3=m1, m4=m2
    3 (won):      mod1 = 1 - 0.125*v110,         mod2 = 1 - 0.25*v110,  m3=m1, m4=m2
    5 (special):  mod1=1.25, mod2=1.125, mod3=1.25, mod4=1.125

PHASE 4: Коррекция по сложности Galaxy.DiffLevels[0]
  level 0: m1,m2 *= 0.5    level 1: ×1.0    level 2: ×1.2    level≥3: (lvl-2)*0.2+1.2

PHASE 5: Затухание mod3, mod4 по v110
  factor := clamp(0.01 * v110² + 1, 0, 2)
  mod3 *= factor,  mod4 *= factor

PHASE 6: modMon = 200 (default)

PHASE 7: Коррекция по соседним звёздам (если Constellation != 20)
  Просматриваются 10 ближайших звёзд:
    Owner=0 (Normals): score += 2
    Owner != 1 (любой не-Kling): иначе --score
  mod1, mod2 *= PortionInDiapason(score, -10, 20, 0.5, 2.0)
  mod3, mod4 *= PortionInDiapason(score, -10, 20, 0.8, 1.2)
  modMon = PortionInDiapason(score, -10, 20, 50, 200)

PHASE 8: Коррекция по NPC worth (если !PirateConstellation или Player в системе)
  worth := sum по всем TNormalShip на star: ship[+0x4F4] байт
  mod1, mod2 *= (1 - worth/15)

PHASE 9: Star.Battle penalty
  Если Star.Battle: mod1, mod2 *= 0.05 (!) — хаос боя резко сокращает спавн

PHASE 10: Kling-research спавн (если Star.Id == KellerNewResearch И Keller жив)
  TPlanet_NextDay_TrySpawnKling, ApplyInventionBoost
  Tranclucator-планеты: с шансом 5% RandRange(0,4) → Planet.Gov

PHASE 11 (BLOCK A): Pirate/Warrior spawn (если !Flag_NoBuyShips И !KellerResearch)
  prtCnt1 := CntPowerPirate(Scavengers only)
  prtCnt2 := CntPowerPirate(warriors only)
  mod1 /= max(prtCnt1 / 9, 1)
  mod2 /= max(prtCnt2 / 21, 1)

  Scavenger (FPirateType=0):
    Если prtCnt1 < 3 × mod3
       И Galaxy.PiratesClanCnt < CntOwnersSystem(Pirates) × (3*mod3 + 6*mod4)
       И RndOut01 < 0.02 × mod1:
      ship := BuyPirate(round(modMon))
      Если Constellation==20 И PirateWinType!=3:
        upgrade_loop(mod3):
          ImprovementItems + IncPoints(FPoints/7) + SMoney × 8/7
          modUpg *= 0.85
        FreePointsToSkill + Refit×3

  Warrior-pirate (FPirateType 1..3 = Blocker/Flanker/Supporter):
    Если prtCnt2 < 7 × mod4 И RndOut01 < 0.05 × mod2:
      ship := BuyWarrior(round(modMon))
      Если Constellation==20 И PirateWinType!=3:
        upgrade_loop(mod4)
    Подтип внутри BuyWarrior на PirateClan-планете:
      roll ≤ 3: Blocker  /  roll 4..7: Flanker  /  roll 8..9: Supporter

PHASE 12: BuyTransport на пиратских планетах (НЕ в PirateConstellation)
  Если Constellation != 20 И CntPowerPirate(0,1) > 0:
    Стандарт (коалиция жива):
      !CoalitionDefeatedTurn И *(int*)[WB] < 5
      И 9*N + 3*P > TransportCount  И RndOut01 < 0.02  И TransportCnt < 1:
        → BuyTransport
    Усиленный (после поражения коалиции):
      CoalitionDefeatedTurn > 0 И *(int*)[WB] < 5
      И 5*CntOwnersSystem(Pirates) > TransportCount  И RndOut01 < 0.05  И TransportCnt < 2:
        → BuyTransport

PHASE 13: ApplyInventionBoost, UpdateEquipmentShop refresh
```

## ЧАСТЬ VI. ВЕРОЯТНОСТНЫЕ КОНСТАНТЫ В TPlanet.NextDay

```text
Все значения tbyte (80-bit extended) — НЕ путать с раздачами IDA double:

  Addr          Value  Где
  tbyte_775CD4  0.7    ApplyInventionBoost prob (Owner=5)
  tbyte_775CE0  0.02   Block A BuyPirate, Phase 12 BuyTransport #1
  tbyte_775CEC  0.001  редкое событие
  tbyte_775D00  0.04   BuyRanger в Owner<5
  tbyte_775D0C  0.05   BuyTransport Owner<5 / Block A BuyWarrior / Phase 12 #2
  tbyte_775D1C  0.01   Block B Warrior daily roll
  tbyte_775D28  0.1    BuyBigWarrior внутри BlockB
  tbyte_775D34  0.2    Refit-related
  tbyte_775D40  0.3    вспомогательное
  tbyte_775D4C  0.8    вспомогательное
  tbyte_775D58  0.6    вспомогательное
  tbyte_775D78  0.85   modUpg decay в Block A
  tbyte_775D84  1.12   population/economy
  tbyte_775D90  0.12   population/economy
  tbyte_775DA4  0.4    вспомогательное
```

## ЧАСТЬ VII. БОССЫ ВЫЗЫВАЮТ СВИТУ (TKling_Boss_SpawnFollowers)

```text
Помимо обычного per-day спавна, БОССЫ (Blazer, Keller, Terron, Bertor)
самостоятельно вызывают приспешников.

ОБЩИЙ АЛГОРИТМ (TKling_Boss_SpawnFollowers @ 0x5CAF9C):
  1. Подсчёт текущих миньонов: ship.+1020 == self
  2. Гейт: roll = RandRange(0, 10, boss.rnd_seed)
     if roll >= (target_count - currentCount): СПАВН
  3. Цикл while currentCount + spawnedNow < target:
     newMinion = TPlanet_BuyKling(context, klingType)
     newMinion.+1020 = boss (marker)
     newMinion.x/y = boss.x/y
     newMinion.target = boss
     OrderJump(newMinion)
     newMinion.angle = atan2(...)
     spawnedNow++

----- BERTOR (KlingType=6) → KLIG (KlingType=7) -----
Источник: TKling_Bertor_NextDay (0x5C8888)

Когда срабатывает (каждый день, если):
  • bertor.Order != 3 (не в режиме прыжка)
  • bertor в космосе (CanAct)
  • нет mission flag

Параметры: TKling_Boss_SpawnFollowers(bertor, klingType=7, target_count=5)

Шансы прохода гейта в день:
  currentKligs=0 → 6/11 ≈ 54.5%
  currentKligs=1 → 7/11 ≈ 63.6%
  currentKligs=2 → 8/11 ≈ 72.7%
  currentKligs=3 → 9/11 ≈ 81.8%
  currentKligs=4 → 10/11 ≈ 90.9%
  currentKligs=5 → выходит из цикла

Контекст спавна (dword_889BE0):
  context.star = bertor.star
  bertor.star.+73 = bertor.Series (временный хак для генерации)
  context.pos = bertor.pos
  После цикла: bertor.star.+73 ← восстановить

Если spawnedNow > 0: TShip_ChangeState(bertor, 0).

ИТОГ: Bertor стремится иметь ровно 5 Klig вокруг себя. Создаёт эффект "роя".
```

## ЧАСТЬ VIII. ОСВОБОДИТЕЛЬНЫЙ/ВОЕННЫЙ ОТРЯД КОАЛИЦИИ

```text
⚠ Это «WB → Enemy Star» — НАСТУПАТЕЛЬНЫЙ механизм Коалиции, не освобождение.

Источник: TGalaxy_SpawnHelper_FindSuitableStar (0x85C9A8) / TGalaxy_NextDay фаза 15
News template: GalaxyNews.WBGoToEnemyStar.Create

УСЛОВИЯ ЗАПУСКА:
  • Galaxy.Day >= 300
  • Day mod 133 == 0
  • RandFloat01 > 50%
  • !Galaxy.CoalitionDefeatedTurn
  • На какой-то звезде есть Военная База (WB, type=8)
  • Есть подходящая «вражеская» звезда

ДЕЙСТВИЕ:
  • WB-станция как source
  • Ближайшая вражеская звезда как target
  • count = Rnd(4, 6) воинов через BuyWarrior(monKoef=200)
    (или BigWarrior если FlagmansToBuild[Race] > 0)
  • Каждому приказ лететь в target
  • Уведомление в GalaxyNews
```

## ЧАСТЬ IX. ПИРАТСКИЙ ДИВЕРСИОННЫЙ РЕЙД

```text
Источник: TPlanet_NextDay_PirateRaidGroup (0x776A94, бывш. TPlanet_ProcessMilitaryUpdate)
Вызывается из TPlanet.NextDay Owner=7 ветка.
News template: Artefacts.ArtAnalyzer.AttackPirates (видна только при skill ArtAnalyzer > 0)

УСЛОВИЯ:
  • Galaxy.PirateWinType != 3 И != 5
  • Day mod (55 - 5 × Galaxy_difficulty_index) == 0
    (на высокой сложности рейды чаще)
  • RandRange(1, 100) ≤ raid_probability
    raid_probability = PortionInDiapason(KlingExtraSpawn, 0..5, 0.3..1.0)

АЛГОРИТМ:
  Перебор всех звёзд Owner=0 (Coalition):
    Фильтр: не в бою, не Flag_NoComeKling, не в KellerSpawnHelper
    Учёт расстояния до игрока (на ранних ходах — далеко от игрока)
    Подсчёт v37 (no-standing ships), v38 (standing=1), v39 (warriors), v40 (pirates)
    Поиск v42 (рейнджер-капитан с type=7=Klig)
    Score = pirate_helper / (2*warrior + ranger + 10) × RandRange(10, 15)
    Лучшая → target.

  На целевой звезде:
    Выбирается случайная планета (sub_860C58)
    Временно Planet.Owner := 7 (PirateClan)
    Порождаются 6-8 пирато-воинов через BuyWarrior с приказом атаковать v42
    Planet.Owner восстанавливается
```

## ЧАСТЬ X. ДРОП НОДОВ И ОБЛОМКОВ (TShip_TakeWeaponDamage_FullCycle @ 0x704490)

```text
ПРЕДВАРИТЕЛЬНЫЙ ГЕЙТ: if target.Owner != 5: SKIP (только Kling-owned ships).

----- САЙТ 1 @ 0x705895 — основной drop, КАЖДОЕ ПОПАДАНИЕ -----
  coin = (Galaxy.tick + ship.rnd_seed) & 1
  if coin == 0: DROP TProtoplasm                            // 50%
  else:
    if ship.KlingType == 0 (Boss): DROP TProtoplasm         // 100% для Boss
    else: DROP TUselessItem (Remains)                       // 50% минионам

  NodeCount = ship.NodePayload + 1   (ship.NodePayload = Kling[+0x3A4])
  protoplasm.Series = ship.Series (для UI цвета)
  Сгусток добавляется в ship.EquipmentItems.

----- САЙТ 2 @ 0x706020 — малый бонус, не для Terron -----
  if ship == Terron: SKIP
  coin = (Galaxy.tick + ship.rnd_seed) & 3
  if coin != 0: SKIP                                        // 75% пропусков
  if ship.star.items.count >= 25: SKIP                       // антиспам

  NodeCount = RandRange(nodePayload/8, nodePayload/4, seed) + 1

----- САЙТ 3 @ 0x7060FC — death-bonus drop -----
  if ship.willDieFlag == 0: SKIP
  if !(arg_0 & 0x010000): SKIP
  if RndOut01() >= 0.95: SKIP                                // 95% chance

  NodeCount ≈ nodePayload/2

ОБЩАЯ ПИКОВАЯ ВЫДАЧА:
  Boss (nodePayload=100), 10 попаданий + смерть:
    Сайт 1: 10 × 100% × 101 ноду = ~1010
    Сайт 2: ~2.5 × ~20 = ~50
    Сайт 3: 1 × 50 = ~50
    Итого: ~1110 нодов
  Минион (nodePayload=20), 3 попадания:
    Сайт 1: ~1.5 сгустка по 21 ноду = ~30 + ~1.5 Remains
    Сайт 2: ~0.75 × ~5 = ~4
    Сайт 3: 1 × 10 = ~10
    Итого: ~44 нода + 1-2 Remains

----- REMAINS (TUselessItem) -----
Имя строки: "Remains" @ 0x7066BC
typeIndex = (Galaxy.tick / 10) + ship.+0xE0
series = ship.Series (сохраняется)
sub_746454(uselessItem, off_7066BC, typeIndex, 0, ship.Series)

ЭКОНОМИКА:
  • TProtoplasm (ноды) сдаются в RC-базах через RC_SaleNod_ShowDialog (0x5740DC).
    Это депозит на нод-счёт игрока, не разовая продажа.
  • TUselessItem (Remains) обмениваются у BigWarrior'ов через
    Talk_MilitarySupport_SellRemains (0x6192CC). Не за деньги — на УСЛУГИ
    (буст, ремонт). BigWarrior — это NPC, не «военная база».

ДОП. МНОЖИТЕЛИ ПРИ УРОНЕ (arg_0 биты):
  0x40    → sub_72DA10 (множитель урон→loot): Kling ×0.123, Other ×0.200
  0x80    → sub_72E108 (доп hull processing)
  0x40000 → TryDropGoodsAsRansom (выкуп товаром при жизни)
  0x100000→ sub_72DA10 type=3 (death-event)
  0x200000→ sub_72DA10 type=4 (другой death-event)
```

## ЧАСТЬ XI. ENUM-Ы И ПОЛЯ

```text
TPlanet.Owner @ +0x74 (race-extended):
  0..4 = Maloc, Peleng, People, Fei, Gaal
  5    = Kling
  6    = None
  7    = PirateClan

TStar.Owner @ +0x42 (faction-level):
  0 = Normals   1 = Klings   2 = Pirates

TPirate.PirateType @ +0x514:
  0 = Scavenger  (BuyPirate без role)
  1 = Blocker    (BuyWarrior на PirateClan, roll ≤ 3)
  2 = Flanker    (roll 4..7)
  3 = Supporter  (roll 8..9)

TTransport.subtype @ +0x510:
  0 = Cargo Transport (46% random)
  1 = Liner (34%)
  2 = Diplomat (~20%)

eDominatorSeries (Star.Series @ +0x49):
  0 = Blazeroid  1 = Kelleroid  2 = Terronoid

eShipType:
  0..13 (Kling/Ranger/Transport/Pirate/Warrior/Tranclucator/RC/PB/WB/SB/BK/MC/CB/UB)

TPlanet ФЛАГИ:
  +0x75 InNormalOwner       (bool, обновляется TPlanet_CalcParam)
  +0x15E Flag_NoBuyShips     (bool, гейтит ВСЕ Buy*)
  +0x15F Flag_NoMilitaryUpdate (гейтит ProcessMilitaryUpdate)
  +0x160 (?) флаг shop force-refresh / разрешает spawn

ГЛОБАЛЬНЫЕ ФАКТОРЫ:
  Galaxy.PirateWinType (0..5)              фаза пиратской войны
  Galaxy.TransportCount_Galactic (+0x16C)  живые транспорты (бывш. PirateSpawnTimer)
  Galaxy.PiratesClanCnt                    общее число пиратских кораблей
  Galaxy.CntOwnersSystem(faction)          0=Normals, 1=Klings, 2=Pirates
  Galaxy.RangersMaxCapital                 для расчёта стартового бюджета NPC
  Galaxy.FlagmansToBuild[5]                per-race byte 0..255, очередь флагманов
  Galaxy.CoalitionDefeatedTurn             0 = коалиция жива
  Galaxy.DiffLevels[0]                     уровень сложности (×0.5/×1.0/×1.2+)
  Star.DayWithoutCreateShip                cooldown для Kling-спавна
  Star.Battle                              flag «идёт бой»
  Star.Constellation.Id == 20              PirateConstellation (особый upgrade-loop)
```

## ЧАСТЬ XII. СВОДНАЯ ТАБЛИЦА ПРАВИЛ

```text
КОАЛИЦИОННАЯ ПЛАНЕТА (Owner = 0..4):
  Ranger        4%/день    R < min(N*1.5, 63) + econ_offset; demograph.gate;
                            начальная цель N×1.2; Constellation != 20
  Transport     5%/день    кап 2; 9N+3P > TransportCount; нет SB/BK/MC; subtype 46/34/20
  Warrior       1-2%/день  k×1..3 по Radius; k=2 при N≤1 (last surviving)
  BigWarrior    10% доп    после порога обычных; FlagmansToBuild[Race]>0 → -1
  Pirate-vassal mood²×%    N > PirateCnt; нет UB/Ruins

ПИРАТСКАЯ ПЛАНЕТА (Owner = 7):
  Scavenger     2%×mod1    prtCnt1 < 3*mod3 лок.;
                            PiratesClanCnt < P*(3*m3+6*m4) глоб.
  Warrior-pirat 5%×mod2    prtCnt2 < 7*mod4
  Transport     2% норм.   ≤1 норм. / ≤2 при CoalDef. Только не-PirateConstellation
                5% CoalDef
  Raid group    раз в      6-8 пирато-воинов на коалиц. звезду с рейнджером
                ~55 дней   (TPlanet_NextDay_PirateRaidGroup)

KLING ПЛАНЕТА (Owner = 5, через TrySpawnKling):
  Random Kling  v27%/день  Кап по серии: Blaz=13 Kell=15 Terr=11 def=10
                            Cooldown v25; Series alive guards
                            CheatKlingStrength=1/2/3 → v27=70/85/100, v25=3/2/1, v29=15

ГАЛАКТИЧЕСКИЕ СОБЫТИЯ:
  WB attack squad   каждые 133 хода, 50% roll, Day>=300, !CoalDef
                    4-6 воинов с WB-станции на вражескую звезду
  Pirate raid group каждые ~55 дней, 6-8 пирато-воинов на coal. звезду
  Boss followers    Bertor→Klig (target=5); Blazer/Keller/Terron→свои миньоны
                    через TKling_Boss_SpawnFollowers

ВЛИЯНИЯ НА ПОДТИПЫ:
  Klings: 5-уровневая таблица dword_87C0FC по плотности
          (level = ROUND(5 - klingsCount/25))
  Coalition: НЕТ аналога. Подтипы транспорта (46/34/20) фиксированы.
             k=2 для последней системы. FlagmansToBuild для флагманов.
```

## ЧАСТЬ XIII. КЛЮЧЕВЫЕ ВЫВОДЫ

```text
  1. Корабли НЕ спавнятся в TStar.NextDay — только в TPlanet.NextDay
     и TPlanet_NextDay_TrySpawnKling.

  2. Пиратский спавн каскадный:
     mod-ы зависят от PirateWinType → корректируются сложностью, окружением,
     боем → локально режутся текущим числом пиратов → globally PiratesClanCnt
     vs число пиратских систем → random roll 2%/5%.

  3. Klings имеют per-system cap по серии (10-15) и cooldown по дням.
     Боссы дополнительно поддерживают свиту через SpawnFollowers.

  4. Коалиция имеет «форс-мажорные» механизмы:
     • Последняя выжившая Normals-система: k=2
     • WB → Enemy Star раз в 133 дня
     • BuyRanger 4% при отставании в галактической мощи

  5. Глобальные счётчики (TransportCount, PiratesClanCnt, FlagmansToBuild)
     создают самобалансирующуюся систему.

  6. GTL не влияет на количество спавнящихся доминаторов, только на
     качество их вспомогательного оборудования
     (см. GTL_Consolidated.txt).

  7. NodePayload (Kling[+0x3A4]) определяет сколько нодов выпадет —
     задаётся при создании миньона из off_882670[KT*14+8] (word).
```

## КОНЕЦ ДОКУМЕНТА
