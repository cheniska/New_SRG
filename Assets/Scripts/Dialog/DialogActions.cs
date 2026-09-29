using System;
using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Core;
using SRG.Dialog.PlanetGreetings;
using SRG.Economy;
using SRG.Scripting;
using SRG.Ships;
using SRG.Ships.Player;
using SRG.UI.Screens;

namespace SRG.Dialog
{
    // Базовый набор действий и тегов подстановки, доступных из реплик диалогов.
    // Регистрируется автоматически при первом старте DialogUIController.
    //
    // Чтобы добавить свои методы — вызовите DialogService.RegisterAction("MyMethod", handler)
    // или DialogService.RegisterTag("my_var", ctx => ...) из любого MonoBehaviour (например, в Start()).
    public static class DialogActions
    {
        public static void RegisterDefaults()
        {
            RegisterDefaultTags();
            RegisterCompositeTags();
            RegisterPluralTag();
            RegisterLuaTag();

            // Управление диалогом
            DialogService.RegisterAction("EndDialog", (ctx, args) => ctx.ShouldClose = true);

            // Лог в игровую консоль
            DialogService.RegisterAction("Log", (ctx, args) =>
            {
                if (args == null || args.Count == 0) return;
                GameConsoleController.AddEntry(string.Join(" ", args));
            });

            // Деньги
            DialogService.RegisterAction("AddMoney", (ctx, args) =>
            {
                if (ctx.PlayerShip == null || args.Count == 0) return;
                if (int.TryParse(args[0], out int amount)) ctx.PlayerShip.Money += amount;
            });

            DialogService.RegisterAction("TakeMoney", (ctx, args) =>
            {
                if (ctx.PlayerShip == null || args.Count == 0) return;
                if (int.TryParse(args[0], out int amount))
                    ctx.PlayerShip.Money = Mathf.Max(0, ctx.PlayerShip.Money - amount);
            });

            // Корпус
            DialogService.RegisterAction("Repair", (ctx, args) =>
            {
                if (ctx.PlayerShip == null || args.Count == 0) return;
                if (int.TryParse(args[0], out int hp))
                    ctx.PlayerShip.CurrentHull = Mathf.Min(ctx.PlayerShip.MaxHull,
                                                          ctx.PlayerShip.CurrentHull + hp);
            });

            DialogService.RegisterAction("Damage", (ctx, args) =>
            {
                if (ctx.PlayerShip == null || args.Count == 0) return;
                if (int.TryParse(args[0], out int dmg))
                    ctx.PlayerShip.CurrentHull = Mathf.Max(0, ctx.PlayerShip.CurrentHull - dmg);
            });

            // Произвольные переменные диалога (ctx.Data)
            DialogService.RegisterAction("SetVar", (ctx, args) =>
            {
                if (args.Count < 2) return;
                ctx.Data[args[0]] = args[1];
            });

            // Покинуть планету
            DialogService.RegisterAction("LeavePlanet", (ctx, args) =>
            {
                ctx.ShouldClose = true;
                PlayerManager.Instance?.LeavePlanet();
            });

            // Мгновенный ход (полезно для квестов на планете, ускоряющих время)
            DialogService.RegisterAction("SkipTurn", (ctx, args) =>
            {
                GalaxyManager.Instance?.ExecuteInstantTurn();
            });

            // ── Навигация между диалогами ──────────────────────────────────
            // StartDialog(dialogId, [scope]) — заменяет текущий диалог на новый (без возврата).
            DialogService.RegisterAction("StartDialog", (ctx, args) =>
            {
                if (args == null || args.Count == 0) return;
                var scope = ParseScope(args.Count > 1 ? args[1] : null) ?? ctx?.Scope ?? DialogScope.Space;
                var player = ctx?.PlayerShip;
                var planet = ctx?.TargetPlanet;
                var target = ctx?.TargetShip;
                ctx.ShouldClose = true;
                DialogService.End();
                DialogService.Start(args[0], scope, player, planet, target);
            });

            // PushDialog(dialogId, [scope]) — открыть поддиалог; End внутри вернёт в текущий.
            DialogService.RegisterAction("PushDialog", (ctx, args) =>
            {
                if (args == null || args.Count == 0) return;
                var scope = ParseScope(args.Count > 1 ? args[1] : null);
                DialogService.Push(args[0], scope);
            });

            // PopDialog — закрыть текущий и вернуться к вызывающему (если стек пуст — обычный End).
            DialogService.RegisterAction("PopDialog", (ctx, args) => DialogService.Pop());

            // CloseAll — оборвать всю цепочку (родительские тоже).
            DialogService.RegisterAction("CloseAll", (ctx, args) => DialogService.EndAll());
        }

