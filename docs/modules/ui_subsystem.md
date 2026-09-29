# UI Subsystem

Модули: `UI/*.cs` (15 файлов) + `Dialog/*.cs` (5 файлов) + утилиты `UI/UIBuilder.cs`, `UI/UIColorPalette.cs`.

Полный список UI: `GalaxyMapController`, `ShipFormView`, `PlanetUIController`, `ObjectInfoPopup`,
`MenuController`, `GameConsoleController`, `HUDController`, `PilotPanelController`,
`DeathScreenController`, `ShipScanUIController`, `InventoryUIController`, `LoadingScreenController`,
`HudMessageController`, `InventoryItemIcon`, `ClickableInfo`.

Dialog: `DialogUIController`, `DialogService`, `DialogActions`, `DialogConfigModels`, `DialogCursor`.

---

## Назначение

Все экраны игрока: галакарта, форма корабля (инвентарь/слоты/оборудование), экран планеты
(торговля/правительство/ангар), HUD во время полёта, всплывающее окно при наведении на объект,
диалог между кораблями и с планетами, игровая консоль (отладка), экран смерти, загрузочный
экран при гиперпереходе.

Сервисный слой (`InventoryService`/`ShopService`) был введён на этапе C — UI больше не пишет в
коллекции напрямую. Утилиты `UIBuilder`/`UIColorPalette` (этап A9) централизуют построение
GameObject'ов и цветовую палитру; `PlanetUIController` мигрирован на них в этапе B5 через
wrapper-делегаты.

---

## Публичный интерфейс

### `GalaxyMapController` (singleton)

```csharp
public void Show();                          // открыть галакарту
public void Hide();
public void SetZoom(float zoom);
public void CenterOn(StarData star);
public void RefreshVoronoi();                // пересчитать диаграмму владений
public void RefreshSectorLabels();           // ярлыки секторов
```

Внутри: зум-логика (`HandleZoom`), построение Вороной (`RebuildVoronoi`), ярлыки секторов
(`SpawnSectorLabels`), тултипы при наведении, отрисовка линий гиперпрыжка.

### `ShipFormView` (компонент, используется PlanetUIController и ShipScanUIController)

```csharp
public bool AllowInteraction;
public ShipData BoardingLootRecipient;       // если задан — UI работает в режиме «грабёж абордажа»
public System.Action OnEquipmentChanged;

public void SetShip(ShipData ship);
public void Refresh();
public void Hide();
public static string BuildItemDescription(ItemInstance item);  // для попапа
```

После этапа C мутации идут через `InventoryService` (см. модуль). Координаты слотов (`FixedSlotLayout`,
`WeaponPositions`, `ArtefactPositions`, `CargoFirstCell`, `ActivationSlotPos`, `ThrowSlotPos`,
`PilotPanelPos`) — в коде (B4 миграция в JSON отложена).

### `PlanetUIController` (singleton)

```csharp
public void Open(PlanetData planet);
public void Close();
public void ShowScreen(string screenId);     // SCR_OVERVIEW / GOVERNMENT / HANGAR / GOODS / EQUIP / INFO
```

После этапа B5 — использует `UIColorPalette` и `UIBuilder` через wrapper-делегаты (локальные
`ColBg/ColPanel/…` и `MakeText/MakeButton/MakePanel/SetAnchors/ClearChildren` — тонкие враппинги).

Магазин — через `ShopService.TryBuy/TrySell` (этап C4).

### `HUDController` (singleton)

Постоянный игровой HUD: HP корабля, топливо, текущий приказ, индикаторы пауз, мини-карта.
Подписан на `OnTurnCalculate/OnTurnComplete/OnPlayerDeath*`.

### `GameConsoleController` (singleton)

```csharp
public static bool IsOpen;
public static event Action OnTurnPerformed;
public static void AddEntry(string msg);     // вызывается ОТКВАСЮДУ — игровая лента событий
```

Отладочная консоль (по умолч. F-клавиша) — лог событий + ввод команд.

### `ObjectInfoPopup` (singleton)

Всплывающее окно при наведении на объект (планета/корабль/астероид/контейнер).
Задержка появления — `GameSettingsConfig.InfoPopupHoverDelay`. Опционально следует за курсором
(`InfoPopupFollowMouse`).

### `DialogService` (static + события)

```csharp
public static event Action<DialogContext> OnDialogStarted;
public static event Action<DialogContext> OnDialogNodeChanged;
public static event Action<DialogContext> OnDialogEnded;

public static void OpenSpaceDialog(ShipData self, ShipData other);
public static void OpenPlanetDialog(ShipData self, PlanetData planet);
public static void SelectChoice(int choiceIndex);
public static void EndDialog();
```

### `DialogUIController` (singleton)

