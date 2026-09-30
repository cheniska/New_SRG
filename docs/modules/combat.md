# Combat

Модули: `Systems/WeaponSystem.cs`, `Systems/MissileSystem.cs`, `Systems/AsteroidSystem.cs`,
`Galaxy/Models/*.cs` (`StarData.CombatSubTurn`, `StarData.ProcessPlayerManualShot`,
`ShotEvent`, `ActiveMissile`, `CombatResult`, `WeaponShotParams`).

Связанные: `weapons_system.md` (старый дизайн-документ урона/эффектов в `docs/`).

---

## Назначение

Единственная цепочка обработки боевого взаимодействия: NPC и игрок стреляют в цель,
расчитывается урон с учётом уязвимости/брони/щита, регистрируется смерть, наносится износ
оружию и оборудованию цели, ставятся боевые эффекты (Slow/Shutdown/ExecuteBonus/…).

Три источника урона:
1. **Прямые выстрелы** (`WeaponSystem.ProcessShot`) — мгновенно по цели, инициирует `CombatSubTurn`.
2. **Ракеты** (`MissileSystem.TickMissiles` + `WeaponSystem.ApplyMissileImpact`) — летят несколько сабтёрнов, swept-collision.
3. **Астероиды** (`AsteroidSystem`) — кинетический удар при столкновении.

Все три используют общую точку регистрации смерти — `WeaponSystem.RegisterTargetDeath`.

---

## Публичный интерфейс

### `WeaponSystem` (static)

```csharp
// Прямой выстрел: расчёт урона, эффекты, износ, регистрация смерти, лог ShotEvent для анимации.
public static CombatResult ProcessShot(
    ShipData attacker, ShipData target, string weaponSlotKey,
    EquipmentConfig equipConfig, TurnAnimationData anim, int subTurn);

// Урон от ракеты — отдельная точка (без ShotEvent — его добавит MissileSystem).
public static CombatResult ApplyMissileImpact(
    ActiveMissile missile, ShipData target, ShipData attacker, EquipmentConfig equipConfig);

// Единая регистрация смерти. Возвращает true, если цель «считается мёртвой»;
// false, если игрок выжил (Phoenix/Repair) — вызывающий должен откатить TargetDestroyed.
public static bool RegisterTargetDeath(
    ShipData target, PlayerDeathCause cause,
    string killerName, string killerOwner,
    TurnAnimationData anim);

// Расчёт урона по слоям (vuln → armor → shield → hull). Используется и Shot, и Missile.
public static (float hullDamage, float shieldBlocked) CalculateDamage(
    float baseDamage, WeaponShotParams shot, ShipData target, ShipData attacker = null);

// Сбор параметров выстрела с оружия (включая эффекты артефактов и парсинг DamageType/HitPattern).
public static WeaponShotParams BuildShotParams(
    ShipData attacker, ShipData target, string slotKey, ItemInstance weapon);

// Тик активных эффектов (Slow/Shutdown/...) — раз в ход для каждого корабля.
public static void TickEffects(ShipData ship);

// Случайный «бронебойный» предмет для урона по слоту. Перенесён в EquipmentSystem,
// здесь — заглушка/совместимость через EquipmentSystem.GetRandomDamageableSlot.
internal static (ItemInstance, string) /* via EquipmentSystem */;

// Заглушки совместимости (щит больше не имеет состояния):
public static void RegenerateShield(ShipData);
public static void RebuildShieldState(ShipData);

// Парсеры из строкового конфига.
public static DamageType ParseDamageType(string);
public static HitPattern ParseHitPattern(string);
```

### `MissileSystem` (static)

