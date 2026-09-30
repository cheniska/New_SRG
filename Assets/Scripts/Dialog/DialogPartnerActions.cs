using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using SRG.Galaxy;
using SRG.Ships;
using SRG.Ships.Services;
using SRG.Simulation;

namespace SRG.Dialog
{
    /// <summary>
    /// Регистрирует dialog actions и tags для системы партнёрства.
    /// Ставится из <see cref="DialogModules"/> вместе с остальными модулями диалогов.
    ///
    /// Действия:
    ///   PartnerOfferInit          — ctx.Data["partner_offer"] = стартовая цена (Preview → fee, если Reason=Accepted; иначе minFee).
    ///   PartnerOfferLess          — /2
    ///   PartnerOfferMore          — ×2 (кэп по деньгам игрока)
    ///   PartnerTryHire            — вызов PartnerService.TryHire; сохраняет reason/fee в ctx.Data.
    ///   PartnerBreak              — PlayerDismiss текущего target.
    ///   PartnerOrderFlyToMe       — ставит target.PartnerOrder = FlyToMe
    ///   PartnerOrderAttack &lt;uid&gt; — ставит Attack
    ///   PartnerOrderLandOn &lt;uid&gt; — LandOn (планета)
    ///   PartnerOrderFlyToStar &lt;uid&gt; — FlyToStar
    ///   PartnerOrderForAll &lt;kind&gt; &lt;uid&gt; — тот же приказ всем партнёрам в текущей звезде
    ///
    /// Теги:
    ///   {partner_offer}           — текущее предложение (ctx.Data)
    ///   {partner_fee}             — цена, за которую цель согласилась/попросила
    ///   {partner_refusal}         — код причины отказа (Accepted/AlreadyHasLeader/…)
    ///   {is_hireable}             — 1 если target.ShipTypeId в whitelist, иначе 0
    ///   {is_partner_of_player}    — 1 если TargetShip.PartnerLeaderUid == Player.Uid
    ///   {max_partners}            — MaxPartners(Player)
    ///   {current_partners}        — Player.PartnerFollowerUids.Count
    ///   {partners_in_star}        — сколько партнёров игрока сейчас в текущей звезде
    /// </summary>
    public static class DialogPartnerActions
    {
        private const string OFFER_KEY   = "partner_offer";
        private const string FEE_KEY     = "partner_fee";
        private const string REASON_KEY  = "partner_refusal";

        public static void RegisterDefaults()
        {
            RegisterBargain();
            RegisterActions();
            RegisterTags();
        }

        // ── Торг за наём ─────────────────────────────────────────────────────────
        // Общая механика (Init/Less/More/Commit + теги суммы и вердикта) живёт в
        // DialogBargain; здесь только то, чем наём отличается от выкупа и вымогательства.

        private static void RegisterBargain()
        {
            DialogBargain.Register(new BargainSpec
            {
                InitAction   = "PartnerOfferInit",
                LessAction   = "PartnerOfferLess",
                MoreAction   = "PartnerOfferMore",
                CommitAction = "PartnerTryHire",

                OfferKey  = OFFER_KEY, FeeKey = FEE_KEY, ReasonKey = REASON_KEY,
                OfferTag  = "partner_offer",
                FeeTag    = "partner_fee",
                ReasonTag = "partner_refusal",
                VerdictTag = "partner_offer_verdict",

                // Потолка по кошельку нет: игрок волен пообещать больше, чем у него есть.
                // Нехватку денег обнаружит сам найм (Preview шаг 10 / TryHire) — торг об этом
                // не знает, иначе пилот «подсматривал» бы чужой счёт.
                CapMoreByPlayerMoney = false,

                // Цену называет пилот, а не кошелёк игрока: берём его запрос и стартуем с запасом
                // (вдвое), чтобы торг имел смысл — цель принимает только offer >= fee, и без
                // запаса первое же «в 2 раза меньше» било бы в отказ. На итоговую цену это
                // не влияет: TryHire списывает рассчитанный fee, а не предложение.
                Seed = (target, player) =>
                {
                    int fee = PartnerService.CalcFee(target, player);
                    return fee > 0 ? fee * 2 : GuessMinFee(target);
                },

                // Вердикт по ТЕКУЩЕЙ сумме, без попытки нанять. Пересчитывается на рендер узла
                // торга, поэтому реплика NPC меняется сразу после «уменьшить»/«удвоить», а кнопка
                // «договорились» видна только когда цель действительно согласна.
                //   Accepted    — согласен за эту сумму
                //   OfferTooLow — мало, но торг имеет смысл
                //   остальное   — отказ не про деньги (характер, свита полна, уже нанят)
                //
                // Хватает ли денег у игрока — не предмет торга: пилот в чужой счёт не смотрит и
                // соглашается на устраивающую его сумму. Нехватка всплывёт при самом найме
                // (узел result). Иначе «мало» висело бы даже когда пилот всем доволен.
                Verdict = (target, player, offer) =>
                {
                    var reason = PartnerService.Preview(target, player, offer).Reason;
                    return reason == HireRefusalReason.NotEnoughMoney
                        ? nameof(HireRefusalReason.Accepted) : reason.ToString();
                },

                Commit = (target, player, offer) =>
                {
                    var r = PartnerService.TryHire(target, player, offer);
                    return (r.Reason.ToString(), r.Fee);
                },
            });
        }

