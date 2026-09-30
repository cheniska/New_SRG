# Схема диалогов в космосе (Talk-раздел из Lang.txt) — Космические Рейнджеры HD

> Диздок перенесён из `Space_Dialog_Schemes.md` без правок текста: разделы оформлены заголовками,
> содержимое — дословно, моноширинным блоком (в исходнике — ASCII-вёрстка).

```text
Источник реплик: Lang.txt / SR_2_HD_Lang_vanilla.txt, ветка `Talk ^{}` (стр. 29528..32018).
Дублирующая ветка `Female ^{}` — те же самые сценарии, только реплики от лица NPC-женщины
(отбирается по TShip.Sex / имени в FaceList). Дублирующая ветка `PirateClan ^{}` —
полностью повторяет Talk для пиратских клановых NPC (peleng-пираты).

Точки входа в коде:
  • Форма диалога с кораблём:        TfTalk_Create / TfTalk_MenuHandler (0x6096F8)
  • Приветствие при открытии:        TfTalk_ChooseShipGreeting
  • Партнёрство (рейнджер):          Talk_Partner_AskJoin (0x61067C),
                                     Talk_Partner_OrderFlyToMe (0x610F00),
                                     Talk_Partner_OrderForAll_ApplyToGroup (0x6123C0)
  • Партнёрство (пират):             Talk_Pirate_OrderFlyToMe (0x615000), TalkWithParent stub
  • Гейт найма:                      TShip_CanBeHiredAsPartner_Check (0x5B3068)
  • Транклюкатор:                    Talk_Tranclucator_FlyToMe / _Return / (Attack/DropCargo/…)
  • Защита обломков:                 Talk_PreserveItems (0x6103DC)
  • BigWarrior-сервисы:              Talk_MilitarySupport_MenuHandler (0x6178C0),
                                     Talk_MilitarySupport_Main / _AfterCancel / _AfterNo
  • Доминатор (6 диалогов):          Talk_Dominator_HiDialog (0x616EB8),
                                     Talk_Dominator_PeaceDialog (0x616F84),
                                     Talk_Dominator_GoodsDialog (0x617058),
                                     Talk_Dominator_CommandDialog (0x61712C),
                                     Talk_Dominator_ProgramDialog (0x616B14),
                                     Talk_Dominator_ChameleonBossDialog (0x60C9CC)
  • Враждебный доминатор fallback:   TfTalk_DominatorHostileDialog (0x60FF0E)

Легенда:
  [X]   — пункт меню, который игрок может выбрать (открывает подветку/действие)
  (X)   — реплика NPC в ответ на выбранный пункт (одна из 5 рандомных)
  <X>   — конечное действие / переход в форму / изменение состояния
  → gate — условие, гейтящее выбор ответа
  Для персонажных веток NPC-ответы префиксуются по классу:
    Player/Ranger — обычный рейнджер (Player-ветка для мужчины, Ranger-ветка для nested)
    Pirate    — пират
    Warrior   — воин коалиции (t_Warrior)
    Transport — торговый транспорт
    Liner     — пассажирский лайнер
    Diplomat  — дипломат
    Computer  — реплика бортового компьютера партнёра/транклюкатора (после найма)
```

## 0. КАРТА ВЕТОК TALK-БЛОКА

```text
Talk ^{
    Accept / Reject / Cancel / Exit / ShipSay / PlanetSay / To — общие лейблы кнопок
    Attack           — совместное нападение (игрок предлагает)
    Trade            — торговля с кораблём в космосе
    Money            — вымогать деньги (игрок пират)
    Goods            — вымогать груз (игрок пират)
    Truce            — предложить мир (после обмена огнём)
    Protect          — попросить прекратить атаку на союзника
    PreserveItems    — попросить прекратить расстрел ошмётков/грузов на орбите
    DropGoodsInFear  — NPC-инициатива: бросает груз в надежде отвязаться
    Partner          — нанять/уволить рейнджера-напарника
    Pirate           — нанять/уволить пирата-напарника + приказ АТАКОВАТЬ конкретную цель
    PirateClan       — полная копия всех веток, но для NPC клана пиратов
    Tranclucator     — приказы своему боевому роботу-компаньону
    Dominator        — общение с доминаторами (Hi/Peace/Goods/Command/Programm/Chameleon)
    MilitarySupport  — сервисы у BigWarrior (Rank=1 военного коалиции)
    Refuse           — короткая реплика "не буду вести переговоры" (Pirate/Warrior)
    ExTalk           — расширенные лорные диалоги (OldHull…)
    Female           — зеркальная копия всего блока для NPC-женщин
    Count            — форматирование "день/дня/дней", "единица/…"
}
```

## 1. ОБЩАЯ АРХИТЕКТУРА

```text
Открытие: двойной клик по вражескому/нейтральному кораблю → TfTalk_Create → форма Talk.
Показывается TfTalk_ChooseShipGreeting (одна из ~30 приветственных фраз,
условия см. Articles/ship_greetings_conditions.txt), затем — главное меню.

Главное меню (TfTalk_MenuHandler) собирается динамически в зависимости от
характеристик собеседника. Присутствующие пункты по умолчанию:

    [Trade]           — если у собеседника есть трюм и это не доминатор/транклюкатор
    [Attack]          — предложить совместную атаку
    [Money]           — если игрок = t_Pirate или PirateClan (мораль пирата)
    [Goods]           — если игрок = t_Pirate или PirateClan
    [Truce]           — если в бою (у игрока или у собеседника CombatTarget указывает друг на друга)
    [Partner]         — если собеседник = TRanger и у игрока ещё нет партнёра / есть место
    [Pirate]          — если собеседник = TPirate и игрок может нанимать пиратов
    [Protect]         — если собеседник кого-то атакует
    [PreserveItems]   — если собеседник атакует ошмётки/дроп на орбите
    [MilitarySupport] — если собеседник = TWarrior и Rating=1 (BigWarrior) коалиции игрока
    [Exit]            — всегда

Если собеседник = TDominator (Blazer/Keller/Terron) → вместо обычного меню
показывается спец-диалог Talk.Dominator (см. §12).
Если собеседник = свой TTranclucator → упрощённое меню приказов (см. §11).
```

