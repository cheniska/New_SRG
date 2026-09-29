# Микромодули и встраиваемые артефакты — дизайн-документ

Черновой диздок системы встраивания предметов в оборудование.
«Встраиваемость» — **флаг** `IsEmbeddable=true`, который выставляется у
предметов двух видов:

- **микромодулей** — это **item**, а не оборудование
  (`IsEquipment=false`, `IsGoods=false`, `IsEmbeddable=true`), живёт в
  списке предметов корабля наравне с находками/квестовыми штуками, не
  занимает слот оборудования;
- **части артефактов** — они по-прежнему оборудование
  (`IsEquipment=true`, категория `Artefacts`, ставятся в слот
  артефактов), но у некоторых из них дополнительно `IsEmbeddable=true`,
  чтобы можно было «утопить» артефакт в другое оборудование вместо
  использования обычного слота (Пропорционар, Армс, Артефактор,
  Проглот и т.п.).

Основано на классических правилах SR2HD (см. `docs/микромодули.txt`),
адаптировано под нашу кодовую базу и правки после ревью.

Что портируем из исходного:
- концепцию встраиваемого предмета,
- градацию редкости через `Priority`,
- бонусы к статам, `Cost / Size / Durability` (у нас — прямой множитель, не
  обратный, см. §5.3),
- фильтр по типу оборудования-носителя,
- список специальных эффектов оружия.

Что НЕ портируем:
- SR2-модовый API (`FindBonusByNameInCfg`, `HullSpecial`,
  `SwitchToMirrorImage` и т.п.);
- акрины и спецакрины (у нас это `HullType`+«серии корпусов»);
- параметры под моды SR2 (EvoSB / ExpTC / ExpScienceRanks / ExpAutoSearch /
  ExpRepair / ExpBlackMarket / ShuDomiks);
- SR2-хардкод в код: у нас все стороны / расы / эффекты — из конфига.

---

## 1. Модель данных

### 1.1 Встраиваемый предмет vs предмет-носитель

- **Носитель** — существующий `ItemInstance` оборудования
  (`IsEquipment=true`), любой категории (Engine, Weapons, Hull и т.д.).
  У него в конфиге есть флаг `AllowEmbeds` (bool; переименовать из
  существующего `AllowModules`), плюс новое поле `MaxEmbeds` — сколько
  встраиваемых он может принять.
- **Встраиваемый предмет** — `ItemInstance` с флагом
  `IsEmbeddable=true` и заполненным блоком `Embed` (§2).
  - Микромодуль: **не оборудование**. `IsEquipment=false`,
    `IsGoods=false`. В инвентаре лежит рядом с находками/квестовыми
    предметами.
  - Встраиваемый артефакт: **оборудование** (`IsEquipment=true`,
    категория `Artefacts`). Можно ставить как обычно в слот артефактов
    ИЛИ встраивать в другой предмет — выбор пользователя.

Никакой отдельной категории «Embed» не заводим — встраиваемость это
характеристика, а не категория. Категорию `Category` для микромодуля
проставляем строкой `"MicroModule"` (см. §7.2) — она нужна только для
UI-фильтров инвентаря и не входит в `EquipmentCategory.DisplayOrder`.

### 1.2 Множественная установка

- В `ItemInstance` у нас уже есть `Modules : List<string>`. Переименовываем
  в `Embeds : List<string>` (список `Uid` встроенных `ItemInstance`).
  Порядок сохраняем — от него зависит порядок применения бонусов.
- Число одновременно встроенных предметов ограничено полем
  `ItemInstance.MaxEmbeds` (задаётся шаблоном/`Defaults`, по умолчанию 1
  у оборудования и 0 у всего остального). Для корпуса можно поднимать
  до 3; артефакт-«хаб» может дать +N слотов через `SlotBonuses.Embeds` (§6).
- Встраиваемый предмет `A` может быть несовместим с уже установленным
  `B` — см. §4.3.

### 1.3 Хранение в сейве

- `ItemInstance.Embeds : List<string>` хранит `Uid` встроенных инстансов.
- Сами встроенные инстансы **живут внутри** контейнера-носителя: в
  `ItemInstance.EmbedItems : Dictionary<string, ItemInstance>` (уид →
  инстанс). Не путаем с общим инвентарём — эти инстансы недоступны для
  экипировки/торговли.
