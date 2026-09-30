using System.Collections.Generic;
using UnityEngine;
using SRG.Combat;
using SRG.Config;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy.Generation;
using SRG.Galaxy.Politics;
using SRG.NpcAI;
using SRG.NpcAI.Spawning;
using SRG.Ships.Movement;
using SRG.Ships.Services;
using SRG.UI.Screens;
using SRG.Utils;

namespace SRG.Galaxy.Simulation
{
    /// <summary>
    /// Симуляция дневного хода одной звёздной системы. Вынесена из <see cref="StarData"/>
    /// на этапе T1 рефакторинга (июнь 2026): StarData теперь содержит только данные и
    /// связанные с ними утилиты (Power-кэш для NpcBrain), а здесь — тяжёлая логика хода:
    ///   • орбиты планет, кадры субходов кораблей и астероидов;
    ///   • CombatSubTurn (автоприцеливание NPC + игрок через ProcessPlayerManualShot);
    ///   • Pickup/Tow/Boarding-апдейты + хоминг ракет;
    ///   • Pull-distance check, плотные render-пути буксируемых;
    ///   • уборка мёртвых кораблей и регистрация смерти для спавнера.
    ///
    /// Поведение идентично прежнему StarData.StarNextDay/CombatSubTurn/ProcessPlayerManualShot.
    /// </summary>
    public static class StarSimulator
    {
        // ── Per-turn instrumentation (см. ResetTelemetry / LogTelemetry) ───────
        // Аккумулируется по всем 75 звёздам за ход в GalaxyNextDay. Полезно понимать, куда
        // уходит `stars=Xms` (planets/subturns/missiles/combat/…). Оверхед — 8 Stopwatch.Start/Stop
        // на секцию × 75 звёзд ~ микросекунды, порядки ниже полезной работы.
        private static long _tPlanets, _tMove, _tCombat, _tMissile, _tChain, _tPullQueue,
                            _tPower, _tHostile, _tArtefact, _tAsteroids;
        // Диагностика штормовых ходов (T322 move=531 artefact=430): для секций move/artefact
        // помним пик per-star и накопленное состояние (ship count, carrier count) в этой звезде.
        private static long _tMoveStarMax, _tArtefactStarMax;
        private static string _tMoveStarName, _tArtefactStarName;
        private static int _tMoveStarShips, _tArtefactStarCarriers, _tArtefactStarItems;
        // Аккумулированные по галактике сущности этого хода.
        private static int _totalShips, _totalArtefactCarriers, _totalArtefactItems, _totalMissiles;

        public static void ResetTelemetry()
        {
            _tPlanets = _tMove = _tCombat = _tMissile = _tChain = _tPullQueue =
                _tPower = _tHostile = _tArtefact = _tAsteroids = 0;
            _tMoveStarMax = _tArtefactStarMax = 0;
            _tMoveStarName = _tArtefactStarName = null;
            _tMoveStarShips = _tArtefactStarCarriers = _tArtefactStarItems = 0;
            _totalShips = _totalArtefactCarriers = _totalArtefactItems = _totalMissiles = 0;
        }
        public static void LogTelemetry(int currentTurn)
        {
            long sum = _tPlanets + _tMove + _tCombat + _tMissile + _tChain + _tPullQueue
                     + _tPower + _tHostile + _tArtefact + _tAsteroids;
            if (sum < 5) return; // порог: сумма секций <5мс — молчим (типичный тихий ход)
            SRG.Utils.PerfLog.Log($"[GND-stars] Turn {currentTurn}: " +
                $"planets={_tPlanets} move={_tMove} combat={_tCombat} missile={_tMissile} " +
                $"chain={_tChain} pull={_tPullQueue} power={_tPower} hostile={_tHostile} " +
                $"artefact={_tArtefact} astro={_tAsteroids} | " +
                $"ships={_totalShips} artCarriers={_totalArtefactCarriers} artItems={_totalArtefactItems} " +
                $"missiles={_totalMissiles}");
            // Спайк-логи: только когда секция реально дорогая — 100мс+ это уже штормовой ход
            // (на фоне типичных 20-40мс). Помогает найти виновника без per-star спама.
            if (_tMove >= 100 && !string.IsNullOrEmpty(_tMoveStarName))
                SRG.Utils.PerfLog.Log($"[GND-stars-spike] Turn {currentTurn} move: " +
                    $"worstStar={_tMoveStarName} ms={_tMoveStarMax} ships={_tMoveStarShips}");
            if (_tArtefact >= 100 && !string.IsNullOrEmpty(_tArtefactStarName))
                SRG.Utils.PerfLog.Log($"[GND-stars-spike] Turn {currentTurn} artefact: " +
                    $"worstStar={_tArtefactStarName} ms={_tArtefactStarMax} " +
                    $"carriers={_tArtefactStarCarriers} items={_tArtefactStarItems}");
        }

        // Вызывается из SimulateTurn после раздела movement — фиксирует пик.
        private static void UpdateMoveStar(StarData star, long ms, int ships)
        {
            if (ms > _tMoveStarMax)
            {
                _tMoveStarMax = ms;
                _tMoveStarName = star?.Name ?? star?.Uid;
                _tMoveStarShips = ships;
            }
        }

