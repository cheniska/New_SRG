# МЕХАНИКА ОТКУПА И ОГРАБЛЕНИЯ (Rangers HD, Rangers.exe)

> Диздок перенесён из `Ransom_And_Robbery.md` без правок текста: разделы оформлены заголовками,
> содержимое — дословно, моноширинным блоком (в исходнике — ASCII-вёрстка).

```text
Обновление: 2026-07-20
Источники: IDB (Rangers.exe.i64), Lang.txt (dialog strings), связанные статьи
  (AI_Dispatch_Cadence, Partner_AI, TRuins_AI, Illness_Stimulants_System,
   Relations_System, InFear_System).
```

## 0. ТЕРМИНОЛОГИЯ

```text
"Откуп" (ransom)  = ДОБРОВОЛЬНАЯ отдача части имущества, чтобы прекратить
                    преследование/бой.
                    Две формы:
                       goods-ransom  — сброс товаров в космос (TGoods).
                       money-ransom  — прямая передача кредитов target→offerer.
"Ограбление"       = навязанный (диалогом либо стрельбой) отбор груза/денег.
                    Игровой Player инициирует его через диалог PlayerSend*,
                    NPC-пират — через TPirate_AI_TryMoneyRansom
                    (money) и общий OnCombatHit-ответ (goods).

Ключевое: денежного откупа В ФАЗЕ AI по инициативе offerer НЕТ у voин/торговец/
рейнджер — они умеют лишь принимать чужое предложение (VMT+44) и, если припёрт,
сбрасывать goods (TryDropGoodsAsRansom). Активно ПРЕДЛАГАЕТ деньги только пират.
```

## 1. ОБЩАЯ СХЕМА

```text
                         ┌──────────────────────────┐
                         │  ОБЩИЙ БАЗОВЫЙ АППАРАТ   │
                         └──────────────────────────┘
    TShip_ChanceToWin              0x70B380  формула HP*Dmg/Size ÷ HP*Dmg/Size
    TShip_GetRansomThreshold_ByChance
                                   0x70B8E0  chance → 0..100 порог доверия
    TShip_GetMoneyByOwnerMultiplier
                                   0x70083C  self.Wealth * OwnerMul[idx];
                                             soft-cap 5000+ (delta*0.5)
    PortionInDiapason              0x8729AC  линейная интерполяция
    RndOut01                       0x8716DC  rnd генератор [0..1)
    TShip_TryDropGoodsAsRansom     0x717CF8  сбросить товары как откуп
    TShip_DropGoodsByType          0x717C74  spawn TGoods в текущей позиции
    TShip_AI_HandleOverload        0x716508  сброс лишнего груза (overload)
    TShip_PostMessage_GaveRansom   0x7277A0  журнал: "X отдал Y груз"
    TShip_PostMessage_AcceptedMoneyRansom
                                   0x727AA4  журнал: "X принял N cr от Y"

                         ┌──────────────────────────┐
                         │       VMT+44 SLOT        │
                         │  TryAcceptMoneyRansom    │
                         └──────────────────────────┘
    TRanger    0x75B964            принимает от >~0.25 chance / >порог суммы
                                   ПЛЮС спец. Player-branch (см. §4)
    TPirate    0x5B228C            reject-first, много спец. случаев
    TWarrior   0x5BD0AC            НЕ пугается: только сумма vs PathTotal
    TTransport 0x5C3DDC            почти всегда accept при выше порога
    TKling     0x5CD464            ВСЕГДА return 0 — от доминаторов не откуп.

                         ┌──────────────────────────┐
                         │  ОФФЕР MONEY-RANSOM      │
                         └──────────────────────────┘
    TPirate_AI_TryMoneyRansom      0x5AF2E4  сам предлагает деньги преследующему

                         ┌──────────────────────────┐
                         │      "PAY" HELPERS       │
                         │ money target→offerer     │
                         └──────────────────────────┘
    (pirate  side) sub_5B20F4      accept: transfer + PirateClan -10 к рейнджеру
    (warrior side) sub_5BCE84      accept: transfer + разлочка escorts
    (ranger  side) sub_75B6B8      accept: transfer + деньги идут в MB_bank_pending
                                   если Player перенаправляет через SB (>0 skill)
                                   + PirateRankPoints (2 за Ranger, 1 за Transport)
    (transport side) sub_5C3D8C    просто transfer
```

