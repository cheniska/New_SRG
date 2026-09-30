using System;
using System.Collections.Generic;
using SRG.Combat;
using SRG.Config;
using SRG.Galaxy.Simulation;
using SRG.NpcAI;
using SRG.Science;
using SRG.Simulation;

namespace SRG.Galaxy.Politics
{
    /// <summary>
    /// Единичная новость галактики (инфоцентр планетарной формы).
    /// Категории — Occupation/Planet/Directive/Science/…,
    /// но не привязаны к ним 1-в-1 — часть реализована на существующих подсистемах, часть свои.
    /// </summary>
    /// <summary>Группа уведомлений — управляет тем, в какую секцию нижней панели попадёт иконка.
    /// <see cref="Planet"/> в панель уведомлений НЕ выводится (шумная категория: событие/строй у
    /// каждой обитаемой планеты), но остаётся в полной ленте инфоцентра.</summary>
    public enum NewsKind
    {
        Galaxy,   // общегалактическая сводка (война, экономика, наука, оккупация систем)
        Player,   // касается непосредственно игрока или его партнёра
        Planet,   // событие на конкретной планете — фильтруется из панели уведомлений
        Quest     // задания (пока placeholder — квестов в игре нет)
    }

    [Serializable]
    public class GalaxyNewsEntry
    {
        /// <summary>Монотонный ID — стабилен после FIFO-обрезки. Нужен для «dismiss» на панели.</summary>
        public int Id;
        public int Turn;
        public string Date;
        public string Category;
        public string Text;
    }

    /// <summary>
    /// Лента новостей галактики. Хранение — в <see cref="GalaxyData.News"/>, чтобы
    /// новости выживали save/load. Размер буфера — <see cref="GameSettingsConfig.NewsMaxCount"/>,
    /// вытеснение по FIFO. Публикация — через <see cref="Post"/>; UI (PlanetUIController.InfoCenter)
    /// подписывается на <see cref="OnNewsAdded"/> для инкрементального обновления.
    ///
    /// Wire: все статические подписки (OccupationService, PlanetaryEventSystem, DirectiveManager,
    /// GovernmentChangeService и т.п.) зарегистрированы в <see cref="Initialize"/>, который зовётся
    /// один раз из <see cref="GalaxyManager.Awake"/>.
    ///
    /// TODO(«знакомые корабли»): при повторных мирных контактах игрока с одним и тем же NPC
    /// сохранять UID в PlayerShipData.KnownContacts и подставлять специальные реплики при их
    /// гибели (расширение DeadShip.* для не-партнёров). Сейчас реализованы только партнёры.
    /// </summary>
    public static class GalaxyNewsService
    {
        // Ключи категорий — стабильные латинские идентификаторы. Отображаемые имена
        // берутся из TextConfig.News.Categories через NewsTexts.Category(key); UI (NewsFeedView,
        // NotificationPanelController) переводит их на лету при рендере.
        public const string CAT_OCCUPATION  = "occupation";
        public const string CAT_LIBERATION  = "liberation";
        public const string CAT_ATTACK      = "attack";
        public const string CAT_PLANET      = "planet";
        public const string CAT_GOVERNMENT  = "government";
        public const string CAT_SCIENCE     = "science";
        public const string CAT_ECONOMY     = "economy";
        public const string CAT_PLAYER      = "player";
        public const string CAT_COALITION   = "coalition";
        public const string CAT_SYSTEM      = "system";
        public const string CAT_RANGERS     = "ranger_rating";
        public const string CAT_ASTEROID    = "asteroid";
        public const string CAT_DEAD_SHIP   = "ship_lost";
        public const string CAT_DEFEAT      = "faction_defeat";

        public static event Action<GalaxyNewsEntry> OnNewsAdded;
        /// <summary>Новость удалена/скрыта из ленты (обычно RMB на иконке в панели уведомлений).
        /// Слушают UI-виджеты для инкрементального обновления.</summary>
        public static event Action<GalaxyNewsEntry> OnNewsRemoved;

        private static bool _initialized;
        private static int  _nextId = 1;