        // ── Actions ──────────────────────────────────────────────────────────────

        private static void RegisterActions()
        {
            DialogService.RegisterAction("PartnerBreak", (ctx, args) =>
            {
                var target = ctx.TargetShip;
                if (target == null) return;
                PartnerService.Break(target, BreakReason.PlayerDismiss);
            });

            // Приказы. FlyToMe — «ко мне», аргументы не нужны.
            DialogService.RegisterAction("PartnerOrderFlyToMe", (ctx, args) =>
            {
                var target = ctx.TargetShip;
                if (target == null) return;
                if (!Obeys(ctx, target, "FlyToMe")) return;
                target.PartnerOrder = PartnerOrderKind.FlyToMe;
                target.PartnerOrderTargetUid = null;
            });

            DialogService.RegisterAction("PartnerOrderAttack", (ctx, args) =>
            {
                var target = ctx.TargetShip;
                if (target == null || args == null || args.Count == 0) return;
                target.PartnerOrder = PartnerOrderKind.Attack;
                target.PartnerOrderTargetUid = args[0];
            });

            DialogService.RegisterAction("PartnerOrderLandOn", (ctx, args) =>
            {
                var target = ctx.TargetShip;
                if (target == null || args == null || args.Count == 0) return;
                target.PartnerOrder = PartnerOrderKind.LandOn;
                target.PartnerOrderTargetUid = args[0];
            });

            DialogService.RegisterAction("PartnerOrderFlyToStar", (ctx, args) =>
            {
                var target = ctx.TargetShip;
                if (target == null || args == null || args.Count == 0) return;
                target.PartnerOrder = PartnerOrderKind.FlyToStar;
                target.PartnerOrderTargetUid = args[0];
            });

            // Дрон-специфика: приказ вернуться и упаковаться.
            DialogService.RegisterAction("DroneRequestReturn", (ctx, args) =>
            {
                var target = ctx.TargetShip;
                if (target == null) return;
                DroneService.RequestReturn(target);
            });

            // Партнёр сбрасывает весь свой груз (стеки) контейнерами рядом с собой.
            // Использование: команда «Выкинь груз» в диалоге управления партнёром/транклюкатором.
            DialogService.RegisterAction("PartnerDropCargo", (ctx, args) =>
            {
                var target = ctx.TargetShip;
                if (target?.Inventory == null || target.CurrentStar == null) return;
                var ids = new List<string>(target.Inventory.Stacks.Keys);
                int dropped = 0;
                foreach (var id in ids)
                {
                    if (!target.Inventory.Stacks.TryGetValue(id, out var st) || st == null || st.TotalWeight <= 0) continue;
                    var chunk = target.Inventory.TakeStack(id, st.TotalWeight);
                    if (chunk == null || chunk.TotalWeight <= 0) continue;
                    ContainerFactory.SpawnContainerWithStack(target, chunk, target.CurrentStar);
                    dropped += chunk.TotalWeight;
                }
                // От этого зависит ответная реплика: «выкину, хотя это грабёж» либо «нет у меня ничего».
                if (ctx.Data != null) ctx.Data[DROP_KEY] = dropped > 0 ? "1" : "0";
            });

            // Партнёр переходит в режим «патрулирование + подбор всего в радиусе захвата»
            // (флаг AutoPullActive — уже используется PickupSystem).
            DialogService.RegisterAction("PartnerSeekItems", (ctx, args) =>
            {
                var target = ctx.TargetShip;
                if (target == null) return;
                target.AutoPullActive = true;
            });

            DialogService.RegisterAction("PartnerStopSeek", (ctx, args) =>
            {
                var target = ctx.TargetShip;
                if (target == null) return;
                target.AutoPullActive = false;
            });

            // ── Разрешения подчинённого (подменю «Настроить поведение», Tranclucator.Options.*).
            // Тумблеры: реплика в диалоге показывает текущее состояние, клик его переключает.
            DialogService.RegisterAction("ToggleAllowCollect", (ctx, args) =>
            {
                var target = ctx.TargetShip;
                if (target == null) return;
                target.AllowCollect = !target.AllowCollect;
                // Запрет собирать снимает и уже включённый автоподбор — иначе флаг ни на что
                // не влияет до следующего выбора активности.
                if (!target.AllowCollect) target.AutoPullActive = false;
                // Реплики «Разрешаю/Запрещаю собирать <Item>» подставляют сюда название
                // категории — оно лежит в пуле Options.Collect («Артефакты»).
                ctx.Data["item_name"] = DialogService.PickFromPool("Tranclucator.Options.Collect") ?? "";
            });

            DialogService.RegisterAction("ToggleAllowLand", (ctx, args) =>
            {
                var target = ctx.TargetShip;
                if (target == null) return;
                target.AllowLand = !target.AllowLand;
                // Запрет посадок отменяет уже выданный приказ садиться.
                if (!target.AllowLand && target.PartnerOrder == PartnerOrderKind.LandOn)
                {
                    target.PartnerOrder = PartnerOrderKind.None;
                    target.PartnerOrderTargetUid = null;
                }
            });

            DialogService.RegisterAction("ToggleAllowArrange", (ctx, args) =>
            {
                var target = ctx.TargetShip;
                if (target == null) return;
                target.AllowArrange = !target.AllowArrange;
            });

            // OrderForAll: аргумент[0] = kind (FlyToMe/Attack/…), аргумент[1] = uid (для non-FlyToMe).
            DialogService.RegisterAction("PartnerOrderForAll", (ctx, args) =>
            {
                var player = ctx.PlayerShip;
                if (player == null || args == null || args.Count == 0) return;
                if (!System.Enum.TryParse<PartnerOrderKind>(args[0], out var kind)) return;
                string uidArg = args.Count > 1 ? args[1] : null;

                var star = player.CurrentStar;
                if (star == null) return;
                foreach (var uid in player.PartnerFollowerUids)
                {
                    var follower = star?.FindShip(uid);
                    if (follower == null || follower.CurrentHull <= 0) continue;
                    follower.PartnerOrder = kind;
                    follower.PartnerOrderTargetUid = kind == PartnerOrderKind.FlyToMe ? null : uidArg;
                }
            });
        }