## 2. GOODS-RANSOM: TShip_TryDropGoodsAsRansom (0x717CF8)

```text
Прототип:  bool TryDropGoodsAsRansom(self, target_value)

Guard-flag: self[+1176] (RansomGivenToday) — предотвращает повторный сброс
            за один тик.

Алгоритм (Delphi-псевдокод):
    if self[+1176] then exit(false);
    total_value := 0;
    dropped_types := 0;
    for pass := 1..3 do
        for i := 0..7 do
            if self.Goods[i] > 0 then begin
                base_price := g_GoodsParamsTable[i].base_price;
                stock_val  := self.Goods[i] * base_price;
                qty := PortionInDiapason(
                          hi_out = 7,  lo_out = 1,
                          hi_in  = 3*target_value,
                          lo_in  = target_value/3,
                          x = stock_val );
                qty := round(qty); if qty < 1 then qty := 1;
                total_value := total_value + qty*base_price;
                TShip_DropGoodsByType(self, i, qty);   // spawn TGoods
                inc(dropped_types);
                if total_value > target_value then break;
                if dropped_types > 2                then break;
            end;

Ключевое:
  • сбрасывается 1..3 типа товара, не более 2..3 итераций;
  • объём каждого пропорционален размеру запасов (много угля — много угля
    и уйдёт), но зажат [1..7] от stock_value в [target/3..3*target];
  • total_value стремится дойти до target_value, но плавает вокруг него;
  • НЕТ прямой передачи денег — только груз в TGoods, лежащий рядом с self;
    подобрать может кто угодно (в т.ч. Player) обычным подбором.

DropGoodsByType (0x717C74):
    good := TGoods.Create;
    GoodsDrop_PlaceInSpace(good, type, qty);
    good.Price := round( sub_70D344(self, type) * qty );  // локальная цена
    good.PosX := self.PosX;  good.PosY := self.PosY;
    sub_717EC0(self, good, 0);       // регистрация в системе
    sub_70D394(self, type, qty);     // вычесть у self
    TShip_CalcParam(self, true);     // пересчёт массы

Точки вызова TryDropGoodsAsRansom (xrefs):
    0x5AC7EB  TPirate_AI_DailyDecision    — низкий HP при отсутствии cash
    0x5C08A3  TTransport_AI_DailyDecision — атакован и chance<1
    0x60FF0E  TfTalk_DominatorHostileDialog — доминатор торгуется с Player
    0x7054A5 / 0x705BAD / 0x7061B7
             TShip_TakeWeaponDamage_FullCycle — реакция на попадание
             (3 разных ветки — по типу владельца / состоянию цели)
    0x7522CC  TRanger_AI_DailyDecision    — рейнджер сбрасывает груз в панике
    0x79361B  TPlayer_NextDay             — инфекция Player'а: раз в 21 день
                                            в safe-state RandRange(Tiny,Big) cr
                                            даёт TargetValue, потом сброс
```

## 3. VMT+44 «TryAcceptMoneyRansom» — DECISION МОДЕЛЬ

