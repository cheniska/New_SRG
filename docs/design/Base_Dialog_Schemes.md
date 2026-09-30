# Схема диалогов на базах (FormRuins.* из Lang.txt) — Космические Рейнджеры HD

> Диздок перенесён из `Base_Dialog_Schemes.md` без правок текста: разделы оформлены заголовками,
> содержимое — дословно, моноширинным блоком (в исходнике — ASCII-вёрстка).

```text
Источник реплик: Lang.txt / SR_2_HD_Lang_vanilla.txt, ветки
  FormRuins ^{ ... }  (стр. 3753..4996) — все интерактивные пункты меню
                                          для всех типов баз
  FormRuinsRC ^{...} (стр. 4998..5049)  — победный концерт в RC после
                                          уничтожения последнего доминатора
  FormRuinsSB ^{...} (стр. 5051..5064)  — исторический экскурс "война с
                                          клисанами" в SB

Точки входа и главные диспетчеры (IDA):
  • TfRuinsTalk.Create          @ 0x5658B8   — создание формы
  • TfRuinsTalk_BuildDialogMenu @ 0x570C8C   — сборка меню под базу
  • TfRuinsTalk_Update          @ 0x56AC00   — цикл обновления
  • TfRuinsTalk_ActionStart     @ 0x5684F0   — старт выбранного действия
  • RuinsTalk_TakeOff_Handler   @ 0x573478   — общий "Взлёт"
  • RuinsTalk_ButFormClose_Handler @ 0x5913F0 — общий "Закрыть форму"

Класс TRuins наследует TShip; в игре 7 обитаемых баз (RC/PB/WB/SB/BK/MC/CB)
+ технический тип GN (fallback-Modern) + собственный "мостик" уникального
корабля (Bridge). Пункт «Поучаствовать в развитии базы» (BaseUpgrade)
общий для ВСЕХ баз.

  Общий пункт [BaseUpgrade]  "Я желаю поучаствовать в развитии вашей базы"
    Обработчик: RuinsTalk_BaseUpgrade_ShowDialog (0x573A2C)
                RuinsTalk_BaseUpgrade_ConfirmAction (0x573F08)
    Реплики (для каждой базы своя):
      RC.Modern.AfterOk    SB.Modern.AfterOk   MC.Modern.AfterOk
      WB.Modern.Answer/AfterNo   PB.Modern.Answer/AfterNo
      CB.Modern.Answer/AfterNo   BK.Modern.AfterOk   GN.Modern.Answer/AfterNo
    После оплаты: строится "Наука ускорена / Оборона усилена / Оборудование
    добавлено" (внутри BaseUpgrade), и открывается панель Snarayenie.

Легенда:
  [X]   — пункт меню, доступный игроку
  (X)   — реплика NPC-персонажа (продавца, ученого, банкира и т.д.)
  <X>   — конечное действие / переход в форму / изменение состояния игрока
  → gate — условие, гейтящее показ пункта / текст реплики
  ↩     — возврат в главное меню базы
```

## 0. КАРТА ВЕТОК FormRuins-БЛОКА

```text
FormRuins ^{
    BK       — Бизнес-центр / Банк
    Bridge   — Мостик собственного уникального корабля (не база; технически
               использует TfRuinsTalk-форму)
    CB       — Клановая база / Доминион пиратов (мобильная)
    GN       — Технический fallback для Modern-диалога (без своих сервисов)
    MC       — Медицинский центр
    PB       — Пиратская база
    RC       — Центр рейнджеров
    SB       — Научная база
    WB       — Военная база
    I_Continue / I_TakeOff / NotEngine / NotFuelTank / ShipOvercharging
      — общие технические ярлыки для UI ангара/трюма
}

FormRuinsRC ^{ Win.*  — финальный концерт после победы над доминаторами
                       (BlazerN / KellerN / TerronN / TheEnd) }
FormRuinsSB ^{ History.* — экскурс "война с клисанами" (бесплатная лекция) }
```

## 1. ОБЩАЯ АРХИТЕКТУРА

```text
Открытие: игрок кликает по станции в системе → корабль летит и стыкуется
→ TfRuinsTalk.Create → форма TfRuinsTalk. Меню собирается динамически
TfRuinsTalk_BuildDialogMenu по типу базы (RC/PB/WB/SB/BK/MC/CB) и текущим
условиям (наличие лицензии, ранга, ноды, кредита и т.д.).

Первое сообщение — приветствие. Для большинства баз выбирается из фиксированных
Greeting-ключей, зависящих от статуса базы (Normal/Best/Good/Bad) и игрока
(в случае RC — рейтинг). Некоторые базы (CB, WB) имеют дополнительные
приветствия в зависимости от контекста (гипер, полёт, война).

Общее для всех баз:
  [BaseUpgrade]  «Я желаю поучаствовать в развитии вашей базы»
  [I_TakeOff]    «Пока» (перейти в ангар)
  [I_Continue]   «Далее» (в многошаговых сценариях)
```

## 2. RC — ЦЕНТР РЕЙНДЖЕРОВ

```text
  Greeting → (RC.Greeting) + (RC.GreetingBest / GreetingGood / GreetingNormal /
                              GreetingBad — по Player.RangerRank)
             + (RC.GreetingAdd)  "нод-счёт: <BaseNod> шт."

  ─── меню RC ───────────────────────────────────────────────────────────

  [RC.TakeNod.PlayerSend]  "Получить новый микромодуль"
    └── RC_GetMicroModule_ShowDialog (0x5742C4)
        │
        └── (RC.TakeNod.RCAnswer)  "сегодня предлагаем следующие ММ"
            + (RC.TakeNod.RCAnswerBig/Average/Small)  3 варианта на выбор
            + (RC.TakeNod.RCAnswerEnd)  "нод-счёт: <BaseNod> шт."
             │
             ├── [RC.TakeNod.PlayerOk]  "Обменять <Count> нодов на <Name>"
             │    → gate: BaseNod >= Count
             │    └── (RC.TakeNod.RCAfterPlayerOk)  <BaseNod -= Count;
             │                                       Player.MicroModules += <Name>>
             │
             └── [RC.TakeNod.PlayerNo]  "Отказаться от обмена"
                  └── (RC.TakeNod.RCAfterPlayerNo)  "разумно, возите ещё нодов"  ↩

  [RC.GiveNod.PlayerSend]  "Сдать микромодули в обмен на ноды"
    └── RC_ExchangeMicroForNodes_ShowDialog (0x57537C)
        │
        ├── gate: Player.MicroModules.Count == 0
        │           → (RC.GiveNod.Nothing)  "у вас нет ММ"  ↩
        │
        └── (RC.GiveNod.Answer)  "заберём ненужные ММ за ноды"
            + список: (RC.GiveNod.Nod)  "- ММ <Name> за <Count> нд"
            + (RC.GiveNod.Sum)  "нод-счёт: <BaseNod> шт"
            │
            ├── [RC.GiveNod.PlayerOk]  "Сдать ММ <Name> за <Count> нд"
            │    └── (RC.GiveNod.AfterOk)  <Player.MM -= 1; BaseNod += Count>
            │
            └── [RC.GiveNod.PlayerNo]  "Отказаться"
                 └── (RC.GiveNod.AfterNo)  ↩

  [RC.SaleNod.PlayerSend]  "Сдать партию нодов: <Count> шт."
    → gate: Player.Cargo содержит TProtoplasm (ноды)
    └── RC_SaleNod_ShowDialog (0x5740DC)
        └── (RC.SaleNod.RCAnswer)  "забираем ноды, счёт: <BaseNod>"
             <Player.Cargo -= Count nodes; BaseNod += Count;
              Player.RangerPoints += bonus>

  [RC.AboutNod.PlayerSend]  "Мне нужна информация о нодах"
    └── RC_AboutNod_ShowDialog (0x576248)
        → (RC.AboutNod.RCAnswer + RCAnswerAdd)  информационный монолог  ↩

  [RC.PirateClan.PlayerSend]  "Как я могу помочь Коалиции в борьбе с пиратами?"
    └── RC_PirateClan_ShowDialog (0x5765D0)
        → (RC.PirateClan.RCAnswer)  агент под прикрытием — лети на PB  ↩

  [RC.Rating.PlayerSend]  "Что такое рейтинг рейнджеров?"
    → (RC.Rating.RCAnswer)  информация об опыте  ↩

  [RC.BestRanger.PlayerSend]  "В чём заключаются обязанности рейнджера?"
    → (RC.BestRanger.RCAnswer)  устав рейнджеров  ↩

  [BaseUpgrade]  → (RC.Modern.AfterOk) "правильно, база защитит стажёров"

  [I_TakeOff]  "Пока"

  ─── СПЕЦ: после финальной победы над доминаторами ──────────────────────
  При посадке в RC срабатывает FormRuinsRC.Win.*:
    (Win.Blazer1..2 / Keller1..3 / Terron1..5)  сообщение о победе
    (Win.TheEnd)  "серии повержены, коалиция салютует"
    → [Win.AfterPlayerEnd1..3]  игрок выбирает эмоциональную реакцию
    → (Win.AddNews) публикация новости и приглашение на концерт
```