        private static void UpdateArtefactStar(StarData star, long ms, int carriers, int items)
        {
            if (ms > _tArtefactStarMax)
            {
                _tArtefactStarMax = ms;
                _tArtefactStarName = star?.Name ?? star?.Uid;
                _tArtefactStarCarriers = carriers;
                _tArtefactStarItems = items;
            }
        }

        private static readonly System.Diagnostics.Stopwatch _swSection = new();

        // Переиспользуемые буферы для избежания per-turn/per-star аллокаций
        // (SimulateTurn/CombatSubTurn — single-threaded gameplay). Clear+заполняется в начале
        // SimulateTurn.
        private static readonly Dictionary<string, ShipData> _shipsByUidBuf = new();

        // Per-star cache артефакт-носителей: строится 1 раз ПЕРЕД subturn loop (не ×10 внутри).
        // ArtefactTurnRegistry.RunForStar раньше перебирал ВСЕ AllItems каждого корабля × 10 сабтёрнов.
        // Теперь: 1 обход на ход + per-subturn итерация только по item-ам с TurnCode.
        private struct ArtefactCarrier { public ShipData Ship; public List<(ItemInstance item, ItemConfig cfg)> Items; }
        private static readonly List<ArtefactCarrier> _artefactCarriersBuf = new();
        private static readonly Stack<List<(ItemInstance, ItemConfig)>> _artefactItemListPool = new();
        private static List<(ItemInstance, ItemConfig)> RentArtefactList()
            => _artefactItemListPool.Count > 0 ? _artefactItemListPool.Pop() : new List<(ItemInstance, ItemConfig)>();
        private static void ReleaseArtefactCarriers()
        {
            for (int i = 0; i < _artefactCarriersBuf.Count; i++)
            {
                var lst = _artefactCarriersBuf[i].Items;
                lst.Clear();
                _artefactItemListPool.Push(lst);
            }
            _artefactCarriersBuf.Clear();
        }
        // Precomputed один раз на ход: для каждого потенциального атакующего — список UID
        // враждебных кандидатов. Избавляет CombatSubTurn от N² × 10 сабтёрнов вызовов AreHostile
        // (с их DisguiseService.GetEffective*). Пары со сменой disguise/personal-delta внутри
        // хода — редкие; отношения кэшируются на ход.
        private static readonly Dictionary<string, List<string>> _hostileByAttackerBuf = new();
        // Пул List<string>, чтобы не аллоцировать per-attacker на каждый ход.
        private static readonly Stack<List<string>> _stringListPool = new();

        private static List<string> RentStringList()
            => _stringListPool.Count > 0 ? _stringListPool.Pop() : new List<string>();

        private static void ReturnStringLists(Dictionary<string, List<string>> map)
        {
            foreach (var kv in map)
            {
                kv.Value.Clear();
                _stringListPool.Push(kv.Value);
            }
            map.Clear();
        }