## 2. TRADE — торговля в космосе

```text
  [Trade.PlayerSend]  "Давай поторгуем" / "Поторговать хочешь?"
    │
    ├── gate: distance > 200        → (Trade.AnswerBigDist)  "подлети поближе"
    │                                                         ↩ возврат в main
    │
    ├── gate: attitude(Player) < N  → (Trade.AnswerBadRelations)  "не желаю"
    │                                                              ↩ возврат
    │
    ├── gate: у NPC на глазах предмет,│
    │        летит поднимать          → (Trade.AnswerAlreadyTakeItem)
    │                                                              ↩ возврат
    │
    ├── gate: NPC.CombatTarget !=nil → (Trade.AnswerWar)
    │                                   вариант 1: "закончу бой — потом"
    │                                   вариант 2: "помоги мне с <ShipBad> и тогда торговля"
    │                                                              ↩ возврат
    │
    ├── gate: NPC.HasGoodsToSell==0
    │        AND NPC.Money==0        → (Trade.AnswerNoNeedGoods)
    │                                                              ↩ возврат
    │
    ├── (Trade.TradeOkMayBuyNo)   "буду только продавать, покупать не могу"
    │
    └── (Trade.TradeOkMayBuyOk[/1]) "могу купить не более <Cnt> ед."
         │
         ├── [Trade.TradeGo]  "Давай торговать!"
         │     └─→ <открывается форма TfGoodsShop2 (кросс-трюм торговля)>
         │           • если у NPC не хватает денег на текущий выбор →
         │              tooltip "<Ship>: NoMoney"
         │           • если у NPC не хватает места →
         │              tooltip "<Ship>: NoSpace"
         │
         └── [Trade.TradeBreak]  "Отложить сделку"
              └─→ (Trade.AfterBreak)  "поторгуем в другой раз"
                    ↩ возврат в main → (Trade.AfterTrade) "ещё вопросы?"

    [Exit]  "Конец связи"
```

## 3. ATTACK — совместное нападение

```text
  [Attack.PlayerOffersAttack{0..4}]  "Предлагаю совместное нападение"
    │
    ├── (Attack.ComputerAsk{0..4})   "А на кого нападать?"
    │     └── игрок выбирает <Target> через прицел на радаре
    │
    ├── gate: NPC.InFear==True       → (Attack.ComputerInFear{0..4})
    │                                   "мне сейчас не до боя"     ↩ main
    │
    ├── gate: NPC.CombatTarget != nil→ (Attack.ComputerReadyAttack{0..4})
    │                                   "я уже дерусь с <Target2>, помоги"
    │                                    (изменяет предложение)     ↩ main
    │
    │   ─── по <Target>: ──────────────────────────────────────────────────
    │
    ├── gate: Target — свой транклюкатор (TTranclucator у игрока)
    │                                → ({Class}.PlayerItsMyTranc)  ↩ main
    │
    ├── gate: Target — транклюкатор игрока-собеседника
    │                                → ({Class}.PlayerItsYourTranc)↩ main
    │
    ├── gate: NPC.WeFriends(Target) OR pact           → ({Class}.WeAlreadyHavePact)  ↩
    ├── gate: attitude(NPC,Target) высокое            → ({Class}.PlayerWeFriends)    ↩
    ├── gate: Target — чужой транклюкатор             → ({Class}.PlayerWeFriendsTranc)↩
    ├── gate: ChanceToWin(NPC+Player, Target) низкая  → ({Class}.[Player|Class]Fear) ↩
    ├── gate: NPC занят своими делами (task set)      → ({Class}.HaveBusiness)       ↩
    ├── gate: attitude(NPC,Player) < 50               → ({Class}.Suspect)            ↩
    │
    └── (согласие) → ({Class}.[Player|Class]Ok{0..4})
           └─→ <NPC.CombatTarget := Target; NPC переходит в t_FollowAttack на Target>

  Class ∈ {Player, Ranger, Pirate, Warrior, Transport, Liner, Diplomat}
  → выбирается по TShip._VMT / SubType собеседника (Ranger vs Player только nesting differs).

  Warrior особенности:
    • Warrior.Ok часто содержит фразу "командование даёт добро"
      (при этом ходит запрос на HomePlanet — визуально это флейвор,
       код проверяет только attitude+ChanceToWin, как у остальных)
    • Warrior.HaveBusiness = "у меня приказ командования"

  Ranger.Suspect отдельная реплика от Player.Suspect — просто разные тексты,
  но выбор одной или другой зависит от рейтинга/расы собеседника.

    [Exit]  "Конец связи"
```

## 4. MONEY — вымогать деньги (только у Player.SubType==Pirate)

```text
  [Money.PlayerSend{0..6}]  "Кошелёк или жизнь!" / "Гони деньги" / …
    │
    └── (Money.ComputerAsk)  "И сколько тебе надо?"
        │
        ├── [Money.PlayerSendSum{0..3}]  "Сумма в <Money> cr меня бы устроила"
        │     • [Money.PlayerLess] "Меньше"   — сумма /=2
        │     • [Money.PlayerMore] "Больше"   — сумма *=2
        │
        ├── gate: distance > 200        → ({Class}.LongDistance) "догони сначала"
        │                                                       (fear-неглатим) ↩ main
        │
        ├── gate: NPC.Money < <Money>   → ({Class}.NotMoney)    ↩ main
        │
        ├── gate: NPC.WeAlreadyHavePact(Player) → (Money.WeAlreadyHavePact)  ↩ main
        │
        ├── gate: ChanceToWin(NPC, Player) > 0.4 OR NPC=Warrior
        │                                → ({Class}.No) — открытый отказ
        │                                   NPC.CombatTarget := Player  ↩ main
        │
        ├── gate: <Money> > NPC.Money*0.7 (жадный NPC)
        │                                → ({Class}.SumIsVeryBig) "снизь запросы"
        │                                                       ↩ повторный запрос
        │
        └── ({Class}.Ok{0..9})  согласие
             └─→ <NPC.Money -= <Money>; Player.Money += <Money>>
                 <NPC переходит в t_Peace с Player на N дней>

  Warrior.PlayerSend всегда → Warrior.No (никаких выкупов военным).
  Diplomat/Liner имеют расширенный набор No (9 реплик) с флейвором отказа.

    [Exit]
```