- Модификации, которые встроенные предметы наложили на статы носителя,
  хранятся в `ItemInstance.Params` носителя как обычные значения. При
  извлечении встроенного предмета (см. §3) применённые бонусы
  откатываются — для этого держим оригинал в `ItemInstance.BaseParams`
  (см. §6.4).

---

## 2. Конфиг встраиваемого предмета

### 2.a Микромодули — свой конфиг

Микромодули **не** живут в `EquipmentConfig`. У них собственная секция в
`Config/Items.json` (либо отдельный `Config/MicroModules.json`,
подключаемый рядом с `ItemsConfig`):

```jsonc
// Config/MicroModules.json
{
  "MicroModules": {
    "MM_Apper": {
      "Name": "Аппер",
      "Tier": "T3",                    // ключ уровня редкости (см. §4.4)
      "Priority": 80,                   // 0..100
      "Description": "Наращивает мощность двигателя и радара...",
      "Icon": "MicroModules/apper",     // инвентарная иконка
      "BasePrice": 800,                 // цена в кредитах (RC/магазин)
      "Weight": 1,                      // место в трюме (пока лежит вне оборудования)
      "Embed": {
        "Compat": {
          "CarrierCategories": ["Engine", "Radar"],
          "CarrierRaces":      ["*"],
          "CarrierSides":      ["*"],
          "CarrierTechLevelMin": 1,
          "CarrierTechLevelMax": 10,
          "CarrierSizeMin":     0,
          "CarrierSizeMax":     0,
          "Tags":               ["Signal"],
          "ConflictsWith":      ["Signal"],
          "Requires":           []
        },
        "Removable": 0,                  // 0 = нельзя извлечь никогда, 1 = можно
        "CarrierMods": {
          "PriceMul":      1.0,
          "SizeMul":       1.0,
          "DurabilityMul": 1.0
        },
        "Bonuses": {
          "Engine.Speed":  20,
          "Radar.Range":   400
        }
      }
    }
  }
}
```

При создании `ItemInstance` из `MicroModuleConfig` фабрика проставляет:
`Category="MicroModule"`, `IsEquipment=false`, `IsGoods=false`,
`IsEmbeddable=true`, `Embed=<блок из конфига>`.

### 2.b Встраиваемые артефакты — тот же блок `Embed` в оборудовании

Артефакт живёт в `EquipmentConfig.Equipment.Artefacts.*` как оборудование
(это уже так). Чтобы сделать его встраиваемым — просто добавить
`IsEmbeddable=true` и блок `Embed` в JSON записи артефакта:

```jsonc
// Config/Equipment.json → Equipment.Artefacts.AF_Proportionar
{
  "IsEquipment": true,
  "IsEmbeddable": true,
  "Name": "Пропорционар",
  "Embed": { ... тот же формат, что у ММ ... }
}
```

Ключевые особенности формата:

- **Никаких зашитых списков рас/сторон/категорий.** Валидные значения
  берутся из `GalaxyConfig.Races`, `EquipmentConfig.Clusters` и
  `EquipmentCategory.DisplayOrder` соответственно (§8.1).
- **Никакого «пустой список = любая».** Пустой `CarrierRaces` = «ни одна»,
  что делает предмет неустановимым (безопасное значение по умолчанию).
  Для «универсального» ММ явно перечисляем все расы или используем
  специальный ключ `"*"` (§4.2).
- **Bonuses** ключуются как `<Category>.<ParamKey>` (§6.1). При установке
  бонус применяется только если носитель принадлежит указанной категории —
  ММ с несколькими категориями автоматически применяет корректный
  сегмент к нужному носителю.
- **`Tier` — только у встраиваемых.** У обычного оборудования есть
  `TechLevel` (техноуровень 1..10). Оба поля не путаются: `Tier`
  описывает редкость самого ММ (T1/T2/T3), `TechLevel` — техуровень
  предмета-оборудования. См. §7.0 про удаление слова «Tier» из
  оборудования.

### 2.1 C# модели