```csharp
public void OpenSpaceDialog(ShipData self, ShipData other);     // делегирует в DialogService + UI
public void OpenPlanetDialog(ShipData self, PlanetData planet);
public void Close();
```

### `DialogActions` (static)

Обработчики действий из конфига диалога: `TransferMoney`, `TransferItem`, `ChangeRelations`,
`ApplyEffect`, `OpenShop`, `LaunchPolice` и т.п.

### `MenuController` (singleton)

Главное меню: Новая игра / Загрузка / Настройки / Выход.

### `DeathScreenController` (singleton)

Подписан на `PlayerManager.OnPlayerDeathConfirmed`. Показывает экран с причиной смерти и убийцей.

### Утилиты — `UIBuilder` (static, этап A9)

```csharp
public static GameObject MakePanel(Transform parent, string name, Color color);
public static Text MakeText(Transform parent, string name, string text, Font font,
                            int size, FontStyle style, Color color,
                            TextAnchor align = TextAnchor.MiddleLeft, bool richText = true);
public static Button MakeButton(Transform parent, string name, string label, Font font,
                                int fontSize, Color bgColor);
public static void SetAnchors(GameObject go, float xMin, float yMin, float xMax, float yMax);
public static void ClearChildren(Transform t);
```

### Утилиты — `UIColorPalette` (static, этап A9)

```csharp
public static Color Bg { get; }          // 0.05, 0.07, 0.12
public static Color Panel { get; }       // 0.08, 0.10, 0.16
public static Color Nav { get; }
public static Color Button { get; }
public static Color ButtonSel { get; }
public static Color ButtonAlt { get; }
public static Color Accent { get; }
public static Color Text { get; }
public static Color Green { get; }
public static Color Red { get; }
```

---

## Зависимости

```
ShipFormView
   ├─► InventoryService (этап C3 — все мутации)
   ├─► EquipmentSystem.GetEquipped / SlotCategory / ApplySeriesToHull / GetHullSlotPlan
   ├─► ContainerFactory.SpawnContainerWithItem / SpawnContainerWithStack
   ├─► ShipFactory.RecalculateSpriteWorldSize (после PlaceHull)
   ├─► SystemViewManager.EnsureShipVisual
   └─► GameConsoleController.AddEntry

PlanetUIController
   ├─► ShopService (этап C4 — Buy/Sell)
   ├─► TradeSystem (IsLegal, GetDisplayName)
   ├─► EquipmentShopSystem (RefreshEquipShop)
   ├─► UIBuilder / UIColorPalette (этап B5 — через wrapper-делегаты)
   ├─► DialogUIController.OpenPlanetDialog
   └─► PlayerManager.OnPlanetLanded/Left (события для авто-открытия/закрытия)

GalaxyMapController
   ├─► GalaxyManager.JumpToStar / TeleportToStar / CalcDistanceParsecs
   ├─► OwnerRaceRelationsManager (для Вороной-цветов)
   ├─► VoronoiHelper
   └─► CameraController

DialogUIController
   ├─► DialogService
   ├─► DialogActions (для применения эффектов выбранной ветки)
   └─► GameSettingsConfig (DialogModeKey)

HUDController
   ├─► PlayerShip.Instance
   ├─► GalaxyManager.OnTurnCalculate/OnTurnComplete
   └─► PlayerManager.OnPlayerDeath*
```

---

## Алгоритмы и формулы

### `ShipFormView.TryPlaceToSlot` (после рефактора C3)

```
если targetSlot.IsCargo → PlaceToCargo(handItem, targetSlot); return
если targetSlot.SlotKey == Throw → ThrowHandIntoSpace; return
если targetSlot.SlotKey == Activate → ActivateHandItem; return

# Совместимость категории
если !ship.Equipment.Slots.ContainsKey(targetSlot.SlotKey) → return
targetCategory = EquipmentSystem.SlotCategory(targetSlot.SlotKey)
если !handItem.CanFitInSlotCategory(targetCategory) → return

# Установка через InventoryService
если handItem.Category == Hull:
    displaced = EquipmentSystem.GetEquipped(ship, targetSlot.SlotKey)
    PlaceHull(handItem)     # внутри вызывает Equipment.RebuildForHullSlots
иначе:
    displaced = InventoryService.EquipToSlot(ship, targetSlot.SlotKey, handItem)

# Куда деть вытесненный
если displaced != null и displaced.Uid != handItem.Uid → SetHand(displaced, targetSlot)
иначе → ClearHand()
```

### `PlanetUIController.BuyGood` (после рефактора C4)