```csharp
// Запуск залпа (SalvoCount, углы ±5°, ±10° …). Боезапас и износ — один раз на залп.
public static void LaunchSalvo(
    ShipData attacker, ShipData target, string slotKey, ItemInstance weapon,
    StarData star, EquipmentConfig equipConfig, TurnAnimationData anim, int subTurn);

// Тик всех ракет звезды на каждом CombatSubTurn (движение + swept-collision + impact).
public static void TickMissiles(
    StarData star, int subTurn, EquipmentConfig equipConfig, TurnAnimationData anim);

// Конец дня: -1 к DaysLeft, сброс LaunchPhase, удаление истёкших.
public static void TickMissilesEndOfDay(StarData star, TurnAnimationData anim);

// Инициализация фреймов в начале дня (без spawn'ов в TurnAnimationData).
public static void InitMissileFrames(StarData star, TurnAnimationData anim);
```

### `AsteroidSystem` (static)

```csharp
// Один Tick физики астероидов (гравитация центра + демпфирование + столкновения).
public static void Tick(StarData star, GalaxyGenerationContext ctx, TurnAnimationData anim);

// При столкновении астероид→корабль — урон и регистрация смерти.
public static int CalculateDamage(AsteroidData asteroid, float dampingConst, float shipProtection);

// Дроп при разрушении астероида (вес/тиры).
public static (List<ItemInstance> items, List<ItemStack> stacks) RollDrop(
    AsteroidData asteroid, GalaxyGenerationContext ctx);
```

---

## Зависимости

```
WeaponSystem.ProcessShot
   ├─► EquipmentSystem.GetEquipped/SlotCategory/GetHullParam/GetHullSusceptibility
   ├─► EquipmentSystem.GetRandomDamageableSlot     (общий буфер с MissileSystem)
   ├─► EquipmentSystem.ApplyShieldHitWear / ApplyWeaponShotWear
   ├─► PlayerManager.KillPlayer                    (через RegisterTargetDeath, если IsPlayer)
   ├─► OwnerRaceRelationsManager                   (snowball-эффект StarReputationHitDelta)
   └─► GameLog.Add              (логи попаданий)

MissileSystem.TickMissiles
   ├─► WeaponSystem.ApplyMissileImpact             (3 ветки HandleMissileImpact)
   ├─► WeaponSystem.RegisterTargetDeath
   ├─► ShipTrajectory.NormalizeAnglePi             (для хоминга)
   ├─► EquipmentSystem.GetEquipped                 (SizeSmall корпуса для радиуса)
   └─► GameLog.Add

AsteroidSystem
   ├─► WeaponSystem.RegisterTargetDeath
   ├─► ItemFactory                                 (для дропа)
   ├─► SpriteUtility.ShortId                       (для текстов лога)
   └─► GalaxyConstants                             (Gravity/Damping)
```

---

## Алгоритмы и формулы

### `CalculateDamage` — порядок слоёв

```
1. vuln  = GetHullSusceptibility(target, shot.DamageType)      # 0..N
   afterVuln = baseDamage * vuln

2. armor = max(0, EquipmentSystem.GetHullParam(target, "Armor") - armorDebuff(target))
   armorAbsorption = clamp(
       max(0, armor - shot.ArmorPenetration),
       0,
       afterVuln * ArmorAbsorptionCap                          # ArmorAbsorptionCap = 0.75
   )
   afterArmor = max(0, afterVuln - armorAbsorption)

3. shield = GetEquipped(target, SlotKeys.Shield)
   если shield.IsWorking:
       blockNorm = clamp01(shield.GetParam("BlockPercent") / 100)
       effectiveBlock = max(0, blockNorm - clamp01(shot.ShieldPenetration))
       afterShield = afterArmor * (1 - effectiveBlock)
       shieldBlocked = afterArmor - afterShield
   иначе afterShield = afterArmor; shieldBlocked = 0

return (hullDamage = max(0, afterShield), shieldBlocked)
```

### `ApplyExecuteBonus` — добивание

Если у атакующего или цели стоит эффект `ExecuteBonus`:
```
damageFraction = 1 - target.CurrentHull / target.MaxHull            # 0 (полный HP) .. 1 (0 HP)
baseDmg *= 1 + 0.33 * damageFraction                                # до +33% при умирающей цели
```
Иначе baseDmg возвращается без изменения.

