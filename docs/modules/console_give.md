# Консольная команда `give`

Универсальная выдача любого игрового предмета игроку. Единая точка входа: `ItemGrantService.Give`. Автоопределяет тип (стек/экземпляр) и раскладывает в трюм или слот.

Открыть консоль — <code>`</code> (backtick).

## Синтаксис

```
give <id | Category:Id> [amount] [inv | slot | <slotKey>]
```

| Аргумент | Значение |
|---|---|
| `id` | id предмета. Автопоиск: `MicroModule` → `Good` → `Mineral` → `Equipment` (перебор рукописных записей). |
| `Category:Id` | Форсированное указание категории (см. таблицу ниже). Обязательно для шаблонного оборудования (Hull/Engine/…), потому что перебор шаблонов бесконечен. |
| `amount` | Для стеков (Goods/Minerals) — размер стека. Для экземпляров (Equipment/MicroModule) — сколько отдельных предметов создать. По умолчанию `1`. |
| `inv` | Форсировать трюм (для оборудования). |
| `slot` | Автоподбор слота (по умолчанию для оборудования). |
| `<slotKey>` | Конкретный слот, напр. `Weapons_2`, `Engine_0`, `Artefacts_3`. |

**Особый случай — Hull.** Смена корпуса на живом корабле пересобирает весь SlotPlan, поэтому Hull всегда кладётся в трюм. Наденьте через UI (форма корабля), где вся ре-инициализация слотов отработает штатно.

## Псевдо-категории (для формы `Category:Id`)

| Категория | Что даёт | Куда попадает |
|---|---|---|
| `Goods` (или `Good`) | Товар (Food/Alcohol/…) | Стек в трюме |
| `Mineral` | Минерал/нейроядер (CommonMineral/Nod/…) | Стек в трюме |
| `MicroModule` | Микромодуль (MM_T1_Apper/…) | Отдельный предмет в трюме |
| `Hull` / `Engine` / `Weapons` / `Artefacts` / `Shield` / `Radar` / `Scanner` / `FuelTank` / `Forsage` / `Droid` / `CargoGrabber` / `BoardingHook` / `TowingRig` | Оборудование | Слот (авто/явный) или трюм |

---

## Примеры

```
give W_Laser                     # автопоиск: находит Weapons/W_Laser → в свободный слот
give Weapons:W_Laser 3           # три отдельных лазера
give Alcohol 50                  # стек из 50 бутылок в трюм
give Nod 200                     # стек из 200 нейроядер (не считается «товаром»)
give CommonMineral 10 inv        # минералы, форсированно в трюм
give MM_T3_Fortress              # микромодуль (не оборудование, всегда в трюм)
give Engine:E_Coalition_T5       # двигатель Coalition пятого тира
give Hull:Hull_Coalition_Race1_R_T3   # корпус → трюм (наденьте через UI)
give AF_SpeedBoost slot Artefacts_2  # артефакт в конкретный слот
```

---

## Каталог id

### Товары (Goods)
Категория стека `Goods`, `IsGoods=true`. `amount` = размер стека. Источник — `Assets/Config/ItemsConfig.json`.

| id | Имя | BasePrice | Легально |
|---|---|---:|:---:|
| `Food` | Еда | 5 | ✓ |
| `Medicine` | Медикаменты | 40 | ✓ |
| `Alcohol` | Алкоголь | 15 | ✓ |
| `Minerals` | Минералы (товар) | 12 | ✓ |
| `Tech` | Техника | 80 | ✓ |
| `Weapons` | Оружие | 150 | ✓ |
| `Narcotics` | Наркотики | 250 | ✗ (контрабанда) |
| `Luxury` | Роскошь | 300 | ✓ |

> Внимание: `Weapons` и `Minerals` в этой таблице — торговые **товары**, стеки. Оборудование `Weapons` и минералы `Minerals` — отдельные вещи (см. ниже). При коллизии выигрывает первое совпадение в порядке автопоиска (Goods раньше Equipment), поэтому для оружия-как-оборудования пишите `Weapons:W_Laser`.

### Минералы (Minerals)
Категория стека `Mineral`. Нейроядра (`Nod`) — стакабельные, но `IsGoods=false` (не торговый товар).

| id | Имя | BasePrice | DropRange |
|---|---|---:|---|
| `CommonMineral` | Обычный минерал | 10 | 1..50 |
| `PreciousMineral` | Драгоценный минерал | 150 | 1..20 |
| `Nod` | Нейроядра | 20 | 10..300 |

### Микромодули (MicroModules)
Категория `MicroModule`. Всегда попадают в трюм — не оборудование, ставятся в носителя через встраивание (UI).

| id | Название | Tier | Носитель | BasePrice |
|---|---|:---:|---|---:|
| `MM_T1_Apper` | Аппер I | T1 | Engine / Radar | 200 |
| `MM_T1_Torpedo` | Тормоз I | T1 | Weapons | 250 |
| `MM_T1_Armor` | Скорлупа I | T1 | Hull | 300 |
| `MM_T2_Apper` | Аппер II | T2 | Engine / Radar | 800 |
| `MM_T2_Sharpshot` | Прицел II | T2 | Weapons | 900 |
| `MM_T2_Slotter` | Расширитель II | T2 | Hull | 1200 |
| `MM_T3_Apper` | Аппер III | T3 | Engine / Radar | 2500 |
| `MM_T3_Piercer` | Пробойник III | T3 | Weapons | 3200 |
| `MM_T3_Fortress` | Крепость III | T3 | Hull | 3500 |

---

### Оружие (Weapons) — рукописное
Категория `Weapons`. Отдельные экземпляры, встают в слот `Weapons_N`. Ставятся с расой производителя = раса корабля.

| id | Название |
|---|---|
| `W_Laser` | Лазер |
| `W_Cannon` | Пушка |
| `W_Rocket` | Ракетомёт |
| `W_ChainGun` | Пулемёт |
| `W_PlasmaCannon` | Плазменная пушка |
| `W_CorrosionGun` | Коррозионное орудие |
| `W_EMIPulse` | ЭМИ-импульс |
| `W_VampireLaser` | Лазер-вампир |
| `W_Torpedo` | Торпеда |
| `W_PiercingRailgun` | Пробивной рельсотрон |
| `W_Shotgun` | Дробовик |
| `W_ChainBolt` | Цепной болт |
| `W_GravBomb` | Гравибомба |

### Артефакты (Artefacts) — рукописные
Категория `Artefacts`. Слот `Artefacts_N`.

| id | Название |
|---|---|
| `AF_SpeedBoost` | Ускоритель |
| `AF_HullRepair` | Ремонтник корпуса |
| `AF_LootFocus` | Фокус на добыче |
| `AF_ExecuteChip` | Чип-исполнитель |

---

## Шаблонное оборудование

Эти категории генерируются шаблоном: id формируется из `<Side>_<Line>` (для большинства) или `<Side>_<Race>_<HullType>_<Line>` (только Hull). Всегда указывайте `Category:Id`, автопоиск шаблоны не индексирует.

Линейки `Line` = `T1..T10`.
Стороны `Side`: `Coalition`, `Dominators`.

### Простые (Side + Line)

| Категория | Prefix | Пример id | Описание |
|---|---|---|---|
| `Engine` | `E_` | `E_Coalition_T5` | Двигатель |
| `FuelTank` | `FT_` | `FT_Dominators_T3` | Топливный бак |
| `Forsage` | `AB_` | `AB_Coalition_T4` | Форсажная камера |
| `Shield` | `SH_` | `SH_Coalition_T7` | Щит |
| `Radar` | `R_` | `R_Coalition_T6` | Радар |
| `Scanner` | `SC_` | `SC_Coalition_T5` | Сканер |
| `Droid` | `DR_` | `DR_Coalition_T4` | Ремонтный дроид |
| `CargoGrabber` | `CG_` | `CG_Coalition_T3` | Грузовой захват |
| `BoardingHook` | `BH_` | `BH_Coalition_T3` | Абордажный крюк (совместим со слотом CargoGrabber) |
| `TowingRig` | `TR_` | `TR_Coalition_T4` | Буксир (слоты CargoGrabber или Weapons) |

**Формат:** `<Prefix><Side>_<T1..T10>` (без пробелов, регистр важен).
Примеры валидных id: `E_Coalition_T1`, `E_Coalition_T10`, `SH_Dominators_T5`, `TR_Coalition_T10`.

### Корпуса — `Hull`

Формат: `Hull_<Side>_<Race>_<HullType>_<Line>`.

**Sides и их допустимые расы (`SideRaces`):**

| Side | Races |
|---|---|
| `Coalition` | `Race1`, `Race2`, `Race3`, `Race4`, `Race5` |
| `Dominators` | `RaceDominators1`, `RaceDominators2`, `RaceDominators3` |

**HullTypes по стороне (`SideHullTypes`):**

| Side | HullTypes |
|---|---|
| `Coalition` | `R` (вольный пилот), `T` (транспорт), `W` (военный), `PC` (клановый пират), `BW` (линкор), `D` (дипломат), `P` (пират), `L` (лайнер) |
| `Dominators` | `Dom1` (разведчик), `Dom2` (перехватчик), `Dom3` (штурмовик), `Dom4` (крейсер), `Dom5` (линкор), `Dom6` (разрушитель), `Dom7` (владыка) |

**Станции** (`HullType`; ставятся на корабль-станцию, но давать игроку смысла обычно нет):
`BK`, `CB`, `MC`, `PB`, `RC`, `RG`, `WB`, `SB`, `Blazer`, `Keller`, `Terron`.

**Примеры валидных id:**
```
Hull:Hull_Coalition_Race1_R_T1
Hull:Hull_Coalition_Race3_W_T5
Hull:Hull_Coalition_Race5_BW_T10
Hull:Hull_Dominators_RaceDominators1_Dom3_T4
Hull:Hull_Dominators_RaceDominators3_Dom7_T10
```

Все Hull-выдачи форсированно уходят в трюм — надевать через форму корабля.

---

## Полный список сгенерированных id (справочно)

Комбинаторно: `Engine`/`FuelTank`/`Forsage`/`Shield`/`Radar`/`Scanner`/`Droid`/`CargoGrabber`/`BoardingHook`/`TowingRig` × 2 стороны × 10 тиров = **20 id на категорию**.

`Hull` × (5 рас Coalition × 8 HullType + 3 расы Dominators × 7 HullType + 11 станций × любая раса, обычно связка со стороной) — многие сотни id; используйте по формуле.

---

## Родственные команды

- `save` / `load` — сохранение/загрузка
- `givedrone [race]` — упакованный дрон-компаньон в инвентарь (`race` = Race1..Race5)
- `spawndrone [race]` — развернуть дрон рядом
- `help` — список всех команд консоли