## 3. PB — ПИРАТСКАЯ БАЗА

```text
  Greeting → (PB.GreetingPre + GreetingNormal/Mod/Aft)
             — динамически собранное приветствие в зависимости от того,
               есть ли у игрока антидоминаторские программы (Mod) и ноды.

  ─── меню PB ───────────────────────────────────────────────────────────

  [PB.ChangeSide.ChangeSideToPirate]  "Хочу перейти на сторону пиратского клана"
   или
  [PB.ChangeSide.ChangeSideToNormal]  "Хочу получить обратно рейнджерское звание"
    → gate: Player.Money >= GalaxyMoney_Huge(ruins.Owner) (свой cost)
    └── PB_ChangeSide_ShowDialog (0x5778B4)
        │
        └── (ChangeSide.AnswerChangeSideToPirate) — уговоры + цена <Cost>
            (или AnswerChangeSideToNormal — уговоры вернуться в рейнджеры)
            │
            ├── [PlayerOk]  "Да, я твердо решил"
            │    → (AnswerPlayerOkPirate / OkNormal)
            │       <Player.SubType := Pirate/Ranger;
            │        Player.Money -= <Cost>; Attitude всех правительств пересчитан>
            │
            └── [PlayerNo]  "Нет, я передумал"
                 → (AnswerPlayerNoPirate / NoNormal)  ↩

  [PB.ChangeNationality.ChangeNationality]  "Меня интересует смена подданства"
    → gate: Player.Money >= minCost (для дешёвой расы)
    └── PB_ChangeNationality_ShowDialog (0x576730)
        │
        └── (ChangeNationality.AnswerChangeNationality)  объяснение услуги
            + (ChangeNationality.PBNext)  цены по расам (Малок/Пеленг/Люди/
                                                        Фэянин/Гаалец)
            │
            ├── [ChooseMaloc / ChoosePeleng / ChoosePeople / ChooseFei / ChooseGaal]
            │    → (Confirm) "готов выложить <Money> cr?"
            │       [PlayerOk] → (AfterOperationXxx)  <Player.Race := Xxx;
            │                     Player.Money -= <Money>; лицо переигрывается>
            │       [PlayerNo] → (PBAfterNo/NoAlt)  "жалеть будешь"  ↩
            │
            └── gate: Player.Money < needed
                     → (NoMoney)  "не по карману"  ↩

  [PB.Program.PlayerAsk]  "Хочу приобрести программы"
    → gate: у базы есть доступные программы (массив 0x87EB54)
    └── PB_Program_BuyProgram_ShowDialog (0x578B40)
        │
        └── (PB.Program.PBStart)  "все программы 'CopyRight' + прайс-лист"
            + (PB.Program.NodCost) стоимость в нодах для каждой программы
            │
            ├── [PB.Program.PlayerOk]  "Обменять <Nod> нод на <Text>"
            │    → gate: у игрока достаточно нод в трюме или на нод-счёте
            │       └── PB_DomPrograms_ConfirmBuy (0x578834)
            │          → (PB.Program.PBAfterOk)  <-<Nod>; +Program>  ↩
            │
            └── [PB.Program.PlayerNo]
                 → (PB.Program.PBAfterNo/NoAlt)  ↩

  [PB.Nod.PlayerAsk]  "Мне нужны ноды"
    → gate: показывается всегда, но:
    └── PB_Nod_BuyNodes_ShowDialog (0x5782EC)
        │
        ├── gate: curRuins.Protoplasm == 0
        │      → (PB.Nod.PBEnd)  "товар закончился"
        │        + (PB.Nod.PBEndPlus)  "в секторе <ToSector> база <ToBase>
        │                                у них есть контейнеры"
        │                              (поиск через sub_6D3038)  ↩
        │
        └── (PB.Nod.PBStart)  "<Count> ед. за <MoneyAll> cr,
                               скидка <Percent>%, итого <MoneyDec>"
            │
            ├── [PB.Nod.PlayerOk]  "Хорошо, я беру контейнер!"
            │    → (PB.Nod.PBSell)  <контейнер на корабль;
            │                        Player.Money -= <MoneyDec>>
            │
            └── [PB.Nod.PlayerNo]  → (PB.Nod.PBAfterNo)  ↩

  [PB.Repair.PlayerSend]  "Мне нужно починить оборудование"
    → gate: Ruins_CanRepairEquipment(curRuins) == true
    └── PB_Repair_ShowDialog (0x579640)
        │
        ├── gate: нечего чинить
        │      → (PB.Repair.PBYouNotNeedRepair)  ↩
        │
        └── (PB.Repair.PBYouNeedRepair) + (PBCostAnswerNeedNode если dom-оружие)
             "цена <MoneyAll>, скидка <Percent>%, итого <MoneyDec>"
             │
             ├── [PB.Repair.PlayerOk]
             │    → PB_Repair_ConfirmRepair (0x579C5C)
             │      → (PB.Repair.PBAfterOk)  <Player.Money -= MoneyDec;
             │                                Player.Nodes -= NeedNode;
             │                                HP всё оборудования = max>
             │
             └── [PB.Repair.PlayerNo]  → (PB.Repair.PBAfterNo)  ↩

  [PB.Chameleon.PlayerAsk]  "Я слышал, у вас есть камуфляжное устройство"
    → gate: PB имеет предложение хамелеона (обычно всегда доступно)
    └── PB_Chameleon_ShowDialog (0x57A98C)
        │
        └── (PB.Chameleon.PBAsk)  "модели: Blazer/Terron/Keller + <List>"
            │  (для каждого камо: (PB.Chameleon.PlayerOk) "серии <S> за <Cost>")
            │  — цена возрастает с числом уже купленных копий
            │
            ├── [PlayerOk] выбор конкретного камо
            │    → (PBAfterOk)  <Player.Chameleon += 1;
            │                    Player.Money -= <Cost>>
            │
            └── [PlayerNo]  → (PBAfterNo/NoAlt)  ↩

  [PB.SabCrack.PlayerInfo]  "И что же такое интересное вы хотите мне продать?"
    → gate: PB_IsSabCrackAvailable_new (0x793E88) == true
    └── PB_SabCrack_ShowDialog (0x579FF8)
        │
        ├── (PB.SabCrack.PBInfo)  большая история про профессора Саба
        │    │
        │    └── [PlayerContinue]  "И это всё?"
        │        → (PB.SabCrack.PBContinue)  продолжение истории
        │        + (PB.SabCrack.PBGreetingAdd) "предложение на <Money> cr"
        │
        ├── [PB.SabCrack.PlayerOk]  "Ладно, плачу <Money> cr"
        │    → (PBAfterOk)  <Player.Money -= Money; +диск программы>
        │
        ├── [PB.SabCrack.PlayerOkHalf]  "Возьму за половину цены"
        │    → (PBAfterOkHalf) "не фуфло... ладно, за половину"  <-Money/2>
        │
        └── [PB.SabCrack.PlayerNo]
             → (PBAfterNo)  "и это называется рейнджер?"  ↩

  [PB.SpecialShip.Ask]  "Я бы хотел получить уникальный корабль"
    → gate: PB.HasSpecialShip == true AND все условия из Info выполнены
    └── PB_SpecialShip_ShowDialog (0x590064)
        │
        └── (SpecialShip.Before + Info)  описание корабля + требования:
             "- Статус пират: <PirateComplate>
              - Ранг не ниже аса: <RankComplate>
              - Уничтожено <KillCnt> мирных: <KillComplate>"
            │
            ├── [PlayerBuy]  → (AfterBuy) <Player.Ship := SpecialHull;
            │                              Attitude тонко корректируется>
            │
            └── [PlayerNo]  → (AfterNo)  ↩

  [PB.AskQuestion.PlayerAsk]  "Хочу спросить пару вопросов..."
    → gate: Player = t_Pirate
    └── (PB.AskQuestion.PBAsk)  "спрашивай, кореш"
         │
         ├── [PB.AskQuestion.PlayerAskAboutCapture]  "могу ли захватывать системы?"
         │    → (PBAnswersAboutCapture)  инструкция как захватить систему
         │      [PlayerAfterAnswerAboutCapture]  "все понятно"  ↩
         │
         └── [PB.AskQuestion.PlayerNoQuestions]  "вопросов больше нет"  ↩

  [PB.RankPoints]  (условный/автоматический) — таблица очков за пиратский рейтинг
     — показывается в отдельном ответе после ChangeSide→ToPirate.

  [PB.WarOperation.PlayerSend]  "Заказать пиратский набег"
    → gate: у игрока есть <Money> cr AND есть военные конфликты в галактике
    └── (PB.WarOperation.PB)  "100% предоплата <Money> cr"
        │
        ├── [PlayerOk]  → (PBAfterOk[Good/Bad])  <-Money; операция или отказ>
        └── [PlayerNo]  → (PBAfterNo)  ↩

  [PB.WarWithKlingAndCoalition.PlayerSend]  "Как обстановочка на фронтах?"
    └── (по PiratesPercent):
           WeControl100Percent / More90Percent / More70Percent / More50Percent /
           More30Percent / More10Percent / More00Percent
        и по контексту (CoalitionOnly / KlingAndCoalition / KlingOnly)  ↩

  [BaseUpgrade]  → (PB.Modern.Answer)  "<Money> cr в общак"
                    [PlayerOk] → (Modern.AfterOk) "модернизация"
                    [PlayerNo] → (Modern.AfterNo)

  [I_TakeOff]  "Пока"
```