        private static DialogScope? ParseScope(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            if (System.Enum.TryParse<DialogScope>(raw, ignoreCase: true, out var s)) return s;
            return null;
        }

        // ──────────────────────────────────────────────────────────────────
        //  {lua:expr} — Lua-выражение в тексте: "У вас {lua:Player().Money} кр."
        //  expr исполняется в CoreScript с локалами ctx/player/target/planet.
        //  Результат стрингифицируется через LuaHost.Format. При ошибке — пусто (сработает fallback).
        // ──────────────────────────────────────────────────────────────────
        private static void RegisterLuaTag()
        {
            DialogService.RegisterParamTag("lua", (ctx, expr) =>
            {
                if (string.IsNullOrEmpty(expr)) return "";
                try
                {
                    // EvalCached — тег резолвится на каждый рендер узла (см. DialogService.BuildLuaEnv).
                    var result = LuaHost.EvalCached(expr, DialogService.BuildLuaEnv(ctx));
                    var text = LuaHost.Format(result);
                    return text == "nil" ? "" : text;
                }
                catch (MoonSharp.Interpreter.InterpreterException iex)
                { Debug.LogError($"[DialogActions] {{lua:{expr}}}: {iex.DecoratedMessage ?? iex.Message}"); return ""; }
                catch (Exception e)
                { Debug.LogError($"[DialogActions] {{lua:{expr}}}: {e.Message}"); return ""; }
            });
        }

        // ──────────────────────────────────────────────────────────────────
        //  Базовые теги подстановки. Доступны в Text/Replies всех узлов:
        //     "Лечу с {last_planet} на {next_planet}"
        //  Если тег вернул пусто — значение берётся из TextsConfig.Dialog.TagDefaults
        //  (см. DialogTexts.ResolveDefault). Инлайновых фоллбэков в JSON быть не должно.
        // ──────────────────────────────────────────────────────────────────