## 5. GOODS — вымогать груз (только у Player.SubType==Pirate)

```text
  [Goods.Send{0..4}] / [Goods.PlayerSend{0..4}]  "Это ограбление!"
    │
    ├── gate: distance > 200        → ({Class}.LongDistance)  ↩ main
    │
    ├── gate: NPC.CargoTotal == 0   → ({Class}.[Player]NotGoods)  ↩ main
    │
    ├── gate: NPC.WeAlreadyHavePact(Player)
    │                               → (Goods.WeAlreadyHavePact)   ↩ main
    │
    ├── gate: Warrior (все варианты) → (Warrior.No/1..4)  ("Warrior.Ok"="not talk")
    │                                                             ↩ main
    │
    ├── gate: ChanceToWin(NPC, Player) высокая
    │                               → ({Class}.No)  враждебная реакция
    │                                  NPC.CombatTarget := Player ↩ main
    │
    └── ({Class}.Ok{0..4}) согласие
         └─→ <NPC вызывает TryDropGoodsAsRansom (0x717CF8):
              выбрасывает часть груза в контейнерах вокруг себя,
              NPC.CombatTarget := nil, NPC.Attitude(Player)-=малое>

  Классы: Player, Ranger, Pirate, Warrior, Transport, Liner, Diplomat
  (см. rangers_ransom_and_robbery в memory: тот же путь).

    [Exit]
```

## 6. TRUCE — предложить мир (доступно во время боя NPC↔Player)

```text
  [Truce.PlayerSend{0..4}]  "Предлагаю заключить мир" / "Как насчёт перемирия?"
    │
    ├── случай A — без денег
    │     └── (Truce.ComputerAsk{0..4})  "И сколько ты готов выложить за мир?"
    │           игрок либо предлагает 0 либо переходит к варианту B
    │
    │     если предложил 0 (или у игрока PlayerNotHaveMoney*):
    │        • gate: attitude(NPC,Player) высокое / случайный шанс
    │           → (Truce.ComputerOkWithoutMoney{0..4})  <мир без денег>
    │        • иначе                     → повторный запрос суммы
    │
    └── случай B — с суммой
          [Truce.PlayerSendSum{0..4}]  "Я готов заплатить за мир <Money> cr"
            • [Truce.PlayerLess] / [Truce.PlayerMore]  корректировка суммы
            │
            ├── gate: NPC.WeAlreadyHavePact + предыдущее нарушение
            │                          → (Truce.WeAlreadyHavePact{0..4})  ↩ main
            │
            ├── gate: <Money> слишком мала (< порога от NPC.type/Cost)
            │                          → ({Class}.No)  "мало", ждёт новую сумму
            │
            ├── gate: NPC=Warrior — отдельная логика с "отчётом на HomePlanet"
            │                          → (Warrior.Ok / Warrior.No)
            │
            └── ({Class}.Ok{0..4})  согласие
                 └─→ <Player.Money -= <Money>; NPC.Money += <Money>;
                      NPC.CombatTarget := nil; NPC.Attitude(Player) увеличивается;
                      выставляется TShip.PactBits — на N дней мирное соглашение>

  Warrior.Send = "not talk" — воин НЕ инициирует Truce сам (игрок только).
  Diplomat.Send, Transport.Send и т.п. — стороны NPC при слабом положении.

    [Exit]
```

## 7. PROTECT — прекратить атаку на союзника

```text
  Показывается если NPC.CombatTarget != nil AND Target — знакомый/союзник игрока
  (attitude(Player,Target) >= порога).

  [Protect.PlayerSend{0..4}]  "Прекрати атаку. <Target> под моей защитой!"
    │
    ├── gate: NPC.InFear==False AND NPC.CombatTarget==Player (аггрессор без страха)
    │                                → (Protect.ComputerNotFearAndWar)  ↩ main
    │                                   (никакой реакции, NPC настроен агрессивно)
    │
    ├── gate: ChanceToWin(NPC, Player) высокая AND ({Class}==Ranger|Pirate|Warrior|…)
    │                                → ({Class}.No{0..4})  открытый отказ  ↩ main
    │
    └── ({Class}.Ok{0..4})  согласие прекратить атаку
         └─→ <NPC.CombatTarget := nil; в некоторых случаях attitude(NPC,Player) снижается>
         │
         ├── (target реагирует):
         │    gate: Target имел на борту деньги AND Target.NotFear==False
         │       → (Protect.TargetGiveMoney{0..4})  <Target.Money → Player.Money>
         │
         │    gate: Target имел груз AND Target.NotFear==False
         │       → (Protect.TargetGiveGoods{0..4})   <Target бросает контейнеры>
         │
         │    gate: Target был БЫСТРЕЕ NPC (не смог бы догнать)
         │       → (Protect.TargetMayRunAway{0..4}) "я бы и сам ушёл, спасибо"
         │
         │    gate: Target не боялся (ChanceToWin(Target,NPC) высокая)
         │       → (Protect.TargetNotFearShip{0..4}) "справился бы сам"
         │
         │    fallback:
         │       → (Protect.TargetThanks{0..4})  "нечем отблагодарить, но спасибо"

    [Exit]
```

## 8. PRESERVEITEMS — прекратить расстрел ошмётков/грузов на орбите

```text
  Показывается если NPC стреляет по TItem (Goods/Remains/Item),
  которые считаются "чужой собственностью" игрока или дропом на его глазах.

  [PreserveItems.PlayerSend{0..4}]  "Не стреляй по ошмёткам, они мои!"
    │
    ├── gate: NPC агрессивен и не в страхе → (PreserveItems.ComputerNotFearAndWar)  ↩
    │
    ├── gate: ({Class}.No условия — по силе, репутации, привычке пирата)
    │                                → ({Class}.No)  ↩ main
    │
    └── ({Class}.Ok{0..4}) согласие
         └─→ <NPC.CombatTarget := nil (перестаёт стрелять по item)>

  Реализация в коде: Talk_PreserveItems (0x6103DC).
  Использует те же class-подветки, что и Protect (структура зеркальна),
  только текст обыгрывает "ошмётки/hlam" вместо "<Target>".

    [Exit]
```

