using System.Collections.Generic;
using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Galaxy.Politics;
using SRG.NpcAI.Orders;
using SRG.Ships.Services;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.NpcAI.Actions
{
    /// <summary>
    /// AI-грабёж груза: пират сначала выводит цель из строя (ActionDisable), затем через
    /// <see cref="CargoRobberyService.TryRob"/> заставляет её сбросить контейнеры и
    /// подбирает их <see cref="PickupSystem"/>-очередью через собственный CargoGrabber.
    ///
    /// Фазы:
    ///  <list type="bullet">
    ///   <item><b>Disable</b> — стреляет по цели пока HP не упадёт до 25% (общий путь с ActionDisable).</item>
    ///   <item><b>Rob</b> — один тик: вызов CargoRobberyService.TryRob. Если Accepted — контейнеры
    ///         добавлены в PulledQueue грабителя, переход к Loot. Если Refused — LowerToLevel(Hostile)
    ///         + ForceActivity в ActionPursueAndAttack (пункт D требований).</item>
    ///   <item><b>Loot</b> — ждём, пока все свои контейнеры не подобраны/потеряны, но не дольше
    ///         <see cref="LootTimeoutTurns"/> ходов. Подбор выполняет глобальный
    ///         <see cref="PickupSystem.ProcessPullQueueSubturn"/>, который тикается основным loop'ом.</item>
    ///  </list>
    /// Отсутствие CargoGrabber у грабителя не блокирует Rob-фазу (жертва всё равно скидывает),
    /// но Loot-фаза сразу истекает — контейнеры остаются в системе для любого желающего.
    /// </summary>
    public class ActionRob : NpcAction
    {
        private const int LootTimeoutTurns = 12;

        private readonly string _targetUid;
        private enum Phase { Disable, Rob, Loot }
        private Phase _phase = Phase.Disable;

        private ActionDisable _disableAction;
        private List<string> _myContainerUids;
        private OrderLoot _currentLootOrder;
        private string _currentLootUid;
        private int _lootStartTurn;
        // Открывали ли уже входящий диалог игроку — иначе будем спамить каждый тик.
        private bool _incomingRequested;

        public string TargetShipUid => _targetUid;
        public override string CombatTargetUid => _phase == Phase.Disable ? _targetUid : null;

        public ActionRob(string targetUid) => _targetUid = targetUid;

        public override bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            ShipData target = FindShip(star, _targetUid);
            if (target == null || target.CurrentHull <= 0)
            {
                // Цель мертва — фаза Loot всё равно нужна, если контейнеры уже сброшены.
                if (_phase == Phase.Loot) return TickLoot(ship, star, ctx);
                IsCompleted = true;
                return true;
            }

            switch (_phase)
            {
                case Phase.Disable:
                    if (_disableAction == null) _disableAction = new ActionDisable(_targetUid, 0.25f);
                    bool disableDone = _disableAction.Tick(ship, star, ctx);
                    if (disableDone || target.CurrentHull <= 0)
                        _phase = Phase.Rob;
                    break;

                case Phase.Rob:
                    return TickRob(ship, target, star);

                case Phase.Loot:
                    return TickLoot(ship, star, ctx);
            }

            return false;
        }

        private bool TickRob(ShipData ship, ShipData target, StarData star)
        {
            // Цель — игрок: открываем входящий диалог. Пока он активен — ждём выбора игрока.
            // Диалог сам вызывает эффекты (сброс груза / атаку) и сбрасывает Brain пирата;
            // после закрытия диалога action завершается без вызова offline-TryRob (иначе
            // мог бы прогнать «жертва пассивна → контейнеры спавнятся» вторым слоем).
            if (target.IsPlayer)
            {
                if (!_incomingRequested)
                {
                    _incomingRequested = true;
                    var did = SRG.Dialog.DialogService.ResolveIncomingDialogId(ship, "rob");
                    var ui  = GameWorld.Dialogs;
                    if (!string.IsNullOrEmpty(did) && ui != null && ui.OpenIncomingSpaceDialog(did, ship))
                        return false; // ждём выбора игрока
                    // Не удалось открыть (планета/диалог занят/конфига нет) —
                    // fallback: пират сразу переходит в атаку.
                    Relations.LowerToLevel(ship, target, RelationLevel.Hostile);
                    ship.Brain?.ForceActivity(new ActionPursueAndAttack(target.Uid, limitEngageRange: false));
                    IsCompleted = true;
                    return true;
                }
                // Диалог был открыт ранее. Ждём его закрытия; после — завершаемся
                // (диалог уже применил эффекты через DialogIncomingRobActions).
                if (SRG.Dialog.DialogService.IsActive) return false;
                IsCompleted = true;
                return true;
            }

            var result = CargoRobberyService.TryRob(target, ship);
            if (result.Accepted)
            {
                _myContainerUids = result.ContainerUids ?? new List<string>();
                _lootStartTurn = GameWorld.GeneratedGalaxy?.CurrentTurn ?? 0;
                _phase = Phase.Loot;
                Debug.Log($"[ActionRob] {ship.Name} → {target.Name}: сброшено {result.Containers} контейнер(ов) ({result.Weight} вес). CrimeRating={ship.CrimeRating:F0}");
                string tType = NpcConversationLog.PoolTypeOf(target);
                NpcConversationLog.Post(ship, target,
                    "Goods.Send",        "Груз сюда — и катись.",
                    $"Goods.{tType}Ok",  $"Забирай {result.Weight} тонн, только не стреляй.");
                return false;
            }

            // Пункт D: жертва отказала — падаем в атаку.
            Relations.LowerToLevel(ship, target, RelationLevel.Hostile);
            Debug.Log($"[ActionRob] {ship.Name} → {target.Name}: отказ ({result.Reason}), переходим в атаку.");
            string targetType = NpcConversationLog.PoolTypeOf(target);
            string refuseKey = result.Reason == CargoRobRefusalReason.LongDistance
                ? $"Goods.{targetType}LongDistance"
                : result.Reason == CargoRobRefusalReason.WeAlreadyHavePact
                    ? "Goods.WeAlreadyHavePact"
                    : result.Reason == CargoRobRefusalReason.NoCargo
                        ? "Goods.AnswerNotGoods"
                        : $"Goods.{targetType}No";
            NpcConversationLog.Post(ship, target,
                "Goods.Send", "Груз сюда — и катись.",
                refuseKey,    "Ничего не отдам. Стреляй, если хватит духу.");
            ship.Brain?.ForceActivity(new ActionPursueAndAttack(target.Uid, limitEngageRange: false));
            IsCompleted = true;
            return true;
        }

        private bool TickLoot(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            int currentTurn = GameWorld.GeneratedGalaxy?.CurrentTurn ?? _lootStartTurn;
            if (currentTurn - _lootStartTurn >= LootTimeoutTurns) { IsCompleted = true; return true; }
            if (_myContainerUids == null || _myContainerUids.Count == 0) { IsCompleted = true; return true; }
            if (star == null) { IsCompleted = true; return true; }

            // Идём по контейнерам по одному через OrderLoot: летим к позиции ближайшего живого.
            // Когда OrderLoot завершается (дошли до PickupRadiusSq), либо контейнер исчез (подобран
            // магнитом CargoGrabber'а параллельно с движением) — берём следующий из своего списка.
            var current = !string.IsNullOrEmpty(_currentLootUid) ? star.FindShip(_currentLootUid) : null;
            if (current == null || current.CurrentHull <= 0)
            {
                _currentLootUid = null;
                _currentLootOrder = null;
                current = FindNextLiveContainer(star);
                if (current == null) { IsCompleted = true; return true; }
                _currentLootUid = current.Uid;
                _currentLootOrder = new OrderLoot(current.Position);
            }
            else
            {
                // Цель могла сместиться (контейнеры инертны, но всё равно обновим цель).
                _currentLootOrder = new OrderLoot(current.Position);
            }

            _currentLootOrder.Execute(ship, star, ctx);
            return false;
        }

        private ShipData FindNextLiveContainer(StarData star)
        {
            for (int i = 0; i < _myContainerUids.Count; i++)
            {
                var c = star.FindShip(_myContainerUids[i]);
                if (c != null && c.CurrentHull > 0) return c;
            }
            return null;
        }

        public override string DebugName => $"Rob({SpriteUtility.ShortId(_targetUid)},phase={_phase})";
    }
}