```csharp
namespace SRG.Equipment
{
    // Отдельного enum нет: «микромодуль» и «артефакт» различаются категорией
    // (EquipmentCategory.MicroModule vs Artefacts). Общий признак — флаг
    // IsEmbeddable + непустой EmbedConfig.

    public class EmbedConfig
    {
        public string Tier { get; set; }               // T1/T2/T3/... (редкость)
        public int Priority { get; set; }

        public EmbedCompat Compat { get; set; } = new();

        /// <summary>0 = извлекать нельзя никогда; 1 = можно (магазин/сервис/скрипт).</summary>
        public int Removable { get; set; } = 0;

        public EmbedCarrierMods CarrierMods { get; set; } = new();

        // Ключ: "<Category>.<ParamKey>"; значение — аддитивная дельта.
        public Dictionary<string, float> Bonuses { get; set; } = new();

        // Активатор слотов корпуса: "Weapons": +2.
        public Dictionary<string, int> SlotBonuses { get; set; } = new();

        // Добавляемые эффекты оружия (§6.3).
        public List<WeaponEffectSpec> AddedWeaponEffects { get; set; } = new();

        // Флаги оружия без числовых значений (§6.3).
        public List<string> WeaponFlags { get; set; } = new();
    }

    public class EmbedCompat
    {
        public List<string> CarrierCategories { get; set; } = new();
        public List<string> CarrierRaces { get; set; } = new();
        public List<string> CarrierSides { get; set; } = new();
        public int CarrierTechLevelMin { get; set; } = 0;
        public int CarrierTechLevelMax { get; set; } = 0;
        public int CarrierSizeMin { get; set; } = 0;
        public int CarrierSizeMax { get; set; } = 0;
        public List<string> Tags { get; set; } = new();
        public List<string> ConflictsWith { get; set; } = new();
        public List<string> Requires { get; set; } = new();
    }

    public class EmbedCarrierMods
    {
        public float PriceMul { get; set; } = 1f;
        public float SizeMul { get; set; } = 1f;
        public float DurabilityMul { get; set; } = 1f;
    }
}
```

На `ItemInstance`/`EquipmentItemConfig` появляется bool
`IsEmbeddable`, а сам `EmbedConfig` доступен через
`ItemInstance.Embed` (может быть null у обычных предметов).

---

## 3. Установка и извлечение

### 3.1 Установка

`EmbedService.CanInstall(carrier, embed) → InstallCheckResult`:
- носитель разрешает встраивание (`carrier.AllowEmbeds == true`);
- у носителя есть свободный слот (`carrier.Embeds.Count < carrier.MaxEmbeds`);
- проверка совместимости (§4) даёт ok;
- ни один уже установленный встраиваемый не конфликтует с новым.

`EmbedService.Install(carrier, embed)`:
- добавляет `embed.Uid` в `carrier.Embeds`;
- переносит инстанс в `carrier.EmbedItems`;
- применяет `CarrierMods` (§5.3) и `Bonuses` (§6.1);
- пересчитывает план слотов, если носитель — корпус (§6.2).

### 3.2 Извлечение

`EmbedService.Extract(carrier, embedUid)`:

| `Embed.Removable` | Что происходит |
|:---:|:---|
| `1` | Извлечение возможно (магазин / сервис / скриптовая команда). |
| `0` | Отказ. Всегда. Никаких «квестовых» исключений на уровне сервиса — если по сюжету нужно вынуть, квест **уничтожает** носителя и выдаёт новый (или создаёт копию встраиваемого через `EmbedService.Spawn`). |

При извлечении:
- инстанс встраиваемого возвращается в инвентарь корабля;
- `Bonuses` откатываются (сравниваем `Params` с `BaseParams` носителя, §6.4);
- `CarrierMods` откатываются пропорционально (Price/Size/Durability);
- если после извлечения носитель нарушает какое-либо правило (например,
  корпус потерял слот, а на нём уже стоит оборудование) — оборудование
  из «лишнего» слота вываливается в общий инвентарь. (Аналог логики при
  снятии серии корпуса.)

### 3.3 Уничтожение

Носитель уничтожен (потерян в бою) → все встроенные предметы уничтожаются
вместе с ним. Отдельного дропа нет.

---

## 4. Совместимость

Совместимость **разделена** на компат носителя (§4.1–4.2) и компат между
встраиваемыми (§4.3). Обе — валидируются на этапе `CanInstall`.

### 4.1 По категории носителя (`CarrierCategories`)

Список категорий из `EquipmentCategory` (или кастомных, добавленных
модом). Спец-значение `"*"` = «любая категория, у которой
`AllowEmbeds=true`». Пустой список = «ни одна», предмет неустановим (см.
§2).

### 4.2 По расе и стороне носителя (`CarrierRaces` / `CarrierSides`)