## 9. PARTNER — нанять/уволить РЕЙНДЖЕРА-напарника

```text
Гейт: TShip_CanBeHiredAsPartner_Check (0x5B3068). Проверяет:
  1) attitude(target, Player) >= 45 (0x2D)  → иначе Talk.Partner.Suspect
  2) target.Partner == nil AND target.CommonPartner == nil
         → иначе Talk.Partner.AlreadyHavePartner
  3) target.Rank > Player.Rank → Talk.Partner.YouNeedInMoreRank
  4) target.Rating (leadership marker) > Player.Rating → Talk.Partner.ILeader
  5) Player.Skill_Leadership достаточно → иначе Talk.Partner.NeedLeadership

Диалог:
  [Partner.PlayerSend]  "Иди под моё командование, <Ranger>"
    │
    ├── (Partner.Suspect{0..1})            attitude<45     ↩ main
    ├── (Partner.AlreadyHavePartner{0..1}) "мой партнёр <Partner>"  ↩ main
    ├── (Partner.YouNeedInMoreRank)        target.Rank>Player       ↩ main
    ├── (Partner.ILeader / Ileader1)       target ведёт себя как лидер ↩ main
    ├── (Partner.NeedLeadership{0..1})     Skill_Leadership низкий  ↩ main
    │
    └── (Partner.ComputerSayOk{0..1})  "за <Money> cr буду работать <Month> мес"
         │   • Money рассчитывается от target.Money+ExpGrade+GalaxyTechLevel
         │   • Month = 3..12 в зависимости от Rank и Suspect
         │
         ├── [Partner.PlayerLess]  → в 2 раза меньше
         │        (Partner.SmallMoney{0..2})  "не буду за такие копейки"
         │        (перерасчёт: если Money < порога → Partner.ComputerSayNo{0..1})
         │                                                    ↩ main
         │
         ├── [Partner.PlayerMore]  → в 2 раза больше (Month удваивается тоже?)
         │        новая (ComputerSayOk) с большей длительностью
         │
         └── [Partner.PlayerOk]  "Договорились"
              (Partner.Ok{0..1})  "буду служить <Month> мес"
              └─→ <target._Partner := Player; PartnerContract на <Month>*30 дней;
                   Player.Money -= <Money>>

После найма меню меняется, доступны:
  [Partner.FinancesCheck]  "Как твоё финансовое положение?"
      └── (Partner.FinancesReport)  "У меня <Money> cr"
          [Partner.FinancesConfirmed]  "Ясно"    ↩ main

  [Partner.FinancesOfferGift]  "Дать денег напарнику"
      └── (Partner.FinancesWaitForGift)  "сколько?"
          [Partner.FinancesSendGift]  "Возьми <Money> cr"
              └── (Partner.FinancesGotGift)  "спасибо, теперь <Money> cr"
                  <Player.Money -= X; Partner.Money += X>

  [Partner.FlyToMe]  "Хватит дурака валять, лети за мной"
      └── (Partner.IsOrderForAll)  "по группе или лично мне?"
          [Partner.OrderForAll]  → все партнёры (Talk_Partner_OrderForAll_ApplyToGroup)
          [Partner.OrderForYou]  → только этот
              └── (Partner.ComputerAgreeFlyToMe)  "слушаюсь, босс"
                  <TShip_OrderFollowShip(partner, Player, t_FollowNear)>

  [Partner.FlyToStar]  "Совершаем гиперпрыжок в систему <Star>"
      └── (Partner.ComputerAgreeFlyToStar)  <прыжок>

  [Partner.LandingToObject]  "Совершаем посадку на <ObjectName>"
      └── (Partner.ComputerAgreeLandingToObject)  <посадка>

  [Partner.PlayerSendDropCargo]  "выкидывай лишний груз"
      ├── gate: partner.Cargo==0 → (Partner.ComputerDropCargoNo{0..1})
      └── (Partner.ComputerDropCargoOk{0..2})  <partner.DropGoods()>

  [Partner.PlayerDismissSend]  "пора нам с тобой расстаться"
      └── (Partner.ComputerDismissQuestion)  "разве я плохо служу?"
          [Partner.PlayerDismissBad]   "надоел"
              → (Partner.ComputerDismissBad)  <partner→враждебен, атакует Player>
          [Partner.PlayerDismissGood]  "тебе опасно со мной"
              → (Partner.ComputerDismissGood)  <partner→свободен, дружба сохранена>
          [Partner.PlayerDismissNo]    "ха-ха, пошутил"
              → (Partner.ComputerDismissNo)  ↩ main

  In-fear override: [любой приказ] → (Partner.ComputerInFear)  "мне не до тебя"

Автособытия (не через меню, всплывают сами в конце Day):
  MateBreak      — партнёр рвёт контракт по стилю действий игрока (attitude падение)
  MateRiot       — бунт (когда игрок отдаёт неисполнимые приказы или другие партнёры мешают)
  MateTheEnd     — истёк срок → (AnswerLiderTheEnd) "может продлим?"
  MateTheEndLowLeadership — партнёр уходит из-за падения Skill_Leadership
  При попытке продлить: (AnswerLiderTheEndLowLeadership{0..1})

    [Exit]
```

## 10. PIRATE — нанять/уволить ПИРАТА-напарника (+ приказ АТАКОВАТЬ цель)