        private static void RegisterDefaultTags()
        {
            // ── Цель диалога (корабль) ─────────────────────────────────────
            DialogService.RegisterTag("ship_name",  ctx => ctx?.TargetShip?.Name);
            // {speaker_name} — «кто говорит». Отделён от {ship_name} намеренно: в поле Speaker
            // дефолт — должность («Дежурный медик», Speakers[dialogId] в текст-конфиге),
            // а в тексте узла {ship_name} — собственное имя корабля/станции, и его дефолт пустой.
            // Раньше оба смысла сидели на одном теге и разъезжались инлайновыми фоллбэками.
            DialogService.RegisterTag("speaker_name", ctx => ctx?.TargetShip?.Name);
            DialogService.RegisterTag("ship_type",  ctx => ctx?.TargetShip?.ShipTypeId);
            DialogService.RegisterTag("ship_race",  ctx => ctx?.TargetShip?.Race);
            DialogService.RegisterTag("ship_owner", ctx => ctx?.TargetShip?.Owner);

            DialogService.RegisterTag("home_planet",    ctx => PlanetName(ctx?.TargetShip?.HomePlanetUid));
            DialogService.RegisterTag("home_star",      ctx => StarName(ctx?.TargetShip?.HomeStarUid));
            DialogService.RegisterTag("last_planet",    ctx => PlanetName(ctx?.TargetShip?.LastPlanetUid));
            DialogService.RegisterTag("last_star",      ctx => StarName(ctx?.TargetShip?.LastStarUid));
            DialogService.RegisterTag("current_planet", ctx =>
            {
                if (ctx?.TargetShip?.LandedPlanetUid != null)
                    return PlanetName(ctx.TargetShip.LandedPlanetUid);
                // «в космосе» — фраза, поэтому живёт в текст-конфиге, а не в Tuning
                // структурного конфига и не литералом здесь.
                return DialogTexts.Phrase("current_planet_in_space");
            });
            DialogService.RegisterTag("current_star",   ctx => StarName(ctx?.TargetShip?.CurrentStarUid));
            DialogService.RegisterTag("next_planet",    ctx => PlanetName(ctx?.TargetShip?.NextPlanetUid));
            DialogService.RegisterTag("next_star",      ctx => StarName(ctx?.TargetShip?.NextStarUid));

            // ── Груз ───────────────────────────────────────────────────────
            DialogService.RegisterTag("cargo_id", ctx =>
            {
                var (id, _) = CargoUtils.GetLargestCargo(ctx?.TargetShip);
                return id;
            });
            DialogService.RegisterTag("cargo_name", ctx =>
            {
                var (id, _) = CargoUtils.GetLargestCargo(ctx?.TargetShip);
                return string.IsNullOrEmpty(id) ? null : TradeSystem.GetDisplayName(id, GetCfg());
            });
            DialogService.RegisterTag("cargo_amount", ctx =>
            {
                var (_, amt) = CargoUtils.GetLargestCargo(ctx?.TargetShip);
                return amt > 0 ? amt.ToString() : null;
            });
            DialogService.RegisterTag("cargo_unit_price", ctx =>
            {
                var ship = ctx?.TargetShip;
                return ship != null && ship.TraderBuyPrice > 0 ? ship.TraderBuyPrice.ToString() : null;
            });
            DialogService.RegisterTag("cargo_total_value", ctx =>
            {
                var ship = ctx?.TargetShip;
                if (ship == null) return null;
                var (_, amt) = CargoUtils.GetLargestCargo(ship);
                if (amt <= 0 || ship.TraderBuyPrice <= 0) return null;
                return (amt * ship.TraderBuyPrice).ToString();
            });
            DialogService.RegisterTag("has_cargo", ctx => CargoUtils.HasAnyCargo(ctx?.TargetShip) ? "1" : "0");
            // {has_cargo:goodId} — есть ли в трюме конкретный товар.
            DialogService.RegisterParamTag("has_cargo", (ctx, arg) =>
                CargoUtils.HasCargoOfType(ctx?.TargetShip, arg) ? "1" : "0");
            // {cargo_amount:goodId} — сколько единиц конкретного товара.
            DialogService.RegisterParamTag("cargo_amount", (ctx, arg) =>
            {
                int amt = CargoUtils.GetCargoAmount(ctx?.TargetShip, arg);
                return amt > 0 ? amt.ToString() : "0";
            });

            // ── Игрок ──────────────────────────────────────────────────────
            DialogService.RegisterTag("player_name",   ctx => ctx?.PlayerShip?.Name);
            DialogService.RegisterTag("player_money",  ctx => ctx?.PlayerShip?.Money.ToString());
            DialogService.RegisterTag("player_planet", ctx => PlanetName(ctx?.PlayerShip?.LandedPlanetUid));
            DialogService.RegisterTag("player_star",   ctx => StarName(ctx?.PlayerShip?.CurrentStarUid));

            // ── Глобальный контекст ────────────────────────────────────────
            DialogService.RegisterTag("turn", _ => GalaxyManager.Instance?.GeneratedGalaxy?.CurrentTurn.ToString());
            DialogService.RegisterTag("date", _ => GalaxyManager.Instance?.GeneratedGalaxy?.GetCurrentDate());

            // Контекст «где идёт разговор» — фильтруют реплики через Condition:
            //   "scope == \"Gov\""    — только правительство/мостик/командование
            //   "scope == \"Space\""  — только по радио из космоса
            // is_gov/is_space — сокращения (boolean-атомы), удобно в &&/||.
            DialogService.RegisterTag("scope",    ctx => ctx?.Scope.ToString());
            DialogService.RegisterTag("is_gov",   ctx => ctx?.Scope == DialogScope.Gov   ? "1" : "0");
            DialogService.RegisterTag("is_space", ctx => ctx?.Scope == DialogScope.Space ? "1" : "0");

            // Игрок «считается пиратом», если CrimeRating >= порога Tuning.PirateCrimeMin.
            // Используется в диалогах Ship_Pirate_default (найм в банду), Money/Goods-репликах.
            DialogService.RegisterTag("player_is_pirate", ctx =>
            {
                var player = ctx?.PlayerShip;
                if (player == null) return "0";
                float min = GetCfg()?.Dialogs?.Tuning?.PirateCrimeMin ?? 50f;
                return player.CrimeRating >= min ? "1" : "0";
            });

            // ── Приветствие правительства планеты ──────────────────────────
            // Выбирает правило из DialogsConfig.PlanetGreetings, подставляет <Player>/<CurPlanet>/...
            // и кэширует результат на диалог (ctx.Data["_greeting"]), чтобы повторный вход
            // в узел не менял текст. Пустая строка (нет подходящих правил) → fallback тега.
            DialogService.RegisterTag("planet_greeting", ctx =>
            {
                if (ctx == null || ctx.TargetPlanet == null) return null;
                const string key = "_greeting";
                if (ctx.Data.TryGetValue(key, out var cached)) return cached;
                var text = PlanetGreetingRenderer.Render(
                    ctx.PlayerShip, ctx.TargetPlanet, GetCfg(),
                    GalaxyManager.Instance?.GeneratedGalaxy);
                ctx.Data[key] = text ?? string.Empty;
                return text;
            });

            // ── Приветствие капитана корабля ───────────────────────────────
            DialogService.RegisterTag("ship_greeting", ctx =>
            {
                if (ctx == null || ctx.TargetShip == null) return null;
                const string key = "_ship_greeting";
                if (ctx.Data.TryGetValue(key, out var cached)) return cached;
                var text = PlanetGreetingRenderer.RenderShip(
                    ctx.PlayerShip, ctx.TargetShip, GetCfg(),
                    GalaxyManager.Instance?.GeneratedGalaxy);
                ctx.Data[key] = text ?? string.Empty;
                return text;
            });

            // ── Вариативные строки из общего пула (StringPools) ────────────
            // Синтаксис: {pool:Attack.<ShipType>Ok}. В arg-е поддерживается замена
            //   <ShipType>/<PlayerRace>/<ShipRace>/<Player>/<Ship>
            // из контекста, чтобы одним ключом было проще ссылаться на разные ship-типы.
            DialogService.RegisterParamTag("pool", (ctx, arg) =>
            {
                if (string.IsNullOrEmpty(arg)) return null;
                arg = arg
                    .Replace("<ShipType>",       ctx?.TargetShip?.ShipTypeId ?? "")
                    // <Race> — алиас <ShipRace>: в конфиге встречается ключ Gossip.<Race>Any,
                    // и без алиаса он не раскрывался вообще, т.е. расовые пулы было невозможно
                    // подключить — ключ навсегда оставался литералом и падал в PoolDefaults.
                    .Replace("<Race>",           ctx?.TargetShip?.Race       ?? "")
                    .Replace("<ShipRace>",       ctx?.TargetShip?.Race       ?? "")
                    .Replace("<DominatorClass>", DominatorClassOf(ctx?.TargetShip?.Race))
                    .Replace("<Ship>",           ctx?.TargetShip?.Name       ?? "")
                    .Replace("<PlayerRace>",     ctx?.PlayerShip?.Race       ?? "")
                    .Replace("<Player>",         ctx?.PlayerShip?.Name       ?? "");
                var pools = GetCfg()?.Dialogs?.StringPools;
                if (pools == null || !pools.TryGetValue(arg, out var list) || list == null || list.Count == 0)
                    return null;
                int idx = _poolRng.Next(list.Count);
                return ExpandPoolPlaceholders(list[idx], DialogService.CurrentVoice);
            // volatile: два {pool:…} в одном узле обязаны дать разные варианты, а memo-кэш
            // рендера вернул бы одну и ту же строку на оба.
            }, isVolatile: true);
        }