```
ship = PlayerShip.Instance?.ShipData
entry = ShopService.GetEntry(_planet, goodId)
если ship == null или entry == null → return

# Контрабанда — UI делает предупреждение, ShopService не отказывает
если !TradeSystem.IsLegal(_planet, goodId, cfg):
    penalty = cfg.Trade?.ContrabandRelationPenalty ?? -20
    GameConsole.AddEntry("[Магазин] X — контрабанда. Отношения: penalty")

# Сама покупка
если !ShopService.TryBuy(ship, _planet, goodId, amount, cfg, out actualAmount, out totalCost):
    # Различаем «нет товара» vs «нет денег»
    desired = min(amount, entry.Stock)
    если desired <= 0 → return
    если ship.Money < desired * entry.BuyPrice:
        GameConsole.AddEntry("[Магазин] Недостаточно кредитов.")
    return

GameConsole.AddEntry("[Магазин] Куплено: X x amount за cost кр.")
UpdateMoneyDisplay(); RefreshGoodsShop(); RefreshHangar()
```

### `GalaxyMapController.HandleZoom`

```
prevZoom = _zoom
zoom += scrollDelta * ZOOM_SPEED
zoom = clamp(zoom, ZOOM_MIN, ZOOM_MAX)
# Корректировка позиции, чтобы курсор оставался над той же точкой мира
worldUnderCursor = ScreenToWorld(mousePos, prevZoom)
worldUnderCursorAfter = ScreenToWorld(mousePos, zoom)
position += worldUnderCursorAfter - worldUnderCursor
```

### `GalaxyMapController.RebuildVoronoi`

```
# 1. Собрать центры — звёзды, окрашенные владельцем
foreach star in galaxy.StarsMap:
    owner = OwnerResolver.Resolve(star, ctx)
    if owner not in (None, Mixed): добавить (star.Position, owner)

# 2. VoronoiHelper.Compute(points, bounds) → диаграмма
cells = VoronoiHelper.Compute(centers, mapBounds)

# 3. Закрасить каждую клетку цветом владельца (из cfg.Owners[id].Color)
foreach cell in cells:
    color = cfg.Owners[cell.OwnerId].Color
    drawPolygon(cell.Polygon, color * 0.4)   # полупрозрачный fill
    drawOutline(cell.Polygon, color)         # яркая граница

# 4. Дополнительно: линии экспансии (стрелки между метрополией и колониями)
DrawExpansionLinks(cfg.GalaxyExpansion)
```

### `GalaxyMapController.SpawnSectorLabels` (90 строк)

```
foreach sector in galaxy.Sectors:
    centroid = computeCentroid(sector.Stars)
    raceColor = cfg.Races[sector.DominantRace].Color (с альфа = доля доминирования)
    ownerName = formatOwnersList(sector.DominantOwners)
    
    label = new GameObject("SectorLabel_" + sector.Id)
    добавить TextMeshPro компонент
    text = "SectorName\n[Race]\n[Owner]"
    color = raceColor
    position = centroid
    fontSize = clamp(log(starsCount), MIN, MAX)
```

### `DialogUIController.Open*`

Делегирует в `DialogService.OpenSpaceDialog/OpenPlanetDialog`, который:
1. Создаёт `DialogContext` со ссылками на стороны, текущий node ID, переменные.
2. Парсит реплику текущего узла из `cfg.Dialogs[dialogId].Nodes[nodeId]`.
3. Вычисляет доступные варианты (фильтр по условиям: отношения / деньги / расы / типы кораблей).
4. Вызывает `OnDialogStarted/OnDialogNodeChanged`.
5. UI-контроллер подписан на эти события и обновляет окно.

При выборе варианта — `DialogService.SelectChoice(index)` → выполнить `DialogActions` →
перейти на следующий узел / закрыть.

---

## Константы и настройки

### Палитра (UIColorPalette — единая, после B5 миграции)

| Цвет | RGB |
|---|---|
| Bg | (0.05, 0.07, 0.12) |
| Panel | (0.08, 0.10, 0.16) |
| Nav | (0.06, 0.09, 0.14) |
| Button | (0.13, 0.22, 0.35) |
| ButtonSel | (0.18, 0.38, 0.60) |
| ButtonAlt | (0.28, 0.16, 0.12) |
| Accent | (0.55, 0.82, 1.00) |
| Text | (0.88, 0.88, 0.93) |
| Green | (0.30, 0.85, 0.45) |
| Red | (0.95, 0.35, 0.30) |

### `ShipFormView` (в коде, B4 миграция в JSON отложена)

| Имя | Значение | Назначение |
|---|---|---|
| `RefWidth, RefHeight` | 1920, 1080 | Опорные размеры для координат |
| `FormImageWidth, FormImageHeight` | 700, 900 | Размер фона формы |
| `SlotSize` | 96 | Размер одного слота |
| `HullIconSize` | 220 | Иконка корпуса (центральный слот) |
| `CargoCellSize` | 80 | Ячейка трюма |
| `CargoColumns, CargoRows` | 4, 6 | Сетка трюма |
| `FixedSlotLayout` | 9 пар (slotKey, Vector2) | Координаты слотов оборудования |
| `WeaponPositions` | Vector2[5] | До 5 слотов оружия |
| `ArtefactPositions` | Vector2[4] | До 4 слотов артефактов |
| `CargoFirstCell` | (1350, 225) | Левый-верх трюма |
| `ActivationSlotPos` | (1260, 450) | Справа от радара (псевдо-слот активации) |
| `ThrowSlotPos` | (80, 620) | Левый-низ (псевдо-слот «выбросить») |
| `PilotPanelPos` | (220, 540) | Слева от инвентаря |