```text
Все 5 обработчиков имеют один общий скелет проверок ⇒ различаются частностью:

    // Общие reject-и (для Player-инициатора):
    if VMT+48(...)   then exit(false);              // «специальный скип»
    if offerer._3F8 == self then exit(false);       // мой escort
    if Player==offerer and self._49D then exit(false);
    if Player==offerer and Galaxy.Day < self.MB_dock_timer + 30
                     then exit(false);              // только что из дока

    // Общие условия ACCEPT (любой одной достаточно):
    A) ShouldFlee_ChanceBased() = true   // паника (VMT+33) → всё равно accept
    B) ChanceToWin(self,offerer) < 1.0
        AND HealthPercent(self) < 40 %   // подбит и не выигрываю в бою
        AND VMT+34() = true              // «согласен уступить» флаг
    C) ChanceToWin(self,offerer) < 0.25  // явно проигрываю
    D) amount > PortionInDiapason(
                 GetMoneyByOwnerMultiplier(self, HI_IDX),
                 GetMoneyByOwnerMultiplier(self, 1),
                 100, 0,
                 GetRansomThreshold_ByChance(self, offerer) )
        (сумма превысила «плату по классу владельца»,
         масштабированную по chance-порогу)

    В случае accept: специфичный helper переносит деньги и очищает бой.

Различия по классам:

  TPirate (0x5B228C):
    Верхний множитель — 5 (idx=5)
    Дополнительная логика:
      • если self==Player.AssignedTask и звёзды совпадают —
        РЕКУРСИЯ в Player.VMT+44 (Player сам решает за напарника-пирата)
      • «pirate-pirate» кросс-система (self.Owner=7, target.Owner!=7,
        star.Owner=Pirates=2, MB_route_flags) —
        отдельная ветка: accept только если
                amount > 2*PathTotal * 2.0 (~ 4·PathTotal)

  TWarrior (0x5BD0AC):
    ChanceToWin/HealthPercent ветки НЕ ИСПОЛЬЗУЕТ — воин не пугается.
    Единственный критерий:
      • pirate-owner-in-normal-star ветка: amount vs 2*PathTotal
      • иначе:       accept iff amount > PathTotal
    → Money-ransom воину заплатить ПРАКТИЧНО (нужна крупная сумма).
    Goods-ransom воин НЕ принимает: см. §5 (VMT+184/188 для боя).

  TTransport (0x5C3DDC):
    Верхний множитель — 4 (idx=4)
    Всё как в общем скелете (принимает и по chance, и по сумме).
    При принятии: TRanger_AddClassProgress_Trader(+1)
    — offerer, если он рейнджер, получает +1 к Trader-прогрессу
    (сама сделка учитывается как торговая операция).

  TRanger (0x75B964):
    Верхний множитель — 3 (idx=3)
    Порог ChanceToWin — 0.25 (вместо "< 0" у пирата).
    Условие ACCEPT (в дополнение к общим) в специфической ветке
    Player-является-target и g_AutoEquipMode:
       — обычная accept-логика;
    Player-является-target и НЕ AutoEquipMode и *off_882850 >= 7
       (порог Points/Rating рейнджера-игрока):
       — открывается диалог-подтверждение sub_727230(target, 2, ...)
         "Send" с суммой; при confirm:
             • sub_75B6B8 (transfer)
             • g_PlayerStar[0]+242 = 1 (флаг «Player атаковал рейнджера»)
             • TShip_ProcessMissileHit(Player, 32, target)
               — как будто нанёс некий «удар» (attack progress?)

    ACCEPT helper (0x75B6B8) специфика:
       • если Player!=offerer → обычный перевод.
       • ИНАЧЕ (Player сам НПС-жертву раскулачивает):
             тратит "жизненные силы" через (**+36)(),
             если Player жив и здоров → сумма * const/const идёт в
             offerer.Money и одновременно в Player.MB_bank_pending,
             капается 100 000 000 (100 M cr) — банковский счёт
             роутит доход через SB/BK.
       • если offerer.Owner==7 (пират), а target — рейнджер:
             TShip_AddPirateRankPoints(offerer, 2)   ← 2 очка ранга
         если target — транспорт:
             TShip_AddPirateRankPoints(offerer, 1)   ← 1 очко ранга
         Соответствует диалогу:
             RankPoints=Ограбление рейнджера    — 2 очка
             RankPoints=Ограбление дипломата, лайнера или торгаша — 1 очко

  TKling (0x5CD464):
    return 0;   // всегда reject → нельзя дать взятку доминатору
```