        public static void SimulateTurn(StarData star, TurnAnimationData anim, GalaxyGenerationContext ctx = null)
        {
            // Пер-звёздные счётчики для спайк-диагностики (см. UpdateMoveStar/UpdateArtefactStar).
            long _sMoveThisStar = 0, _sArtefactThisStar = 0;

            bool playerPresent = false;
            foreach (var s in star.Ships)
                if (s.IsPlayer) { playerPresent = true; break; }

            if (playerPresent && ctx != null)
                AsteroidSystem.TrySpawnAsteroid(star, ctx, anim);

            // PlanetNextDay всегда крутит орбиты (симуляция для всех звёзд обязательна — см. memory
            // feedback_simulation). anim.PlanetAngles пишем только если игрок видит систему —
            // иначе TurnAnimationData никто не проигрывает и запись 200-400 планетных углов в
            // словарь на каждую фоновую звезду × 75 звёзд/ход — чистая аллокация в GC.
            _swSection.Restart();
            foreach (var planet in star.Planets)
            {
                planet.PlanetNextDay();
                if (playerPresent)
                    anim.PlanetAngles[planet.Uid] = (planet.PreviousAngle, planet.CurrentAngle);
            }
            _tPlanets += _swSection.ElapsedMilliseconds;

            foreach (var ship in star.Ships)
            {
                ship._turnCachedSpeed = EquipmentSystem.CalculateSpeed(ship);
                // Гиперпереход переопределяет цель/скорость/позицию для нужной фазы.
                HyperjumpController.PrepareTurn(ship, star);

                // Anim-кадры нужны только когда игрок видит систему (визуал хода).
                if (playerPresent)
                {
                    ship.SubTurnFrames.SubTurns[0] = ship.Position;
                    anim.ShipFrames[ship.Uid] = ship.SubTurnFrames;

                    ship.TrailPositions.Add(ship.Position);
                    if (ship.TrailPositions.Count > 20)
                        ship.TrailPositions.RemoveAt(0);
                }
            }

            if (playerPresent)
            {
                foreach (var asteroid in star.Asteroids)
                {
                    if (asteroid.IsDestroyed) continue;
                    asteroid.SubTurnFrames.SubTurns[0] = asteroid.Position;
                    anim.AsteroidFrames[asteroid.Uid] = asteroid.SubTurnFrames;
                }
            }

            MissileSystem.InitMissileFrames(star, GalaxyManager.Instance?.Context?.ItemsConfig, anim);

            int currentTurn = GalaxyManager.Instance?.GeneratedGalaxy?.CurrentTurn ?? 0;
            _swSection.Restart();
            star.RebuildPowerCache(currentTurn);
            _tPower += _swSection.ElapsedMilliseconds;

            // Кеш UID→ship для абордажа/буксира внутри субходов. Переиспользуемый static-буфер:
            // per-star аллокация словаря давала ~75 dict/ход в GC.
            var shipsByUid = _shipsByUidBuf;
            shipsByUid.Clear();
            foreach (var s in star.Ships) shipsByUid[s.Uid] = s;
            // Task 8: ItemsConfig берём один раз на весь ход; равно доступен для tow/missile/artefact.
            var equipConfig = GalaxyManager.Instance?.Context?.ItemsConfig;

            // Task 5+7: precomputed hostile pairs. Одновременно даёт early-out для боя, когда
            // ни у кого нет враждебных целей (типичный случай пустой/дружественной системы).
            var hostileByAttacker = _hostileByAttackerBuf;
            ReturnStringLists(hostileByAttacker);
            var relations = OwnerRaceRelationsManager.Instance;
            _swSection.Restart();
            if (relations != null && star.Ships.Count >= 2)
            {
                for (int i = 0; i < star.Ships.Count; i++)
                {
                    var attacker = star.Ships[i];
                    if (attacker.CurrentHull <= 0) continue;
                    if (!string.IsNullOrEmpty(attacker.LandedOnShipUid)) continue;
                    if (attacker.IsPlayer) continue;
                    List<string> hostiles = null;
                    for (int j = 0; j < star.Ships.Count; j++)
                    {
                        if (i == j) continue;
                        var candidate = star.Ships[j];
                        if (candidate.CurrentHull <= 0) continue;
                        if (!string.IsNullOrEmpty(candidate.LandedOnShipUid)) continue;
                        if (!relations.AreHostile(attacker, candidate)) continue;
                        (hostiles ??= RentStringList()).Add(candidate.Uid);
                    }
                    if (hostiles != null) hostileByAttacker[attacker.Uid] = hostiles;
                }
            }
            _tHostile += _swSection.ElapsedMilliseconds;

            // Игрок может стрелять вручную даже когда враждебных ИИ нет — учитываем.
            bool playerHasManualShot = false;
            for (int i = 0; i < star.Ships.Count; i++)
                if (star.Ships[i].IsPlayer
                    && !string.IsNullOrEmpty(star.Ships[i].ManualShootTargetUid))
                { playerHasManualShot = true; break; }
            bool combatNeeded = hostileByAttacker.Count > 0 || playerHasManualShot;

            // Per-star cache артефакт-носителей: 1 обход AllItems всех кораблей вместо ×10 сабтёрнов.
            // Ранее artefact доминировал в perf-логе (500-680 мс/ход). Equipment.Slots в течение
            // хода не меняется (реэкип NPC — только на планете, вне симуляции), поэтому item.IsEquipped
            // выставляем один раз при построении. Скрипты не могут менять список артефактов на
            // корабле по ходу симуляции. Новые корабли (контейнеры от pickup overflow) TurnCode-items
            // не несут — их можно игнорировать.
            _swSection.Restart();
            ReleaseArtefactCarriers();
            bool artefactsRun = equipConfig != null && SRG.Equipment.ArtefactTurnRegistry.HasAny;
            if (artefactsRun)
            {
                var equipBuf = new HashSet<string>();
                for (int i = 0; i < star.Ships.Count; i++)
                {
                    var s = star.Ships[i];
                    if (s == null || s.CurrentHull <= 0) continue;
                    if (!string.IsNullOrEmpty(s.LandedPlanetUid)) continue;
                    if (!string.IsNullOrEmpty(s.LandedOnShipUid)) continue;
                    if (s.Equipment == null || s.AllItems == null) continue;

                    List<(ItemInstance, ItemConfig)> matches = null;
                    // equippedBuf нужен ТОЛЬКО если у корабля есть artefact-item (иначе не строим).
                    bool equippedBuilt = false;
                    foreach (var kv in s.AllItems)
                    {
                        var item = kv.Value;
                        if (item == null || !item.IsWorking) continue;
                        var cfg = equipConfig.GetItem(item.Category, item.ItemId);
                        if (cfg == null || string.IsNullOrEmpty(cfg.TurnCode)) continue;
                        if (!equippedBuilt)
                        {
                            equipBuf.Clear();
                            foreach (var eq in s.Equipment.Slots)
                                if (eq.Value != null) equipBuf.Add(eq.Value);
                            equippedBuilt = true;
                        }
                        item.IsEquipped = equipBuf.Contains(kv.Key);
                        (matches ??= RentArtefactList()).Add((item, cfg));
                    }
                    if (matches != null)
                        _artefactCarriersBuf.Add(new ArtefactCarrier { Ship = s, Items = matches });
                }
            }
            {
                long _artBuild = _swSection.ElapsedMilliseconds;
                _tArtefact += _artBuild;
                _sArtefactThisStar += _artBuild;
            }
            // Пер-звёздный подсчёт artefact-нагрузки для UpdateArtefactStar в конце SimulateTurn.
            int _starArtCarriers = _artefactCarriersBuf.Count;
            int _starArtItems = 0;
            for (int _ci = 0; _ci < _artefactCarriersBuf.Count; _ci++)
                _starArtItems += _artefactCarriersBuf[_ci].Items.Count;
            _totalArtefactCarriers += _starArtCarriers;
            _totalArtefactItems += _starArtItems;

            // Ранний выход для полностью пустых систем: нет кораблей и нет ракет в полёте.
            // playerPresent → Ships.Count >= 1, поэтому здесь заведомо !playerPresent (астероиды
            // тикают только для игрока). MissileSystem.TickMissilesEndOfDay ниже — no-op при пустом
            // ActiveMissiles. Post-loop блоки (undock/pull-check/render/dead cleanup) все по Ships.
            // Экономия: 10 итераций subturn-цикла × 60-70 «фоновых» звёзд без событий.
            int missileCount = star.ActiveMissiles != null ? star.ActiveMissiles.Count : 0;
            bool anySubturnWork = star.Ships.Count > 0 || missileCount > 0;

            // Pre-turn флаги активности. Для тихих систем (нет ни одного из этих состояний ни у
            // одного корабля) большинство подсистем внутри сабтёрн-цикла пропускается. Проверка
            // пересчитывается перед КАЖДЫМ сабтёрном — состояния могут появиться в процессе
            // (например, PickupSystem.UpdatePullStep выбрасывает контейнер при перегрузе,
            // новый контейнер сразу получает PulledByUid/TowedByUid). O(N) на сабтёрн дёшево.
            for (int subTurn = 1; anySubturnWork && subTurn <= GalaxyData.SubTurnsPerTurn; subTurn++)
            {
                bool hasPullQueue = false;
                bool hasChainedState = false; // PulledBy / BoardingTarget / LandedOnShip / TowedObjects
                for (int i = 0; i < star.Ships.Count; i++)
                {
                    var s = star.Ships[i];
                    if (!hasPullQueue && s.PulledQueue != null && s.PulledQueue.Count > 0) hasPullQueue = true;
                    if (!hasChainedState
                        && (!string.IsNullOrEmpty(s.PulledByUid)
                            || !string.IsNullOrEmpty(s.BoardingTargetUid)
                            || !string.IsNullOrEmpty(s.LandedOnShipUid)
                            || s.TowedObjectUids.Count > 0))
                        hasChainedState = true;
                    if (hasPullQueue && hasChainedState) break;
                }
                bool hasMissiles = star.ActiveMissiles != null && star.ActiveMissiles.Count > 0;

                if (combatNeeded)
                {
                    _swSection.Restart();
                    CombatSubTurn(star, subTurn, anim, shipsByUid, hostileByAttacker, equipConfig);
                    _tCombat += _swSection.ElapsedMilliseconds;
                }

                // Очередь захвата: гейт по hasPullQueue.
                if (hasPullQueue)
                {
                    _swSection.Restart();
                    foreach (var ship in star.Ships)
                        if (ship.PulledQueue != null && ship.PulledQueue.Count > 0)
                            PickupSystem.ProcessPullQueueSubturn(ship, equipConfig, shipsByUid);
                    _tPullQueue += _swSection.ElapsedMilliseconds;
                }

                _swSection.Restart();
                foreach (var ship in star.Ships)
                    ship.ShipNextDay(subTurn, anim, playerPresent);
                long _mvMs = _swSection.ElapsedMilliseconds;
                _tMove += _mvMs;
                _sMoveThisStar += _mvMs;

                // Ракеты тикают ПОСЛЕ движения кораблей — хоминг должен использовать
                // обновлённую позицию цели в этом сабтёрне, а не "прошлую".
                if (hasMissiles)
                {
                    _swSection.Restart();
                    MissileSystem.TickMissiles(star, subTurn, equipConfig, anim);
                    _tMissile += _swSection.ElapsedMilliseconds;
                }

                // TurnCode/TurnScript-скрипты артефактов и других предметов. Тикает после
                // движения кораблей и ракет — скрипт видит уже актуальные позиции. Используется
                // pre-turn кэш (см. artefactsRun/_artefactCarriersBuf выше): итерируем только
                // корабли с TurnCode-items вместо всех AllItems ×10 сабтёрнов.
                if (artefactsRun && _artefactCarriersBuf.Count > 0)
                {
                    _swSection.Restart();
                    for (int ci = 0; ci < _artefactCarriersBuf.Count; ci++)
                    {
                        var carrier = _artefactCarriersBuf[ci];
                        var s = carrier.Ship;
                        if (s.CurrentHull <= 0) continue;
                        if (!string.IsNullOrEmpty(s.LandedPlanetUid)) continue;
                        if (!string.IsNullOrEmpty(s.LandedOnShipUid)) continue;
                        var items = carrier.Items;
                        for (int ii = 0; ii < items.Count; ii++)
                        {
                            var (item, cfg) = items[ii];
                            if (item == null || !item.IsWorking) continue;
                            if (!cfg.RunsOnSubturn(subTurn)) continue;
                            var script = SRG.Equipment.ArtefactTurnRegistry.Get(cfg.TurnCode);
                            if (script == null) continue;
                            try { script.OnTurn(s, item, equipConfig, subTurn); }
                            catch (System.Exception e)
                            { UnityEngine.Debug.LogError($"[ArtefactTurnRegistry] TurnCode '{cfg.TurnCode}' у '{item.Name ?? item.ItemId}': {e.Message}"); }
                        }
                    }
                    long _artRun = _swSection.ElapsedMilliseconds;
                    _tArtefact += _artRun;
                    _sArtefactThisStar += _artRun;
                }

                // После того как все корабли подвинулись: применяем притягивание (PulledByUid)
                // в соответствующем режиме (Pickup → к центру и в трюм; Tow → к якорю),
                // привязываем абордируемых к атакующему и обновляем цепочку буксира.
                // Гейт: скипаем блок, только если в системе НЕТ ни chained-состояний, ни pull
                // queue (которая на этом сабтёрне могла перевести кандидата в PulledByUid).
                // Индексный цикл со снапшотом: завершение подбора внутри UpdatePullStep может
                // выбросить контейнеры при перегрузе (star.Ships.Add) — foreach бы упал,
                // а новые контейнеры в этом субходе обрабатывать не нужно.
                if (hasChainedState || hasPullQueue)
                {
                _swSection.Restart();
                int shipCountSnapshot = star.Ships.Count;
                for (int si = 0; si < shipCountSnapshot; si++)
                {
                    var ship = star.Ships[si];
                    if (!string.IsNullOrEmpty(ship.PulledByUid))
                    {
                        if (ship.PullMode == PullKind.Pickup)
                            PickupSystem.UpdatePullStep(ship, shipsByUid, GalaxyData.SubTurnsPerTurn);
                        else if (ship.PullMode == PullKind.Tow)
                            TowSystem.UpdateTowPullStep(ship, equipConfig, shipsByUid, GalaxyData.SubTurnsPerTurn);
                        if (anim.ShipFrames.TryGetValue(ship.Uid, out var pf))
                            pf.SubTurns[subTurn] = ship.Position;
                    }
                    if (!string.IsNullOrEmpty(ship.BoardingTargetUid)
                        && shipsByUid.TryGetValue(ship.BoardingTargetUid, out var boardTarget))
                    {
                        BoardingSystem.SnapBoardedTarget(ship, boardTarget, equipConfig);
                        // Записываем итоговую позицию в кадр субхода, чтобы визуал не «прыгал».
                        if (anim.ShipFrames.TryGetValue(boardTarget.Uid, out var bf))
                            bf.SubTurns[subTurn] = boardTarget.Position;
                    }
                    // Пристыкованные корабли (LandedOnShipUid) следуют за носителем.
                    if (!string.IsNullOrEmpty(ship.LandedOnShipUid)
                        && shipsByUid.TryGetValue(ship.LandedOnShipUid, out var dockCarrier)
                        && dockCarrier.CurrentHull > 0)
                    {
                        ship.Position = dockCarrier.Position;
                        if (!float.IsNaN(dockCarrier.CurrentHeading))
                            ship.CurrentHeading = dockCarrier.CurrentHeading;
                        ship.TargetPosition = ship.Position;
                        if (anim.ShipFrames.TryGetValue(ship.Uid, out var dockFrames))
                            dockFrames.SubTurns[subTurn] = ship.Position;
                    }
                    // Дерево буксира обновляется только для корней (ship.TowedByUid == null).
                    // Внутри UpdateTowChain рекурсивно обходит всё поддерево; здесь пишем кадры
                    // субхода для всех потомков, чтобы визуал не «прыгал».
                    if (ship.TowedObjectUids.Count > 0 && string.IsNullOrEmpty(ship.TowedByUid))
                    {
                        TowSystem.UpdateTowChain(ship, equipConfig, shipsByUid, GalaxyData.SubTurnsPerTurn);
                        foreach (var towed in TowSystem.EnumerateTowedDescendants(ship, shipsByUid))
                        {
                            if (anim.ShipFrames.TryGetValue(towed.Uid, out var tf))
                                tf.SubTurns[subTurn] = towed.Position;
                        }
                    }
                }
                _tChain += _swSection.ElapsedMilliseconds;
                } // if (hasChainedState || hasPullQueue)

                if (playerPresent && ctx != null)
                {
                    _swSection.Restart();
                    AsteroidSystem.TickAsteroids(star, subTurn, anim, ctx);
                    _tAsteroids += _swSection.ElapsedMilliseconds;
                }
            }

            MissileSystem.TickMissilesEndOfDay(star, anim);

            // Расстыковка при потере носителя: носитель уничтожен либо ушёл из системы
            // (гиперпрыжок — его уже нет в star.Ships). Корабль освобождается на месте.
            foreach (var ship in star.Ships)
            {
                if (string.IsNullOrEmpty(ship.LandedOnShipUid)) continue;
                shipsByUid.TryGetValue(ship.LandedOnShipUid, out var dockCarrier);
                if (dockCarrier != null && dockCarrier.CurrentHull > 0) continue;
                ShipDockingService.Undock(ship, dockCarrier);
                if (ship.IsPlayer)
                    GameConsoleController.AddEntry("[Стыковка] Носитель потерян — корабль в свободном полёте.");
            }

            // ВНИМАНИЕ: HyperjumpController.FinalizeTurn вызывается НЕ здесь, а в
            // GalaxyManager.CompleteCurrentTurn — ПОСЛЕ того, как анимация хода проиграется.

            // Pull: проверяем, что дистанция не растёт два хода подряд. Если растёт — буксир рвётся.
            for (int i = 0; i < star.Ships.Count; i++)
                PickupSystem.TickPullDistanceCheck(star.Ships[i], shipsByUid, equipConfig);

            // Плотный render-путь буксируемых (только для корней дерева).
            if (playerPresent)
            {
                foreach (var ship in star.Ships)
                    if (ship.TowedObjectUids.Count > 0 && string.IsNullOrEmpty(ship.TowedByUid))
                        TowSystem.RebuildTowedRenderPaths(ship, equipConfig, shipsByUid, anim);
            }

            // Сбрасываем абордаж/буксир для погибших кораблей.
            for (int i = 0; i < star.Ships.Count; i++)
            {
                var ship = star.Ships[i];
                if (ship.CurrentHull > 0) continue;
                if (!string.IsNullOrEmpty(ship.BoardingTargetUid))
                {
                    shipsByUid.TryGetValue(ship.BoardingTargetUid, out var target);
                    BoardingSystem.EndBoarding(ship, target);
                }
                if (!string.IsNullOrEmpty(ship.BoardedByUid))
                {
                    shipsByUid.TryGetValue(ship.BoardedByUid, out var attacker);
                    BoardingSystem.EndBoarding(attacker, ship);
                }
                if (ship.TowedObjectUids.Count > 0)
                {
                    foreach (var descendant in TowSystem.EnumerateTowedDescendants(ship, shipsByUid))
                    {
                        descendant.TowedByUid = null;
                        descendant.TowParentUid = null;
                        descendant.TowAnchorKey = null;
                        descendant.IsTowSettled = false;
                        descendant.TowedObjectUids.Clear();
                    }
                    ship.TowedObjectUids.Clear();
                }
                if (!string.IsNullOrEmpty(ship.TowedByUid))
                {
                    if (!string.IsNullOrEmpty(ship.TowParentUid)
                        && shipsByUid.TryGetValue(ship.TowParentUid, out var parent))
                        parent.TowedObjectUids.Remove(ship.Uid);
                    ship.TowedByUid = null;
                    ship.TowParentUid = null;
                    ship.TowAnchorKey = null;
                    ship.IsTowSettled = false;
                }
                if (!string.IsNullOrEmpty(ship.PulledByUid))
                    PickupSystem.ClearPullState(ship);
            }

            // Удаляем мёртвых/выгруженные контейнеры.
            for (int i = star.Ships.Count - 1; i >= 0; i--)
            {
                var s = star.Ships[i];
                if (s.IsPlayer || s.CurrentHull > 0) continue;
                if (anim != null && !anim.DeathUids.Contains(s.Uid))
                    anim.DeathUids.Add(s.Uid);
                SpawnSystem.RegisterDeath(s);
                star.Ships.RemoveAt(i);
            }

            // Аккумулируем per-star вклад для спайк-диагностики (см. LogTelemetry).
            UpdateMoveStar(star, _sMoveThisStar, star.Ships.Count);
            UpdateArtefactStar(star, _sArtefactThisStar, _starArtCarriers, _starArtItems);
            _totalShips += star.Ships.Count;
            if (star.ActiveMissiles != null) _totalMissiles += star.ActiveMissiles.Count;
        }