        // Плейсхолдеры <…> внутри самих строк пулов — наследие старого формата текстов. Раньше подменялся
        // только ключ пула, а возвращённая строка отдавалась как есть, и игрок видел литералы
        // «Гони <Money> cr» / «помоги расправиться с <Target>».
        //
        // Переводим их в обычные {теги} и отдаём наружу: Substitute резолвит вложенные теги
        // рекурсивно, поэтому вся плюмбинг-логика остаётся в существующих резолверах и не
        // дублируется здесь. Порядок Replace важен: длинные префиксы раньше коротких
        // (<FullShipBad> до <FullShip> до <ShipBad> до <Ship>, <ShipName> до <Ship>), иначе
        // короткий съест начало длинного и оставит хвост вида «Bad>».
        //
        // Таблица обязана покрывать ВСЕ плейсхолдеры, встречающиеся в пулах: непокрытый уедет
        // игроку сырым. Проверяется скриптом (см. docs/modules/dialog_system.md).
        private static readonly (string Placeholder, string Tag)[] _poolPlaceholders =
        {
            ("<HomePlanet>", "{home_planet}"),
            ("<FullShipBad>", "{third_party_name}"),
            ("<FullShip>",   "{ship_name}"),
            ("<ShipName>",   "{ship_name}"),
            ("<ShipBad>",    "{third_party_name}"),   // корабль, с которым NPC уже воюет
            ("<Ship>",       "{ship_name}"),
            ("<Partner>",    "{partner_leader_name}"),
            ("<Target>",     "{third_party_name}"),
            ("<Money>",      "{money_amount}"),
            ("<Month>",      "{partner_months}"),
            ("<ObjectName>", "{order_object_name}"),  // цель приказа о посадке
            ("<Star>",       "{order_star_name}"),    // цель приказа о гиперпрыжке
            ("<Item>",       "{item_name}"),
            ("<Cnt>",        "{trade_buy_limit}"),    // сколько ед. NPC ещё может купить
            ("<List>",       "{drone_allow_list}"),   // что дрону разрешено собирать/где садиться
            ("<Land>",       "{drone_land_target}"),
            ("<Name>",       "{dom_program_name}"),   // имя программы синтета
        };

