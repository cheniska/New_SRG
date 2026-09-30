using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System;
using SRG.Combat;
using SRG.Config;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy.Generation;
using SRG.Galaxy.Politics;
using SRG.Galaxy.Simulation;
using SRG.NpcAI;
using SRG.NpcAI.Actions;
using SRG.NpcAI.Spawning;
using SRG.Science;
using SRG.Ships;
using SRG.Ships.Movement;
using SRG.Ships.Services;
using SRG.Simulation;

namespace SRG.Galaxy
{
    // Значения зафиксированы явно (не переставлять!): HyperjumpPhase сериализуется как int
    // в save-файлах. HyperArrive добавлена ПОЗЖЕ HyperExit/CancelEnd — поэтому её номер 5,
    // а не в логическом порядке между HyperEnter и HyperExit. Новые фазы добавлять только
    // с новыми числами в конце, иначе старые сохранения загрузятся с искажёнными фазами.
    public enum HyperjumpPhase
    {
        None        = 0, // Прыжок не идёт.
        Travel      = 1, // Корабль летит к краю системы в направлении цели (произвольное число ходов).
        HyperEnter  = 2, // Ход 1 из 3: в системе-источнике открывается портал (begin+mid),
                         //           корабль входит в него с fade-out. В конце хода —
                         //           списание топлива и миграция в целевую систему.
        HyperExit   = 3, // Ход 3 из 3: портал на стороне цели закрывается (end). Корабль появляется
                         //           у точки выхода (fade-in) и СРАЗУ летит к цели AI-приказа.
        CancelEnd   = 4, // Игрок отменил прыжок во время HyperEnter: на следующий ход портал закрывается (end) и удаляется.
        HyperArrive = 5, // Ход 2 из 3 (логически между HyperEnter и HyperExit): корабль уже в целевой
                         //           системе, стоит невидимо у точки выхода; портал на стороне цели
                         //           открывается (begin+mid). Для игрока — этот ход идёт СРАЗУ после
                         //           scene swap: камера ставится на HyperjumpArrivalEdge и проигрывается
                         //           анимация открытия. Корабль не двигается.
    }

    /// <summary>
    /// Двухфазная посадка (docs/modules/landing.md).
    /// </summary>
    public enum LandingPhase
    {
        None,           // Не садимся (LandingPlanetUid либо пуст, либо ещё не построен курс).
        Approach,       // Подлёт к посадочному кольцу.
        Fading,         // Финальный ход: конец маршрута внутри R_land, корабль растворяется.
    }
}