## 4. WB — ВОЕННАЯ БАЗА

```text
  Greeting → выбирается по Player.MilitaryRank:
    Rookie / Cadet / Pilot / Wingman / Leader / Ace / Commander / Admiral
     — каждый из этих 8 sub-блоков содержит .Greeting и .NewRank
    Если игрок только-только получил новый ранг:
      (Xxx.NewRank) "Теперь ты <Rank>, прими <ItemName>"
      <Player.Inventory += <ItemName>; при Admiral — ещё +MMName уникальный ММ>
    Иначе:
      (Xxx.Greeting)  обычное приветствие

  ─── меню WB ───────────────────────────────────────────────────────────

  [WB.WarWithKlingAndPirates.PlayerSend]  "Расскажите о ходе военных действий"
    └── WB_WarStatus_ShowDialog (0x57B198)
        │
        └── (по CoalitionPercent, контекст KlingAndPirates/KlingOnly/PiratesOnly):
             WeControl100Percent / More90Percent / More70Percent /
             More50Percent / More30Percent / More10Percent / More00Percent
             + указание стратегически важной системы <Star>  ↩

  [WB.NextRank.PlayerSend]  "Когда я получу звание <NextRank>?"
    └── WB_NextRank_ShowDialog (0x57BBD0)
        │
        └── (WBAnswer)  "нужно <NeedPoints> очков"
            + таблица начислений за уничтоженные доминаторы K1..K7
            + начисления за освобождение систем / пиратов  ↩

  [WB.Repair.PlayerSend]  "Мне нужно починить корабль"
    └── WB_Repair_ShowDialog (0x57C064)
        → WB_Repair_AskCost_ShowDialog (0x57C1F4)
        │
        └── (WB.Repair.WBAnswer)  "чиним только стандартное оборудование"
            + (WBCostAnswerYouHaveGoodEquipments / YouHaveBadEquipments)
              "цена <Money> cr"
            + gate: cost==0 → (WBCostAnswerNonEquipmentsForRepair) "не требуется"
            │
            ├── [PlayerOk]  "Я согласен заплатить <Money> cr"
            │    → WB_Repair_OnConfirm (0x57C648)
            │      → (WB.Repair.WBAfterOk)  <-Money; полный ремонт>
            │
            ├── [PlayerCostAsk]  "А во сколько это мне обойдётся?"
            │    → показывает калькуляцию (та же WBAnswer)
            │
            └── [PlayerNo]  → WB_Repair_OnDecline (0x57C750)
                             → (WB.Repair.WBAfterNo)  "жалеть будешь"  ↩

  [WB.Programs.PlayerAsk]  "Получить боевые программы"
    → gate: у игрока есть купленные-но-не-переданные программы (_664[])
    └── WB_Programs_ShowDialog (0x57C7F8)
        │
        └── (WB.Programs.GreetingAdd)  "антидоминаторские программы"
            + список: (WB.Programs.Info)  "программа: <Name> / описание: <Text>"
            │
            └── [PlayerOk]  "Загрузить боевые программы"
                 → WB_Programs_ConfirmBuy (0x57CBF4)
                   → (WB.Programs.WBAfterOk)  <_664[i] -= 1; +Program;
                                               инструкция по использованию>

  [WB.WarOperation.PlayerSend]  "Заказать военную операцию"
    → gate: Player.Money >= <Money>
    └── (WB.WarOperation.WB)  "100% предоплата <Money> cr, правила..."
        │
        ├── [PlayerOk]  "Готов заплатить <Money> cr"
        │    → (WBAfterOk[Good/Bad])
        │       Bad: <-DecMoney (штраф); операция не найдена>
        │       Good: <-Money; операция назначена на <Date> в системе <StarEnemy>>
        │
        └── [PlayerNo]  → WB_WarOperation_OnDecline (0x57D700)
                          → (WB.WarOperation.WBAfterNo)  ↩

  [WB.FlyToEnemy.PlayerAsk]  "Я хочу лететь с вами в систему <StarEnemy>"
    → gate: WB.HasWarOperationPending == true
    └── WB_FlyToEnemy_ShowDialog (0x57D7B4)
        + (WB.FlyToEnemy.GreetingAdd)  "прыжок в <StarEnemy> <Date>"
        │
        ├── [PlayerFly]  "Давай, поехали!" → (WBAfterQuestions) → взлёт
        ├── [PlayerHangar]  "В ангар" → WB_FlyToEnemy_OnHangar (0x57E4BC)
        ├── [PlayerNotFly]  → (WBAfterNotFly) "подумай"  ↩
        ├── [PlayerQuestions]  → диалог с вопросами
        ├── [PlayerToChamber]  "Пройти в анабиозную камеру"
        │    → WB_FlyToEnemy_ShowChamber (0x57D8FC)
        │    → (WBInChamber) "располагайся"
        │       ├── [PlayerFly]  сон → прыжок → (WBInStarEnemy) → (WBStarEnemyInfo)
        │       └── [PlayerUp]  выход из камеры
        │
        └── После прибытия: WB_FlyToEnemy_OnArriveEnemy (0x57DC7C)
             → (WBStarEnemyInfo) "система <Star>, корабли: <Ships>"
             + приказ на освобождение

  [BaseUpgrade]  → (WB.Modern.Answer) "внеси <Money>, разрешим модернизацию"
                    [PlayerOk] <-Money; сервис "стал круче">
                    [PlayerNo] → (Modern.AfterNo) "это грозит трибуналом!"

  [WB.SpecialShip.Ask]  "Я бы хотел получить уникальный корабль"
    → gate: WB.HasSpecialShip == true
    └── (SpecialShip.Before + Info)  требования: "воин + ранг аса +
                                                 уничтожено <KillCnt> пиратов"
        [PlayerBuy]/[PlayerNo] → (AfterBuy)/(AfterNo)

  [I_TakeOff]  "Пока"
```

## 5. SB — НАУЧНАЯ БАЗА