- Проверка идёт по `ItemInstance.ManufacturerRace` и `ManufacturerSide`.
- Значения `"*"` = «любое **явно перечисленное** в
  `GalaxyConfig.Races` / `EquipmentConfig.Clusters`». Никакого
  «универсального включения» через пустой список — пустой список
  запрещает всё.
- `None` (планируемая раса-«производитель отсутствует», будущее
  оборудование без завода-изготовителя) — обычное значение расы, работает
  как любая другая. Если ММ должен подходить к `None`-производителю,
  надо явно перечислить `"None"` в `CarrierRaces`.
- Никакого специального флага `NonDominators` не будет: доминаторы —
  такая же раса/сторона, добавляем/убираем из списка вручную.

### 4.3 Между встраиваемыми (`Tags`, `ConflictsWith`, `Requires`)

- Каждый встраиваемый может нести теги (`Compat.Tags`).
- `Compat.ConflictsWith` — ID (`MM_Berserk`) или теги (`Signal`). Установка
  запрещена, если у носителя уже есть встроенный с таким `Id` или тегом.
- `Compat.Requires` — то же самое, но наоборот: установка требует
  наличия у носителя встроенного с указанным `Id` или тегом.
- Порядок установки важен только для `Requires`: сначала ставим
  «требуемого», потом зависимого.

### 4.4 Уровни редкости (настраиваемые)

Уровни (`Tier`) задаются отдельным конфигом, а не хардкодятся:

```jsonc
// Config/Embeds.json (продолжение)
{
  "EmbedTiers": [
    { "Id": "T3", "DisplayName": "III",  "Color": "17,139,255",
      "PriorityMin": 70, "PriorityMax": 100 },
    { "Id": "T2", "DisplayName": "II",   "Color": "255,240,100",
      "PriorityMin": 31, "PriorityMax": 69  },
    { "Id": "T1", "DisplayName": "I",    "Color": "255,0,0",
      "PriorityMin": 0,  "PriorityMax": 30  },
    { "Id": "TU", "DisplayName": "Уникальный", "Color": "255,255,255",
      "PriorityMin": -1, "PriorityMax": -1, "ExcludeFromRandomDrop": true }
  ]
}
```

- Уровень предмета задаётся полем `Tier` в конфиге; количество и границы
  свободные.
- Диапазоны `Priority` используются формулой подбора по ГТУ (§9.1) и
  таблицами дропа. Игра сама ничего не «знает» про «III/II/I».

---

## 5. Модификаторы носителя (`CarrierMods`)

### 5.1 `PriceMul`

Прямой множитель `carrier.Price`. `1.5` = +50%, `0.7` = −30%.

### 5.2 `SizeMul`

Прямой множитель `carrier.Weight`. `1.2` = +20% размера, `0.6` = −40%.

### 5.3 `DurabilityMul` (прямой, не `Fragility`)

Прямой множитель `carrier.MaxDurability`. `1.5` = прочнее в 1.5 раза,
`0.7` = 70% исходной прочности. У корпуса влияет на `MaxHp`. Никаких
обратных 100/Fragility (SR2HD) — цифра означает то, что написано.

Отдельные модификаторы по типам урона (аналог SR2HD `FragilityEnergy`
и т.п.) выражаются через `Bonuses` (см. §6.1):

```jsonc
"Bonuses": {
  "Hull.Vulnerability.Energy": -0.15,     // -15% восприимчивости к энергии
  "Hull.Vulnerability.Kinetic": 0.10      // +10% к кинетике
}
```

### 5.4 Порядок применения

При нескольких встроенных предметах `CarrierMods` **перемножаются**, а
`Bonuses` **суммируются**. Порядок хранения в `Embeds` определяет
детерминированность.

---

## 6. Бонусы (`Bonuses`)

### 6.1 Формат ключей

Ключ = `<Category>.<ParamKey>` (или `<Category>.<Namespace>.<ParamKey>`),
значение — аддитивная дельта.

- `<Category>` совпадает с `EquipmentCategory` (`Engine`, `Radar`,
  `Weapons`, `Hull`…). Бонус применяется только если носитель
  принадлежит этой категории. Позволяет одному ММ иметь разный эффект в
  разных типах оборудования (аналог SR2HD «Аппера»).
- `<ParamKey>` — существующее поле в `ItemInstance.Params` (см. §6.2).