        // ── Combat subturn ─────────────────────────────────────────────────────

        private static void CombatSubTurn(StarData star, int subTurn, TurnAnimationData anim,
            Dictionary<string, ShipData> shipsByUid,
            Dictionary<string, List<string>> hostileByAttacker,
            ItemsConfig equipConfig)
        {
            // Выстрел игрока по вручную выбранной цели.
            ProcessPlayerManualShot(star, anim, equipConfig, subTurn);

            foreach (var attacker in star.Ships)
            {
                if (attacker.CurrentHull <= 0) continue;
                // Пристыкованные к носителю не стреляют (аналог посадки на планету).
                if (!string.IsNullOrEmpty(attacker.LandedOnShipUid)) continue;
                if (WeaponSystem.HasEffect(attacker, CombatEffectType.Shutdown)) continue;
                if (attacker.IsPlayer) continue;     // игрок — только через ProcessPlayerManualShot

                ShipData bestTarget = null;
                float bestDistSq = float.MaxValue;
                Vector2 aPos = attacker.Position;

                // Обходим только заранее посчитанный список враждебных Uid (task 5): в бою
                // с 50-100 кораблями в дом-системе это ×10 экономия против AreHostile-цикла
                // на каждый сабтёрн. Живость/land-состояние читаем свежими из shipsByUid.
                if (hostileByAttacker.TryGetValue(attacker.Uid, out var hostiles))
                {
                    for (int i = 0; i < hostiles.Count; i++)
                    {
                        if (!shipsByUid.TryGetValue(hostiles[i], out var candidate)) continue;
                        if (candidate.CurrentHull <= 0) continue;
                        if (!string.IsNullOrEmpty(candidate.LandedOnShipUid)) continue;
                        float distSq = (candidate.Position - aPos).sqrMagnitude;
                        if (distSq < bestDistSq) { bestDistSq = distSq; bestTarget = candidate; }
                    }
                }

                // Активная цель Brain (PursueAndAttack / Disable / Rob и т.п.) приоритетна.
                string brainTargetUid = attacker.IsPlayer ? null : attacker.Brain?.GetCombatTargetUid();
                if (!string.IsNullOrEmpty(brainTargetUid)
                    && shipsByUid.TryGetValue(brainTargetUid, out var brainTarget)
                    && brainTarget != null && brainTarget.CurrentHull > 0 && brainTarget != attacker
                    && string.IsNullOrEmpty(brainTarget.LandedOnShipUid))
                {
                    bestTarget = brainTarget;
                    bestDistSq = (brainTarget.Position - aPos).sqrMagnitude;
                }

                if (bestTarget == null) continue;
                if (WeaponSystem.HasEffect(attacker, CombatEffectType.BlockWeapon)) continue;
                // Абордаж: пушки цели всегда заблокированы.
                if (!string.IsNullOrEmpty(attacker.BoardedByUid)) continue;
                // Буксир: пушки буксируемого блокируются, ТОЛЬКО если он враждебен буксирующему.
                if (!string.IsNullOrEmpty(attacker.TowedByUid))
                {
                    shipsByUid.TryGetValue(attacker.TowedByUid, out var tug);
                    var rel = OwnerRaceRelationsManager.Instance;
                    if (tug != null && rel != null && rel.AreHostile(attacker, tug))
                        continue;
                }
                var weaponSlots = attacker.GetSortedWeaponSlots();
                int nextFiringSubTurn = 1;
                foreach (var slotKey in weaponSlots)
                {
                    if (nextFiringSubTurn > GalaxyData.SubTurnsPerTurn) break;
                    string uid = attacker.Equipment.GetItemUid(slotKey);
                    if (uid == null || !attacker.AllItems.TryGetValue(uid, out var weapon)) continue;
                    int duration = Mathf.Max(1, Mathf.RoundToInt(weapon.GetParam("ShotDuration", 1f)));
                    if (weapon.IsWorking && subTurn == nextFiringSubTurn)
                    {
                        float range = SRUnits.ToWorld(weapon.GetParam("Range", 0f));
                        if (bestDistSq <= range * range)
                        {
                            var pattern = WeaponSystem.ParseHitPattern(weapon.GetParamString("ShotPattern", weapon.GetParamString("HitPattern", "Point")));
                            if (pattern == HitPattern.Homing)
                                MissileSystem.LaunchSalvo(attacker, bestTarget, slotKey, weapon, star, equipConfig, anim, subTurn);
                            else
                                WeaponSystem.ProcessShot(attacker, bestTarget, slotKey, equipConfig, anim, subTurn);
                        }
                    }
                    nextFiringSubTurn += duration;
                }
            }
        }