```text
  Greeting → одно из двух:
    (SB.GreetingAfterScn)  когда исследования уже завершены
    (SB.GreetingBeforeScn) когда идут исследования по доминаторам
    + (SB.GreetingAdd)  "предлагаем улучшение/зонды/ремонт"

  ─── меню SB ───────────────────────────────────────────────────────────

  [SB.Improvement.PlayerSend]  "Хочу улучшить своё оборудование"
    └── SB_Improvement_ShowDialog (0x57E748)
        │
        └── (Improvement.SBAnswer)  "работаем только один раз над предметом"
            + (SBSeeItems) список: (ItemReadyForImprovement / ItemNotReady)
                                    "<ItemName> за <Money> cr"
            + gate: пустой список → (SBSeeItemsNotHaveItems) "ничего подходящего"  ↩
            │
            [PlayerAsk] выбор конкретного предмета из списка
             │
             └── (SBNeedCostImprovement / SBNeedCostImprovementNodes)
                  "лучшие спецы <Max> cr, обычные <Average>, стажёры <Min>
                   (+ ноды если dom-оборудование)"
                 │
                 ├── (SBDetailImprovement)  "какому параметру уделить внимание?"
                 │    [PlayerDetailOk "Сделайте упор на <Attr>"] / [PlayerDetailNo]
                 │
                 ├── [PlayerOkMax]  "лучшие спецы" → (SBAfterOkMax)  <-Max;+++stat>
                 ├── [PlayerOkAverage]  → (SBAfterOkAverage)  <-Ave;++stat>
                 ├── [PlayerOkMin]  "стажёры"  → (SBAfterOkMin)  <-Min;+stat>
                 │    → (Improvement.PlayerRepeatOk/No)  "ещё улучшать?"
                 │       [PlayerRepeatOk] возврат к списку
                 │       [PlayerRepeatNo] → (SBAfterRepeatNo)  ↩
                 │
                 └── [PlayerNo]  "грабительские расценки" → (Improvement.SBAnswerNothing)  ↩

  [SB.Repair.PlayerSend]  "Мне нужно починить корабль"
    └── SB_Repair_ShowDialog (0x5800E8)
        → SB_Repair_AskCost_ShowDialog (0x580278)
        │
        └── (SB.Repair.SBAnswer)  "чиним всё, включая специальное"
            + (SBCostAnswerYouHaveGoodEquipments / YouHaveBadEquipments)
              "ремонт оборудования <EqMoney>, артефактов <ArtMoney>, итого <Money>"
              + (SBCostAnswerNeedNode)  "нужно <NeedNode> нодов на нестандарт"
              + gate: cost==0 → (SBCostAnswerNonEquipmentsForRepair) "не требуется"
            │
            ├── [PlayerOk]  "Согласен" → SB_Repair_OnConfirm (0x580910)
            │                          → (SB.Repair.SBAfterOk)  <-Money;-Node>
            │
            └── [PlayerNo]  → SB_Repair_OnDecline (0x580B18)
                             → (SB.Repair.SBAfterNo)  "жалеть будешь"  ↩

  [SB.Satellite.PlayerSend]  "Меня интересуют зонды для исследований"
    └── SB_Probes_ShowDialog (0x580BC0)
        │
        └── (Satellite.SBInfo)  "<SatName>, <SatText>, <SatMoney> cr, вес <SatSize>"
             + (SBInfoOk / SBInfoNo)  зависит от Skill_Technic
             │
             ├── [PlayerInstruction]  "Инструктаж"
             │    → SB_Probes_ShowInstructions (0x58129C)
             │      → (SBInstruction) 4 параграфа про зонды
             │
             ├── [PlayerOk]  "Плачу <SatMoney>"
             │    → SB_Probes_OnConfirm (0x581358)
             │      → (SBAfterOk)  <-SatMoney; +Satellite в трюм>
             │
             └── [PlayerNo]  → SB_Probes_OnDecline (0x581524)
                              → (SBAfterNo)  ↩

  [SB.Scn.PlayerAsk]  "Хочу помочь антидоминаторским исследованиям"
    └── SB_Scan_ShowDialog (0x5815D4)
        │
        └── (Scn.SBAnswer / SBAnswer2)  список отделов + (SBSectionInfo):
             — Отдел 1 (Блазероидный) / 2 (Келлероидный) / 3 (Терроноидный)
             "материал <Count> т, скорость <Speed>%, время <Day> дн"
             │
             ├── [PlayerSaleAllUselessBlazer/Keller/Terron]  "все ошмётки"
             ├── [PlayerSaleAllEq]  "все оборудование доминаторов"
             ├── [PlayerSale <ItemName>]  выбор конкретного предмета
             │    → (SBSection2)  <-Item; +Money счёт; исследования ускоряются>
             │    → (SBSection2Add)  список того что еще интересует
             ├── [PlayerSaleNo]  "нет предметов" → (SBAfterPlayerSaleNo1/No2)  ↩
             ├── [SectionNone]  → (SBAfterSectionNone)  "надо достать, рейнджер"  ↩
             │
             │  ─── если отдел завершил исследования ─────────────────────
             ├── [PlayerBuyTechBlazer/Keller/Terron]  "итоги работы отдела <X>"
             │    → SB_Scan_BuyTechDialog (0x583418)
             │      → (SBBuyTechBlazer/Keller/Terron)  большой лор-текст +
             │         "стоимость <Money> cr"
             │       │
             │       ├── [PlayerBuyTechOk]  → SB_Scan_OnBuyTechConfirm (0x5837D4)
             │       │    → (SBAfterPlayerBuyTechXxx) <-Money;
             │       │       +Program (Противодействие / Дематериализатор /
             │       │                 Энерготрон)>
             │       │
             │       └── [PlayerBuyTechNo]  → (SBAfterPlayerBuyTechNo)  ↩
             │
             └── [PlayerSectionChoose]  "получить повторно данные об отделах"
                  → возврат в (SBAnswer)

  ─── СПЕЦ: экскурс про клисан (только для новичков или по желанию) ─────
  [FormRuinsSB.History.PlayerOk]  "Расскажите мне о войне с клисанами"
    → (FormRuinsSB.History.SB)  предложение бесплатной лекции
    → (History.SBOk / SBOk1 / SBOk2 / SBOk3 / SBOk4)  4 параграфа истории  ↩

  [SB.SpecialShip.Ask]  "Уникальный корабль"
    → gate: SB.HasSpecialShip
    └── (SpecialShip.Before + Info)  требования "ранг аса +
        уничтожено <KillCnt> доминаторов + выполнено <QuestCnt> заданий"
        [PlayerBuy] → (AfterBuy)  <спец. корпус с антенной>
        [PlayerNo]  → (AfterNo)

  [BaseUpgrade]  → (SB.Modern.AfterOk) "аплодируем науке!"
  [I_TakeOff]
```

## 6. BK — БИЗНЕС-ЦЕНТР / БАНК

