using System.Collections.Generic;
using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.NpcAI;
using SRG.NpcAI.Actions;
using SRG.NpcAI.Orders;
using SRG.Ships.Services;

namespace SRG.Dialog
{
    /// <summary>
    /// Actions для ВХОДЯЩИХ ограблений (пират инициирует диалог с игроком).
    ///
    /// Роли инвертированы относительно исходящего грабежа:
    ///   PlayerShip = жертва (игрок)
    ///   TargetShip = агрессор-пират (инициатор диалога)
    ///
    /// Actions:
    ///   IncomingRob         — TryRob(player, pirate): игрок сбрасывает контейнеры, пират подберёт.
    ///                         Затем ResetActivity() у пирата — Rob-action завершается.
    ///   IncomingRobRefuse   — LowerToLevel(Hostile) в обе стороны + пират ForceActivity(PursueAndAttack).
    ///   IncomingExtortInit  — SeedDemand(player) в extort_demand.
    ///   IncomingExtort      — TryDemand(player, pirate, demand): списание с игрока пирату.
    ///   IncomingExtortRefuse — то же, что RobRefuse.
    ///
    /// Данные (для тегов реплик):
    ///   incoming_rob_reason / _weight / _containers / _fee / _reason (extort)
    ///
    /// Используется исходящими action'ами <see cref="ActionRob"/>/<see cref="ActionExtort"/>
    /// через <see cref="DialogUIController.OpenIncomingSpaceDialog"/>, когда цель — игрок.
    /// </summary>
    public static class DialogIncomingRobActions
    {
        private const string ROB_REASON_KEY    = "incoming_rob_reason";
        private const string ROB_WEIGHT_KEY    = "incoming_rob_weight";
        private const string ROB_COUNT_KEY     = "incoming_rob_containers";
        private const string EXT_DEMAND_KEY    = "extort_demand";        // тот же ключ, что и в исходящем — единый тег {extort_demand}
        private const string EXT_FEE_KEY       = "incoming_extort_fee";
        private const string EXT_REASON_KEY    = "incoming_extort_reason";
        private const string RANSOM_OFFER_KEY  = "ransom_offer";
        private const string RANSOM_TAKEN_KEY  = "ransom_taken";