### 6.2 Существующие параметры (полная таблица)

| Категория | Ключ | Что это |
|:---|:---|:---|
| `Engine` | `Speed` | Скорость перемещения |
| `Engine` | `JumpRange` | Дальность гиперпрыжка |
| `FuelTank` | `Capacity` | Ёмкость бака |
| `Radar` | `Range` | Радиус радара |
| `Scanner` | `Power` | Мощность сканера |
| `Droid` | `Efficiency` | HP/ход дроида |
| `CargoGrabber` | `Power` | Мощность захвата |
| `CargoGrabber` | `Range` | Дальность захвата |
| `Shield` | `BlockPercent` | Процент блока щита |
| `Forsage` | `SpeedMul` | Множитель скорости при активации |
| `Hull` | `Armor` | Броня |
| `Hull` | `Vulnerability.<DamageType>` | Восприимчивость к типу урона |
| `Weapons` | `MinDmg` / `MaxDmg` | Разброс урона |
| `Weapons` | `Range` | Дальность стрельбы |
| `Weapons` | `ArmorPenetration` | Пробитие брони |
| `Weapons` | `ShieldPenetration` | Пробитие щита (в процентах) |
| `Weapons` | `EquipmentHitChance` | Шанс попасть по оборудованию |
| `Weapons` | `EquipmentDamage` | Урон по оборудованию |
| `Weapons` | `MaxAmmo` | Ёмкость боезапаса |

**Что убрано из SR2HD-списка:**
- `bonHookRadius` = наш `CargoGrabber.Range` (уже есть);
- `bonWEnergy/bonWSplinter/bonWMissile` = наш `Weapons.MaxDmg` + условие
  «тип урона X» (см. §4.1 через `Tags`, например
  `CarrierRequiredTags: ["DamageType:Energy"]`). У нас 3 типа урона
  (Kinetic/Explosive/Energy), не 3 категории оружия.

**Что добавлено к SR2HD-списку** (новых механик у нас, которых там не было):
- `Weapons.ArmorPenetration` — SR2HD не разделял пробитие;
- `Weapons.ShieldPenetration` — то же;
- `Weapons.EquipmentHitChance/Damage` — унифицированный SR2 «шанс
  повредить оборудование»;
- `Hull.Vulnerability.<DamageType>` — вместо трёх SR2HD-полей;
- `Forsage.SpeedMul` — форсаж у нас отдельная категория.

**Что оставлено на выброс** (не применимо к нашей боёвке):
- Приросты к «эффективности сканера в процентах от объёма трюма» и т.п.
  редких SR2-параметров, которых у нас нет — переносить их не нужно.

### 6.3 Эффекты оружия

Наши `CombatEffectType`, для каждого предусмотрен ММ (список **полный**,
покрывает весь enum):

| `CombatEffectType` | Пример ММ | Как работает |
|:---|:---|:---|
| `Slow` | «Тормоз» | Добавляет эффект замедления в `AddedWeaponEffects`. |
| `Shutdown` | «ЭМИ-контур» | ЭМИ на N ходов. |
| `ArmorDebuff` | «Кислота» | Снижает броню цели. |
| `Jamming` | «Помеха» | Дебафф радара/оружия/сканера. |
| `EngineDisable` | «Дезрайв» | Прямой урон двигателю. |
| `Drain` | «Вампир» | Вампиризм. |
| `BlockWeapon` | «Заглушка» | Блокирует оружие цели. |
| `BlockDroid` | «Диверсант» | Блокирует дроида цели. |
| `ExecuteBonus` | «Добиватель» | Бонус к урону по повреждённой цели. |
| `LootFocus` | «Мародёр» | Повышает шанс дропа. |

Формат в конфиге:

```jsonc
"AddedWeaponEffects": [
  { "Type": "Slow",        "Duration": 2, "Magnitude": 0.30, "Chance": 1.0 },
  { "Type": "ArmorDebuff", "Duration": 3, "Magnitude": 10,   "Chance": 0.5 }
]
```

Плюс `WeaponFlags` — булевы модификаторы без числовых значений:

| Флаг | Эффект |
|:---|:---|
| `NoDamageDelta` | `MinDmg` подтягивается к `MaxDmg` (SR2 `NoDelta`). |
| `IgnoreArmorAndShield` | Аналог SR2 `Undefendable`: применяется прошедший всё урон, обходя броню и щит. |
| `NonLethal` | Не может добить цель до 0 HP. |
| `AmmoFree` | Не тратит боезапас (для скриптовых редкостей). |