```text
Структура почти идентична Partner, но:
  • Гейт: NeedPirate вместо NeedLeadership (репутация в пиратской среде)
  • Гейт: attitude(target,Player) — с учётом Player.PirateRankPoints
  • Добавлен пункт [Attack] (только для пиратов):
      [Pirate.Attack]  "Нужно атаковать корабль"
        └── (Pirate.AttackList)  "Хорошо, босс. Кого?"
            игрок выбирает цель через прицел  [Pirate.AttackShip <ShipName>]
              → (Pirate.AttackShipOk)  <partner.CombatTarget := <ShipName>>
      Игрок может вызвать это неоднократно, натравливая пирата на разные цели.

  • Отказы полёта в опасные системы:
      Pirate.ComputerDisagreeFlyToStar   — "туда не пойду, доминаторы"
      Pirate.ComputerDisagreeLandingToObject — "меня туда посодют" (планета с врагами)

  • Пункт TalkWithParent = "Разговаривай с моим боссом"
      — если игрок наткнулся на пирата, у которого есть свой лидер (не Player)
      → форма закрывается, ничего не происходит.

  • Разрыв контракта:
      MateBreakRating (репутация пирата упала → уход)
      MateBreakRelation (репутация игрока с пиратским кланом упала)
      MateRiot (наезды со стороны других членов банды)
```

## 11. TRANCLUCATOR — приказы своему боевому роботу

```text
Условие: собеседник = TTranclucator И Tranclucator.Owner == Player.

Первый экран: (Tranclucator.Greeting)  "Вся моя боевая мощь к вашим услугам, хозяин"

Меню приказов:
  [Tranclucator.Attack.PlayerSend]  "Приказываю атаковать"
    └── (Tranclucator.Attack.Ask)  "Прошу указать цель"
        игрок выбирает цель → (Tranclucator.Attack.Ok)  <T.CombatTarget := target>

  [Tranclucator.DropCargo.PlayerSend]  "Выбрасывай всё из трюма"
    └── (Tranclucator.DropCargo.Ok)   <T.DropAllGoods()>

  [Tranclucator.FlyToMe.PlayerSend]  "Следуй за мной"
    └── (Tranclucator.FlyToMe.Ok)  <TShip_OrderFollowShip(T, Player, t_FollowNear)>
    (Talk_Tranclucator_FlyToMe @ 0x612480)

  [Tranclucator.Return.PlayerSend]  "Возвращайся назад"
    └── (Tranclucator.Return.Ok)  <T.ReturnAsArtefact()>
    (Talk_Tranclucator_Return @ 0x6125F0)

  [Tranclucator.SeekItems.PlayerSend]  "Приказываю приступить к сбору вещей"
    └── (Tranclucator.SeekItems.Ok)  <T.Mode := SeekItems>
  [Tranclucator.SeekItems.PlayerCancel]  "прекратить сбор вещей"
    └── (Tranclucator.SeekItems.Cancel)  <T.Mode := Patrol>

  [Tranclucator.LandingToObject]  "Совершаем посадку на <ObjectName>"
    └── (Tranclucator.AgreeLandingToObject)  <T.LandingOrder>
  [Tranclucator.LandingToStorage]  "садись на <ObjectName> и на склад"
    └── (Tranclucator.AgreeLandingToStorage)  <T.Landing + StorageMode>

  [Tranclucator.Options.PlayerSend]  "Настроить поведение"
    │
    ├── (Tranclucator.Options.CollectText: "разрешено собирать: <List>")
    │    для каждого <Item> из {Артефакты, Микромодули, Оборудование, Ошметки,
    │                            Товары, Ноды}:
    │       [CollectYes/CollectNo]  переключить бит T.PickupBitmap
    │
    ├── (Tranclucator.Options.LandText: "разрешено садиться на: <List>")
    │    для каждого <Land> из {Планеты, Базы}:
    │       [LandYes/LandNo]
    │    или общий [LandBad] "запрещено совершать посадки"
    │
    ├── (Tranclucator.Options.ArrangeText: "разрешено менять оборудование")
    │       [ArrangeYes/ArrangeNo] — можно ли Т. модифицировать себе шмот
    │       (при заходе на базу)
    │
    └── [Tranclucator.Options.PlayerBack]  ↩ main

  [Tranclucator.OrderForAll]  "и ретранслируй остальным транклюкаторам"
    — модификатор для любого приказа: применит его ко всем своим роботам

    [Exit]
```

## 12. DOMINATOR — общение с доминаторами (6 диалогов)

```text
Открывается двойным кликом по TKling (KlingType ∈ {Blazer, Keller, Terron}).
Ответы жёстко привязаны к KlingType — во всех подветках 3 роли:
  Blazer  — "Уничтожать. Приказ первого уровня."
  Keller  — "Собираю информацию. Введите данные."
  Terron  — "Преобразование. Приготовьтесь к репродуцированию."

Меню (одно для всех типов):
  [Talk.Dominator.HiPlayer]  "Привет! Как дела?"
    └── Talk_Dominator_HiDialog (0x616EB8)
        → (Dominator.Hi{Blazer|Keller|Terron}{0..5})  реплика по типу
                                                                  ↩ main

  [Talk.Dominator.PeacePlayer{0..2}]  "Я прилетел с миром. Положим конец войне!"
    └── Talk_Dominator_PeaceDialog (0x616F84)
        → (Dominator.Peace{Blazer|Keller|Terron}{0..5})  всегда отказ    ↩ main

  [Talk.Dominator.GoodsPlayer{0..1}]  "Товары, деньги есть? Выкидывай!"
    └── Talk_Dominator_GoodsDialog (0x617058)
        → (Dominator.Goods{Blazer|Keller|Terron}{0..4})  всегда отказ
             (доминаторы никогда не выкупаются деньгами — комментарий в коде
              0x60FF0E: "Денежного механизма выкупа НЕТ — только goods")
                                                                          ↩ main

  [Talk.Dominator.CommandPlayer{0..2}]  "Иди под моё командование!"
    └── Talk_Dominator_CommandDialog (0x61712C)
        → (Dominator.Command{Blazer|Keller|Terron}{0..4})  всегда отказ    ↩ main

  Если у игрока активна маскировка "Chameleon" (SE_Chameleon):
    Talk_Dominator_ChameleonBossDialog (0x60C9CC)
      → (Dominator.Chameleon.BossBlazer / BossKeller / BossTerron /
                             BossBlazerLand / BossTerronToStar / BossTerronGrowLock)
         — реплики от лица босса, "принимающего" игрока за своего
      (см. memory: rangers_chameleon_system)

  При запущенной программе управления (SE_ProgrammChameleon):
    Talk_Dominator_ProgramDialog (0x616B14)
      → gate: программа принята
              → (Dominator.ProgrammOk)  "Выполняю программу <Name>"
      → gate: программа отвергнута
              → (Dominator.ProgrammNo)  "Зафиксирована атака на систему"
      игрок инициирует через [Dominator.ProgrammPlayer]  "Запуск программы: <Name>"

  Диалог враждебного доминатора (когда игрок пытается что-то ему сказать
  но доминатор уже атакует Player):
    TfTalk_DominatorHostileDialog (0x60FF0E)
    → выбирает случайную No-реплику из ветки Talk.Dominator, конец связи.
```