        public static void RegisterDefaults()
        {
            // ── Rob: игрок сбрасывает груз пирату ─────────────────────────────
            DialogService.RegisterAction("IncomingRob", (ctx, _) =>
            {
                var player = ctx?.PlayerShip;
                var pirate = ctx?.TargetShip;
                if (player == null || pirate == null) return;
                // Роли инвертированы: жертва=player, грабитель=pirate.
                var res = CargoRobberyService.TryRob(player, pirate);
                ctx.Data[ROB_REASON_KEY] = res.Reason.ToString();
                ctx.WriteInt(ROB_WEIGHT_KEY, res.Weight);
                ctx.WriteInt(ROB_COUNT_KEY,  res.Containers);
                // Пират узнаёт: Rob-action у него ещё открыт (_incomingRequested=true),
                // и на следующем тике завершится по «диалог закрыт» — ResetActivity не нужен;
                // если бы был — пират перевыбрал бы Rob повторно (при наличии другого груза).
                // Но если игрок ответил, пират должен спокойно подобрать контейнеры (Loot-phase).
                // Проще всего — тоже завершить Rob-action и позволить Brain перевыбрать: пират
                // без груза-цели пойдёт в патруль, увидит контейнеры через глобальный PickupSystem.
                pirate.Brain?.ResetActivity();
            });

            // ── Rob: игрок отказал → пират в атаку ────────────────────────────
            DialogService.RegisterAction("IncomingRobRefuse", (ctx, _) =>
            {
                var player = ctx?.PlayerShip;
                var pirate = ctx?.TargetShip;
                if (player == null || pirate == null) return;
                Relations.LowerToLevel(player, pirate, RelationLevel.Hostile);
                pirate.Brain?.ForceActivity(new ActionPursueAndAttack(player.Uid, limitEngageRange: false));
            });

            // ── Extort: инициализация суммы ───────────────────────────────────
            DialogService.RegisterAction("IncomingExtortInit", (ctx, _) =>
            {
                var player = ctx?.PlayerShip;
                if (player == null) return;
                // Пират считает демандед по возможностям ЖЕРТВЫ (=игрока).
                int demand = ExtortionService.SeedDemand(player);
                ctx.WriteInt(EXT_DEMAND_KEY, demand);
            });

            // ── Extort: игрок платит ──────────────────────────────────────────
            DialogService.RegisterAction("IncomingExtort", (ctx, _) =>
            {
                var player = ctx?.PlayerShip;
                var pirate = ctx?.TargetShip;
                if (player == null || pirate == null) return;
                int demand = ctx.ReadInt(EXT_DEMAND_KEY);
                // Роли инвертированы: жертва=player, вымогатель=pirate.
                var result = ExtortionService.TryDemand(player, pirate, demand);
                ctx.Data[EXT_REASON_KEY] = result.Reason.ToString();
                ctx.WriteInt(EXT_FEE_KEY, result.Fee);
                pirate.Brain?.ResetActivity();
            });

            DialogService.RegisterAction("IncomingExtortRefuse", (ctx, _) =>
            {
                var player = ctx?.PlayerShip;
                var pirate = ctx?.TargetShip;
                if (player == null || pirate == null) return;
                Relations.LowerToLevel(player, pirate, RelationLevel.Hostile);
                pirate.Brain?.ForceActivity(new ActionPursueAndAttack(player.Uid, limitEngageRange: false));
            });

            // ── Incoming ceasefire (жертва-NPC просит игрока прекратить атаку, отдаёт часть груза) ──
            // Роли: PlayerShip = агрессор (игрок), TargetShip = NPC-жертва просящая мир.
            DialogService.RegisterAction("IncomingCeasefireAccept", (ctx, _) =>
            {
                var player = ctx?.PlayerShip;
                var npc    = ctx?.TargetShip;
                if (player == null || npc == null) return;
                // Передача части каждого стека — та же формула, что в OrderRequestCeasefire.TransferOffer.
                TransferCeasefireCargo(npc, player);
                // Прекращение вражды: снимаем свежие «обиды» с обеих сторон, поднимаем отношение
                // до нейтрального и отмечаем игрока как «союзника» в персональной памяти NPC.
                if (npc.LastAttackerUid    == player.Uid) npc.LastAttackerUid    = null;
                if (player.LastAttackerUid == npc.Uid)    player.LastAttackerUid = null;
                Relations.RaiseToLevel(npc, player, RelationLevel.Normal);
                Relations.RaiseToLevel(player, npc, RelationLevel.Normal);
                npc.Personality?.ApplyAllyBonus(player.Uid);
                CeasefireSolidarity.ApplyHomePlanetBonus(npc, player, npc.CurrentStar);
                npc.CrimeRating += 5f; // игрок формально «шантажировал» — небольшой рост крайма как в offline-механике
                npc.Brain?.ResetActivity();
            });

            DialogService.RegisterAction("IncomingCeasefireRefuse", (ctx, _) =>
            {
                // Игрок отказал — NPC-жертва не получает мир, её Brain на следующем тике снова
                // выберет действие (может попробовать ещё раз, или Flee/SeekShelter). Никаких
                // penalty игроку — он ничего не должен.
                var npc = ctx?.TargetShip;
                npc?.Brain?.ResetActivity();
            });

            // ── Incoming offer_money (NPC-жертва предлагает игроку взятку за прекращение атаки) ──
            // Роли: PlayerShip = агрессор (игрок), TargetShip = NPC-жертва с деньгами.
            DialogService.RegisterAction("IncomingOfferMoneyInit", (ctx, _) =>
            {
                var npc = ctx?.TargetShip;
                if (npc == null) return;
                int offer = CalcOfferAmount(npc);
                ctx.WriteInt(RANSOM_OFFER_KEY, offer);
            });

            DialogService.RegisterAction("IncomingOfferMoneyAccept", (ctx, _) =>
            {
                var player = ctx?.PlayerShip;
                var npc    = ctx?.TargetShip;
                if (player == null || npc == null) return;
                int offer = ctx.ReadInt(RANSOM_OFFER_KEY);
                int actual = Mathf.Min(offer, npc.Money);
                if (actual <= 0) { npc.Brain?.ResetActivity(); return; }
                npc.Money    -= actual;
                player.Money += actual;
                npc.Personality?.ApplyAllyBonus(player.Uid);
                if (npc.LastAttackerUid    == player.Uid) npc.LastAttackerUid    = null;
                if (player.LastAttackerUid == npc.Uid)    player.LastAttackerUid = null;
                Relations.RaiseToLevel(npc, player, RelationLevel.Normal);
                Relations.RaiseToLevel(player, npc, RelationLevel.Normal);
                CeasefireSolidarity.ApplyHomePlanetBonus(npc, player, npc.CurrentStar);
                ctx.WriteInt(RANSOM_TAKEN_KEY, actual);
                npc.Brain?.ResetActivity();
            });

            DialogService.RegisterAction("IncomingOfferMoneyRefuse", (ctx, _) =>
            {
                var npc = ctx?.TargetShip;
                npc?.Brain?.ResetActivity();
            });

            // ── Теги ──────────────────────────────────────────────────────────
            DialogService.RegisterDataTag("incoming_rob_reason",     ROB_REASON_KEY);
            DialogService.RegisterDataTag("incoming_rob_weight",     ROB_WEIGHT_KEY, "0");
            DialogService.RegisterDataTag("incoming_rob_containers", ROB_COUNT_KEY,  "0");
            DialogService.RegisterDataTag("incoming_extort_reason",  EXT_REASON_KEY);
            DialogService.RegisterDataTag("incoming_extort_fee",     EXT_FEE_KEY,    "0");
            DialogService.RegisterDataTag("ransom_offer",            RANSOM_OFFER_KEY, "0");
            DialogService.RegisterDataTag("ransom_taken",            RANSOM_TAKEN_KEY, "0");
        }

        /// <summary>Дублирует OrderRequestCeasefire.TransferOffer/CeasefireOfferFraction: часть
        /// каждого стека NPC переезжает в инвентарь игрока.</summary>
        private static void TransferCeasefireCargo(ShipData from, ShipData to)
        {
            if (from?.Inventory == null || to == null) return;
            var stackKeys = new List<string>(from.Inventory.Stacks.Keys);
            foreach (var key in stackKeys)
            {
                if (!from.Inventory.Stacks.TryGetValue(key, out var stackEntry)) continue;
                int amount = Mathf.Max(1, Mathf.RoundToInt(stackEntry.TotalWeight * NpcBalance.CeasefireOfferFraction));
                var stack = from.Inventory.TakeStack(key, amount);
                if (stack != null) to.Inventory.AddStack(stack);
            }
        }

        /// <summary>Копия формулы ActionOfferMoneyRansom.CalculateOffer.</summary>
        private static int CalcOfferAmount(ShipData ship)
        {
            if (ship == null || ship.Money <= 0 || ship.MaxHull <= 0) return 0;
            float hpLost = 1f - (float)ship.CurrentHull / ship.MaxHull;
            float fraction = Mathf.Clamp01(0.2f + hpLost * 0.6f);
            return Mathf.Max(50, Mathf.RoundToInt(ship.Money * fraction));
        }

    }
}