## 4. ACCEPT-HELPERS: ЧТО ПРОИСХОДИТ ПРИ УСПЕХЕ

```text
TPirate.sub_5B20F4:
    self._1093 := 0                     (сброс «flag ransom-request»)
    if self.Owner=7 && self.star.Owner=Pirates: for every pirate co-nav
        call sub_70C1E0(pirate, target) → clear-combat cascade
    if target is TRanger:
        Planet_PirateClan.AdjustRelationToRanger(target, +10)
        TRanger_AddClassProgress_Trader(+1)
    TShip_SetMoney(target,  target.Money - amount)
    TShip_SetMoney(self,    self.Money   + amount)
    sub_70C1E0(self, target)            → взаимный peace + очистка

TWarrior.sub_5BCE84:
    money transfer
    sub_70C1E0(self, target)            peace
    self.CarryingWeaponIdx := 0         (снимает атакующее оружие)
    if target is TRanger: Trader-прогресс +1
    Проход по всем TWarrior'ам с тем же HomePlanet:
        сброс их таргетов на target, ChangeState(0) при Order=6 (attack)
        — вся эскадрилья прекращает бой с target.

TRanger.sub_75B6B8:
    см. §3 (Ranger) — маршрутизация через MB_bank_pending при Player-vs-NPC.

TTransport.sub_5C3D8C:
    просто transfer + peace (sub_70C1E0).

sub_70C1E0 (peace-cascade, 0x70C1E0):
    a1.CombatTarget := nil; a2.CombatTarget := nil;
    a1._3F8 := a2;  (лидер/эскорт связка)
    если Player == a2 → Player._3F8 := a1
    сброс weapon-lock у обоих кораблей
    сброс _80 у Hull (missile lock)
    релейшн-адjust:
        if a1.Type=1 (Ranger)  && Get(a2)<10: Adjust(a2)=10   (нормализация)
        if a2.Type=1 (Ranger):                Adjust(a1)=30   (штраф атакеру)
    если Order=6 (Attack) на a2  → ChangeState(a1, 0)
    Рекурсивно чистит слежение партнёров/транклюкаторов
    Чистит missile.target у ракет обоих ships.
```

## 5. МОДЕЛЬ ПИРАТА-ОФФЕРА: TPirate_AI_TryMoneyRansom (0x5AF2E4)

```text
Уникальный AI-шаг: пират сам предлагает ДЕНЬГИ тому, кто его преследует.

Пред-условия (все обязательны):
  1) TShip_CanAct(self)
  2) self.CombatTarget существует и target.DestObj == self
     (цель именно за мной)
  3) self.CombatTarget._3F8 != self          (я не лидер цели)
  4) self.Money > 100
  5) не в гиперпространстве (sub_7036F8 = 0)
  6) target.Type НЕ в bitmap off_882CC0 (skip-mask доминаторов/скриптов)
  7) !self.NoTalk && !target.NoTalk
  8) не Owner=7 + ClassStatus[1] + coalition standing (2..3)
     (пират-в-коалиции лучше не откупается)
  9) dist(self,target)² ≤ sub_70B1E8(target)² (в дистанции пуска ракет)
 10) РЕДКО: одно из
       • (target.Id * Galaxy.Day) mod 3 == 0 && RndOut01(seed2) > 0.25
       • HealthPercent(self) < 20 %          ← гарантированный триггер при <20%

Расчёт суммы (offer):
    money_offered := min(self.Money, GetMoneyByOwnerMultiplier(self, 1))
    max_amount    := GetMoneyByOwnerMultiplier(self, 3)
                     + GetMoneyByOwnerMultiplier(target, 4)
    max_amount    := min(max_amount * 0.5, self.Money)
    // interpolate: чем ниже HP, тем ближе к max_amount
    sum := PortionInDiapason(
             hi_out = money_offered,
             lo_out = max_amount,
             hi_in  = Hull.Size,
             lo_in  = 0,
             x      = HP )
    if sum < 100 then sum := 100
    amount := round(sum)

Вызов:
    accepted := target.VMT[44](target, self, amount)   // TryAcceptMoneyRansom
    if Player!=target && Player.CurStar==self.CurStar:
        TShip_PostMessage_AcceptedMoneyRansom(self, target, ...)  // журнал
    if accepted && self.VMT[33]():                     // ShouldFlee?
        // возможен второй раунд (баг или анти-фрирайдер?)
        recursive TPirate_AI_TryMoneyRansom
```