        /// <summary>Ключ в ctx.Data: подчинился ли партнёр последнему приказу («1»/«0»).
        /// Узлы подтверждения в диалоге выбирают по нему реплику согласия либо отказа
        /// (<c>Partner/Pirate.ComputerAgree*</c> против <c>ComputerDisagree*</c>).</summary>
        private const string OBEY_KEY = "order_obeyed";
        /// <summary>Ключ в ctx.Data: полное имя пула с ответом на последний приказ. Пишет действие,
        /// исполняющее приказ; читает тег <c>{order_ack}</c>, который стоит текстом узла-ответа.
        /// Так реплика NPC остаётся репликой NPC, а не кнопкой игрока.</summary>
        private const string ACK_KEY = "order_ack_pool";
        /// <summary>Ключ в ctx.Data: «1», если по приказу реально что-то выброшено из трюма.</summary>
        private const string DROP_KEY = "order_dropped_cargo";

        /// <summary>Проверка дисциплины перед приказом. Дрон — механизм, он не спорит;
        /// живой партнёр может отказаться (<see cref="DisciplineCheck.OrderKind.PartnerOrder"/>).
        /// Возвращает false — приказ не применяется, и диалог отвечает репликой отказа.
        /// Публичная обёртка — для навигационных приказов из <see cref="DialogNavActions"/>.</summary>
        public static bool ObeysOrder(DialogContext ctx, ShipData target, string order)
            => Obeys(ctx, target, order);

        /// <summary>Семейство пулов подчинённого: у пирата свой говор, у робота — свой.</summary>
        public static string BrandOf(ShipData ship)
            => IsDrone(ship) ? "Tranclucator"
             : string.Equals(ship?.ShipTypeId, "Pirate", System.StringComparison.OrdinalIgnoreCase)
                 ? "Pirate" : "Partner";