        /// <summary>Случайная строка из пула с раскрытыми <c>&lt;…&gt;</c>-плейсхолдерами.
        /// Для кода, который сам выбирает нужную реплику (исходы, вводные фразы): результат ещё
        /// содержит <c>{теги}</c>, их дорезолвит рекурсия <see cref="DialogService.Substitute"/>.
        /// null — пула нет.</summary>
        public static string PickPoolLine(string poolKey)
        {
            var raw = DialogService.PickFromPool(poolKey);
            return string.IsNullOrEmpty(raw) ? null : ExpandPoolPlaceholders(raw, DialogService.CurrentVoice);
        }

        private static string ExpandPoolPlaceholders(string s, DialogVoice voice)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('<') < 0) return s;

            // <Player>/<Ranger> — обращение, и целятся они в СОБЕСЕДНИКА, а не в игрока:
            //   реплика NPC   «Ты не похож на лидера, <Ranger>»      → игрок
            //   реплика игрока «Иди под мое командование, <Ranger>»  → корабль-цель
            string addressee = voice == DialogVoice.Player ? "{ship_name}" : "{player_name}";
            s = s.Replace("<Ranger>", addressee).Replace("<Player>", addressee);

            for (int i = 0; i < _poolPlaceholders.Length; i++)
            {
                var (ph, tag) = _poolPlaceholders[i];
                if (s.IndexOf(ph, System.StringComparison.Ordinal) >= 0) s = s.Replace(ph, tag);
            }
            return s;
        }

        // ──────────────────────────────────────────────────────────────────
        //  Составные теги для <Money> и <Target> из строк пулов.
        //
        //  Одна и та же фраза пула переиспользуется в разных ветках, где «сумма» и «третья
        //  сторона» лежат под разными ключами (extort_demand / ransom_offer / partner_offer…).
        //  Поэтому пробуем ключи по порядку и берём первый существующий. Это безопасно: у
        //  каждого диалога свой ctx.Data (Push создаёт новый контекст), так что в один момент
        //  заполнены только ключи текущей ветки.
        //
        //  Читаем Data напрямую, а не через одноимённые теги: у тех непустые дефолты («0»),
        //  и «первый непустой» всегда цеплялся бы за первый кандидат в списке.
        // ──────────────────────────────────────────────────────────────────
        private static readonly string[] _moneyKeys =
        {
            "extort_demand",          // вымогательство (исходящее и входящее — общий ключ)
            "incoming_extort_fee",    // сумма, которую NPC заплатил игроку
            "extort_fee",
            "ransom_offer",           // выкуп, предложенный NPC за перемирие
            "truce_offer", "truce_fee",
            "partner_offer", "partner_fee",
            "protect_reward_amount",
        };

        private static readonly string[] _thirdPartyKeys =
        {
            "jatk_target_name",       // цель совместной атаки
            "protect_victim_name",    // тот, за кого игрок вступается
        };

        private static void RegisterCompositeTags()
        {
            DialogService.RegisterTag("money_amount", ctx => ctx.FirstPresent(_moneyKeys));

            // Теги-«контекст приказа»: значение кладёт то действие, которое приказ исполняет
            // (см. DialogNavActions/DialogShipTradeActions). Пока действие не отработало, тег пуст
            // и фразу закрывает TextsConfig.Dialog.TagDefaults — сырой {tag} игроку не уедет.
            // (drone_allow_list / drone_land_target регистрирует DialogPartnerActions — они
            // читаются не из Data, а прямо из флагов подчинённого.)
            foreach (var key in new[] { "order_object_name", "order_star_name", "item_name",
                                        "trade_buy_limit", "dom_program_name" })
                DialogService.RegisterDataTag(key, key);

            DialogService.RegisterTag("third_party_name", ctx =>
            {
                var known = ctx.FirstPresent(_thirdPartyKeys);
                if (!string.IsNullOrEmpty(known)) return known;
                // Ни одна ветка не назвала третью сторону — значит фраза про того, с кем NPC
                // уже воюет («помоги расправиться с …»). Берём его боевую цель.
                return CombatTargetName(ctx?.TargetShip);
            });
        }

        // ──────────────────────────────────────────────────────────────────
        //  {plural:N.Form} — русское склонение числительного.
        //
        //  Формы берутся из пулов Count.<0|1|2><Form>: 1Day=«день», 2Day=«дня», 0Day=«дней».
        //  Индекс по правилам русского: 1 (но не 11) → 1-я форма; 2..4 (но не 12..14) → 2-я;
        //  остальное → 0-я. Так «контейнер(ов)» в текстах заменяется на нормальную форму.
        //
        //  Синтаксис: {plural:5.Day} → «дней». Вместо числа можно указать имя тега — оно
        //  резолвится здесь же: «{cargo_rob_containers} {plural:cargo_rob_containers.Unit}».
        //  (Через вложенный {tag} это не сделать: имя параметр-тега вычисляется до подстановки.)
        // ──────────────────────────────────────────────────────────────────
        private static void RegisterPluralTag()
        {
            DialogService.RegisterParamTag("plural", (ctx, arg) =>
            {
                if (string.IsNullOrEmpty(arg)) return null;
                int dot = arg.LastIndexOf('.');
                if (dot <= 0 || dot == arg.Length - 1) return null;
                string form = arg[(dot + 1)..];
                string countRaw = arg[..dot];
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                if (!int.TryParse(countRaw, System.Globalization.NumberStyles.Integer, culture, out int n))
                {
                    // Не число — значит имя тега с числом.
                    var viaTag = DialogService.ResolveTag(countRaw, ctx);
                    if (!int.TryParse(viaTag, System.Globalization.NumberStyles.Integer, culture, out n))
                        return null;
                }

                n = System.Math.Abs(n) % 100;
                int last = n % 10;
                int idx = (n >= 11 && n <= 14) ? 0 : last == 1 ? 1 : (last >= 2 && last <= 4) ? 2 : 0;
                return DialogService.PickFromPool($"Count.{idx}{form}");
            // volatile: тянет строку из пула (Count.*), см. тег pool.
            }, isVolatile: true);
        }

        private static string CombatTargetName(SRG.Galaxy.ShipData ship)
        {
            string uid = ship?.Brain?.GetCombatTargetUid();
            if (string.IsNullOrEmpty(uid)) return null;
            var found = ship.CurrentStar?.FindShip(uid);
            return found?.Name;
        }

        // Отдельный RNG для пулов — чтобы независимые вызовы {pool:...} внутри одного узла
        // возвращали разные варианты.
        private static readonly System.Random _poolRng = new();

        /// <summary>Маппинг «раса → короткое имя класса» для подстановки <c>&lt;DominatorClass&gt;</c>
        /// в ключах StringPools. Приоритет: <see cref="DialogTuning.DominatorClassMap"/> (data-driven,
        /// произвольный набор рас, добавляемый только конфигом). Fallback: внутренние id серий
        /// (RaceDominators1..3 → Blazer/Keller/Terron; substring-match по Blazer/Keller/Terron).
        /// Пусто — возвращает исходное имя расы (для новых рас без пула деградирует через
        /// «|fallback» в самом теге <c>{pool:X|…}</c>).</summary>
        private static string DominatorClassOf(string race)
        {
            if (string.IsNullOrEmpty(race)) return "";
            var map = DialogTuning.Current?.DominatorClassMap;
            if (map != null && map.TryGetValue(race, out var mapped) && !string.IsNullOrEmpty(mapped))
                return mapped;
            switch (race)
            {
                case "RaceDominators1": return "Blazer";
                case "RaceDominators2": return "Keller";
                case "RaceDominators3": return "Terron";
            }
            if (race.IndexOf("Blazer", System.StringComparison.OrdinalIgnoreCase) >= 0) return "Blazer";
            if (race.IndexOf("Keller", System.StringComparison.OrdinalIgnoreCase) >= 0) return "Keller";
            if (race.IndexOf("Terron", System.StringComparison.OrdinalIgnoreCase) >= 0) return "Terron";
            return race;
        }

        private static string PlanetName(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
            if (galaxy?.PlanetsMap != null && galaxy.PlanetsMap.TryGetValue(uid, out var p) && p != null)
                return p.Name ?? uid;
            return uid;
        }

        private static string StarName(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
            if (galaxy?.StarsMap != null && galaxy.StarsMap.TryGetValue(uid, out var s) && s != null)
                return s.Name ?? uid;
            return uid;
        }

        private static GalaxyConfig GetCfg() => GalaxyManager.Instance?.Context?.Config;
    }
}