## 13. MILITARYSUPPORT — BigWarrior (Rank=1 военный коалиции)

```text
Гейт: собеседник = TWarrior AND Rating==1 AND раса∈{Maloc,Peleng,People,Fei,Gaal}
      AND coalition-affiliated.

  [MilitarySupport.PlayerAsk / PlayerAsk1]  "Мне бы пригодились твои способности"
    └── Talk_MilitarySupport_Main (0x617D80)
        │
        ├── gate: NPC.CombatTarget == Player OR attitude<Enemy
        │           → (MilitarySupport.RefuseEnemy{0..2})  <NPC атакует Player>
        │
        ├── gate: Player = t_Pirate OR Player.PirateRankPoints > 0
        │           → (MilitarySupport.RefusePirate{0..2})  ↩ main
        │
        ├── gate: attitude(NPC,Player) < 100 (Wary)
        │           → (MilitarySupport.RefuseWary{0..2})    ↩ main
        │
        ├── gate: distance > 200
        │           → (MilitarySupport.RefuseDistance{0..2})↩ main
        │
        └── (MilitarySupport.AnswerXxx) — реплика зависит от Race:
              Maloc  → "1. Ремонт корпуса + 2. Buff"
              Gaal   → "1. Ремонт оборудования + 2. Buff"
              Fei    → "1. Ремонт оборудования + 2. Приём ошметков"
              Peleng → "1. Ремонт корпуса + 2. Приём ошметков"
              People → "1. Приём ошметков + 2. Buff"

  Меню Talk_MilitarySupport_MenuHandler (0x6178C0) — подпункты
  показываются согласно набору услуг для расы:

    [PlayerSendRepairHull / PlayerSendRepairHull2]  "починить корпус"
       ├── gate: Player.Hull.HP == Max → (AnswerNoNeedToRepairHull{0..2})  ↩
       ├── gate: Player.Nodes == 0     → (AnswerRepairHullNoNodes) "нет нодов"  ↩
       └── (AnswerRepairHull{0..2})  "Полный ремонт = <Nodes> нодов"
           [RepairHullOk] "полный ремонт"        <-Nodes → +HP пропорционально>
           [RepairHullPartialOk] "частичный"     <-все Nodes → +HP частично>
              → (AfterRepairHull)  ↩ main

    [PlayerSendRepairEq / PlayerSendRepairEq2]  "починить оборудование"
       ├── gate: no broken equipment          → (AnswerNoNeedToRepairEq)  ↩
       ├── gate: NPC.Nodes<X                  → (AnswerRepairEqNoNodes)   ↩
       └── (AnswerRepairEq)  "<Nodes> нодов"
           [RepairEqOk] / [RepairEqPartialOk]
              → (AfterRepairEq)  ↩ main
       (внимание: чинится только стандартное оборудование, не артефактное)

    [PlayerSendSellRemains / PlayerSendSellRemains2]  "сдать ошметки"
       ├── gate: Player.Remains == 0          → (AnswerNoRemains{0..1})   ↩
       └── (AnswerSellRemains)  "у тебя <Remains> ошметков за <Cost> cr"
           [SellRemainsSellAll{0..1}]  → <-Remains, +Cost>
                → (AfterSellRemains / AfterSellLastRemains)  ↩ main
           [SellRemainsSellSome{0..1}]  → (AnswerSellSomeRemains) → выбор Item

    [PlayerSendGetBuff / PlayerSendGetBuff2]  "нанодобавки"
       ├── gate: Player.Nodes < BuffCost      → (AnswerRepairHullNoNodes)  ↩
       └── (AnswerBuff)  "стоит <Nodes> нодов, эффект ослабевает при повторе"
           [BuffOk]              → (AfterBuff)  <нанести Buff SE-скрипт>
           [BuffProlongateOk]    → (AfterBuffProlongate)  <усилить existing Buff>
                (см. rangers_ai_pending_actions / SE-скрипты)

    [MilitarySupport.Cancel]  "я передумал"
       → (AfterCancel / AfterNo)  "ещё вопросы?"  ↩ main
```

## 14. DROPGOODSINFEAR — NPC-инициатива: бросить груз чтобы отвязаться

```text
Не открывается через меню. Broadcast-фраза, которую NPC (в основном Transport,
Diplomat, Liner) издаёт в чат, когда:
  • NPC.InFear == True
  • NPC.CombatTarget указывает на конкретного преследователя <FullShipBad>
  • NPC.Cargo > 0

  → (DropGoodsInFear.Drop / Drop1 / Drop2)
      "<FullShipBad>! Я согласен расстаться с частью своего груза."
  Одновременно NPC вызывает TryDropGoodsAsRansom (0x717CF8): бросает часть груза
  в контейнеры вокруг себя. Атакующий (если это игрок) может подобрать.
```

## 15. EXTALK — расширенные лорные диалоги