### `RegisterTargetDeath` — единая регистрация смерти

```
если target == null → return true
если target.IsPlayer:
    died = PlayerManager.Instance?.KillPlayer(cause, killerName, killerOwner) ?? false
    если died и uid ещё не в anim.DeathUids → anim.DeathUids.Add(uid)
    return died
иначе:
    если uid ещё не в anim.DeathUids → anim.DeathUids.Add(uid)
    return true
```

Соглашение по откату:
- **WeaponSystem.ProcessShot**: если возврат false (игрок спасён) → откатываем `result.TargetDestroyed = false`. Это единственное место, где смерть отменяема.
- **MissileSystem.HandleMissileImpact**: игнорирует возврат. Поведение сохранено с до-рефакторинга — ракетный путь не откатывает.
- **AsteroidSystem**: игнорирует возврат. Поведение сохранено.

### Snowball-эффект (репутация)

`WeaponSystem.ProcessShot` после успешной атаки понижает отношение `attacker.Owner` ко всем
уникальным `planet.Owner` в текущей системе:
```
foreach (unique owner in star.Planets):
    если owner != attacker.Owner и owner != None/Mixed:
        Relations.Adjust(attacker, planet, -StarReputationHitDelta)   # StarReputationHitDelta = 2
```
Буфер `_uniqueOwnersBuf` переиспользуется (single-threaded гарантия).

### `MissileSystem` — запуск и наведение

Подробная модель — [`missiles.md`](missiles.md). Коротко:

- залп сходит с направляющих поперёк корпуса веером ширины `SpreadDeg`;
- в ход запуска ракета летит прямо (разгонный участок);
- далее — упреждающее наведение с ограничением `TurnDeg`/ход и «выносом петли» при проскоке;
- если цель отдаляется `MaxRecedingTurns` ходов подряд — захват потерян, самоликвидация;
- головка (`AutoReacquire`) ищет новую цель в конусе `SeekerConeDeg`.

### `HandleMissileImpact` (post-B3) — 3 ветки

```
если missile.IsReturning:                # вернувшаяся торпеда
    RestoreAmmo(attacker, weaponSlotKey)
    лог "Возвращена в attacker — боезапас восстановлен"
    anim.MissileDeathUids[uid] = subTurn
    anim.MissileSilentDeathUids.Add(uid)  # тихая смерть, БЕЗ взрыва
    return

если attacker == null или attacker.CurrentHull <= 0:    # стрелявший погиб
    лог "Снаряд взорвался без эффекта"
    ExplodeMissile(...)                                 # AoE-визуал без урона
    return

# Обычный impact:
result = ApplyMissileImpact(missile, target, attacker, equipConfig)
anim.Shots.Add(new ShotEvent { … HitPattern.Homing … })
если result.TargetDestroyed:
    RegisterTargetDeath(target, Missile, attacker?.Name, attacker?.Owner, anim)  # возврат игнорируется
FreezeFramesAfter(frames, subTurn, newPos)
anim.MissileDeathUids[uid] = subTurn
```

### `SweptCircleHit` — коллизия двух движущихся кружков

Решает квадратное уравнение для |rel(t)|² = combinedR², t ∈ [0,1]:
```
relFrom = aFrom - bFrom
relVel  = (aTo - aFrom) - (bTo - bFrom)
a = relVel·relVel
если a < 1e-10 → return |relFrom|² <= combinedR²       # оба стоят
b = 2 * relFrom·relVel
c = relFrom·relFrom - combinedR²
disc = b² - 4ac
если disc < 0 → no hit
t1 = (-b - √disc) / 2a;  t2 = (-b + √disc) / 2a
если t2 < 0 → no hit (контакт в прошлом)
если t1 > 1 → no hit (контакт в будущем)
если t1 < 0 и b >= 0 → no hit (уже расходятся)
иначе → hit
```