```text
  Greeting → (BK.Greeting)  "приветствуем в бизнес-центре <BK>. Хотите..."

  ─── меню BK ───────────────────────────────────────────────────────────

  [BK.TakeDebt.PlayerSend]  "Взять кредит"
    → gate 1: Player.CreditFlags & Red == 0 (нет "CR" red flag)
              иначе → (AddDebtContinue / DebtNoAccess / DebtNoPenalty)  ↩
    → gate 2: Ruins.System != "war" иначе → (DebtNoWar)  ↩
    → gate 3: у игрока нет активного кредита (иначе показывает AddDebt* реплики)
    └── BK_TakeDebt_ShowDialog (0x583EB0)
        │
        └── (TakeDebt.BK)  3 варианта кредита: срочный/обычный/малый
             с (MaxMoney/MinDay/MaxMoneyAdd) / (AveMoney/AveDay/AveMoneyAdd) /
                (MinMoney/MaxDay/MinMoneyAdd)
            │
            ├── [PlayerOkMaxMoney]  "срочный <MaxMoney>"
            ├── [PlayerOkAveMoney]  "обычный <AveMoney>"
            ├── [PlayerOkMinMoney]  "малый <MinMoney>"
            │    → (BKAfterOk)  <Player.Money += SendMoney;
            │                    Player.Debt := SendMoney + Комиссия;
            │                    Player.DebtDate := Day + MinDay/AveDay/MaxDay>
            │
            └── [PlayerNo]  → BK_TakeDebt_OnDecline (0x585028)
                             → (BKAfterNo) "летите в другой центр"  ↩

  [BK.RetDebt.PlayerSend]  "Вернуть кредит <Money> cr"
    → gate: Player.Debt > 0
    └── BK_RetDebt_ShowDialog (0x585134)
        └── (RetDebt.BK)  "рады получить назад, порядочный рейнджер"
             <Player.Money -= Debt; Player.Debt := 0>

  [BK.Deposit.PlayerSend]  "Открыть депозитный счёт"
    → gate: у игрока нет активного долга (иначе замораживают)
    └── BK_Deposit_ShowDialog (0x585278)
        │
        └── (Deposit.BK)  "минимум 1000 cr, <Percent>% годовых, 30 дней блок"
            │
            ├── [Deposit.PlayerOkMinMoney]  "<MinMoney> cr"
            ├── [Deposit.PlayerOkAveMoney]  "<AveMoney> cr"
            ├── [Deposit.PlayerOkMaxMoney]  "<MaxMoney> cr"
            │    → (BKAfterOk)  <Player.Money -= X; Player.Deposit += X;
            │                    Player.DepositDate := Day>
            │
            └── [PlayerNo]  → (BKAfterNo)  ↩

  [BK.RetDeposit.PlayerSend]  "Снять <Money> cr с депозитного счёта"
    → gate 1: Player.Deposit > 0 AND (Day - DepositDate) >= 30
    → gate 2: Ruins.System не "war" иначе → (RetDeposit.War)  ↩
    └── BK_RetDeposit_ShowDialog (0x585D44)
        └── (RetDeposit.BK)  "счёт закрыт"  <Player.Money += Deposit;
                                             Player.Deposit := 0>

  [BK.Investment.PlayerSend]  "Проинвестировать проекты"
    └── BK_Investment_ShowDialog (0x589E4C)
        │
        └── (Investment.BK)  описание программ + список (BKInvestment):
             (PlayerInvestment <InvestmentShortName>)
            │
            ├── [PlayerInvestment <X>]  выбор проекта
            │    → (BKAfterInvestment)  <Player.Money -= <Cost>;
            │                            <тип программы применяется —
            │                             голодные накормлены, флот усилен,
            │                             медцентры расширены и т.д.>>
            │
            └── [PlayerNo]  → (BKAfterPlayerNo)  "стыдно, стыдно" / "извинения"

  [BK.Policy.PlayerSend]  "Страхование жизни"
    → gate: Player.Policy == 0 OR (Day - PolicyDate) >= Year*30
    └── BK_Policy_ShowDialog (0x585F6C)
        │
        └── (Policy.BK)  "полис за <Money> cr на <Year> лет"
            │
            ├── [PlayerAsk]  "А что такое пожизненная компенсация?"
            │    → (BKAfterAsk) 3 параграфа объяснений
            │
            ├── [PlayerOk]  "Приобрести полис"
            │    → BK_Policy_OnConfirm (0x586514)
            │      → (BKAfterOk) <-Money; +Policy на <Year> лет;
            │                     цена лечения в MC становится /2>
            │
            └── [PlayerNo]  → BK_Policy_OnDecline (0x586628)
                             → (BKAfterNo) "возьми кредит" ↩

  [BK.Trade.PlayerSend]  "Заказать анализ рынка товаров"
    └── BK_Trade_ShowDialog (0x58C048)
        │
        └── (Trade.BK)  "ближний <NeaMoney> / дальний <FarMoney>,
                         скидка <Percent>%"
            │
            ├── [PlayerNea]  "Оплачиваю ближайшие <NeaMoney>"
            ├── [PlayerFar]  "Оплачиваю дальний <FarMoney>"
            │    → (BKAfterOk)  <-Money;
            │                    результат: (BKFindTradePath) N вариантов
            │                    с планетами/ценами/расстояниями>
            │    → (BKAfterOkNea/Far + панели)  <Player.TradeInfo += results>
            │    → gate: не найдено → (BKNotVariant)  <возврат денег>
            │
            └── [PlayerNo]  → (BKAfterNo) "возможно другие услуги?"  ↩

  [BaseUpgrade]  → (BK.Modern.AfterOk) "правильные капиталовложения"

  [I_TakeOff]
```

## 7. MC — МЕДИЦИНСКИЙ ЦЕНТР

```text
  Greeting → (MC.Greeting) 2 части — представление и правила поведения

  ─── меню MC ───────────────────────────────────────────────────────────

  [MC.Illnes.PlayerSend]  "Пройти мед. обследование"
    → gate: всегда доступен
    └── MC_Illnes_ShowDialog (0x58D330)
        │
        └── (Illnes.MCSee[/Pirate])  "признаки переутомления"
             │
             ├── gate: Player.HasIllness == false AND нет вирусов
             │      → (MCSeeGoodNormalToNormal / NormalToPirate /
             │         PirateToNormal / PirateToPirate)  "здоров"  ↩
             │
             └── (Illnes.MCSeeIllness[/Pirate])  "список болезней"
                  + (MCSeeIll) для каждой: "<IllName> [вирус], <Money> cr"
                  + (MCSeeRadiation) специфично: "Лучевую не лечим — стационар"
                  + (MCSeeCureless)
                  + gate: у игрока есть Policy → цена /=2
                  + gate: у игрока Pirate → расширенный MCSeeIllnessPirate
                 │
                 ├── [MC.Illnes.PlayerIll <IllName>]  "Лечить <IllName> (<Money>)"
                 │    → MC_Illnes_AfterTreatment (0x58E21C)
                 │      → (MCSeeAfterIll[/Pirate])  <-Money; -Illness>
                 │
                 ├── [MC.Illnes.PlayerIllAll]  "Пройти уринотерапию (<Money>)"
                 │    → MC_Illnes_AfterAll (0x58E654)
                 │      → (MCSeeAfterIllAll)  5 стадий процедуры
                 │        <Player.Money -= X; все болезни очищены>
                 │
                 ├── [MC.Illnes.PlayerNo]  "Отказаться от лечения"
                 │    → MC_Illnes_OnDecline (0x58E8EC)
                 │      → (MCSeeAfterNo)  "стыдно, продайте что-нибудь"  ↩
                 │
                 └── [MC.Illnes.PlayerExit/PlayerExitPirate]  "Галактика в опасности!"
                      → MC_Illnes_OnExit (0x58E9FC)
                      → (MCSeeAfterExit/Pirate)  ↩

  [MC.Stimulants.PlayerSend]  "Принять стимуляторы"
    └── MC_Stimulants_ShowDialog (0x58EBA8)
        │
        └── (Stimulants.MC1 + MC2/MC2Pirate/MC2PirateNoRank)  "лимит по рангу"
            + (MC3[/Pirate/PirateNoRank])  "по закону положено <LawStim>,
                                            но здоровье превыше"
            + (MC4)  "у вас сейчас <CurStim>, можно ещё <AddStim>"
            + (MC5 + MC5MedPolicy)  список: (MCStimInfo)
                                     "<StimName> / <StimText> / <Month> мес /
                                      <Money> cr"
            │
            ├── [MC.Stimulants.PlayerStim <StimName>]  "Принять <StimName>"
            │    → gate: AddStim > 0 AND Player.Money >= <Money>
            │    → MC_Stimulants_AfterPlayerOk (0x58FA78)
            │      → (MCAfterPlayerStim)  <-Money; +Stimulant активен до <Date>;
            │                              CurStim += 1>
            │
            └── [MC.Stimulants.PlayerNo]  "Отказаться от приёма"
                 → MC_Stimulants_OnDecline (0x58FEB0)
                 → (MCAfterPlayerNo[/Pirate])  "здоровый образ жизни"  ↩

  [BaseUpgrade]  → (MC.Modern.AfterOk)  "спасли тысячи больных"

  [I_TakeOff]
```

## 8. CB — КЛАНОВАЯ БАЗА / ДОМИНИОН (мобильная пиратская база)