### 6.4 Пересчёт статов

При установке:
1. если это первый встраиваемый и `BaseParams` пусты — копируем
   `Params` → `BaseParams`;
2. считаем итог: `Params[k] = BaseParams[k] + Σ bonus[k]`;
3. `Price = round(BasePrice × Π PriceMul)`, аналогично `Weight` и
   `MaxDurability`.

При извлечении — просто пересобираем всё заново из `BaseParams` +
оставшихся встроенных.

---

## 7. Изменения в существующих сущностях

### 7.0 Отделить `Tier` от `TechLevel` (общий рефакторинг)

Прямо сейчас в `Assets/Scripts/Equipment/EquipmentTemplate.cs` слово
`Tier` используется как синоним техуровня:

- `Params["Tier"] = tier;` (строка 224)
- переменные подстановки `<Tier>` / `<TierNum>` в графике/именах;
- флаг кластера `GraphicIgnoresTier`;
- метод `TierFromKey`.

При этом в `ItemInstance.TechLevel` уже хранится корректное значение
(1..10). Переименования (одной пачкой перед началом работ над ММ):

| Было | Стало |
|:---|:---|
| `Params["Tier"]` | `Params["TechLevel"]` |
| `<Tier>` / `<TierNum>` в шаблонах | `<TechLevel>` / `<TechLevelNum>` |
| `graphicTierToken` | `graphicTechLevelToken` |
| `ClusterConfig.GraphicIgnoresTier` | `GraphicIgnoresTechLevel` |
| `EquipmentTemplate.TierFromKey` | `TechLevelFromKey` |

После этого термин `Tier` **свободен** и остаётся только у
встраиваемых (описывает редкость: T1/T2/T3, свободный список).

### 7.1 `ItemInstance` и `EquipmentItemConfig`

Добавить/переименовать:

| Было | Стало | Комментарий |
|:---|:---|:---|
| `Modules : List<string>` | `Embeds : List<string>` | Uid встроенных инстансов. |
| `AllowModules : bool` | `AllowEmbeds : bool` | флаг у **носителя**. |
| — | `IsEmbeddable : bool` | флаг у **встраиваемого** (ММ / артефакт). |
| — | `Embed : EmbedConfig` | параметры встраиваемости (null у обычных). |
| — | `MaxEmbeds : int` | по умолчанию 1 у оборудования, 0 у прочего. |
| — | `EmbedItems : Dictionary<string,ItemInstance>` | контейнер инстансов. |
| — | `BaseParams : Dictionary<string,float>` | оригинальные статы. |

Сейв-совместимость через `LegacyBinder`: `Modules → Embeds`,
`AllowModules → AllowEmbeds`.

### 7.2 Категории

- **`EquipmentCategory` не расширяем.** Микромодуль — не оборудование, в
  `DisplayOrder` его нет, шаблон в `EquipmentConfig.Templates` не заводим.
- В `ItemInstance.Category` у микромодуля стоит строка `"MicroModule"` —
  используется UI-фильтром инвентаря и логикой дропа. Совпадений с
  категориями оборудования нет; UI это отдельный раздел «Микромодули»
  внутри инвентарной вкладки предметов (аналогично «Находкам»).
- `MicroModuleConfig` загружается через `GalaxyConfig` рядом с
  `ItemsConfig` (в едином `Items.json` или отдельном
  `MicroModules.json`). Фабрика инстансов —
  `MicroModuleFactory.Create(id) → ItemInstance` — ставит
  `IsEquipment=false`, `IsEmbeddable=true`, `Category="MicroModule"`.
- У встраиваемых артефактов категория остаётся `Artefacts` (они всё ещё
  оборудование).

### 7.3 `EquipmentSystem.GetHullSlotPlan`

К результату шаблонного плана прибавляем `SlotBonuses` всех встроенных
предметов корпуса. Ключи `SlotBonuses` — те же, что в
`HullSlotsHullType` (`Weapons`, `Artefacts`, `Radar`, …).

### 7.4 `WeaponSystem`

- В `BuildShotParams` добавить сборку `AddedWeaponEffects` из встроенных
  предметов оружия.