        /// <summary>Активная галактика для реактивных подписок: во время тика галактики N её
        /// GalaxyNextDay эмитит события — новости должны идти в ленту N, а не активной.
        /// Вне тика fallback на GeneratedGalaxy (для UI-триггеров типа RemoveById).</summary>
        private static GalaxyData Target()
        {
            return GameWorld.TargetGalaxy;
        }

        /// <summary>Убрать одну запись по Id. Возвращает true если что-то удалено.</summary>
        public static bool RemoveById(int id)
        {
            var galaxy = Target();
            if (galaxy?.News == null) return false;
            for (int i = 0; i < galaxy.News.Count; i++)
            {
                if (galaxy.News[i].Id == id)
                {
                    var removed = galaxy.News[i];
                    galaxy.News.RemoveAt(i);
                    OnNewsRemoved?.Invoke(removed);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Классификатор — определяет, в какую секцию нижней панели уведомлений
        /// попадёт новость данной категории. Игрок-релевантные (награда, партнёр, рейтинг),
        /// общегалактические (война/экономика/планеты) и квесты (в будущем).</summary>
        public static NewsKind ClassifyKind(string category)
        {
            if (string.IsNullOrEmpty(category)) return NewsKind.Galaxy;
            switch (category)
            {
                case CAT_PLAYER:
                case CAT_DEAD_SHIP:
                case CAT_ASTEROID:
                case CAT_RANGERS:
                    return NewsKind.Player;
                case CAT_PLANET:
                case CAT_GOVERNMENT:
                    return NewsKind.Planet;
                default:
                    return NewsKind.Galaxy;
            }
        }

        /// <summary>Выставить счётчик Id новостей после генерации/загрузки: следующий Id больше всех
        /// уже существующих во всех галактиках — иначе после перезапуска игры Id начинались бы с 1
        /// и совпадали со старыми (RemoveById удалял бы не ту запись).</summary>
        public static void SyncNextId(IEnumerable<GalaxyData> galaxies)
        {
            int max = 0;
            if (galaxies != null)
                foreach (var g in galaxies)
                    if (g?.News != null)
                        foreach (var n in g.News)
                            if (n != null && n.Id > max) max = n.Id;
            _nextId = max + 1;
        }

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            OccupationService.OnSystemControlChanged += OnSystemControlChanged;
            OccupationService.OnFactionDefeated += OnFactionDefeated;
            DirectiveAttackSystem.OnResolved += OnAttackResolved;
            AsteroidSystem.OnAsteroidShotDown += OnAsteroidShotDown;
            ShipDeathBus.OnShipDestroyed += OnShipDestroyed;
        }

        /// <summary>Полный список текущих новостей (может быть null до генерации галактики).</summary>
        public static IReadOnlyList<GalaxyNewsEntry> Entries
        {
            get
            {
                var galaxy = GameWorld.GeneratedGalaxy;
                return galaxy?.News;
            }
        }

        /// <summary>Опубликовать новость. Игнорируется до генерации галактики.</summary>
        public static void Post(string category, string text)
        {
            var galaxy = Target();
            if (galaxy == null || string.IsNullOrEmpty(text)) return;

            int cap = GameWorld.Settings?.NewsMaxCount ?? 100;
            var list = galaxy.News ??= new List<GalaxyNewsEntry>();

            var entry = new GalaxyNewsEntry
            {
                Id = _nextId++,
                Turn = galaxy.CurrentTurn,
                Date = galaxy.GetCurrentDate(),
                Category = category ?? "",
                Text = text,
            };
            list.Add(entry);
            while (list.Count > cap) list.RemoveAt(0);

            OnNewsAdded?.Invoke(entry);
        }

        /// <summary>
        /// Опубликовать «общую» новость с привязкой к сторонам-владельцам. Показывается игроку
        /// только если хотя бы одна из указанных сторон — своя или дружественная (не hostile).
        /// Стороны, переданные пустыми, игнорируются; если обе пусты — новость публикуется как
        /// обычная (fallback на <see cref="Post"/>). Так же ведёт себя, если игрок ещё не создан
        /// или его сторона неизвестна.
        /// </summary>
        public static void PostForSide(string category, string text, string sideA, string sideB = null)
        {
            int turn = Target()?.CurrentTurn ?? 0;
            string a = string.IsNullOrEmpty(sideA) ? null : sideA;
            string b = string.IsNullOrEmpty(sideB) ? null : sideB;
            if (a == null && b == null)
            {
                NewsLog.Decision(turn, "POST", category, null, sideA, sideB, "no_scope", text);
                Post(category, text);
                return;
            }

            string playerSide = GameWorld.PlayerShip?.Owner;
            if (string.IsNullOrEmpty(playerSide))
            {
                NewsLog.Decision(turn, "POST", category, playerSide, sideA, sideB, "no_player", text);
                Post(category, text);
                return;
            }

            bool okA = IsFriendlyForPlayer(a, playerSide);
            bool okB = IsFriendlyForPlayer(b, playerSide);
            if (okA || okB)
            {
                string reason = okA && okB ? "both_friendly" : okA ? "a_friendly" : "b_friendly";
                NewsLog.Decision(turn, "POST", category, playerSide, sideA, sideB, reason, text);
                Post(category, text);
            }
            else
            {
                NewsLog.Decision(turn, "DROP", category, playerSide, sideA, sideB, "all_hostile", text);
            }
        }

        /// <summary>Проверка «дружественности» стороны игроку (для варьирования текста новостей).</summary>
        public static bool IsFriendlyToPlayer(string owner)
        {
            var player = GameWorld.PlayerShip;
            if (player == null || string.IsNullOrEmpty(player.Owner)) return true;
            return IsFriendlyForPlayer(owner, player.Owner);
        }

        /// <summary>Дружественная сторона = своя или не-hostile по фракционному отношению.</summary>
        private static bool IsFriendlyForPlayer(string side, string playerSide)
        {
            if (string.IsNullOrEmpty(side)) return false;
            if (side == playerSide) return true;
            var rel = OwnerRaceRelationsManager.Instance;
            if (rel == null) return true; // без менеджера отношений — не фильтруем
            return !rel.AreHostile(playerSide, side);
        }

        // ── Подписки на игровые события ────────────────────────────

        // Общий шаблон side1→side2. Для добавления новой фракции достаточно, чтобы её OwnerId
        // фигурировал в CurrentSystemController — никаких дополнительных «Side»-флагов.
        private static void OnSystemControlChanged(StarData star, string newController, string prevController)
        {
            if (star == null) return;
            string starName = star.Name ?? "?";
            string sectorName = star.ParentSector?.Name ?? "?";

            // Стало единой (кто-то полностью захватил/освободил все планеты)
            if (!string.IsNullOrEmpty(newController) && newController != prevController)
            {
                string tplKey = string.IsNullOrEmpty(prevController)
                    ? "system_control.new"
                    : "system_control.transfer";
                string cat = string.IsNullOrEmpty(prevController) ? CAT_OCCUPATION : CAT_LIBERATION;
                string text = NewsTexts.Format(tplKey,
                    ("starName", starName), ("sectorName", sectorName),
                    ("prevController", prevController), ("newController", newController));
                PostForSide(cat, text, newController, prevController);
            }
            // Стала смешанной — контроль потерян.
            else if (string.IsNullOrEmpty(newController) && !string.IsNullOrEmpty(prevController))
            {
                PostForSide(CAT_SYSTEM,
                    NewsTexts.Format("system_control.lost",
                        ("starName", starName), ("sectorName", sectorName),
                        ("prevController", prevController)),
                    prevController);
            }
        }

        private static void OnFactionDefeated(string ownerId)
        {
            if (string.IsNullOrEmpty(ownerId)) return;
            // Поражение стороны — глобальная новость, но её тон зависит от того, была ли эта
            // сторона союзной игроку. Своя/союзная разбита — трагедия; враг разбит — победа.
            bool friendly = IsFriendlyToPlayer(ownerId);
            string text = NewsTexts.Format(friendly ? "faction_defeat.ally" : "faction_defeat.enemy",
                ("ownerId", ownerId));
            int turn = Target()?.CurrentTurn ?? 0;
            string playerSide = GameWorld.PlayerShip?.Owner;
            NewsLog.Decision(turn, "VARIANT", CAT_DEFEAT, playerSide, ownerId, null,
                friendly ? "ally_defeated" : "enemy_defeated", text);
            Post(CAT_DEFEAT, text);
        }

        // Аналог Star.Kling.Lost / Star.Pirates.Lost — когда атака отражена.
        // При успешном захвате новость публикуется через OnSystemControlChanged.
        private static void OnAttackResolved(DirectiveAttackSystem atk, string reason)
        {
            if (atk == null) return;
            var galaxy = Target();
            if (galaxy == null) return;
            string starName = galaxy.StarsMap.TryGetValue(atk.TargetStarUid, out var star) ? star.Name : "?";

            // Reason:
            //  "captured"          — атака удалась (новость про переход контроллёра эмитится в OnSystemControlChanged)
            //  "no_enemies"        — врагов не осталось (тоже успех)
            //  "timeout_or_prior"  — не успели за отведённое время (провал)
            //  "star_missing"      — редкий системный случай
            if (reason == "timeout_or_prior" || reason == "star_missing")
            {
                string defender = galaxy.StarsMap.TryGetValue(atk.TargetStarUid, out var starForDef)
                    ? starForDef?.CurrentSystemController : null;
                PostForSide(CAT_LIBERATION,
                    NewsTexts.Format("attack.repelled",
                        ("attacker", atk.OwnerId), ("starName", starName)),
                    defender, atk.OwnerId);
            }
        }

        // Асстероид сбит выстрелом. Планета != null означает «угрожал планете» — начисляем награду
        // (только игроку) и постим новость по расе планеты (Fei/Gaal/Maloc/Peleng/People из документа).
        private static void OnAsteroidShotDown(AsteroidData asteroid, ShipData shooter, StarData star, PlanetData planet)
        {
            if (planet == null || shooter == null) return; // без угрозы планете новостей нет
            if (!shooter.IsPlayer) return; // сейчас механика награды — только для игрока

            var settings = GameWorld.Settings;
            int baseReward = settings?.AsteroidRewardBase ?? 100;
            int reward = UnityEngine.Mathf.Max(baseReward, UnityEngine.Mathf.RoundToInt(baseReward * UnityEngine.Mathf.Max(1f, asteroid.Mass)));
            shooter.Money += reward;

            string planetName = string.IsNullOrEmpty(planet.Name) ? "неизвестная планета" : planet.Name;
            string shooterName = string.IsNullOrEmpty(shooter.Name) ? "вольный пилот" : shooter.Name;
            string raceKey = (planet.Race ?? "").ToLowerInvariant();
            string tplKey = raceKey switch
            {
                var r when r.StartsWith("race1") || r.Contains("fei") => "asteroid.race1_fei",
                var r when r.StartsWith("race2") || r.Contains("gaal") => "asteroid.race2_gaal",
                var r when r.StartsWith("race3") || r.Contains("mal")  => "asteroid.race3_mal",
                var r when r.StartsWith("race4") || r.Contains("pel")  => "asteroid.race4_pel",
                _ => "asteroid.default"
            };
            string text = NewsTexts.Format(tplKey,
                ("planetName", planetName), ("shooterName", shooterName), ("reward", reward));
            Post(CAT_ASTEROID, text);
        }

        // Смерть корабля — реагируем на партнёров игрока (DeadShip.Partner).
        // TODO(«знакомые корабли»): расширить на PlayerShipData.KnownContacts.
        private static void OnShipDestroyed(ShipData victim, ShipData killer, string cause)
        {
            if (victim == null || victim.IsPlayer) return;
            var player = GameWorld.Player?.PlayerShipData;
            if (player == null) return;
            bool isPartner = victim.PartnerLeaderUid == player.Uid || player.PartnerLeaderUid == victim.Uid;
            if (!isPartner) return;
            string starName = victim.CurrentStar?.Name ?? "неизвестная система";
            string victimName = string.IsNullOrEmpty(victim.Name) ? "ваш партнёр" : victim.Name;
            Post(CAT_DEAD_SHIP, NewsTexts.Format("ship_lost.partner",
                ("starName", starName), ("victimName", victimName)));
        }
    }
}