        // ── Manual shot (игрок) ────────────────────────────────────────────────

        private static void ProcessPlayerManualShot(StarData star, TurnAnimationData anim, ItemsConfig equipConfig, int subTurn)
        {
            ShipData playerShip = FindPlayerShip(star);
            if (playerShip == null) return;
            if (string.IsNullOrEmpty(playerShip.ManualShootTargetUid)) return;

            bool lastSubTurn = subTurn == GalaxyData.SubTurnsPerTurn;

            // Мёртвый или пристыкованный к носителю игрок не должен стрелять.
            if (playerShip.CurrentHull <= 0 || !string.IsNullOrEmpty(playerShip.LandedOnShipUid))
            {
                ClearManualShootFlags(playerShip);
                return;
            }

            string targetUid = playerShip.ManualShootTargetUid;
            bool isAsteroid  = playerShip.ManualShootTargetIsAsteroid;
            bool onlyLongest = playerShip.ManualShootOnlyLongestRange;
            bool isAutoFollow = playerShip.ManualShootIsAutoFollow;

            if (isAsteroid)
                ProcessManualAsteroidShot(star, playerShip, targetUid, anim, subTurn);
            else
                ProcessManualShipShot(star, playerShip, targetUid, onlyLongest, isAutoFollow, equipConfig, anim, subTurn);

            if (lastSubTurn) ClearManualShootFlags(playerShip);
        }