        /// <param name="order">Суффикс пула ответа: FlyToMe / FlyToStar / LandingToObject.
        /// Из него собирается <c>ComputerAgree…</c> либо <c>ComputerDisagree…</c>.</param>
        private static bool Obeys(DialogContext ctx, ShipData target, string order)
        {
            bool obey = IsDrone(target) || SRG.NpcAI.DisciplineCheck.ShouldObey(target?.Personality,
                            SRG.NpcAI.DisciplineCheck.OrderKind.PartnerOrder);
            if (ctx?.Data == null) return obey;
            ctx.Data[OBEY_KEY] = obey ? "1" : "0";
            if (!string.IsNullOrEmpty(order))
            {
                string brand = BrandOf(target);
                // Реплик отказа нет ни у вольного пилота, ни у робота — у них это «мне не до тебя».
                ctx.Data[ACK_KEY] = obey
                    ? $"{brand}.ComputerAgree{order}"
                    : (brand == "Pirate" ? $"Pirate.ComputerDisagree{order}" : $"{brand}.ComputerInFear");
            }
            return obey;
        }

        private static bool IsDrone(ShipData ship)
        {
            var types = GameWorld.Context?.Config?.Ships?.ShipTypes;
            if (ship == null || types == null || string.IsNullOrEmpty(ship.ShipTypeId)) return false;
            return types.TryGetValue(ship.ShipTypeId, out var cfg) && cfg.IsDrone;
        }

        // ── Tags ─────────────────────────────────────────────────────────────────

