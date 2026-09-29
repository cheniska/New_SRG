using System;
using UnityEngine;
using MoonSharp.Interpreter;
using SRG.Combat;
using SRG.Core;
using SRG.Dialog;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Simulation;
using SRG.Ships;
using SRG.Ships.Player;
using SRG.Ships.Services;
using SRG.Utils;
using SRG.UI.Screens;

namespace SRG.Scripting
{
    /// <summary>
    /// Регистрация C#-типов и глобалов для Lua-скриптов. Разбит на два шага:
    /// <br/>
    /// • <see cref="RegisterTypes"/> — <see cref="UserData.RegisterType"/>, статическая инициализация
    ///   на весь процесс, вызывается один раз.
    /// <br/>
    /// • <see cref="BindGlobals"/> — навешивание глобалов на конкретный <see cref="Script"/>. Должно
    ///   вызываться на каждом новом инстансе (Core, каждый мод).
    /// </summary>
    public static class LuaBindings
    {
        // Data-модели: доступ по полям/пропертям как к обычной таблице.
        static readonly Type[] DataTypes =
        {
            typeof(ShipData),
            typeof(GalaxyData),
            typeof(StarData),
            typeof(PlanetData),
            typeof(WormholeData),
            typeof(WormholeLocation),
            typeof(Point),
            typeof(Vector2),
            typeof(DialogContext),
            typeof(DialogScope),
            typeof(ItemInstance),
            typeof(SRG.Ships.Disguise.DisguiseState),
            typeof(SRG.NpcAI.NpcBrain),
            typeof(ActiveMissile),
            typeof(SRG.Config.ItemsConfig),
            typeof(SRG.Config.ItemConfig),
            typeof(ArtefactApi.AttackDirectiveInfo),
        };

        // Сервисы и утилиты — доступны через статические методы: WormholeService.Spawn(...).
        // Регистрируются в UserData и добавляются в Script.Globals под своим Type.Name.
        static readonly Type[] StaticApiTypes =
        {
            typeof(DialogService),
            typeof(SRG.Ships.Services.ProtectService),
            typeof(SRG.Ships.Services.TruceService),
            typeof(SRG.Ships.Services.ExtortionService),
            typeof(SRG.Ships.Services.CargoRobberyService),
            typeof(SRG.Ships.Services.JointAttackService),
            typeof(SRG.Equipment.InventoryService),
            typeof(SRG.Ships.ContainerFactory),
            typeof(SRG.Ships.CargoUtils),
            typeof(SRG.Ships.Disguise.DisguiseService),
            typeof(SRG.Galaxy.Politics.OwnerRaceRelationsManager),
            typeof(SRG.Economy.TradeSystem),
            typeof(WormholeService),
            typeof(WormholeGraphics),
            typeof(GalaxyManager),
            typeof(PlayerManager),
            typeof(PartnerService),
            typeof(PartnerScriptApi),
            typeof(DroneService),
            typeof(AmmoService),
            typeof(FuelService),
            typeof(RepairService),
            typeof(ShipRatingService),
            typeof(ShipDockingService),
            typeof(ShipLoadoutService),
            typeof(ShipFactory),
            typeof(ItemFactory),
            typeof(Positions),
            typeof(DebugScripting),
            typeof(PlayerShip),
            typeof(ArtefactApi),
            typeof(ArtefactApi.News),
            typeof(EquipmentSystem),
            typeof(StatBus),
            typeof(Mathf),
            typeof(UnityEngine.Debug),
        };

        static bool _typesRegistered;

        public static void RegisterTypes()
        {
            if (_typesRegistered) return;
            _typesRegistered = true;

            foreach (var t in DataTypes)      UserData.RegisterType(t);
            foreach (var t in StaticApiTypes) UserData.RegisterType(t);
        }

        public static void BindGlobals(Script s)
        {
            // Статические API — под коротким именем типа.
            foreach (var t in StaticApiTypes)
                s.Globals[t.Name] = UserData.CreateStatic(t);

            // Короткий алиас Api = ArtefactApi для скриптов артефактов.
            s.Globals["Api"] = UserData.CreateStatic(typeof(ArtefactApi));

            // Удобные функции-акцессоры (всегда свежие).
            s.Globals["Player"]  = (Func<ShipData>)   (() => PlayerShip.Instance != null ? PlayerShip.Instance.ShipData : null);
            s.Globals["Galaxy"]  = (Func<GalaxyData>) (() => GalaxyManager.Instance?.GeneratedGalaxy);
            s.Globals["Star"]    = (Func<StarData>)   (() => GalaxyManager.Instance?.CurrentStar);
            s.Globals["Turn"]    = (Func<int>)        (() => GalaxyManager.Instance?.GeneratedGalaxy?.CurrentTurn ?? 0);

            // Логирование в игровую консоль. print(...) тоже туда идёт (см. LuaHost.Options.DebugPrint).
            s.Globals["Log"] = (Action<object>)(msg =>
                GameConsoleController.AddEntry(msg?.ToString() ?? "nil"));

            // Быстрый доступ к рандому.
            s.Globals["Rand"]    = (Func<float, float, float>)((a, b) => UnityEngine.Random.Range(a, b));
            s.Globals["RandInt"] = (Func<int, int, int>)      ((a, b) => UnityEngine.Random.Range(a, b));
        }
    }
}