        private static ShipData FindPlayerShip(StarData star)
        {
            for (int i = 0; i < star.Ships.Count; i++)
                if (star.Ships[i].IsPlayer) return star.Ships[i];
            return null;
        }

        private static void ClearManualShootFlags(ShipData playerShip)
        {
            playerShip.ManualShootTargetUid = null;
            playerShip.ManualShootOnlyLongestRange = false;
            playerShip.ManualShootIsAutoFollow = false;
        }

        private static void ProcessManualAsteroidShot(StarData star, ShipData playerShip, string targetUid, TurnAnimationData anim, int subTurn)
        {
            if (subTurn != 1) return;
            var asteroid = star.Asteroids?.Find(a => a.Uid == targetUid && !a.IsDestroyed);
            if (asteroid == null) return;
            var ctx = GalaxyManager.Instance?.Context;
            AsteroidSystem.PlayerShootAsteroid(playerShip, asteroid, anim, 1, ctx);
            GameConsoleController.AddEntry(
                $"[Стрельба] Астероид {SpriteUtility.ShortId(targetUid)} уничтожен.");
        }

        private static string FindLongestRangeWeaponSlot(ShipData playerShip, List<string> weaponSlots)
        {
            string longestRangeSlot = null;
            float maxRange = -1f;
            foreach (var slotKey in weaponSlots)
            {
                string uid = playerShip.Equipment.GetItemUid(slotKey);
                if (uid == null || !playerShip.AllItems.TryGetValue(uid, out var w)) continue;
                if (!w.IsWorking) continue;
                float r = w.GetParam("Range", 0f);
                if (r > maxRange) { maxRange = r; longestRangeSlot = slotKey; }
            }
            return longestRangeSlot;
        }

