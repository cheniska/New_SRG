using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Config;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Simulation;
using SRG.NpcAI.Spawning;
using SRG.Ships;
using SRG.Controllers;
using SRG.Ships.Services;
using SRG.Scripting;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.UI.Screens
{
    public partial class GameConsoleController
    {
        // ── Партнёрство и дроны ──────────────────────────────────────────────────

        void SpawnDroneCmd()
        {
            var player = PlayerShip.Instance?.ShipData;
            if (player == null) { Log("Player not ready."); return; }
            string race = string.IsNullOrEmpty(_lastArg) ? null : _lastArg;
            var drone = PartnerScriptApi.SpawnDrone(player, race);
            if (drone == null) { Log("Spawn failed (см. лог Unity)."); return; }
            Log($"Дрон {drone.Name} [{race ?? PartnerScriptApi.DefaultDroneRace}] развёрнут, uid={SpriteUtility.ShortId(drone.Uid)}");
        }

        void GiveItemCmd()
        {
            var player = PlayerShip.Instance?.ShipData;
            if (player == null) { Log("Player not ready."); return; }
            if (string.IsNullOrEmpty(_lastArg))
            {
                Log("Usage: give <id|Cat:Id> [amount] [inv|slot|<slotKey>]");
                Log("  Автопоиск: MicroModule → Good → Mineral → Equipment. Форсировать: Weapons:W_Laser, Goods:Alcohol, Mineral:Minerals, MicroModule:...");
                Log("  amount игнорируется для оборудования/ММ. По умолчанию 1.");
                Log("  target: inv (в трюм), slot (авто-слот), Weapons_2 (конкретный слот). По умолчанию — авто.");
                return;
            }

            var parts = _lastArg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string spec  = parts[0];
            int amount   = 1;
            var target   = ItemGrantService.GrantTarget.Auto;
            string slot  = null;

            for (int i = 1; i < parts.Length; i++)
            {
                var p = parts[i];
                if (int.TryParse(p, out var n) && n > 0) { amount = n; continue; }
                if (p.Equals("inv",  StringComparison.OrdinalIgnoreCase)) { target = ItemGrantService.GrantTarget.Inventory; continue; }
                if (p.Equals("slot", StringComparison.OrdinalIgnoreCase)) { target = ItemGrantService.GrantTarget.Auto;      continue; }
                // Иначе трактуем как конкретный slotKey (напр. Weapons_2, Engine_0).
                target = ItemGrantService.GrantTarget.Slot;
                slot   = p;
            }

            // Для стеков (Good/Mineral) amount уходит в сам стек. Для оборудования/ММ — повторяем вызов.
            // Первый вызов сам определит, стек это или предмет; если стек, amount уже применён.
            var first = ItemGrantService.Give(player, spec, amount, target, slot);
            Log(first.Ok ? first.Message : $"give: {first.Message}");
            if (first.Ok && first.Placement != "stack" && amount > 1)
            {
                int extras = 0;
                for (int i = 1; i < amount; i++)
                {
                    var r = ItemGrantService.Give(player, spec, 1, target, slot);
                    if (r.Ok) extras++;
                    else { Log($"  ...прервано на {i + 1}: {r.Message}"); break; }
                }
                if (extras > 0) Log($"  +ещё {extras} шт.");
            }
        }

        void GiveAllCmd()
        {
            var player = PlayerShip.Instance?.ShipData;
            if (player == null) { Log("Player not ready."); return; }
            var ctx = GalaxyManager.Instance?.Context;
            var eq = ctx?.ItemsConfig;
            var items = ctx?.ItemsConfig;
            if (eq == null || items == null) { Log("ItemsConfig / ItemsConfig не загружены."); return; }

            string filter = string.IsNullOrEmpty(_lastArg) ? null : _lastArg.Trim();
            int given = 0, skipped = 0;

            // 1. Оборудование (включая корпуса, оружие, артефакты и т.д.). Каждый предмет — по одному
            // экземпляру. Корпуса пропускаем: несколько корпусов в трюме бесполезны и раздувают вес.
            foreach (var (cat, id, _) in eq.EnumerateAllItems())
            {
                if (!string.IsNullOrEmpty(filter) &&
                    !cat.Equals(filter, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                if (cat == EquipmentCategory.Hull) { skipped++; continue; }
                var res = ItemGrantService.Give(player, cat + ":" + id, 1,
                    ItemGrantService.GrantTarget.Inventory);
                if (res.Ok) given++;
                else skipped++;
            }

            // 2. Микромодули — если фильтра нет или фильтр == "MicroModule".
            if (string.IsNullOrEmpty(filter) ||
                filter.Equals("MicroModule", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var kv in items.EnumerateByKind(ItemKind.MicroModules))
                {
                    var res = ItemGrantService.Give(player, "MicroModule:" + kv.Key, 1,
                        ItemGrantService.GrantTarget.Inventory);
                    if (res.Ok) given++;
                    else skipped++;
                }
            }

            Log($"giveall: выдано {given} предмет(ов), пропущено {skipped}" +
                (filter != null ? $" (фильтр: {filter})" : "") + ".");
        }

        void GiveDroneCmd()
        {
            var player = PlayerShip.Instance?.ShipData;
            if (player == null) { Log("Player not ready."); return; }
            string race = string.IsNullOrEmpty(_lastArg) ? null : _lastArg;
            var item = PartnerScriptApi.GiveDronePackage(player, race);
            if (item == null) { Log("Give failed (см. лог Unity)."); return; }
            Log($"Выдан упакованный дрон [{race ?? PartnerScriptApi.DefaultDroneRace}] в инвентарь, item uid={SpriteUtility.ShortId(item.Uid)}");
        }

        void HireNearestCmd()
        {
            var player = PlayerShip.Instance?.ShipData;
            if (player == null) { Log("Player not ready."); return; }
            var cfg = GalaxyManager.Instance?.Context?.Config?.Partners;
            if (cfg == null) { Log("PartnersConfig не загружен."); return; }

            var target = PartnerScriptApi.FindNearestShip(player, s =>
                string.IsNullOrEmpty(s.PartnerLeaderUid)
                && cfg.PartnerableShipTypes != null
                && cfg.PartnerableShipTypes.ContainsKey(s.ShipTypeId ?? string.Empty));
            if (target == null) { Log("Нет подходящих hireable-кораблей рядом."); return; }

            bool ok = PartnerScriptApi.ForceHire(target, player);
            Log(ok ? $"Найм: {target.Name} ({SpriteUtility.ShortId(target.Uid)}) — партнёр."
                  : "Найм не удался (свита полна или уже занят).");
        }

        void DismissCmd()
        {
            var player = PlayerShip.Instance?.ShipData;
            if (player?.PartnerFollowerUids == null || player.PartnerFollowerUids.Count == 0)
            { Log("Партнёров нет."); return; }

            if (string.IsNullOrEmpty(_lastArg))
            {
                int n = player.PartnerFollowerUids.Count;
                // Копируем, потому что Break модифицирует список.
                var copy = new List<string>(player.PartnerFollowerUids);
                foreach (var uid in copy)
                {
                    var s = PartnerService.FindShipInGalaxy(uid);
                    if (s != null) PartnerScriptApi.BreakContract(s);
                }
                Log($"Разорвано {n} контракт(ов).");
                return;
            }

            if (!int.TryParse(_lastArg, out int idx) || idx < 0 || idx >= player.PartnerFollowerUids.Count)
            { Log($"Индекс вне диапазона (0..{player.PartnerFollowerUids.Count - 1})."); return; }

            var follower = PartnerService.FindShipInGalaxy(player.PartnerFollowerUids[idx]);
            if (follower == null) { Log("Партнёр не найден в галактике."); return; }
            PartnerScriptApi.BreakContract(follower);
            Log($"Контракт с {follower.Name} разорван.");
        }

        void ListPartnersCmd()
        {
            var player = PlayerShip.Instance?.ShipData;
            if (player?.PartnerFollowerUids == null || player.PartnerFollowerUids.Count == 0)
            { Log("Партнёров нет."); return; }

            int max = PartnerService.MaxPartners(player);
            Log($"Партнёры {player.PartnerFollowerUids.Count}/{max}:");
            for (int i = 0; i < player.PartnerFollowerUids.Count; i++)
            {
                var s = PartnerService.FindShipInGalaxy(player.PartnerFollowerUids[i]);
                if (s == null) { Log($"  [{i}] (UID {SpriteUtility.ShortId(player.PartnerFollowerUids[i])}) — не найден"); continue; }
                string state = s.CurrentHull <= 0 ? "мёртв" : $"HP {s.CurrentHull}/{s.MaxHull}";
                Log($"  [{i}] {s.Name} ({s.ShipTypeId}, {state}, uid={SpriteUtility.ShortId(s.Uid)})");
            }
        }
    }
}