- Обработать `WeaponFlags` (§6.3): `NoDamageDelta` — при сборе
  `WeaponShotParams`; `IgnoreArmorAndShield` — новая ветка в
  `ApplyDamage`; `NonLethal` — clamp Hp min 1; `AmmoFree` — не декрементим
  `Params["Ammo"]`.

### 7.5 `EquipmentTemplate`

- В `Defaults` добавить чтение `MaxEmbeds`, `AllowEmbeds` (сейчас читается
  `AllowModules`).

---

## 8. Расширяемость модами

### 8.1 Ссылки на категории/расы/стороны/эффекты

Все идентификаторы в конфиге ММ — **строки**, валидируемые против внешних
конфигов на этапе загрузки:

| Поле | Валидируется по |
|:---|:---|
| `CarrierCategories` | `EquipmentCategory.DisplayOrder` + `EquipmentConfig.Templates.Keys` (только категории носителей — микромодуль сам себя носителем не является) |
| `CarrierRaces` | `GalaxyConfig.Races.Keys` (плюс спец. `"*"`, `"None"`) |
| `CarrierSides` | `EquipmentConfig.Clusters.Keys` (плюс `"*"`) |
| `AddedWeaponEffects[i].Type` | `CombatEffectType` enum + мод-регистр |
| `WeaponFlags` | реестр `EmbedFlags.Registered` |
| `Bonuses` ключи | реестр `Params` конкретной категории |

Валидация выполняется в `GalaxyConfigValidator`. Неизвестное значение —
ошибка загрузки (не тихо игнорируем), с указанием файла и id ММ.

### 8.2 Расширение эффектов оружия из мода

Мод может ввести новый `CombatEffectType`:
1. добавить значение в `CombatEffectType` enum (Roslyn source-gen из
   мод-конфига), либо использовать строковой реестр эффектов;
2. добавить обработчик в `WeaponSystem.ApplyEffects` — регистрируется
   через `EffectRegistry.Register(id, handler)`;
3. в `Config/Embeds.json` спокойно ссылаться на новый `Type`.

Аналогично — для новых `WeaponFlags` (регистр в
`EmbedFlags.Register(id, applyFn)`).

### 8.3 Расширение категорий и рас

Категории оборудования уже задаются шаблонами (`EquipmentTemplate`) — мод
добавляет новый шаблон, автоматом получает валидное имя категории для
ссылок из `Embeds.json`. С расами то же (`GalaxyConfig.Races` — dict).

### 8.4 Правило «no hidden defaults»

Никаких «пустой список = все». Никаких «`Owner` без записи =
доминаторы». Всё явно. Это даёт модам предсказуемое поведение при
частичной переопределяющей конфигурации.

---

## 9. Источники получения

Правила разделены с правилами установки. Установка использует `Compat`
(§4), источники — отдельный блок `Sources`.

### 9.1 Дроп с NPC

```jsonc
"Sources": {
  "NpcDrop": {
    "SideChancePerHit": {
      "Pirates":    0.03,
      "Dominators": 0.06,
      "Coalition":  0.0
    },
    "RaceChancePerHit": {
      "PirateClan": 0.03,
      "RaceDominators1": 0.05
    },
    "GtlWindow":  [3, 10]           // ГТУ, при которых ММ дропается
  },
  ...
}
```

- `SideChancePerHit` и `RaceChancePerHit` **независимы** — суммарный шанс
  = min(1, side + race). Так решается требование «источники не только по
  расе, но и по side»: у ММ есть отдельные списки сторон и рас для дропа,
  не завязанные на его установочные ограничения.
- Отбор ММ для сессии дропа — правило Priority-окна:
  ```
  maxP = lerp(70, 0, clamp((gtl-3)/(7-3)))
  minP = maxP + 40
  ```
  (то же, что в SR2HD.) Только те ММ, у которых
  `PriorityMin ≤ mm.Priority ≤ PriorityMax`.

### 9.2 Магазины планет и станций

```jsonc
"Sources": {
  "Shop": {
    "RaceWhitelist": ["People", "Fei"],
    "SideWhitelist": ["Coalition"],
    "MinPtu": 4
  }
}
```

- Планета: `RaceWhitelist` фильтрует по расе планеты, `MinPtu` — по её
  ПТУ.
- Станция: то же, но по `Standing` (`Coalition` / `PirateClan` / …).
- Пустой whitelist = «никогда не появится в магазине». Универсальная
  выкладка — явное `"*"`.