        private static void ProcessManualShipShot(
            StarData star, ShipData playerShip, string targetUid,
            bool onlyLongest, bool isAutoFollow,
            ItemsConfig equipConfig, TurnAnimationData anim, int subTurn)
        {
            ShipData target = null;
            for (int i = 0; i < star.Ships.Count; i++)
                if (star.Ships[i].Uid == targetUid) { target = star.Ships[i]; break; }

            if (target == null || target.CurrentHull <= 0
                || !string.IsNullOrEmpty(target.LandedOnShipUid)) return;

            var playerWeaponSlots = playerShip.Equipment.GetOccupiedSlotsOfCategory(EquipmentCategory.Weapons);
            playerWeaponSlots.Sort();

            string longestRangeSlot = onlyLongest ? FindLongestRangeWeaponSlot(playerShip, playerWeaponSlots) : null;
            int nextFiringSubTurn = 1;
            float dist = (target.Position - playerShip.Position).magnitude;
            foreach (var slotKey in playerWeaponSlots)
            {
                if (nextFiringSubTurn > GalaxyData.SubTurnsPerTurn) break;
                string uid = playerShip.Equipment.GetItemUid(slotKey);
                if (uid == null || !playerShip.AllItems.TryGetValue(uid, out var weapon)) continue;
                int duration = Mathf.Max(1, Mathf.RoundToInt(weapon.GetParam("ShotDuration", 1f)));
                bool slotAllowed = !onlyLongest || slotKey == longestRangeSlot;
                if (slotAllowed && weapon.IsWorking && nextFiringSubTurn == subTurn)
                    FireWeaponSlot(star, playerShip, target, slotKey, weapon, dist, isAutoFollow, equipConfig, anim, nextFiringSubTurn);
                nextFiringSubTurn += duration;
            }
        }