Учёт движения цели обязателен — без него ракета «пролетает мимо», если цель пересекает её путь.

### `MissileSystem.TickMissilesEndOfDay`

```
foreach missile:
    missile.DaysLeft -= 1
    missile.LaunchSubTurn = 0       # на следующий день видна с первого сабтёрна
    missile.LaunchPhase = false     # включается хоминг
    если DaysLeft <= 0:
        лог "Ракета не достигла цели"
        anim.MissileDeathUids[uid] = SubTurnsPerTurn
        remove
```

---

## Константы и настройки

### `WeaponSystem`

| Имя | Значение | Назначение |
|---|---|---|
| `ArmorAbsorptionCap` | 0.75 | Броня не может поглотить >75% afterVuln-урона |
| `EnergyShieldOverloadMult` | 1.5 | Множитель overload-урона по энерго-щиту |
| `SlowStackCap` | 0.75 | Максимальная суммарная замедляемость от стека Slow-эффектов |
| `ArmorDebuffCap` | 200 | Максимальный debuff брони |
| `StarReputationHitDelta` | 2 | -2 к репутации с каждым уникальным owner-ом планет в системе при атаке |
| `0.33f` | внутри `ApplyExecuteBonus` | Линейный коэффициент Execute (до +33% при HP=0) |

### `MissileSystem`

| Имя | Значение | Назначение |
|---|---|---|
| `HitRadius` | 0.15 | Запас сверх радиуса корабля для попадания |
| `DefaultShipRadius` | 0.35 | Фолбэк, если у корабля нет SizeSmall в Hull |
| `MaxSalvo` | 72 | Максимум ракет в залпе |
| `ExtendRadiusFactor` | 2 | Во сколько радиусов разворота отходить перед петлёй |
| `MaxLeadTurns` | 1.5 | Потолок времени упреждения, ходов |
| `1e-10f` | внутри `SweptCircleHit` | Эпсилон «оба стоят» |
| `MissileConfig.SalvoCount` | конфиг | Реальное число ракет в залпе |
| `MissileConfig.TurnDeg` | конфиг (default 720°/ход) | Скорость поворота для хоминга |
| `MissileConfig.Hp` | конфиг (default 30) | Прочность ракеты |
| `MissileConfig.Speed` | конфиг (default 1.5 units/subturn) | Скорость движения |
| `MissileConfig.Lifedays` | конфиг (default 5) | Срок жизни в днях |
| `MissileConfig.ReturnsOnTargetDeath` | конфиг (default false) | Торпеда-возвращенец (восстанавливает Ammo) |

### `AsteroidSystem`

| Имя | Источник | Назначение |
|---|---|---|
| `ASTEROID_GRAVITY_CONST` | `GalaxyConstants` | Сила притяжения к звезде |
| `ASTEROID_DAMPING_CONST` | `GalaxyConstants` | Демпфирование |
| `Asteroids.DampingConstant` | `GalaxyConfig` | Переопределение глобал. константы |

---

## Внутренняя структура данных

### `WeaponShotParams`

```
AttackerUid, TargetUid, WeaponSlotKey : string
MinDmg, MaxDmg, Range : float
ArmorPenetration, ShieldPenetration, EquipHitChance, EquipDamage : float
Ammo : int
DamageType : DamageType       # Kinetic / Explosive / Energy
HitPattern : HitPattern       # Point / AoE / Beam / Falloff / Chain / Homing
Effects : List<WeaponEffect>  # Slow, Shutdown, ExecuteBonus, ArmorDebuff, BlockWeapon, BlockDroid…
```

### `CombatResult`

```
Hit : bool
HullDamage, ShieldDamage, DamageDealt : float
TargetDestroyed : bool                        # может быть откатан в WeaponSystem.ProcessShot
EquipmentHit : bool
EquipmentSlotHit : string
ShieldBypassed : bool
```