```text
ВАЖНО:
  • Сесть на CB может ТОЛЬКО игрок-пират (t_Pirate AND PirateRankPoints > 0).
  • CB — единственная база, которая перемещается между системами (Relocate).
  • Многие пункты меню гейтятся по пиратскому званию (юнга/шкипер/головорез/
    атаман/хан) — «шкала Рачехана».

  Greeting → выбирается по состоянию базы:
    (CB.GreetingNormal)     "добро пожаловать на доминион <CB>, у нас..."
    (CB.GreetingHyperspace) "мы в гипере, покинуть базу невозможно"
    (CB.GreetingFlyToStar)  "собираемся прыгать в <FlyToStar>, отчаливай"
    (CB.GreetingPlayerFlyToStar)  "готовы отправить тебя в <FlyToStar>"

  Обёртка отказа (для всех пунктов):
    CB_GenericRefuseChecks_ShowDialog (0x595CDC) выдаёт одну из:
      (CB.GenericRefuseBattle)      "нас атакуют, взлетай!"
      (CB.GenericRefuseHaveOtherPlans) "у нас другие планы"
      (CB.GenericRefuseNeedLicense) "купи лицензию и работай на общак"
      (CB.GenericRefuseNeedPoints)  "работай на общак"
      (CB.GenericRefuseNeedRank <PirateRank>)  "рановато, обращайся при <ранге>"

  ─── меню CB ───────────────────────────────────────────────────────────

  [CB.PirateLicense.PlayerSend]  "Я хочу приобрести лицензию пирата"
   или
  [CB.PirateLicense.PlayerSendProlongate]  "Я хочу продлить лицензию пирата"
    → gate: если уже есть лицензия → показывается Prolongate вариант
    └── CB_PirateLicense_ShowDialog (0x595408)
        │
        └── (PirateLicense.CBAnswer / CBAnswerProlongate)
             большой текст + "исходная <Money>, скидка <Discount>%,
             итого <DiscountMoney>"
            │
            ├── [PlayerOk / PlayerOkProlongate]  "приобрести/продлить"
            │    → (CBAfterOk / CBAfterOkProlongate)
            │      <-DiscountMoney; +PirateLicense на 1 год;
            │       рутинно даёт +1% опыта с награбленных кредитов;
            │       PB забирает 10% с грабежа>
            │
            └── [PlayerNo]  → (CBAfterNo / CBAfterNoProlongate)  ↩

  [CB.ConstructPirate.PlayerSend]  "Я хочу собрать корабль одному из ваших пилотов"
    → gate: обёртка GenericRefuseChecks (лицензия / ранг Юнга / очки)
    → gate: WarningPirateCnt — если PirateRating низкий, предупреждает
            что пилот не станет партнёром + макс 4 напарника
    └── CB_ConstructPirate_ShowStats (0x591650)
        │
        └── (ConstructPirate.PickHull)  "выбери корпус"
             │
             └── multi-step wizard (10 функций CB_ConstructPirate_*):
                  (PickEngine) → (PickFuelTanks) → (PickWeapon) →
                  (PickDefGenerator) → (PickRadar) → (PickScaner) →
                  (PickCargoHook) → (PickRepairRobot)
                  на каждом шаге:
                    [Add<Slot>]   → выбор из списка на складе/в магазине
                    [skip]        → "обойдёмся без этого"
                    (StatsXxx)    — статы выбранного предмета
                  │
                  ├── (ReadyCanAddMore) "уже можно, но можно добавить"
                  │   или (ReadyCanNotAddMore) "уже добавить нечего"
                  │
                  ├── [confirm]  "Собирай!"
                  │    → (CompletionText[/NotPartner])  "корабль собран,
                  │                                       пилот твой партнёр
                  │                                       <CntMonth> мес"
                  │       <-стоимость всех выбранных запчастей;
                  │        создан TPirate с указанной конфигурацией;
                  │        стал партнёром на <CntMonth> мес>
                  │
                  └── [cancel]  → (Cancelled) "смотри повнимательней"  ↩

  [CB.Improvement.PlayerSend]  "Я хочу улучшить оборудование"
    → gate: GenericRefuseChecks
    └── CB_Improvement_ShowDialog (0x59425C)
        │
        └── (Improvement.CBAnswer + CBAnswerNothing)
             "результат СЛУЧАЙНЫЙ, зависит от типа предмета"
            + (CBSeeItems) список: (ItemReadyForImprovement) "<ItemName> за <Money>"
            + gate: пустой → (CBSeeItemsNotHaveItems)  ↩
            │
            [PlayerAsk] выбор предмета
             │
             └── (CBNeedCostImprovement / CBNeedCostImprovementNodes)
                  "<Money> cr, скидка <Discount>%, итого <MoneyDiscount>"
                 │
                 ├── [PlayerOk]  "Валяйте"
                 │    → (CBAfterOk) <-MoneyDiscount; +случайный бонус к статам>
                 │    → (CBAfterRepeatNo/Ok) ещё улучшать?
                 │
                 ├── [PlayerNothing]  ↩
                 └── [PlayerNo]  "грабительские расценки"  ↩

  [CB.ShuffleTeleport.PlayerSend]  "Я хочу воспользоваться вашим портальным генератором"
    → gate: GenericRefuseChecks
    → gate: CBNoEnergy — если энергии генератора < порог
    → gate: CBNoInHyper — если CB в гипере
    → gate: CBNoPath — нет доступных маршрутов
    └── CB_ShuffleTeleport_ShowDialog (0x596B10)
        │
        └── (ShuffleTeleport.CBChoseDestination) + список
             (ShuffleTeleport.ToStar)  "В систему <ToStar>, <Cost> cr"
            │
            ├── [ToStar <ToStar>] выбор системы
            │    → (CBConfirmation)  "прокладываем курс, <Cost>. Верно?"
            │      [Confirm] → (CBAfterConfirm) <-Cost; CB прыгает в <ToStar>;
            │                                    игрок перемещается вместе>
            │      [NoConfirm] → (CBAfterNoConfirm) "обломал малину"  ↩
            │
            ├── [ToHangar]  "в ангар" (отмена)
            │
            └── [Refuse]  "не подходит" → (CBAfterRefuse) "это всё что есть"  ↩

  [CB.WarPlans.PlayerAsk]  "Я хочу обсудить наши военные планы. Как обстановочка?"
    → gate: GenericRefuseChecks
    └── CB_WarPlans_AskAboutCaptainOrRank (0x598194)
        │
        ├── (WarWithKlingAndCoalition — CoalitionOnly / KlingAndCoalition /
        │    KlingOnly): статус фронта по PiratesPercent  (7 порогов)
        │
        ├── (CBAsk)  "что тебя интересует?"
        │    │
        │    ├── [PlayerAskAboutCapture]  "могу ли я сам захватывать системы?"
        │    │    → (CBAnswersAboutCapture)  инструкция как захватить  ↩
        │    │
        │    ├── [PlayerAskAboutRanks]  "Что за шкала Рачехана?"
        │    │    → (CBAnswersAboutRanks)  таблица званий и полномочий
        │    │      + (ExtraTextAboutRanks)  ↩
        │    │
        │    └── [PlayerNoQuestions]  "у меня другие дела"  ↩
        │
        └── подменю операций:
             │
             ├── [Ambush.PlayerSend]  "Надо помешать военной операции"
             │    → gate: PirateRank >= головорез
             │    → CB_WarPlans_Ambush_ShowDialog (0x59A4C0)
             │      (Ambush.CBRefusePrefix + (варианты по галактике):
             │       CBAnswerNoOperations / CBAnswerNoOperationsSoon /
             │       CBAnswerAmbushAlreadyInProgress /
             │       CBAnswerNotEnoughPirates / CBAnswerOperationSoon
             │      "мы можем прилететь и испортить, <Cost> cr"
             │      + gate: CBNoEnergy  → недостаточно энергии
             │       [Confirm]/[Refuse] → (CBAfterOk)/(CBAfterRefuse)
             │
             ├── [Assault.PlayerSend/PlayerSend2]  "Атаковать систему коалиции/доминаторов"
             │    → gate: PirateRank >= атаман/хан
             │    → CB_WarPlans_Assault_ShowDialog (0x59B254)
             │      (Assault.CBChoseDestination + ToStar list)
             │       + gate: CBNoPath / CBNoEnergy
             │       [ToStar] → (CBAfterConfirm) <-Cost; CB прыгает + атакует>
             │       [Refuse] → (CBAfterRefuse)  ↩
             │
             ├── [Relocate.PlayerSend]  "Я хочу заказать передислокацию вашей базы"
             │    → gate: PirateRank >= шкипер
             │    → CB_WarPlans_Relocate_ShowDialog (0x597610)
             │      (Relocate.CBChoseDestination + ToStar list)
             │       + gate: CBNoPath / CBNoEnergy
             │       [ToStar] → (CBAfterConfirm) <-Cost; CB в <ToStar>>
             │       [Refuse] → (CBAfterRefuse)  ↩
             │
             └── [WarOperation.PlayerSend]  "Заказать пиратский набег"
                  → gate: PirateRank >= юнга + Player.Money >= <Money>
                  → CB_WarPlans_WarOperation_ShowDialog (0x5995E4)
                    (WarOperation.CBAboutWarOperation)  большой лор-текст
                     + "100% предоплата <Money> cr"
                     [PlayerOk] → (CBAfterOkGood/CBAfterOkBad)
                                   Good: <-Money; операция в <StarEnemy>>
                                   Bad:  <-Money; ничего не нашли>
                     [PlayerNo] → (CBAfterNo)  ↩

  [BaseUpgrade]  → (CB.Modern.Answer)  "<Money> cr в общак"
                    [PlayerOk] → <Money>-=; сервис прокачан
                    [PlayerNo] → (Modern.AfterNo)  ↩

  [I_TakeOff]  "Пока"
```