### `GameSettingsConfig` (UI-relevant)

| Поле | Назначение |
|---|---|
| `InventoryKey` | Клавиша открытия формы корабля |
| `WeaponModeKey, DialogModeKey, ForsageKey` | Игровые модусы |
| `MultiWaypointKey` | Shift для добавления waypoint'ов |
| `InfoPopupHoverDelay` | Задержка попапа |
| `InfoPopupWidth, InfoPopupMaxHeight` | Размеры попапа |
| `InfoPopupOffsetX/Y, InfoPopupFollowMouse` | Позиционирование |
| `ShowNpcTrajectoryOnHover` | Рисовать ли траекторию NPC при наведении |
| `DefaultMinimapColor, DefaultStarNameColor` | Fallback цвета |

### `GalaxyMapController` (магия в коде)

| Имя | Значение | Назначение |
|---|---|---|
| `ZOOM_MIN / ZOOM_MAX` | 1 / 6 | Диапазон зума |
| `ZOOM_SPEED` | 0.12 | Скорость зума по wheel |
| `SB` | 4 | Star border thickness |

---

## Внутренняя структура данных

### `SlotWidget` (в ShipFormView)

```
Root : RectTransform
Image : Image           # фон слота (Usual/MouseEntered/Blocked/Broken)
IconImage : Image       # иконка предмета (если есть)
SlotKey : string        # "Hull", "Weapons_0", … или "Cargo_5" (для трюма)
CargoIndex : int        # для трюмных
IsCargo : bool
IsBlocked : bool
Item : ItemInstance     # сейчас в слоте (или null)
```

### `HandState` (в ShipFormView)

```
Item : ItemInstance     # что у игрока в руке (null = пусто)
Icon : Image            # визуальное отображение под курсором
OriginSlot : SlotWidget # откуда взяли (для отката)
```

### `DialogContext` (Dialog/DialogConfigModels.cs)

```
DialogId : string
Self : ShipData
Other : ShipData / PlanetData
CurrentNodeId : string
Variables : Dictionary<string, string>      # переменные диалога (накопленные выборы)
History : List<string>                       # пройденные узлы
Choices : List<DialogChoice>                 # доступные варианты на текущем узле
```

### `DialogChoice`

```
Label : string
TargetNodeId : string
Actions : List<DialogAction>                 # что выполнить при выборе
Condition : DialogCondition                  # фильтр доступности
```

---

## Известные ограничения / TODO

1. **`GalaxyMapController` (1481 LOC)** — god-класс. Кандидат на разделение в Тире C:
   `GalaxyMapZoom`, `GalaxyMapVoronoi`, `GalaxyMapSectorLabels`, `GalaxyMapTooltip`. Отложено
   (риск разрыва межкомпонентного state).
2. **`ShipFormView` координаты слотов в коде** — B4 миграция в JSON отложена. Изменение требует
   расширения схемы `ShipFormSlots.json`, парсера и fallback-значений. Сейчас балансится только
   через перекомпиляцию.
3. **B5 миграция UI на UIBuilder/UIColorPalette** — выполнена через wrapper-делегаты только для
   `PlanetUIController`. Остальные контроллеры (`GalaxyMapController`, `DialogUIController`,
   `HUDController`, `MenuController`) пока используют локальные Make*-методы. Миграция тривиальна
   и низкорискована, но требует тестовой проверки UI.
4. **`UIBuilder.MakeText` требует Font параметром** — отражает legacy uGUI требование. Каждый
   wrapper в контроллере добавляет `_font` автоматически.
5. **`GalaxyMapController.RebuildVoronoi`** аллоцирует новые GameObject'ы каждое обновление —
   потенциальный hotspot, если игрок часто открывает галакарту. Кэширование клеток по
   `(OwnerSetHash, BoundsHash)` — оптимизация на потом.
6. **`SpawnSectorLabels` (90 LOC)** делает 6 разных шагов в одном методе. Подметоды `BuildLabelText`,
   `ComputeLabelColor`, `ComputeLabelFontSize` — кандидаты на извлечение.
7. **DialogService и DialogUIController частично дублируют API** (`OpenSpaceDialog`/`OpenPlanetDialog`
   есть в обоих). Контракт: UI делегирует в Service, Service оповещает через события, UI
   подписан и обновляется. Если caller знает только UI — это нормально.