### `ShotEvent` (для визуала)

```
AttackerUid, TargetUid, WeaponId : string
DamageDealt : int
SubTurn, ShotDuration : int
HitPattern : HitPattern
DamageType : DamageType
Visual : WeaponVisualDef       # палитра, спрайт
HitEffect : HitEffectConfig    # пер-паттерн+тип эффект попадания
```

### `ActiveMissile`

```
Uid, AttackerUid, AttackerOwner, AttackerRace, TargetUid : string
WeaponId, WeaponSlotKey : string
Position, LaunchDirection : Vector2
CurrentHeading, TurnRadPerTurn : float        # для хоминга
CurrentHp, MaxHp, Speed : float
DaysLeft : int
GraphicPath, Scale : string/float             # визуал
LaunchSubTurn : int                            # на каком сабтёрне вылетела (= 0 со следующего дня)
LaunchPhase : bool                             # true в день вылета, иначе хоминг
IsReturning, ReturnsOnTargetDeath : bool       # торпеда-возвращенец
MinDmg, MaxDmg, ArmorPenetration, ShieldPenetration, EquipHitChance, EquipDamage : float
DamageType : DamageType
Effects : List<WeaponEffect>
```

### `MissileSubTurnFrames`

`SubTurns: Vector2[SubTurnsPerTurn + 1]` — позиции на каждом сабтёрне (включая нулевой = старт).
`FreezeFramesAfter(frames, subTurn, pos)` заполняет хвост массива после impact-сабтёрна, чтобы
визуал ракеты не «отскакивал» обратно.

### `MissileConfig` (из `EquipmentConfig`)

См. `equipment_tiers (2).md`. Главные поля: `SalvoCount`, `Speed`, `TurnDeg`, `Hp`, `Lifedays`,
`GraphicPath`, `Scale`, `ReturnsOnTargetDeath`.

### `DamageType` (enum)

`Kinetic, Explosive, Missile, Energy`.

### `HitPattern` (enum)

`Point, AoE, Beam, Falloff, Chain, Homing`.

### `CombatEffectType` (enum)

`Slow, Shutdown, ExecuteBonus, ArmorDebuff, BlockWeapon, BlockDroid`, и др.

---

## Известные ограничения / TODO

1. **`RegisterTargetDeath` имеет асимметричный контракт** между Weapon (откатывает) и Missile/Asteroid
   (не откатывают). Это сохранено намеренно при рефакторинге, но архитектурно — потенциальный баг:
   игрок может получить ракету в спасённый Phoenix'ом HP-1 кадр и умереть без последствий. Если
   подтвердится как баг — менять Missile/Asteroid на откат.
2. **Snowball-эффект 2-к-каждому-owner'у в системе** — экспериментальный, может быть слишком жёстким
   для систем с большим числом разных владельцев планет.
3. **`MissileSystem` в `TickMissiles` всё ещё ~250 строк** после извлечения двух helper'ов
   (`ComputeNewMissileHeading`, `HandleMissileImpact`). Главная сложность — переплетение
   target-resolution, returning-torpedo logic и swept-collision в одном цикле.
4. **`AsteroidSystem` использует buffer `_damageableSlotCandidates` через `EquipmentSystem`** —
   single-threaded гарантия Unity, но если когда-то появится threading — нужны thread-local буферы.
5. **`WeaponShotParams.Range` не используется WeaponSystem напрямую** — фильтр по дальности делает
   `StarData.CombatSubTurn` ДО вызова ProcessShot. Если кто-то вызовет ProcessShot вне CombatSubTurn,
   расстояние не проверится. Принять как протокол; обернуть в guard было бы defensiveness.
6. **`HitEffectConfig` берётся по строковым ключам `pattern.ToString() + damageType.ToString()`**.
   Опечатка в JSON-конфиге → `null` HitEffect → дефолтный визуал. Логирования mismatch'ей нет.