## 9. Bridge — МОСТИК УНИКАЛЬНОГО КОРАБЛЯ (не база — свой корабль)

```text
Не является базой в общем смысле — это меню на МОСТИКЕ игрока
(доступно по кнопке B на мостике особого прототипа Ni-Da),
но использует ту же форму TfRuinsTalk. Специфично:

  Greeting → (Bridge.BridgeGreeting)  "приветствую, капитан. Обстановка:"
              Энергия / щиты / истребители / стратегия / длительность вылетов

  ─── меню Bridge ───────────────────────────────────────────────────────

  [Bridge.BridgeBHAsk]  "Создать ЧД (<Cost> эн.)"
    → gate: Bridge.Energy >= <Cost>
    → gate: not BridgeBHHyperLock
    └── (BridgeBHChooseDestination)  "выберите систему назначения"
        [BridgeBHToStarMap]  "перейти к карте" — интеграция с галактической картой
        [BridgeBHCancel]  "отмена"
         <-Energy; черная дыра открыта в <ToStar> на несколько дней>

  [Bridge.BridgeImpulseShieldsOn/Off]  "Перевести щиты в импульсный/нормальный режим"
    → gate: Energy >= <SwitchCost>
    └── <Ship.ShieldMode := импульс/норм; -Energy>

  [Bridge.BridgeInterceptorsAsk]  "Управление истребителями"
    └── (BridgeInterceptorsChooseAction)  "звеньев: <Count>, цель: <Ship>,
                                            стратегия: <Strategy>, длительность:
                                            <Duration> дн, энергия: <DeployCost>"
        │
        ├── [BridgeInterceptorsTargetChangeAsk]  "Задать цель атаки"
        │    → (BridgeInterceptorsTargetChoose) + список кораблей
        │      или gate: (BridgeInterceptorsNextTargetMissing / NoEnergy /
        │                 NotNormalSpace / Off) — недоступно
        │      [BridgeInterceptorsTargetShip <Ship>] → <вылет звена>
        │      [BridgeInterceptorsTargetCancelManual]  "отменить выбор цели"
        │      [BridgeInterceptorsTargetCancel]  "отмена"
        │
        ├── [BridgeInterceptorsTargeting]  "Настройки автовыбора цели"
        │    → (BridgeInterceptorsTargetingChoose)
        │       6 стратегий: DefMax / DistMax / DistMin / HPMax / HPMin /
        │                    StrMax / Manual
        │      [BridgeInterceptorsTargetingAttack <StrategyName>] → сохранено
        │
        ├── [BridgeInterceptorsDurationChangeAsk]  "Изменить длительность"
        │    → (BridgeInterceptorsDurationChoose)  "<Duration>, <DeployCost>"
        │      [DurationLess]  / [DurationMore]  → скорректирует
        │      [DurationDone]  → сохранено
        │
        ├── [BridgeInterceptorsCallOffAsk]  "Отозвать звено"
        │    → (BridgeInterceptorsCallOffChoose)  + список <ShipList>
        │      [BridgeInterceptorsCallOffShip <Ship>]  → возврат звена
        │      [BridgeInterceptorsCallOffAll]  → все звенья возвращаются
        │      [BridgeInterceptorsCallOffCancel]  "отмена"
        │
        └── [BridgeInterceptorsDone]  "Завершить"  ↩

  [Bridge.BridgeHelpAsk]  "Расскажи мне о специальных системах корабля"
    └── (BridgeHelpChoose)  "что интересует?"
         │
         ├── [BridgeHelpQuestion1..4]  4 вопроса:
         │    → (BridgeHelpAnswer1..4)  подробное объяснение:
         │       1. Энергия
         │       2. Черная дыра
         │       3. Импульсные щиты
         │       4. Истребители
         │
         ├── [BridgeHelpQuestion5]  "А ты вообще кто такой?"
         │    → (BridgeHelpAnswer5)  "я Ni-Da, голограмма"
         │
         ├── [BridgeHelpMoreQuestions]  "ещё вопросы"  → возврат
         │
         └── [BridgeHelpCancel / NoQuestions]  "мне всё понятно" ↩

  [Bridge.BridgeExit]  "Вернуться к пилотированию"
```

## 10. GN — TECHNICAL FALLBACK (только Modern-диалог)

```text
GN — служебный тип, не имеющий собственных сервисов. Используется как
"общий шаблон" для Modern-диалога, когда база не имеет специфичных
для BaseUpgrade реплик:

  [BaseUpgrade]  "Я желаю поучаствовать в развитии вашей базы"
    → (GN.Modern.PlayerAsk)  "поучаствовать"
    → (GN.Modern.Answer)  "рады спонсорам! Требуется <Money> cr страховки"
       │
       ├── [PlayerOk]  "Начнём модернизацию"  <-Money; сервис прокачан>
       │
       └── [PlayerNo]  → (GN.Modern.AfterNo)  "приходите, спонсорская помощь
                                                никогда не помешает"  ↩
```

## 11. СВОДКА — КАКИЕ УСЛУГИ НА КАКОЙ БАЗЕ

```text
База | Ремонт | Улучш. | Прогр. | Гов.услуги          | Особое
─────┼────────┼────────┼────────┼─────────────────────┼──────────────────────
RC   | нет   | нет   | нет   | ММ↔Ноды, рейтинг     | финальный концерт
     |       |       |       | Sale/Take/GiveNod    | (Win.*)
PB   | ★★   | нет   | ★★   | смена расы/стороны,  | Хамелеон, SabCrack,
     |       |       |(за ноды)| набег, ноды, лор  | SpecialShip
WB   | ★★★  | нет   | ★★   | война/операция,      | FlyToEnemy, SpecialShip
     |(станд)|       |       | статус фронта,       |
     |       |       |       | звания               |
SB   | ★★★  | ★★★  | нет   | зонды, ТехПрогр.,    | SpecialShip, лекция
     |(всё)  |       |       | помощь исследованиям | (FormRuinsSB.History)
     |       |       |       | (SBscn)              |
BK   | нет   | нет   | нет   | Кредит, Депозит,     | Полис страхования
     |       |       |       | Инвестиции, Анализ   | (влияет на MC)
     |       |       |       | торговли             |
MC   | нет   | нет   | нет   | Диагноз, Уринотер.,  | Учёт Policy=/2 цены
     |       |       |       | Стимуляторы          |
CB   | нет   | ★★   | нет   | Лицензия, Сборка     | ShuffleTeleport,
     |       |(рандом)|       | корабля-партнёра,    | WarPlans (5 операций),
     |       |       |       | Военные планы        | мобильная база (Relocate)
Bridge| нет  | нет   | нет   | Черная дыра, Щиты,   | Только на прототипе
     |       |       |       | Истребители, Помощь  | (Ni-Da)
GN   | нет   | нет   | нет   | (только Modern)      | fallback для BaseUpgrade

★★★ — полный набор услуг ремонта (включая нестандарт/dom-оборудование)
★★  — базовый набор
нет — услуга не поддерживается на этой базе
```

## 12. ГЛОБАЛЬНЫЕ ГЕЙТЫ И ФОРМУЛЫ

