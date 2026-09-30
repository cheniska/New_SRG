using System;
using System.Collections.Generic;
using UnityEngine;
using MoonSharp.Interpreter;
using SRG.Combat;
using SRG.Dialog;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Simulation;
using SRG.Ships;
using SRG.Ships.Services;
using SRG.Utils;
using SRG.Simulation;

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
            typeof(ArtefactApi),
            typeof(ArtefactApi.News),
            typeof(EquipmentSystem),
            typeof(StatBus),
            typeof(Mathf),
            typeof(UnityEngine.Debug),
        };

        // Статические API из слоёв выше симуляции (GalaxyManager, PlayerManager, PlayerShip…).
        // Симуляция о них не знает — их регистрирует слой приложения через LuaHost.RegisterStaticApi.
        static readonly List<Type> ExtraStaticApiTypes = new();

        static bool _typesRegistered;

        public static void RegisterTypes()
        {
            if (_typesRegistered) return;
            _typesRegistered = true;

            foreach (var t in DataTypes)           UserData.RegisterType(t);
            foreach (var t in StaticApiTypes)      UserData.RegisterType(t);
            foreach (var t in ExtraStaticApiTypes) UserData.RegisterType(t);
        }

        /// <summary>Добавить статический API. Возвращает false, если тип уже добавлен.</summary>
        internal static bool AddStaticApi(Type t)
        {
            if (t == null || ExtraStaticApiTypes.Contains(t)) return false;
            ExtraStaticApiTypes.Add(t);
            if (_typesRegistered) UserData.RegisterType(t);
            return true;
        }

        internal static void BindStaticApi(Script s, Type t) => s.Globals[t.Name] = UserData.CreateStatic(t);

        public static void BindGlobals(Script s)
        {
            // Статические API — под коротким именем типа.
            foreach (var t in StaticApiTypes)
                s.Globals[t.Name] = UserData.CreateStatic(t);
            foreach (var t in ExtraStaticApiTypes)
                BindStaticApi(s, t);

            // Короткий алиас Api = ArtefactApi для скриптов артефактов.
            s.Globals["Api"] = UserData.CreateStatic(typeof(ArtefactApi));

            // Удобные функции-акцессоры (всегда свежие).
            s.Globals["Player"]  = (Func<ShipData>)   (() => GameWorld.PlayerShip);
            s.Globals["Galaxy"]  = (Func<GalaxyData>) (() => GameWorld.GeneratedGalaxy);
            s.Globals["Star"]    = (Func<StarData>)   (() => GameWorld.CurrentStar);
            s.Globals["Turn"]    = (Func<int>)        (() => GameWorld.GeneratedGalaxy?.CurrentTurn ?? 0);

            // Логирование в игровую консоль. print(...) тоже туда идёт (см. LuaHost.Options.DebugPrint).
            s.Globals["Log"] = (Action<object>)(msg =>
                GameLog.Add(msg?.ToString() ?? "nil"));

            // Быстрый доступ к рандому.
            s.Globals["Rand"]    = (Func<float, float, float>)((a, b) => GameRng.Range(a, b));
            s.Globals["RandInt"] = (Func<int, int, int>)      ((a, b) => GameRng.Range(a, b));
        }
    }
}