        private static void FireWeaponSlot(
            StarData star, ShipData playerShip, ShipData target, string slotKey, ItemInstance weapon,
            float distToTarget, bool isAutoFollow,
            ItemsConfig equipConfig, TurnAnimationData anim, int firingSubTurn)
        {
            float range = SRUnits.ToWorld(weapon.GetParam("Range", 0f));
            if (distToTarget <= range)
            {
                var pattern = WeaponSystem.ParseHitPattern(
                    weapon.GetParamString("ShotPattern", weapon.GetParamString("HitPattern", "Point")));
                if (pattern == HitPattern.Homing)
                {
                    MissileSystem.LaunchSalvo(playerShip, target, slotKey, weapon, star, equipConfig, anim, firingSubTurn);
                    GameConsoleController.AddEntry(
                        $"[Ракета] {playerShip.Name} → {target.Name}: ракета запущена.");
                }
                else
                {
                    var result = WeaponSystem.ProcessShot(playerShip, target, slotKey, equipConfig, anim, firingSubTurn);
                    if (result.Hit)
                    {
                        string destroyed = result.TargetDestroyed ? " — УНИЧТОЖЕН" : "";
                        GameConsoleController.AddEntry(
                            $"[Стрельба] {playerShip.Name} → {target.Name}: " +
                            $"{result.HullDamage:F0} урона корпусу{destroyed}.");
                    }
                }
            }
            else if (firingSubTurn == 1 && !isAutoFollow)
            {
                GameConsoleController.AddEntry(
                    $"[Стрельба] Нет оружия в радиусе поражения цели {target.Name}.");
            }
        }
    }
}