### 9.3 Центр рейнджеров

Обычная очередь для ММ с `Priority ≥ 10`. Для «редких» (`Priority < 10`)
— отдельный слот в цепочке выдачи, чтобы они не проигрывали слоту по
обычному пулу.

### 9.4 Квесты

`EmbedService.Spawn(id)` — прямой спавн из скрипта, минуя правила
§9.1–9.3. Единственный способ получить `Tier=TU` (уникальные) и любые
встраиваемые с `ExcludeFromRandomDrop=true` (см. §4.4).

---

## 10. UI

- **Инвентарный вид**: встраиваемый предмет — обычный стек с иконкой,
  имя в цвете `Tier.Color`. Всплывающая карточка показывает `Compat`
  (совместимые категории/расы), бонусы и модификаторы.
- **Установка**: drag-and-drop на предмет-носителя в инвентаре или
  экипировке. Показ модального окна (стиль `AmountSliderDialog`, но
  без слайдера) с диффом стат. При наличии конфликта — красная строка с
  причиной отказа.
- **Извлечение**: в обычной ситуации кнопка неактивна с тултипом «Нельзя
  извлечь». В квестовом сценарии (`QuestForce`) появляется в интерфейсе
  сервиса на планете/спецсцене.
- **Карточка носителя** отображает список встроенных предметов
  (упорядоченный, как в `Embeds`).

---

## 11. Порядок реализации

1. **Рефакторинг Tier → TechLevel в оборудовании** (§7.0). Отдельным
   коммитом, до любых работ над ММ.
2. **Модели.** Добавить `EmbedConfig`, `EmbedCompat`, `EmbedCarrierMods`.
   Расширить `ItemInstance`/`EquipmentItemConfig`: `IsEmbeddable`,
   `Embed`, `AllowEmbeds`, `MaxEmbeds`, `EmbedItems`, `BaseParams`,
   переименование `Modules → Embeds`. `LegacyNamespaceBinder` для старых
   сейвов.
3. **Конфиг ММ и фабрика.** `MicroModuleConfig` в
   `Config/MicroModules.json`, загрузка через `GalaxyConfig`.
   `MicroModuleFactory.Create(id) → ItemInstance` (не оборудование).
   Артефактам-встраиваемым — просто расширить существующие записи в
   `EquipmentConfig.Equipment.Artefacts.*` полями `IsEmbeddable` и
   `Embed`.
4. **`EmbedService`.** `CanInstall / Install / Extract`, пересчёт статов,
   `Spawn`.
5. **Валидатор конфига.** Проверка ссылок на расы/стороны/категории/
   эффекты/флаги (§8.1). Ошибки — при загрузке, не в рантайме.
6. **Импортёр из `docs/микромодули.txt`.** Оффлайн-скрипт
   (`Assets/Editor/EmbedImporter.cs`), однократно генерит стартовый пул
   встраиваемых.
7. **Слоты корпуса.** Правки `EquipmentSystem.GetHullSlotPlan` — учёт
   `SlotBonuses` встроенных.
8. **Оружие.** `WeaponFlags` и `AddedWeaponEffects` в `WeaponSystem`.
9. **UI.** Драг-энд-дроп, окно установки, отображение в карточке.
10. **Источники.** Расширение `NpcOutfitter` (дроп), `EquipmentShopSystem`
    (планета/станция), `RangerCenter` (§9.3).
11. **Артефакты-встраиваемые.** Перевести «Пропорционар», «Артефактор» и
    другие SR2HD «встраиваемые артефакты» на `IsEmbeddable=true`
    (категория остаётся `Artefacts`). Существующая система активных
    артефактов остаётся для «обычных» артефактов.

---

## 12. Открытые вопросы

- **Ремонт носителя с встроенным.** SR2HD блокировал улучшение (`BlockImp`)
  и извлечение через сервис при некоторых бонусах. Сейчас у нас нет
  «улучшения» отдельно от ремонта — можно не переносить.
- **Автоматическая генерация ММ при создании NPC.** Нужен ли лимит «не
  более одного встроенного на предмет для NPC» ради визуальной чистоты
  боя? Пока — да.
- **Реорганизация артефактов.** Какие именно артефакты станут
  встраиваемыми, а какие останутся «активными»? Требует отдельного
  ревью списка `docs/артефакты.txt`.
