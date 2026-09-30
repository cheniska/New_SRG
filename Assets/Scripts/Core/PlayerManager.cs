using UnityEngine;
using System;
using SRG.Combat;
using SRG.Config;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Galaxy.Politics;
using SRG.Galaxy.Simulation;
using SRG.Ships;
using SRG.Controllers;
using SRG.UI.Screens;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.Core
{
    // Управляет состоянием игрока: корабль, посадка, взлёт, спавн.
    // Должен быть на том же GameObject, что и GalaxyManager (DontDestroyOnLoad).
    public class PlayerManager : MonoBehaviour, IPlayerHost
    {
        public static PlayerManager Instance { get; private set; }

        public static event Action<ILandingSite> OnLanded;
        public static event Action               OnLeft;

        // Обратная совместимость: старые события с типом PlanetData. Fire-ятся только для
        // настоящих планет (не станций/носителей); подписчикам, которых интересует «любая
        // посадка», лучше слушать OnLanded/OnLeft.
        public static event Action<PlanetData> OnPlanetLanded;
        public static event Action             OnPlanetLeft;

        /// <summary>
        /// Срабатывает в момент, когда HP игрока обнуляется ДО регистрации смерти.
        /// Подписчики могут установить info.Cancelled = true (например, спасательная капсула)
        /// — тогда игрок не умирает, а его HP восстанавливается до 1.
        /// </summary>
        public static event Action<PlayerDeathInfo> OnPlayerDeathAttempt;

        /// <summary>Игрок мёртв и взрыв уже отыгран. Подписчик — экран смерти.</summary>
        public static event Action<PlayerDeathInfo> OnPlayerDeathConfirmed;

        // Пороги для CheckPauseConditions:
        // — в автобое игра ставит планировочную паузу только при тяжёлом уроне за ход;
        // — вне автобоя — при низком HP (более мягкий порог, чтобы вернуть управление раньше).
        private const float HEAVY_DAMAGE_FRACTION_PER_TURN = 0.20f;
        private const float LOW_HULL_AUTOCOMBAT_FRACTION   = 0.05f;
        private const float LOW_HULL_MANUAL_FRACTION       = 0.25f;

        public ShipData PlayerShipData { get; private set; }

        /// <summary>Текущая посадочная цель — реальная планета, станция или корабль-носитель.
        /// null, если игрок в космосе.</summary>
        public ILandingSite LandedSite { get; private set; }

        /// <summary>Обратная совместимость: настоящая планета, если посадка на планету; null
        /// для станций/носителей. Прежние callers, работавшие только с планетами, продолжают
        /// работать. Новый код — <see cref="LandedSite"/>.</summary>
        public PlanetData LandedPlanet => LandedSite as PlanetData;
        public bool IsLanded => LandedSite != null;

        /// <summary>true, если смерть игрока уже была зарегистрирована в этом ходу.
        /// Защита от повторных KillPlayer-вызовов из разных источников урона.</summary>
        public bool IsPlayerDead { get; private set; }

        /// <summary>true после того, как игрок выбрал «Наблюдать за миром» на экране
        /// смерти. В этом режиме CheckPauseConditions не запрашивает паузу по LowHull,
        /// и симуляция продолжается ход за ходом без остановок.</summary>
        public bool IsObserving { get; private set; }

        /// <summary>Последняя причина смерти, кэшируется до показа экрана смерти.</summary>
        public PlayerDeathInfo LastDeathInfo { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            GameWorld.Attach(this);
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            GameWorld.Detach(this);
            Instance = null;
        }

        /// <summary>UID корабля, за которым летит игрок (режим «следовать»), либо null.</summary>
        public string FollowShipUid => PlayerShip.Instance != null ? PlayerShip.Instance.FollowShipUid : null;

        public void ClearPlayerShip()
        {
            PlayerShipData = null;
            IsPlayerDead = false;
            IsObserving = false;
            LastDeathInfo = null;
        }

        /// <summary>Переключает игрока в режим наблюдения после смерти. Вызывается
        /// из DeathScreenController при нажатии кнопки «Наблюдать за миром».</summary>
        public void EnterObserverMode()
        {
            if (!IsPlayerDead) return;
            IsObserving = true;
        }

        public ShipData GetOrFindPlayerShip()
        {
            if (PlayerShipData != null) return PlayerShipData;

            var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
            if (galaxy == null) return null;

            foreach (var star in galaxy.StarsMap.Values)
            {
                var ships = star.Ships;
                int count = ships.Count;
                for (int i = 0; i < count; i++)
                    if (ships[i].IsPlayer)
                    {
                        PlayerShipData = ships[i];
                        return ships[i];
                    }
            }
            return null;
        }

        public StarData FindPlayerStar()
        {
            var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
            if (galaxy == null) return null;

            foreach (var star in galaxy.StarsMap.Values)
            {
                var ships = star.Ships;
                int count = ships.Count;
                for (int i = 0; i < count; i++)
                    if (ships[i].IsPlayer) return star;
            }
            return null;
        }

        public void SpawnPlayer(StarData star, GalaxyGenerationContext ctx, GameSettingsConfig settings)
        {
            if (star == null) return;

            var ships = star.Ships;
            int count = ships.Count;
            for (int i = 0; i < count; i++)
                if (ships[i].IsPlayer) return;

            string shipTypeId  = settings != null ? settings.PlayerShipTypeId : "Ranger";
            string playerOwner = settings != null ? settings.PlayerOwnerId    : "Race3_Main";
            string playerRace  = settings != null ? settings.PlayerRaceId     : "Race3";

            PlanetData spawnPlanet = null;
            foreach (var p in star.Planets)
                if (p.Owner == playerOwner) { spawnPlanet = p; break; }
            if (spawnPlanet == null)
                foreach (var p in star.Planets)
                    if (!string.IsNullOrEmpty(p.Owner) && p.Owner != GalaxyConstants.OWNER_NONE_KEY) { spawnPlanet = p; break; }
            if (spawnPlanet == null && star.Planets.Count > 0)
                spawnPlanet = star.Planets[0];

            Vector2 spawnPos;
            if (spawnPlanet != null)
                spawnPos = OrbitMath.GetPlanetWorldPosition(spawnPlanet);
            else
                spawnPos = settings != null ? settings.PlayerSpawnPosition : new Vector2(1.5f, 0f);

            var playerShip = ShipFactory.BuildShipData(shipTypeId, playerOwner, playerRace, ctx.AvailableShipTypes, ctx);
            if (playerShip == null)
            {
                Debug.LogError($"[PlayerManager] Не удалось создать ShipData для '{shipTypeId}'.");
                return;
            }

            playerShip.Name = "Player";
            playerShip.IsPlayer = true;
            playerShip.Money = settings != null ? settings.PlayerStartMoney : 1000;

            if (ctx.AvailableShipTypes.TryGetValue(shipTypeId, out var shipTypeCfg)
                && (shipTypeCfg.StarterKit != null || shipTypeCfg.StarterWeapons != null))
            {
                ItemFactory.EquipStarterKit(playerShip, shipTypeCfg.StarterKit, ctx.ItemsConfig, ctx.Config, shipTypeCfg.StarterWeapons);
            }

            playerShip.SpawnedFromUid = spawnPlanet?.Uid;
            playerShip.InitOnSpawn(star, spawnPos);
            star.Ships.Add(playerShip);
            PlayerShipData = playerShip;

            string spawnDesc = spawnPlanet != null ? $"планета '{spawnPlanet.Name}' ({spawnPos})" : $"позиция {spawnPos}";
            Debug.Log($"[PlayerManager] Игрок создан в '{star.Name}', место спавна: {spawnDesc}.");
        }

        /// <summary>Единая точка входа: посадка/стыковка на любую <see cref="ILandingSite"/>.
        /// Работает для планет (<see cref="PlanetData"/>), станций и кораблей-носителей
        /// (<see cref="ShipData"/>). PlanetUIController слушает <see cref="OnLanded"/> и по
        /// <see cref="ILandingSite.Kind"/> выбирает набор вкладок.</summary>
        // Методы IPlayerHost меняют сцену/UI: из расчёта хода (фоновый поток) выполняются на
        // главном, поток расчёта ждёт — порядок и результат те же, что при расчёте на главном.
        public void LandOn(ILandingSite site)
        {
            if (!MainThread.IsCurrent) { MainThread.Send(() => LandOn(site)); return; }
            if (site == null || IsLanded) return;
            LandedSite = site;
            GalaxyManager.Instance?.RequestPlanning(PlanningReason.PlayerInput);
            SystemViewManager.Instance?.SetSystemVisible(false);
            OnLanded?.Invoke(site);
            if (site is PlanetData planet) OnPlanetLanded?.Invoke(planet);
            Debug.Log($"[PlayerManager] Посадка ({site.Kind}): {site.Name}");
        }

        /// <summary>Стыковка с носителем — станцией или обычным кораблём-носителем. Готовит
        /// <see cref="SettlementData"/> носителя (лениво создаёт, наполняет магазин на станции)
        /// и открывает UI через общий путь <see cref="LandOn"/>. Разница «станция vs линкор»
        /// решается PlanetUIController по <see cref="ILandingSite.Kind"/>.</summary>
        public void DockOnShip(ShipData carrier)
        {
            if (!MainThread.IsCurrent) { MainThread.Send(() => DockOnShip(carrier)); return; }
            if (carrier == null || IsLanded) return;
            var site = SRG.Ships.Services.ShipDockingService.AsLandingSite(carrier);
            if (site == null) return;
            PlayerShipData?.FreezeRoute();
            LandOn(site);
        }

        public void LeavePlanet()
        {
            if (!MainThread.IsCurrent) { MainThread.Send(() => LeavePlanet()); return; }
            if (!IsLanded) return;
            var was = LandedSite;
            LandedSite = null;
            SystemViewManager.Instance?.SetSystemVisible(true);
            OnLeft?.Invoke();
            if (was is PlanetData) OnPlanetLeft?.Invoke();
            Debug.Log("[PlayerManager] Взлёт.");
        }

        /// <summary>
        /// Регистрирует попытку убить игрока. Источники урона (WeaponSystem, MissileSystem,
        /// AsteroidSystem) обязаны вызывать это при достижении CurrentHull <= 0 у игрока.
        /// Возвращает true, если смерть подтверждена; false — если перехвачена (например,
        /// спасательной капсулой). В случае перехвата HP восстанавливается до 1 — источник
        /// урона не должен помечать корабль уничтоженным.
        /// </summary>
        public bool KillPlayer(PlayerDeathCause cause, string killerName = null, string killerOwner = null)
        {
            if (!MainThread.IsCurrent) return MainThread.Send(() => KillPlayer(cause, killerName, killerOwner));
            if (IsPlayerDead) return true;
            var ship = PlayerShipData ?? GetOrFindPlayerShip();
            if (ship == null) return false;

            var info = new PlayerDeathInfo
            {
                Cause       = cause,
                KillerName  = killerName,
                KillerOwner = killerOwner,
                StarName    = ship.CurrentStar?.Name,
                Position    = ship.Position,
            };

            OnPlayerDeathAttempt?.Invoke(info);
            if (info.Cancelled)
            {
                ship.CurrentHull = Mathf.Max(1, ship.CurrentHull);
                Debug.Log($"[PlayerManager] Смерть игрока перехвачена ({cause}).");
                return false;
            }

            IsPlayerDead = true;
            LastDeathInfo = info;
            Debug.Log($"[PlayerManager] Игрок убит: cause={cause} killer='{killerName}'.");

            string starName = string.IsNullOrEmpty(info.StarName) ? "?" : info.StarName;
            string killer = string.IsNullOrEmpty(killerName) ? "неизвестными" : killerName;
            GalaxyNewsService.Post(GalaxyNewsService.CAT_PLAYER,
                $"Экстренное сообщение! В системе {starName} погиб вольный пилот {ship.Name}. Обстоятельства смерти: {killer} ({cause}).");
            return true;
        }

        /// <summary>
        /// Вызывается из SystemViewManager после того, как взрыв корабля игрока отыгран.
        /// Запускает подписчиков экрана смерти.
        /// </summary>
        public void NotifyDeathAnimationFinished()
        {
            if (!IsPlayerDead) return;
            OnPlayerDeathConfirmed?.Invoke(LastDeathInfo);
        }

        public void CheckPauseConditions(TurnAnimationData anim)
        {
            var ship = PlayerShipData ?? GetOrFindPlayerShip();
            if (ship == null) return;

            if (IsPlayerDead || ship.CurrentHull <= 0)
            {
                // В режиме наблюдения симуляция должна идти без остановок —
                // LowHull-пауза имеет смысл только до первого подтверждения смерти.
                if (!IsObserving)
                    anim.PlanningReasons.Add(PlanningReason.LowHull);
                return;
            }

            if (ship.MaxHull <= 0)
            {
                Debug.LogWarning($"[PlayerManager] Player ship '{ship.Uid}' has MaxHull=0 — hull check skipped.");
                return;
            }

            float hullPct = (float)ship.CurrentHull / ship.MaxHull;
            var playerCtrl = PlayerShip.Instance;
            bool autoCombat = playerCtrl != null && playerCtrl.IsFollowingShip;

            if (autoCombat)
            {
                // Автобой: пауза только при тяжёлых событиях, иначе игра не выходит в planning.
                // 1) Получено >20% MaxHull урона за этот ход.
                if (playerCtrl.HullBeforeLastSimulation > 0)
                {
                    int dmg = playerCtrl.HullBeforeLastSimulation - ship.CurrentHull;
                    if (dmg > 0 && (float)dmg / ship.MaxHull > HEAVY_DAMAGE_FRACTION_PER_TURN)
                        anim.PlanningReasons.Add(PlanningReason.HeavyDamage);
                }

                // 2) Цель следования уничтожена ИМЕННО в этом ходу.
                // Сверяемся с anim.DeathUids (список погибших на текущем ходу), а не
                // с фактом отсутствия в star.Ships — иначе триггер ложно срабатывал бы
                // при гиперпрыжке/миграции follow-цели или при star == null.
                if (!string.IsNullOrEmpty(playerCtrl.FollowShipUid)
                    && anim.DeathUids.Contains(playerCtrl.FollowShipUid))
                    anim.PlanningReasons.Add(PlanningReason.EnemyDestroyed);

                // 3) HP игрока упало ниже порога автобоя (5%).
                if (hullPct < LOW_HULL_AUTOCOMBAT_FRACTION)
                    anim.PlanningReasons.Add(PlanningReason.LowHull);
            }
            else
            {
                if (hullPct < LOW_HULL_MANUAL_FRACTION)
                    anim.PlanningReasons.Add(PlanningReason.LowHull);
            }
        }
    }
}