```text
Открываются как отдельная под-ветка меню, если у собеседника есть спец. флаг
(например, необычный корпус — OldHull, установленный на TShip). Формат: цепочка
из вопросов/ответов, разветвлённая на несколько ступеней с суффиксом _1, _1_1 и т.д.
Партнёрская версия каждой реплики имеет суффикс P.

  Пример: OldHull chain (у NPC установлен архаичный "нейроинтерфейсный" корпус)

  [ExTalk.OldHullPlayerSend / OldHullPlayerSendP]
      "А что это у тебя за корпус такой?"
    └── (ExTalk.OldHullRangerAnswer / P)  "с какой целью интересуешься?"
         │
         ├── [OldHullPlayerSend_1 / _1P]  "мы же все рейнджеры, одно дело"
         │    └── (OldHullRangerAnswer_1 / _1P)  рассказ о нейроинтерфейсе
         │        │
         │        ├── [OldHullPlayerSend_1_1] "что пошло не так?"
         │        │      → (OldHullRangerAnswer_1_1)  проблемы синхронизации
         │        │
         │        ├── [OldHullPlayerSend_1_2] "что стало с проектом?"
         │        │      → (OldHullRangerAnswer_1_2)  проект свёрнут, оборудование
         │        │                                    демонтировано в тайное место
         │        │
         │        ├── [OldHullPlayerSend_1_3] "мне такой не достать?"
         │        │      → (OldHullRangerAnswer_1_3)  "судьба сведёт вас иначе"
         │        │
         │        └── [OldHullPlayerSend_1_4 / _1_4P]  "спасибо / удачи"
         │              → (OldHullRangerAnswer_1_4 / _1_4P)  прощание  ↩ main
         │
         └── [OldHullPlayerSend_2]  "я великий рейнджер, выкладывай!"
              → (OldHullRangerAnswer_2)  "научись разговаривать"       ↩ main

Аналогично устроены другие ExTalk-цепочки (добавляются в моде — в vanilla
только OldHull).
```

## 16. REFUSE — короткие универсальные отказы

```text
Не самостоятельный диалог, а fallback-реплика, показываемая когда игрок
пытается открыть Truce/Peace с врагом, отвергающим переговоры по классу:

  (Refuse.Pirate)   — пират отказывает при попытке мира ("сосунок, отведай плазмы")
  (Refuse.Warrior)  — воин отказывает ("у меня приказ освободить систему")

Вызываются из Talk_Truce_Main когда TShip.NoTalk = True или доминатор/wild pirate.
```

## 17. ПРИМЕР ПОЛНОЙ СХЕМЫ (в формате пользователя)

```text
Схема для Trade — как в примере из запроса:

  -[Trade.PlayerSend]
    --(Trade.Answer(AlreadyTakeItem/BadRelations/BigDist(>200)/NoNeedGoods/War))
    --(Trade.TradeOkMayBuyNo/TradeOkMayBuyOk)
      --[Trade.TradeGo]
        ---открытие формы TfGoodsShop2
        ---если NPC не может торговать: tooltip NoMoney / NoSpace
      --[Trade.TradeBreak]
        ---(Trade.AfterBreak), возврат в main
      --[Exit]

Схема для Money (аналогично):

  -[Money.PlayerSend]
    --(Money.ComputerAsk)
      --[Money.PlayerSendSum]
        --[Money.PlayerLess/PlayerMore] — корректировка суммы
        --( {Class}.LongDistance/NotMoney/WeAlreadyHavePact/No/SumIsVeryBig )
        --( {Class}.Ok ) → <Player.Money+=<Money>; NPC pact на N дней>
    --[Exit]

Схема для Truce:

  -[Truce.PlayerSend]
    --(Truce.ComputerAsk)
      --[Truce.PlayerSendSum]
        --[Truce.PlayerLess/PlayerMore]
        --( {Class}.No/WeAlreadyHavePact ) — отказ / повторный запрос
        --( {Class}.Ok ) → <мир, pact, NPC.CombatTarget:=nil>
      --[Truce.PlayerNotHaveMoney]
        --(Truce.ComputerOkWithoutMoney) — если attitude высокий, мир бесплатно
    --[Exit]

Схема для Partner:

  -[Partner.PlayerSend]
    --(Partner.Suspect) attitude<45
    --(Partner.AlreadyHavePartner) уже занят
    --(Partner.YouNeedInMoreRank) target.Rank>Player.Rank
    --(Partner.ILeader) target ведёт себя как лидер
    --(Partner.NeedLeadership) skill_leadership низкий
    --(Partner.ComputerSayOk "<Money> cr, <Month> мес")
      --[Partner.PlayerLess] → пересмотр
        --(Partner.SmallMoney) слишком мало → (ComputerSayNo)
      --[Partner.PlayerMore] → удвоить
      --[Partner.PlayerOk] → (Partner.Ok) → <контракт>
    --[Exit]

  После найма — второе меню:
  -[Partner.FlyToMe/FlyToStar/LandingToObject/…]
    --(Partner.IsOrderForAll) → [OrderForAll/OrderForYou]
    --(Partner.ComputerAgree*) → <исполнение приказа>
    --(Partner.ComputerInFear) — если в страхе, приказы игнорируются
  -[Partner.PlayerSendDropCargo] → ComputerDropCargoOk/No
  -[Partner.FinancesCheck / FinancesOfferGift]
  -[Partner.PlayerDismissSend]
    --(ComputerDismissQuestion)
      --[PlayerDismissBad]  → ComputerDismissBad → <враг>
      --[PlayerDismissGood] → ComputerDismissGood → <свободен>
      --[PlayerDismissNo]   → ComputerDismissNo → ↩

Схема для Pirate — как Partner, плюс:
  -[Pirate.Attack]
    --(Pirate.AttackList)
      --[Pirate.AttackShip <ShipName>]
        --(Pirate.AttackShipOk) → <partner.CombatTarget := ship>
  -[Pirate.TalkWithParent] — если пират чей-то партнёр, диалог закрывается
```

## 18. КАРТА "КТО КАК СЕБЯ ВЕДЁТ" (сводка ответов по классам собеседника)