        private static void RegisterTags()
        {
            // Суммы, счёт, причина отказа и вердикт торга регистрируются фабрикой (RegisterBargain).
            DialogService.RegisterDataTag("order_obeyed", OBEY_KEY, "1");

            // Ответ подчинённого на приказ — текстом узла (см. ACK_KEY).
            // volatile: берёт случайную строку из пула.
            DialogService.RegisterTag("order_ack", ctx =>
            {
                string key = ctx.ReadStr(ACK_KEY);
                return string.IsNullOrEmpty(key) ? null : DialogActions.PickPoolLine(key);
            }, isVolatile: true);

            // Ответ на «выкини груз»: было чего выкидывать или трюм пуст.
            DialogService.RegisterTag("dropcargo_ack", ctx =>
            {
                bool had = ctx.ReadStr(DROP_KEY) == "1";
                return DialogActions.PickPoolLine(
                    $"{BrandOf(ctx?.TargetShip)}.ComputerDropCargo{(had ? "Ok" : "No")}");
            }, isVolatile: true);

            // Разрешения подчинённого — состояние тумблеров подменю «Настроить поведение».
            DialogService.RegisterTag("drone_allow_collect", ctx => ctx?.TargetShip?.AllowCollect != false ? "1" : "0");
            DialogService.RegisterTag("drone_allow_land",    ctx => ctx?.TargetShip?.AllowLand    != false ? "1" : "0");
            DialogService.RegisterTag("drone_allow_arrange", ctx => ctx?.TargetShip?.AllowArrange != false ? "1" : "0");

            // <List> стоит и в «Разрешено собирать: <List>», и в «Разрешено садиться на: <List>»,
            // поэтому формулировка должна читаться в обеих. Категорий у нас нет — собирается и
            // садится на что угодно, отсюда «всё»/«ничего» (обе фразы — в текст-конфиге).
            DialogService.RegisterTag("drone_allow_list", ctx =>
                DialogTexts.Phrase(ctx?.TargetShip?.AllowCollect != false
                    ? "drone_allow_list_all" : "drone_allow_list_none"));
            // <Land> в «Разрешаю садиться на <Land>» — название категории из пула Options.Land
            // («Планеты»); нет пула — обобщённая формулировка из текст-конфига.
            // volatile: значение приходит из пула.
            DialogService.RegisterTag("drone_land_target", _ =>
                DialogService.PickFromPool("Tranclucator.Options.Land")
                ?? DialogTexts.Phrase("drone_land_target_any"), isVolatile: true);

            // Имя вольного пилота, который уже нанял цель («меня уже нанял вольный пилот …» — <Partner>
            // в строках пула Partner.AlreadyHavePartner).
            //
            // Порядок поиска — по возрастанию цены: игрок → текущая звезда (партнёр обычно
            // держится рядом с лидером) → полный обход галактики. Последний вариант кэшируется
            // в ctx.Data, потому что тег резолвится на каждый рендер узла, а обход — O(N) по
            // всем кораблям. Не нашли — тег пуст, фразу закроет TextsConfig.Dialog.TagDefaults.
            DialogService.RegisterTag("partner_leader_name", ctx =>
            {
                var target = ctx?.TargetShip;
                string uid = target?.PartnerLeaderUid;
                if (string.IsNullOrEmpty(uid)) return null;
                if (ctx.PlayerShip != null && ctx.PlayerShip.Uid == uid) return ctx.PlayerShip.Name;

                var inStar = target.CurrentStar?.FindShip(uid);
                if (inStar != null) return inStar.Name;

                const string cacheKey = "_partner_leader_name";
                if (ctx.Data.TryGetValue(cacheKey, out var cached)) return cached;
                string name = PartnerService.FindShipInGalaxy(uid)?.Name ?? "";
                ctx.Data[cacheKey] = name;
                return name;
            });

            // Срок контракта в месяцах (<Month> в строках пулов Partner/Pirate). Берётся из
            // ContractTermYears типа корабля; при бессрочном контракте (-1) значения нет —
            // тег вернёт пусто, и фразу подхватит TextsConfig.Dialog.TagDefaults.
            DialogService.RegisterTag("partner_months", ctx =>
            {
                var t = ctx?.TargetShip;
                var cfg = GameWorld.Context?.Config?.Partners;
                if (t == null || cfg?.PartnerableShipTypes == null) return null;
                if (!cfg.PartnerableShipTypes.TryGetValue(t.ShipTypeId ?? "", out var typeCfg)) return null;
                return typeCfg.ContractTermYears > 0
                    ? (typeCfg.ContractTermYears * 12).ToString(CultureInfo.InvariantCulture)
                    : null;
            });

            DialogService.RegisterTag("is_hireable", ctx =>
            {
                var t = ctx?.TargetShip;
                var cfg = GameWorld.Context?.Config?.Partners;
                if (t == null || cfg == null) return "0";
                return cfg.PartnerableShipTypes != null
                    && cfg.PartnerableShipTypes.ContainsKey(t.ShipTypeId ?? "") ? "1" : "0";
            });

            DialogService.RegisterTag("is_drone", ctx =>
            {
                var t = ctx?.TargetShip;
                var types = GameWorld.Context?.Config?.Ships?.ShipTypes;
                if (t == null || types == null || string.IsNullOrEmpty(t.ShipTypeId)) return "0";
                return types.TryGetValue(t.ShipTypeId, out var cfg) && cfg.IsDrone ? "1" : "0";
            });

            DialogService.RegisterTag("is_partner_of_player", ctx =>
            {
                var (t, p) = ctx.Pair();
                if (t == null || p == null) return "0";
                return t.PartnerLeaderUid == p.Uid ? "1" : "0";
            });

            DialogService.RegisterTag("max_partners", ctx =>
                ctx?.PlayerShip == null ? "0" : PartnerService.MaxPartners(ctx.PlayerShip).ToString());

            DialogService.RegisterTag("current_partners", ctx =>
                (ctx?.PlayerShip?.PartnerFollowerUids?.Count ?? 0).ToString());

            DialogService.RegisterTag("partners_in_star", ctx =>
            {
                var p = ctx?.PlayerShip;
                var star = p?.CurrentStar;
                if (p == null || star == null || p.PartnerFollowerUids == null) return "0";
                int n = 0;
                foreach (var uid in p.PartnerFollowerUids)
                {
                    var f = star?.FindShip(uid);
                    if (f != null && f.CurrentHull > 0) n++;
                }
                return n.ToString();
            });
        }

        // ── Utils ────────────────────────────────────────────────────────────────

        private static int GuessMinFee(ShipData target)
        {
            int fallback = GameWorld.Context?.Config?.Dialogs?.Tuning?.PartnerMinFeeFallback ?? 1000;
            var cfg = GameWorld.Context?.Config?.Partners;
            if (cfg == null) return fallback;
            if (!cfg.PartnerableShipTypes.TryGetValue(target.ShipTypeId ?? "", out var t)) return fallback;
            return Mathf.RoundToInt(cfg.Global.BasePrice * t.PriceMultiplier * cfg.Global.MinFeeRatio);
        }
    }
}
