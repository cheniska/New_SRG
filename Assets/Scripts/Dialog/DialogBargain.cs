using System;
using UnityEngine;
using SRG.Galaxy;

namespace SRG.Dialog
{
    /// <summary>
    /// Общая механика «торга суммой»: узел показывает названную сумму, игрок двигает её вдвое
    /// вниз/вверх, реплика собеседника пересчитывается сразу, и отдельным действием сделка
    /// закрывается. По этой схеме работают три ветки — наём партнёра, выкуп за перемирие и
    /// вымогательство денег, — которые до этого держали три почти дословные копии.
    ///
    /// <para>Разница между ветками сведена к полям <see cref="BargainSpec"/>: чем засевается сумма,
    /// каким Preview считается вердикт, ограничен ли рост кошельком игрока и как трактуется
    /// «у игрока не хватает денег».</para>
    ///
    /// <para>Правила, которые фабрика делает структурными, — раньше каждое было отдельным
    /// исправленным багом в каждой из трёх копий:</para>
    /// <list type="bullet">
    ///   <item><b>Init идемпотентен.</b> Узел торга возвращает сам в себя, поэтому его
    ///   <c>OnEnter</c> отрабатывает после каждого шага. Безусловная перезапись откатывала бы
    ///   только что сделанное игроком изменение, и сумма «залипала».</item>
    ///   <item><b>Сумма не опускается ниже 1.</b> Деление вдвое иначе доводит торг до нуля,
    ///   из которого удвоение уже не выводит.</item>
    ///   <item><b>Вердикт считается по текущей сумме без совершения сделки</b> — тег зовёт
    ///   Preview, а не Try*, поэтому реакция собеседника видна сразу, а не после подтверждения.</item>
    /// </list>
    /// </summary>
    public sealed class BargainSpec
    {
        /// <summary>Имена действий ровно как в DialogsConfig.json — менять нельзя, на них ссылаются
        /// узлы (<c>PartnerOfferInit</c>, <c>TruceOfferLess</c>, <c>ExtortDemand</c>, …).</summary>
        public string InitAction, LessAction, MoreAction, CommitAction;

        /// <summary>Ключи в <c>ctx.Data</c>, где живут текущая сумма, итоговый счёт и код причины.</summary>
        public string OfferKey, FeeKey, ReasonKey;

        /// <summary>Имена тегов. <see cref="VerdictTag"/> — живой вердикт по текущей сумме;
        /// пустое имя означает «этой ветке тег не нужен».</summary>
        public string OfferTag, FeeTag, ReasonTag, VerdictTag;

        /// <summary>Что показывают теги, пока действие ничего не положило.</summary>
        public string OfferDefault = "0", FeeDefault = "0", ReasonDefault = "";

        /// <summary>Стартовая сумма. Вызывается только при отсутствии ключа (см. идемпотентность).</summary>
        public Func<ShipData, ShipData, int> Seed;

        /// <summary>Код реакции собеседника на сумму <c>offer</c>, без совершения сделки.</summary>
        public Func<ShipData, ShipData, int, string> Verdict;

        /// <summary>Совершить сделку: вернуть код причины и итоговый счёт.</summary>
        public Func<ShipData, ShipData, int, (string Reason, int Fee)> Commit;

        /// <summary>Ограничивать ли рост суммы деньгами игрока. Верно для выкупа (игрок платит
        /// сам и не может пообещать больше, чем у него есть в момент передачи) и неверно для найма:
        /// там сумма — это обещание, а нехватку обнаружит уже сам найм.</summary>
        public bool CapMoreByPlayerMoney;

        /// <summary>Требуется ли игрок для засева суммы. У вымогательства сумму называет сам
        /// вымогатель по возможностям цели, игрок в расчёте не участвует.</summary>
        public bool SeedNeedsPlayer = true;
    }

    /// <summary>Регистратор торга по описанию (<see cref="BargainSpec"/>).</summary>
    public static class DialogBargain
    {
        public static void Register(BargainSpec s)
        {
            if (s == null) return;

            DialogService.RegisterAction(s.InitAction, (ctx, _) =>
            {
                var (target, player) = ctx.Pair();
                if (target == null || (s.SeedNeedsPlayer && player == null)) return;
                // Идемпотентность: узел торга самовозвратный, повторный вход не должен
                // затирать сумму, которую игрок только что подвинул.
                if (ctx.Data.ContainsKey(s.OfferKey)) return;
                ctx.WriteInt(s.OfferKey, Mathf.Max(1, s.Seed(target, player)));
            });

            DialogService.RegisterAction(s.LessAction, (ctx, _) =>
                ctx.WriteInt(s.OfferKey, Mathf.Max(1, ctx.ReadInt(s.OfferKey) / 2)));

            DialogService.RegisterAction(s.MoreAction, (ctx, _) =>
            {
                int next = ctx.ReadInt(s.OfferKey) * 2;
                if (s.CapMoreByPlayerMoney)
                {
                    var player = ctx?.PlayerShip;
                    if (player != null && next > player.Money) next = player.Money;
                }
                ctx.WriteInt(s.OfferKey, Mathf.Max(1, next));
            });

            DialogService.RegisterAction(s.CommitAction, (ctx, _) =>
            {
                var (target, player) = ctx.Pair();
                if (target == null || player == null) return;
                var (reason, fee) = s.Commit(target, player, ctx.ReadInt(s.OfferKey));
                ctx.Data[s.ReasonKey] = reason;
                ctx.WriteInt(s.FeeKey, fee);
            });

            DialogService.RegisterDataTag(s.OfferTag,  s.OfferKey,  s.OfferDefault);
            DialogService.RegisterDataTag(s.FeeTag,    s.FeeKey,    s.FeeDefault);
            DialogService.RegisterDataTag(s.ReasonTag, s.ReasonKey, s.ReasonDefault);

            if (!string.IsNullOrEmpty(s.VerdictTag))
                DialogService.RegisterTag(s.VerdictTag, ctx =>
                {
                    var (target, player) = ctx.Pair();
                    if (target == null || player == null) return "";
                    return s.Verdict(target, player, ctx.ReadInt(s.OfferKey));
                });
        }
    }
}