## 6. ОГРАБЛЕНИЕ В ДИАЛОГЕ (Player ➜ NPC)

```text
Диалог инициируется активацией NPC (клик по кораблю на радаре).
Ключи Lang.txt в секции ShipCommunication:

Ветка Goods (грабёж товаров):
    PlayerSend      — «Мне нужен твой груз, отдай по-хорошему»
    PlayerSend1..4  — варианты угроз («Вытряхивай груз...», «...разнесу корыто»)
    PlayerLongDistance{,1..4}  — NPC отказывает: слишком далеко (нет угрозы)
    PlayerNo{,1..4}            — NPC отказывает по разговорной модели
    PlayerNotGoods{,1..4}      — «трюм пуст»
    PlayerOk{,1..4}            — соглашается, сброс части груза
    RangerNo/Ok/LongDistance — тот же набор для offerer'а-рейнджера
    Send{,1,2,3}    — те же реплики от NPC-пирата, инициирующего грабёж Player
    WeAlreadyHavePact — «мы же заключили пакт»

Ветка Money (грабёж денег):
    PlayerSend, PlayerSend1..6   — «Гони деньги, красотка», «Кошелек или жизнь!»
    PlayerSendSum{,1..3}         — «Сумма в <Money> cr меня бы устроила»
    PlayerLongDistance{,1..7}    — отказ по расстоянию
    PlayerNo{,1..6}, PlayerNotMoney{,1..9}, PlayerOk{,1..6}, ComputerAsk{,1}
    Send{,1,2}                   — реплики NPC-пирата, требующего денег

Что за код это дергает:
  • Диалог собирается в TfTalk_MenuHandler (0x6096F8, ~30 KB — гигантский switch).
  • Выбор «требую груз» приводит к вызову TryDropGoodsAsRansom (или прямо
    DropGoodsByType, если это принудительный «Send» пирата-НПС), см. xrefs §2.
  • Выбор «требую деньги» приводит к вызову target.VMT[44] (TryAccept…).
  • Reply-строка выбирается набором ChanceToWin / distance / attitude:
    LongDistance-строки → radar_range < dist, PlayerNo-строки → target пугать
    невыгодно, и т.д.
  • При добровольном сбросе NPC груза (Goods.PlayerOk) вслед за drop
    вызывается sub_70C1E0 (peace) и очищается CombatTarget.

Специальный случай — Player грабит РЕЙНДЖЕРА:
  См. §3, ветка TRanger + Player+off_882850>=7 → sub_727230 (yes/no confirm) +
  sub_75B6B8, деньги маршрутизируются через MB_bank_pending (Player получает
  их не сразу, а через SB/BK/квесты).
  Также: g_PlayerStar[0]+242 := 1 — флаг «Player совершил акт пиратства
  против рейнджера», используется другими системами (reputation drop и т.п.).

Награда за грабёж (TRanger.sub_75B6B8 при offerer.Owner==7):
    target=TRanger    → +2 pirate rank points
    target=TTransport → +1 pirate rank point
    (см. Lang: RankPoints=Ограбление рейнджера — 2 очка;
               RankPoints=Ограбление дипломата/лайнера/торгаша — 1 очко)
```

## 7. АТАКА → OVERLOAD → ДРОП (косвенное «ограбление»)