```text
Класс NPC          | Attack | Trade | Money    | Goods    | Truce  | Partner | Protect | Special
-------------------|--------|-------|----------|----------|--------|---------|---------|-------------------
TRanger (обычный)  | ★★★   | ★★★  | ★★      | ★★      | ★★★   | нанимаем| ★★     | ExTalk (OldHull)
TPirate            | ★★★   | ★★   | ★★★     | ★★★     | ★★    | нанимаем| ★      | AttackList (нанятый)
                   |        |       | (fear low)| (fear low)|        | (Pirate)|         |
TWarrior           | ★★    | ★    | НЕТ("no talk") | НЕТ("no talk") | Send="not talk"| никогда | ★       | MilitarySupport (BigWarrior)
TTransport         | ★★    | ★★★  | ★★★     | ★★★     | ★★★   | никогда | ★★     | DropGoodsInFear (fear)
TLiner             | ★★    | ★★   | ★★★     | ★★★     | ★★★   | никогда | ★★     | DropGoodsInFear (fear)
TDiplomat          | ★★    | ★★   | ★★★     | ★★★     | ★★★   | никогда | ★★     | Immune to Warrior attack
TKling(Dominator)  | НЕТ   | НЕТ  | НЕТ     | всегда No | НЕТ   | никогда | НЕТ    | 6 диалогов Dominator.*
TTranclucator(свой)| Command| НЕТ  | НЕТ     | НЕТ     | НЕТ   | приказы | НЕТ    | Options
TRuins (станция)   | —     | —    | —      | —       | —     | —       | —      | TfRuinsTalk (наземный интерфейс)

★★★ — полный набор реплик (5 вариантов) / реагирует активно
★★  — набор упрощённый
★   — минимум / преимущественно отказ
НЕТ — пункт меню не показывается либо всегда → универсальный отказ

Female-версия зеркалит все звёздочки — просто другой текст.

Пираты клана (PirateClan) — те же самые Talk-ветки, только реплики более
"по фене", + свой пиратский префикс к некоторым атрибутам (например,
Attack.PirateOk = "Эта дичь мне по нраву!").
```

## 19. ДИАЛОГИ NPC ↔ NPC В КОСМОСЕ

```text
Формальных диалогов между NPC (двумя не-Player кораблями) нет — все Talk-*
функции требуют FShip == Player. Однако наблюдаются публичные broadcast-фразы,
которые NPC "говорят" в общий чат и которые видит игрок:

  1. DropGoodsInFear (§14) — NPC-жертва обращается к NPC-агрессору
     "<FullShipBad>! Я бросаю груз!" — публично, но техически монолог.

  2. Ship-greetings (Articles/ship_greetings_conditions.txt) — при пролёте
     мимо игрока NPC может произнести приветствие, зависящее от:
       • Class + Race
       • Attitude к Player
       • Player.CurrentQuest / faction status
       • Player имеет транклюкатор / уникальный корпус
     Это не диалог, а monologue chat-line.

  3. Талант-обмен между двумя NPC пиратами:
       — Внутри AI (TPirate_AI_DailyDecision, memory: rangers_pirate_ai)
         пираты внутри одного клана обмениваются сообщениями при делёжке
         добычи — но это внутренние сообщения, невидимые в UI.

  4. Военные NPC (TWarrior_AI_FullCycle) — при получении приказа "устранить"
     могут звать других воинов через OnCombatHit_ApplyRelationPenalty +
     публичное сообщение "Всем кораблям в системе <Star>!" — но это тоже
     broadcast, не диалог.

Настоящий диалог NPC↔NPC (обмен репликами по очереди) в vanilla игре не
реализован. Единственный способ увидеть NPC-NPC "разговор" — это когда
игрок использует Chameleon (маскируется под доминатора) и слушает
приказы командира-хамелеона (Talk.Dominator.Chameleon.BossXxx) — но игрок
там формально участник, а не наблюдатель.
```

## 20. ЗАМЕТКИ ПО ГЕЙТАМ И ПОДБОРУ КЛАСС-ВАРИАНТА

```text
Класс-суффикс подветки ({Class}.PlayerXxx / {Class}.RangerXxx / etc.) выбирается
в TfTalk_MenuHandler по цепочке:

  if target is TDominator                      → уходим в Talk_Dominator_*
  else if target is TTranclucator (свой)       → Tranclucator.*
  else:
     if target._VMT == TWarrior                → Warrior.*
     elif target._VMT == TTransport:
        if SubType == Cargo                    → Transport.*
        elif SubType == Liner                  → Liner.*
        elif SubType == Diplomat               → Diplomat.*
     elif target._VMT == TPirate               → Pirate.*
     elif target._VMT == TRanger               → Ranger.*  (+ Player.* дублирует)
     else                                      → Player.*  (общий fallback)

Общие числовые пороги (по декомпилу и комментариям):
  • distance > 200          — гейт LongDistance / BigDist
  • attitude < 10           — авто-агрессия (см. rangers_relations_system)
  • attitude < 45 (0x2D)    — Partner.Suspect
  • attitude < ~50          — {Class}.Suspect в Attack/Money/Goods
  • ChanceToWin < 0.4       — {Class}.Fear (в Attack)
  • pact активен            — WeAlreadyHavePact
  • NPC.InFear == True      — ComputerInFear (для partner), либо смягчение (для остальных)
  • NPC.NoTalk == True      — вообще не открывается меню Talk (Refuse)

Money vs Truce vs Goods шкала суммы:
  • База: NPC.Money * (0.1..0.7)
  • При NPC.Money*0.7 < Player.Ask → SumIsVeryBig / No
  • При <10% → всегда Ok (если нет других блокеров)
```

## СВЯЗАННОЕ

```text
Articles/planet_base_dialogs_report.txt          — диалоги на планетах и базах
Articles/ship_greetings_conditions.txt           — reactionary приветствия
Articles/Chameleon_System.md                    — механика Chameleon-диалогов
Articles/Chameleon_NPC_Interactions.md          — доминаторские реплики
Articles/Ransom_And_Robbery.md                  — механика Goods-выкупа
Articles/Partner_AI.txt                          — механика партнёрства (ranger)
Articles/NonPartner_AI.txt                       — почему Warrior/Kling не нанимаемы
Articles/Tranclucator_AI.txt                     — транклюкатор
Articles/Relations_System.txt                    — attitude и гейты
Articles/InFear_System.md                       — InFear-состояние и его влияние
Articles/TRuins_AI.txt                           — станции (у них свой TfRuinsTalk)
================================================================================
```