```text
  Общие параметры цен (Ruins_CalcBaseRepairCost, GalaxyMoney_*):
    • GalaxyMoney_Tiny    cap=250
    • GalaxyMoney_Small   cap=1000
    • GalaxyMoney_Average cap=5000
    • GalaxyMoney_Big     cap=10000
    • GalaxyMoney_Huge    cap=25000
    Формула: result = ROUND(RangersAverageCapital * (1/divisor) * RaceMult)
             если result > cap: result = cap + ROUND((result - cap) * 0.3)

  pirateDiscountPct = ROUND(Player.PirateRating / 1.3) + 1
    → применяется к PB.Nod / PB.Repair / PB.Chameleon / CB.PirateLicense /
                    CB.Improvement

  Доступ к базам:
    • RC/PB/WB/SB/BK/MC — открыты всем
    • CB — только для игрока-пирата (t_Pirate AND PirateRankPoints>0)
    • Внутри CB — гейт по «шкале Рачехана» (пиратский ранг):
        Юнга (Junga)          — заказ пиратского набега
        Шкипер (Skipper)      — передислокация доминиона
        Головорез (Golovorez) — засада на военную операцию
        Атаман (Ataman)       — атака системы коалиции
        Хан (Khan)            — атака системы доминаторов

  Влияние Policy (страховки от BK):
    • MC.Illnes.<Money>  → цена /=2 если Player.HasPolicy
    • MC.Stimulants      → цена /=2 (MC5MedPolicy)

  Влияние SubType (пират vs рейнджер) на реплики:
    • RC.GreetingBad → игрок с pirate-статусом не получает бонусы
    • MC.MCSeeIllness → отдельная ветка Pirate ("для неудачников")
    • PB.ChangeSide → доступна перекидка в обе стороны
    • CB — доступ только пиратам

  Общий пункт-модификатор:
    [BaseUpgrade] расходует деньги игрока на постоянное улучшение сервисов
    базы (магазин пополняется, программа/материал даётся с скидкой и т.п.).
    Формула цены хранится в RuinsTalk_BaseUpgrade_ShowDialog (0x573A2C):
    зависит от текущего уровня Ruins.UpgradeLevel и RaceMult.
```

## 13. ФИНАЛЬНЫЙ КОНЦЕРТ В RC (после победы над доминаторами)

```text
Отдельный подблок FormRuinsRC.Win.*, срабатывает при следующей посадке
игрока в любой RC ПОСЛЕ уничтожения всех трёх серий доминаторов
(Blazer + Keller + Terron).

  Greeting → (Win.AddNews)  "прибыть в центр рейнджеров, форма парадная"

  Автоматический показ (по последнему уничтоженному доминатору):
    (Win.Blazer1 / Blazer2)  — победа над Блазером (1 или 2 механизма)
    (Win.Keller1 / Keller2 / Keller3)  — победа над Келлером (3 варианта)
    (Win.Terron1 / Terron2 / Terron3 / Terron4 / Terron5)  — Терроном
    (Win.TheEnd)  "серии повержены, коалиция салютует, значки сдать"

  → [AfterPlayerEnd1]  "рад победе"    → приглашение на концерт
  → [AfterPlayerEnd2]  "гнев не утих"  → приглашение на концерт
  → [AfterPlayerEnd3]  "будущее"       → приглашение на концерт
      (все три ветки → одна финальная сцена с концертом)

  <Player.Ended := true; экран статистики FormScore>
```

## 14. ИСТОРИЧЕСКИЙ ЭКСКУРС В SB (лекция про клисан)

```text
Отдельный подблок FormRuinsSB.History, доступен внутри диалога SB
как бесплатная лекция для новичков.

  [History.PlayerOk]  "Расскажите мне о войне с клисанами"
    ← показывается только когда:
       - SB имеет свободного лектора (обычно вначале игры)
       - игрок ещё не слушал эту лекцию

    → (History.SB)  "предложение бесплатного экскурса"
      (History.SBOk)  параграф 1 (Гаальцы vs Клисаны, "пузыри" в гипер)
      (History.SBOk1) параграф 2 (Махпелла, поражение Гаальской экспедиции)
      (History.SBOk2) параграф 3 (Рачехан, кража архива, случайный контакт)
      (History.SBOk3) параграф 4 (вторая война, ранджеры, разгадка Махпеллы)
      (History.SBOk4) финал: "Война Недоразумения"  ↩
```

## 15. ПРИМЕР ПОЛНОЙ СХЕМЫ (в формате пользователя)

```text
Схема для BK.TakeDebt:

  -[BK.TakeDebt.PlayerSend]  "Взять кредит"
    --gate: red-flag = 0 иначе (AddDebtContinue/DebtNoAccess/DebtNoPenalty) ↩
    --gate: system not-war  иначе (DebtNoWar) ↩
    --gate: no active debt  иначе (AddDebtYes "долг <Money> до <Date>")
    --(TakeDebt.BK)  3 варианта: срочный/обычный/малый
      --[PlayerOkMaxMoney/AveMoney/MinMoney]
        ---(BKAfterOk) <-Комиссия; +SendMoney; +Debt; +DebtDate>
      --[PlayerNo]
        ---(BKAfterNo) "летите в другой центр" ↩
    --[Exit / I_TakeOff]

Схема для PB.Chameleon:

  -[PB.Chameleon.PlayerAsk]  "У вас есть камуфляжное устройство"
    --(PB.Chameleon.PBAsk + <List> моделей)
      --[PlayerOk "серии <S> за <Cost>"]
        ---gate: Player.Money >= price[i]
        ---(PBAfterOk) <-Cost; +Chameleon; ChameleonBuyCount[i]++>
      --[PlayerNo]
        ---(PBAfterNo/NoAlt) "испугался?/лоси разбегутся?" ↩

Схема для CB.WarPlans (составной):

  -[CB.WarPlans.PlayerAsk]  "Как обстановочка на фронтах?"
    --(WarWithKlingAndCoalition — 7 порогов по PiratesPercent)
    --(CB.WarPlans.CBAsk)  "что интересует?"
      --[Ambush]
        ---gate: PirateRank>=Головорез, CBNoEnergy, CBAnswerXxx
        ---[Confirm] → (CBAfterOk)
        ---[Refuse]  → (CBAfterRefuse) ↩
      --[Assault]
        ---gate: PirateRank>=Атаман/Хан
        ---[ToStar]  → (CBAfterConfirm) <-Cost; +прыжок>
        ---[Refuse]  ↩
      --[Relocate]
        ---gate: PirateRank>=Шкипер
        ---[ToStar]  → (CBAfterConfirm) <CB прыгает>
      --[WarOperation]
        ---gate: PirateRank>=Юнга + Money>=Cost
        ---[PlayerOk] <-Cost; (CBAfterOkGood/Bad)>
        ---[PlayerNo] ↩
      --[PlayerAskAboutCapture]  → (CBAnswersAboutCapture) ↩
      --[PlayerAskAboutRanks]    → (CBAnswersAboutRanks) ↩
      --[PlayerNoQuestions]      ↩
```

## СВЯЗАННОЕ

```text
Articles/planet_base_dialogs_report.txt   — полный отчёт (3519 строк) с
                                            декомпиляцией всех обработчиков
D:\disasm2500\base_menus_structure.txt    — карта функций-обработчиков
                                            для каждой базы
D:\disasm2500\base_services_formulas.txt  — формулы всех цен услуг
Articles/Space_Dialog_Schemes.md         — диалоги в космосе (Talk.*)
Articles/SB_Equipment_Improvement.md     — детали SB-улучшения
Articles/Invention_Levels_and_Tech.txt    — TL магазинов и inv-levels
Articles/Illness_Stimulants_System.txt    — MC.Illnes/Stimulants механика
Articles/Ranger_Rating_System.txt         — что показывает RC.Greeting

Связанные memory-заметки:
  rangers_ruins_is_station (TRuins = все базы)
  rangers_sb_improvement (SB.Improvement механика)
  rangers_illness_stimulants (MC.Illnes/Stimulants)
  rangers_pilot_character_system (WB.NextRank очки)
  rangers_chameleon_system (PB.Chameleon)
  rangers_relations_system (attitude-гейты)
================================================================================
```