```text
TShip_AI_HandleOverload (0x716508) — не связан с диалогом, а вызывается
в общем ship-tick. Если у корабля отрицательный FreeSpace (MB_exp_to_next<0):

  if !NoDrop:
      выбирает самый ДЕШЁВЫЙ TGoods (sub_71814C) или item (sub_7182D8);
      предпочтение по value/mass ratio (v27, v26);
      если найден goods-типа item подходящих размеров:
          TShip_DropGoodsByType(self, type, qty)
      если найден equipment: sub_717340 (обычный EjectItem)
      если ничего нельзя сбросить: self.Destroy = 1  (корабль лопается)

Как ЭТО связано с грабежом:
  Player сбивает вражеский корабль лёгкими попаданиями, тот, оценивая ChanceToWin,
  вызывает TShip_TryDropGoodsAsRansom из TShip_TakeWeaponDamage_FullCycle
  (0x704490) на попаданиях (3 xrefs — по нарастанию урона). Игрок собирает
  падающие TGoods. Никакого прямого money-transfer нет: экономика — через goods.
```

## 8. ЖУРНАЛ И УВЕДОМЛЕНИЯ

```text
TShip_PostMessage_GaveRansom (0x7277A0):
    Проверки: Player.CurStar==self.CurStar, Player.CanAct, radar-range,
              !NoTalk у обоих, !ChameleonConfusion.
    Формат: «<Ship> отдал груз <Ship> в системе <Star>».
    TGalaxy_PostMessage(type=1) → лента событий галактики.
    Id ship-a и ship-b пишутся в message.Extra.

TShip_PostMessage_AcceptedMoneyRansom (0x727AA4):
    Аналогично, для money-версии. Вставляет сумму через "Send" ключ Lang.
    Вызывается из TPirate_AI_TryMoneyRansom.
```

## 9. РЕЗЮМЕ / ЧТО НУЖНО ЗАПОМНИТЬ

```text
  1. Goods-ransom (TShip_TryDropGoodsAsRansom) — универсальный механизм,
     работает у всех классов кроме TKling. Guard 1176 предотвращает
     повторный сброс за один тик. Дропает до 3 типов груза общей стоимостью
     ≈ target_value (плавает).

  2. Money-ransom существует в ДВА этапа:
     • Offer: TPirate_AI_TryMoneyRansom (только пираты сами предлагают)
     • Accept: VMT+44 у каждого класса. Kling всегда reject.

  3. TWarrior отличается: не «пугается» chance-ом, критерий только сумма
     против PathTotal. Крупный откуп воин деньгами принимает; grib-ем — нет.

  4. TRanger при Player-attacker имеет специальную ветку:
     • открывается yes/no confirm;
     • деньги идут через MB_bank_pending (SB/BK-роутинг);
     • cap 100 M cr;
     • пирату (offerer.Owner=7) дают PirateRankPoints (2 за рейнджера,
       1 за транспорт).

  5. accept-helper sub_70C1E0 всегда снимает CombatTarget/weapon-lock, ставит
     _3F8 (escort binding), при Ranger-target ещё и штрафует Adjust(a1)=30
     (обидчик рейнджера теряет 30 attitude).

  6. TPirate accept-helper (sub_5B20F4) при sekcтр отдельно улучшает
     отношение Planet_PirateClan к рейнджеру-жертве на +10 (компенсация).

  7. От доминаторов откупиться нельзя ни товаром (TryDropGoodsAsRansom
     работает — но dominator-boss AI игнорирует; см. TfTalk_DominatorHostileDialog
     — отдельный диалог с ChanceToWin-порогом 0.1 и PortionInDiapason
     (Money_Small/2 .. Money_Tiny/2)), ни деньгами (TKling.VMT+44 == 0).
     Единственный «диалог» — сброс груза + отказ атаки на N дней.

  8. Дополнительные точки «косвенного откупа»:
     • overload-cascade (HandleOverload) — сброс лишнего груза, не связан
       с ransom-механикой, но результат тот же: TGoods в космосе.
     • Player-infection (TPlayer_NextDay, инфекция ⇒ safe-state ⇒ раз/21д)
       сбрасывает груз как «дань» доминаторам.

================================================================================
```
