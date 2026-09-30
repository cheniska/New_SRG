using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;
using SRG.Combat;
using SRG.Config;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.Galaxy.Simulation;
using SRG.NpcAI;
using SRG.Ships.Disguise;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.Equipment
{
    /// <summary>
    /// Крупнозернистый фасад для Lua-скриптов артефактов (TurnScript / UseScript).
    /// Все методы — static, зарегистрированы в <see cref="Scripting.LuaBindings"/>.
    ///
    /// Правило дизайна: минимум мелких кроссингов C#↔Lua. Одна функция = одно осмысленное
    /// действие или один готовый список. Скрипты не перебирают слоты/корабли/ракеты сами —
    /// вызывают collector-метод и работают с готовым результатом.
    ///
    /// Единицы: все радиусы принимаются в мировых единицах (world units, 1 unit = 100 SR-units).
    /// Хелпер <see cref="SRToWorld"/> преобразует старые SR-числа из конфигов.
    ///
    /// Идентификаторы категорий новостей: см. <see cref="News"/>.
    /// </summary>
    public static class ArtefactApi
    {
        // ── Утилиты единиц ────────────────────────────────────────────────

        /// <summary>SR-единицы → мировые. Тонкая обёртка над <see cref="SRUnits.ToWorld(float)"/>
        /// для Lua-скриптов (обратная совместимость имени).</summary>
        public static float SRToWorld(float sr) => SRG.Utils.SRUnits.ToWorld(sr);

        // ── Категории новостей (для PostNews) ─────────────────────────────

        public static class News
        {
            public const string PLAYER    = GalaxyNewsService.CAT_PLAYER;
            public const string ATTACK    = GalaxyNewsService.CAT_ATTACK;
            public const string SYSTEM    = GalaxyNewsService.CAT_SYSTEM;
            public const string PLANET    = GalaxyNewsService.CAT_PLANET;
            public const string ECONOMY   = GalaxyNewsService.CAT_ECONOMY;
            public const string SCIENCE   = GalaxyNewsService.CAT_SCIENCE;
            public const string ASTEROID  = GalaxyNewsService.CAT_ASTEROID;
        }

        // ── Читалки конфига ──────────────────────────────────────────────

        /// <summary>Прочитать строковое значение из <see cref="ItemConfig.ScriptParams"/>
        /// для предмета. Возвращает <paramref name="def"/>, если ключа нет или тип не приводится.</summary>
        public static string GetConfigString(ItemsConfig cfg, ItemInstance item, string key, string def = null)
        {
            if (cfg == null || item == null || string.IsNullOrEmpty(key)) return def;
            var ic = cfg.GetItem(item.Category, item.ItemId);
            var sp = ic?.ScriptParams;
            if (sp == null) return def;
            var tok = sp[key];
            if (tok == null) return def;
            try { return tok.ToObject<string>() ?? def; } catch { return def; }
        }

        /// <summary>Прочитать числовое значение из <see cref="ItemConfig.ScriptParams"/>.</summary>
        public static float GetConfigFloat(ItemsConfig cfg, ItemInstance item, string key, float def = 0f)
        {
            if (cfg == null || item == null || string.IsNullOrEmpty(key)) return def;
            var ic = cfg.GetItem(item.Category, item.ItemId);
            var sp = ic?.ScriptParams;
            if (sp == null) return def;
            var tok = sp[key];
            if (tok == null) return def;
            try { return tok.ToObject<float>(); } catch { return def; }
        }

        /// <summary>Целочисленный вариант <see cref="GetConfigFloat"/>.</summary>
        public static int GetConfigInt(ItemsConfig cfg, ItemInstance item, string key, int def = 0)
            => Mathf.RoundToInt(GetConfigFloat(cfg, item, key, def));

        /// <summary>Достать вложенный ScriptParams-объект как <see cref="JObject"/> (для сложных
        /// структур вроде Summon-спеки). null, если ключа нет.</summary>
        public static JObject GetConfigObject(ItemsConfig cfg, ItemInstance item, string key)
        {
            if (cfg == null || item == null || string.IsNullOrEmpty(key)) return null;
            var ic = cfg.GetItem(item.Category, item.ItemId);
            var sp = ic?.ScriptParams;
            if (sp == null) return null;
            return sp[key] as JObject;
        }

        // ── Читалки предметов ─────────────────────────────────────────────

        /// <summary>Установленные предметы корабля с прочностью ниже <paramref name="threshold"/>
        /// (0..1). Исключает <paramref name="except"/> (обычно сам артефакт). Порядок стабильный,
        /// но неопределённый — используйте <see cref="PickRandom"/> для случайного выбора.</summary>
        public static List<ItemInstance> DamagedItems(ShipData ship, float threshold, ItemInstance except = null)
        {
            var list = new List<ItemInstance>();
            if (ship?.Equipment == null) return list;
            foreach (var kv in ship.Equipment.Slots)
            {
                if (kv.Value == null) continue;
                if (!ship.AllItems.TryGetValue(kv.Value, out var it) || it == null) continue;
                if (except != null && it == except) continue;
                if (it.MaxDurability <= 0 || it.NoWear) continue;
                if (it.Durability >= it.MaxDurability) continue;
                if ((float)it.Durability / it.MaxDurability >= threshold) continue;
                list.Add(it);
            }
            return list;
        }

        /// <summary>Все установленные предметы корабля.</summary>
        public static List<ItemInstance> EquippedItems(ShipData ship)
        {
            var list = new List<ItemInstance>();
            if (ship?.Equipment == null) return list;
            foreach (var kv in ship.Equipment.Slots)
            {
                if (kv.Value == null) continue;
                if (ship.AllItems.TryGetValue(kv.Value, out var it) && it != null)
                    list.Add(it);
            }
            return list;
        }

        /// <summary>Первый установленный предмет заданной категории (или null).</summary>
        public static ItemInstance FindByCategory(ShipData ship, string category, bool requireWorking = true)
            => EquipmentSystem.FindEquipmentByCategory(ship, category, requireWorking);

        /// <summary>Прокси к <see cref="StatBus.GetItem"/>.</summary>
        public static float GetParam(ItemInstance item, string key, float def = 0f)
            => StatBus.GetItem(item, key, def);

        /// <summary>Прокси к <see cref="StatBus.SumShipCategory"/>.</summary>
        public static float SumCategory(ShipData ship, string category, string key)
            => StatBus.SumShipCategory(ship, category, key);

        /// <summary>Случайный элемент списка или null. Работает с любым <see cref="System.Collections.IList"/>
        /// (MoonSharp кастует Lua-таблицы к List, если объявлять их через <see cref="EquippedItems"/>/
        /// <see cref="DamagedItems"/>/<see cref="HostileMissiles"/> — API возвращает готовые List).</summary>
        public static object PickRandom(System.Collections.IList list)
        {
            if (list == null || list.Count == 0) return null;
            return list[GameRng.Range(0, list.Count)];
        }

        // ── Ракеты ────────────────────────────────────────────────────────

        /// <summary>Активные ракеты в системе корабля, летящие в него или его союзников,
        /// в радиусе <paramref name="worldRange"/> от корабля. Исключает свои ракеты
        /// (по AttackerUid) и союзные (по совпадению AttackerOwner). Возвращающиеся торпеды
        /// пропускаются, если <paramref name="ignoreReturning"/>.</summary>
        public static List<ActiveMissile> HostileMissiles(ShipData ship, float worldRange, bool ignoreReturning = true)
        {
            var list = new List<ActiveMissile>();
            var star = ship?.CurrentStar;
            if (star?.ActiveMissiles == null || worldRange <= 0f) return list;
            float r2 = worldRange * worldRange;
            for (int i = 0; i < star.ActiveMissiles.Count; i++)
            {
                var m = star.ActiveMissiles[i];
                if (m == null) continue;
                if (m.AttackerUid == ship.Uid) continue;
                if (ignoreReturning && m.IsReturning) continue;
                if (!string.IsNullOrEmpty(m.AttackerOwner) && m.AttackerOwner == ship.Owner) continue;
                if ((m.Position - ship.Position).sqrMagnitude > r2) continue;
                list.Add(m);
            }
            return list;
        }

        /// <summary>Удалить ракету из системы (перехват/уничтожение вне цели). Возвращает true,
        /// если ракета найдена и убрана. Использовать после <see cref="HostileMissiles"/>.</summary>
        public static bool KillMissile(ShipData ship, ActiveMissile missile)
        {
            var star = ship?.CurrentStar;
            if (star?.ActiveMissiles == null || missile == null) return false;
            return star.ActiveMissiles.Remove(missile);
        }

        /// <summary>Обёртка: найти враждебные ракеты в радиусе и сбить до <paramref name="maxShots"/>
        /// штук. Возвращает число сбитых. Удобно для «Заслон»-подобных ПРО.</summary>
        public static int KillMissilesInRadius(ShipData ship, float worldRange, int maxShots, bool ignoreReturning = true)
        {
            var star = ship?.CurrentStar;
            if (star?.ActiveMissiles == null || worldRange <= 0f || maxShots <= 0) return 0;
            float r2 = worldRange * worldRange;
            int killed = 0;
            for (int i = star.ActiveMissiles.Count - 1; i >= 0 && killed < maxShots; i--)
            {
                var m = star.ActiveMissiles[i];
                if (m == null) continue;
                if (m.AttackerUid == ship.Uid) continue;
                if (ignoreReturning && m.IsReturning) continue;
                if (!string.IsNullOrEmpty(m.AttackerOwner) && m.AttackerOwner == ship.Owner) continue;
                if ((m.Position - ship.Position).sqrMagnitude > r2) continue;
                star.ActiveMissiles.RemoveAt(i);
                killed++;
            }
            return killed;
        }

        // ── Корабли рядом ─────────────────────────────────────────────────

        /// <summary>Живые корабли в системе <paramref name="ship"/> в радиусе <paramref name="worldRange"/>,
        /// исключая сам корабль. <paramref name="hostility"/>: "hostile" — только враждебные (по Relations),
        /// "allied" — только союзники, любое другое значение — все.</summary>
        public static List<ShipData> NearbyShips(ShipData ship, float worldRange, string hostility = null)
        {
            var list = new List<ShipData>();
            var star = ship?.CurrentStar;
            if (star?.Ships == null || worldRange <= 0f) return list;
            float r2 = worldRange * worldRange;
            bool wantHostile = string.Equals(hostility, "hostile", System.StringComparison.OrdinalIgnoreCase);
            bool wantAllied  = string.Equals(hostility, "allied",  System.StringComparison.OrdinalIgnoreCase);
            for (int i = 0; i < star.Ships.Count; i++)
            {
                var s = star.Ships[i];
                if (s == null || s == ship || s.CurrentHull <= 0) continue;
                if ((s.Position - ship.Position).sqrMagnitude > r2) continue;
                if (wantHostile && !Relations.AreHostile(ship, s)) continue;
                if (wantAllied && Relations.AreHostile(ship, s)) continue;
                list.Add(s);
            }
            return list;
        }

        // ── Действия: прочность/ремонт/урон ───────────────────────────────

        /// <summary>Прибавить прочность предмету (клампится в MaxDurability). Возвращает фактически добавленное.</summary>
        public static int RepairItem(ItemInstance item, int points)
        {
            if (item == null || points <= 0 || item.MaxDurability <= 0) return 0;
            int before = item.Durability;
            item.Durability = Mathf.Min(item.MaxDurability, item.Durability + points);
            return item.Durability - before;
        }

        /// <summary>Прибавить процент от MaxDurability. Округление вверх, минимум 1.</summary>
        public static int RepairItemPercent(ItemInstance item, float pct)
        {
            if (item == null || pct <= 0f || item.MaxDurability <= 0) return 0;
            int amount = Mathf.Max(1, Mathf.RoundToInt(item.MaxDurability * pct));
            return RepairItem(item, amount);
        }

        /// <summary>Списать прочность (клампится в 0). Возвращает фактически списанное.</summary>
        public static int SpendDurability(ItemInstance item, int points)
        {
            if (item == null || points <= 0 || item.MaxDurability <= 0) return 0;
            int before = item.Durability;
            item.Durability = Mathf.Max(0, item.Durability - points);
            return before - item.Durability;
        }

        /// <summary>Установить прочность в 0 (артефакт становится сломанным, но остаётся в слоте).</summary>
        public static void MarkBroken(ItemInstance item)
        {
            if (item == null) return;
            item.Durability = 0;
        }

        /// <summary>Долить топливо. Возвращает фактически добавленное (0 если бак полон).</summary>
        public static int AddFuel(ShipData ship, int amount)
            => EquipmentSystem.AddFuel(ship, amount);

        /// <summary>Восстановить корпус. Клампится в MaxHull. Возвращает фактически восстановленное.</summary>
        public static int RepairHull(ShipData ship, int hp)
        {
            if (ship == null || hp <= 0) return 0;
            int before = ship.CurrentHull;
            ship.CurrentHull = Mathf.Min(ship.MaxHull, ship.CurrentHull + hp);
            return ship.CurrentHull - before;
        }

        /// <summary>Прямой урон по корпусу (без учёта щита/брони, для «магических» эффектов артефактов).</summary>
        public static int DamageHull(ShipData ship, int hp)
        {
            if (ship == null || hp <= 0) return 0;
            int before = ship.CurrentHull;
            ship.CurrentHull = Mathf.Max(0, ship.CurrentHull - hp);
            return before - ship.CurrentHull;
        }

        // ── Действия: бонусы (StatBus) ────────────────────────────────────

        /// <summary>Наложить именной аддитивный бонус на предмет. Ключ — "Category.ParamKey".
        /// Повторный вызов с тем же <paramref name="sourceId"/> перезаписывает значение.</summary>
        public static void ApplyBonus(ItemInstance target, string key, string sourceId, float delta,
                                      bool hidden = false, string sourceLabel = null)
            => StatBus.Add(target, key, sourceId, delta, hidden, sourceLabel);

        /// <summary>Снять ранее наложенный бонус с этим sourceId. true если бонус был.</summary>
        public static bool ClearBonus(ItemInstance target, string sourceId)
            => StatBus.Clear(target, sourceId);

        /// <summary>Наложить бонус в один вызов сразу на все установленные предметы категории.
        /// Возвращает число затронутых слотов.</summary>
        public static int ApplyShipCategoryBonus(ShipData ship, string category, string key,
                                                 string sourceId, float delta, bool hidden = false, string sourceLabel = null)
            => StatBus.AddShipCategory(ship, category, key, sourceId, delta, hidden, sourceLabel);

        // ── Действия: события мира ────────────────────────────────────────

        /// <summary>Публикация новости. <paramref name="category"/> — одна из <see cref="News"/>-констант.</summary>
        public static void PostNews(string category, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            GalaxyNewsService.Post(category ?? News.SYSTEM, text);
        }

        /// <summary>Короткая обёртка: новость категории "Вольный пилот" (события игрока).</summary>
        public static void PostPlayerNews(string text) => PostNews(News.PLAYER, text);

        /// <summary>Спавн червоточины в радиусе <paramref name="worldRadius"/> от корабля.
        /// Точка выхода — случайная. Возвращает созданный <see cref="WormholeData"/> или null.</summary>
        public static WormholeData SpawnWormholeNearShip(ShipData ship, float worldRadius = 0.05f)
        {
            if (ship?.CurrentStar == null) return null;
            return WormholeService.Spawn(Positions.Near(ship, worldRadius));
        }

        /// <summary>Призвать сторону в систему корабля (через <see cref="SummonService"/>).
        /// <paramref name="spec"/> — Lua-таблица с полями Side/RaceId/DurationTurns/RequiredStagingPower.
        /// Целевой звездой становится текущая звезда корабля.</summary>
        public static bool SummonSideToShip(ShipData ship, MoonSharp.Interpreter.Table spec)
        {
            if (ship?.CurrentStar == null || spec == null) return false;
            var s = new SummonService.Spec
            {
                Side                 = spec.Get("Side").String,
                RaceId               = spec.Get("RaceId").IsNil() ? null : spec.Get("RaceId").String,
                DurationTurns        = (int)(spec.Get("DurationTurns").Type == MoonSharp.Interpreter.DataType.Number        ? spec.Get("DurationTurns").Number        : 30),
                RequiredStagingPower = (float)(spec.Get("RequiredStagingPower").Type == MoonSharp.Interpreter.DataType.Number ? spec.Get("RequiredStagingPower").Number : 0),
                TargetStarUid        = ship.CurrentStarUid,
                StagingStarUid       = spec.Get("StagingStarUid").IsNil() ? null : spec.Get("StagingStarUid").String,
            };
            return SummonService.Trigger(s, ship.CurrentStarUid);
        }

        /// <summary>Overload: спека берётся прямо из <see cref="ItemConfig.ScriptParams"/>
        /// подобъекта (для маяков — <c>ScriptParams.Summon</c>).</summary>
        public static bool SummonSideFromConfig(ShipData ship, ItemsConfig cfg, ItemInstance item, string key = "Summon")
        {
            var obj = GetConfigObject(cfg, item, key);
            if (obj == null || ship?.CurrentStar == null) return false;
            var s = SummonService.Spec.FromJson(obj);
            return SummonService.Trigger(s, ship.CurrentStarUid);
        }

        // ── Действия: маскировка ──────────────────────────────────────────

        /// <summary>Надеть маскировку. <paramref name="itemUidForSource"/> — обычно UID артефакта-камуфляжа
        /// (чтобы повторная активация того же предмета снимала маску). Возвращает true при успехе.</summary>
        public static bool SetDisguise(ShipData ship, string itemUidForSource,
                                       string targetRace, string targetOwner,
                                       string visualPath = null, string displayName = null)
        {
            if (ship == null) return false;
            if (string.IsNullOrEmpty(targetRace) && string.IsNullOrEmpty(targetOwner)) return false;
            var state = new DisguiseState
            {
                SourceItemUid = itemUidForSource,
                TargetRace    = targetRace,
                TargetOwner   = targetOwner,
                VisualPath    = visualPath,
                DisplayName   = displayName,
            };
            return DisguiseService.Activate(ship, state);
        }

        /// <summary>Снять маскировку с корабля (если надета).</summary>
        public static void ClearDisguise(ShipData ship) => DisguiseService.Deactivate(ship);

        /// <summary>Uid предмета, вокруг которого построена текущая маска (для сравнения «тот же артефакт?»).
        /// Пусто, если маски нет.</summary>
        public static string ActiveDisguiseItemUid(ShipData ship)
            => ship?.Disguise?.SourceItemUid ?? string.Empty;

        /// <summary>Комплексная активация/деактивация камуфляжа с чтением ScriptParams:
        /// TargetRace, TargetOwner, VisualPath, DisplayName, WearOnActivate. Возвращает
        /// Lua-таблицу-результат (ok/message/wear/consume=false) — скрипт возвращает её напрямую.</summary>
        public static MoonSharp.Interpreter.Table ToggleDisguiseFromConfig(ShipData ship, ItemsConfig cfg, ItemInstance item)
        {
            var t = new MoonSharp.Interpreter.Table(SRG.Scripting.LuaHost.Core);
            if (ship == null || item == null) { t["ok"] = false; t["message"] = "[Камуфляж] Неверный контекст активации."; return t; }
            if (!IsPlanning())
            {
                t["ok"] = false;
                t["message"] = $"[Камуфляж] {item.Name}: доступно только в фазе планирования.";
                return t;
            }
            if (ship.Disguise != null && ship.Disguise.SourceItemUid == item.Uid)
            {
                string name = ship.Disguise.DisplayName ?? item.Name;
                DisguiseService.Deactivate(ship);
                t["ok"] = true;
                t["message"] = $"[Камуфляж] {name}: маскировка снята.";
                return t;
            }
            string targetRace  = GetConfigString(cfg, item, "TargetRace");
            string targetOwner = GetConfigString(cfg, item, "TargetOwner");
            string visualPath  = GetConfigString(cfg, item, "VisualPath");
            string displayName = GetConfigString(cfg, item, "DisplayName") ?? item.Name;
            if (string.IsNullOrEmpty(targetRace) && string.IsNullOrEmpty(targetOwner))
            {
                t["ok"] = false;
                t["message"] = $"[Камуфляж] {item.Name}: нужен TargetRace или TargetOwner в ScriptParams.";
                return t;
            }
            if (!SetDisguise(ship, item.Uid, targetRace, targetOwner, visualPath, displayName))
            {
                t["ok"] = false;
                t["message"] = $"[Камуфляж] {item.Name}: активация не удалась.";
                return t;
            }
            int wear = GetConfigInt(cfg, item, "WearOnActivate", 0);
            string tail = ".";
            if (ship.Disguise?.DetectedByRaces != null && ship.Disguise.DetectedByRaces.Count > 0)
                tail = $" — но местные ({string.Join(", ", ship.Disguise.DetectedByRaces)}) видели переоблачение, маска сразу раскрыта.";
            t["ok"] = true;
            t["wear"] = wear;
            t["message"] = $"[Камуфляж] {item.Name}: маскировка надета{tail}";
            return t;
        }

        /// <summary>Список рас, которые уже видели переоблачение (маска для них раскрыта сразу).</summary>
        public static List<string> DisguiseDetectedRaces(ShipData ship)
        {
            var list = new List<string>();
            if (ship?.Disguise?.DetectedByRaces == null) return list;
            foreach (var r in ship.Disguise.DetectedByRaces) list.Add(r);
            return list;
        }

        // ── Директивы фракций (для Анализатора и подобных) ────────────────

        /// <summary>Целевые звёзды текущих активных атакующих директив, попадающие в радиус
        /// <paramref name="scopePc"/> парсек от корабля. Возвращает список tuple-таблиц (ownerId, targetStar).</summary>
        public static List<AttackDirectiveInfo> AttackDirectivesNear(ShipData ship, float scopePc)
        {
            var list = new List<AttackDirectiveInfo>();
            var galaxy = GameWorld.GeneratedGalaxy;
            if (ship == null || galaxy?.Directives == null) return list;
            if (string.IsNullOrEmpty(ship.CurrentStarUid)) return list;
            if (!galaxy.StarsMap.TryGetValue(ship.CurrentStarUid, out var here) || here == null) return list;
            float r2 = scopePc * scopePc;
            foreach (var d in galaxy.Directives)
            {
                if (d is not DirectiveAttackSystem atk) continue;
                if (atk.IsExpired) continue;
                if (!galaxy.StarsMap.TryGetValue(atk.TargetStarUid, out var target) || target == null) continue;
                float dx = target.Position.x - here.Position.x;
                float dy = target.Position.y - here.Position.y;
                if (dx * dx + dy * dy > r2) continue;
                list.Add(new AttackDirectiveInfo { OwnerId = atk.OwnerId, TargetStar = target });
            }
            return list;
        }

        /// <summary>Простой POCO для передачи данных директивы в Lua.</summary>
        public class AttackDirectiveInfo
        {
            public string OwnerId;
            public StarData TargetStar;
            public string TargetStarName => TargetStar?.Name;
            public string TargetStarUid  => TargetStar?.Uid;
        }

        // ── Игровой контекст ──────────────────────────────────────────────

        /// <summary>Текущий номер хода галактики. 0 если нет активной игры.</summary>
        public static int CurrentTurn()
            => GameWorld.GeneratedGalaxy?.CurrentTurn ?? 0;

        /// <summary>Сколько сабтёрнов в ходу (константа <see cref="GalaxyData.SubTurnsPerTurn"/>).</summary>
        public static int SubTurnsPerTurn => GalaxyData.SubTurnsPerTurn;

        /// <summary>true, если сейчас фаза планирования (пользователь может активировать вещи).</summary>
        public static bool IsPlanning()
            => GameWorld.IsAttached && GameWorld.Phase == TurnPhase.Planning;

        /// <summary>true, если идёт анимация хода.</summary>
        public static bool IsSimulation()
            => GameWorld.IsAttached && GameWorld.Phase == TurnPhase.Simulation;

        /// <summary>Логировать сообщение в игровую консоль (алиас Log() в глобалах).</summary>
        public static void Console(string msg) => GameLog.Add(msg ?? "nil");
    }
}
