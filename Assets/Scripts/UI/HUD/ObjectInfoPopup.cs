using UnityEngine;
using System.Collections.Generic;
using SRG.Combat;
using SRG.Config;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.NpcAI;
using SRG.NpcAI.Actions;
using SRG.Presentation.Map;
using SRG.Ships;
using SRG.Ships.Movement;
using SRG.Ships.Player;
using SRG.UI.Common;
using SRG.Utils;
using SRG.UI.Screens;
using SRG.Utils;

namespace SRG.UI.HUD
{
    // Всплывающее информационное окно для кликабельных объектов сцены.
    // Открывается при наведении курсора на объект после заданной задержки.
    // Настраивается через GameSettingsConfig (секция Info Popup).
    public class ObjectInfoPopup : MonoBehaviour
    {
        public static ObjectInfoPopup Instance { get; private set; }

        private Camera _cam;
        private ClickableInfo _hoveredInfo;   // объект под курсором в данный момент
        private ClickableInfo _pinnedInfo;    // объект, для которого открыто окно
        private float _hoverTimer;
        private bool _showPopup;
        private Rect _popupRect;

        private PathRenderer _npcPathRenderer;

        private ShipData _contextMenuShip;
        private bool _showContextMenu;
        private Rect _contextMenuRect;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void Start()
        {
            _cam = Camera.main;
            var pathGo = new GameObject("NpcPathPreview");
            pathGo.transform.SetParent(transform, false);
            _npcPathRenderer = pathGo.AddComponent<PathRenderer>();
        }

        // Истина, когда мышь находится над любым активным IMGUI-окном этого контроллера.
        // PlayerShip и другие системы используют это чтобы заблокировать клики по миру.
        public static bool IsMouseBlocked
        {
            get
            {
                if (Instance == null) return false;
                var mp = Input.mousePosition;
                var p = new Vector2(mp.x, Screen.height - mp.y); // Screen → GUI coords
                if (Instance._showPopup && Instance._popupRect.Contains(p)) return true;
                if (Instance._showContextMenu && Instance._contextMenuRect.Contains(p)) return true;
                return false;
            }
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(1) && GalaxyManager.Instance?.Phase != TurnPhase.Simulation
                    && !IsMouseBlocked)
                HandleRightClick();

            // Во время симуляции или режима стрельбы — не показываем
            if (GalaxyManager.Instance?.Phase == TurnPhase.Simulation)
            {
                ClearHover();
                return;
            }
            if (PlayerShip.Instance != null && PlayerShip.Instance.IsWeaponModeActive)
            {
                ClearHover();
                return;
            }
            if (SystemMinimapController.IsMouseOverMinimap)
            {
                ClearHover();
                return;
            }

            // Не убирать окно если курсор находится над ним
            if (_showPopup && IsMouseOverPopup()) return;

            // Проверка объекта под курсором через OverlapPoint (надёжнее Raycast с нулевым направлением)
            var worldPos = _cam.ScreenToWorldPoint(Input.mousePosition);
            var clickPos = new Vector2(worldPos.x, worldPos.y);
            var col = Physics2D.OverlapPoint(clickPos);

            ClickableInfo newHover = null;
            if (col != null)
                newHover = col.GetComponentInParent<ClickableInfo>();

            if (newHover != _hoveredInfo)
            {
                _hoveredInfo = newHover;
                _hoverTimer = 0f;

                if (newHover == null)
                {
                    // Мышь ушла с объекта — закрываем окно
                    Close();
                }
                else if (newHover != _pinnedInfo)
                {
                    // Навели на новый объект — сбросить окно, начать новый отсчёт
                    Close();
                }
            }
            else if (newHover != null && !_showPopup)
            {
                _hoverTimer += Time.deltaTime;
                float delay = GalaxyManager.Instance?.Settings?.InfoPopupHoverDelay ?? 0.5f;
                if (_hoverTimer >= delay)
                    OpenFor(newHover, Input.mousePosition);
            }
            else if (_showPopup && ShouldFollowMouse())
            {
                UpdatePopupPosition(Input.mousePosition);
            }
        }

        private bool ShouldFollowMouse()
            => GalaxyManager.Instance?.Settings?.InfoPopupFollowMouse ?? false;

        private bool IsMouseOverPopup()
        {
            var mp = Input.mousePosition;
            // В IMGUI y-ось инвертирована относительно Screen
            float guiY = Screen.height - mp.y;
            return _popupRect.Contains(new Vector2(mp.x, guiY));
        }

        private void HandleRightClick()
        {
            if (_cam == null) return;
            var worldPos = _cam.ScreenToWorldPoint(Input.mousePosition);
            var col = Physics2D.OverlapPoint(new Vector2(worldPos.x, worldPos.y));

            _showContextMenu = false;
            if (col == null) return;

            var info = col.GetComponentInParent<ClickableInfo>();
            if (info?.Ship == null || info.Ship.IsPlayer) return;

            _contextMenuShip = info.Ship;
            _showContextMenu = true;

            float guiX = Mathf.Clamp(Input.mousePosition.x + 4f, 0f, Screen.width - 180f);
            float guiY = Mathf.Clamp(Screen.height - Input.mousePosition.y + 4f, 0f, Screen.height - 80f);
            _contextMenuRect = new Rect(guiX, guiY, 160f, 10f);
        }

        private void ClearHover()
        {
            _hoveredInfo = null;
            _hoverTimer = 0f;
            _showContextMenu = false;
            Close();
        }

        private void OpenFor(ClickableInfo info, Vector3 screenPos)
        {
            _pinnedInfo = info;
            _showPopup = true;
            UpdatePopupPosition(screenPos);
            RefreshTrajectory();
        }

        private void UpdatePopupPosition(Vector3 screenPos)
        {
            var cfg = GalaxyManager.Instance?.Settings;
            float w   = cfg?.InfoPopupWidth   ?? 300f;
            float offX = cfg?.InfoPopupOffsetX ?? 18f;
            float offY = cfg?.InfoPopupOffsetY ?? 18f;

            float guiX = Mathf.Clamp(screenPos.x + offX, 0f, Screen.width  - w - 4f);
            float guiY = Mathf.Clamp(Screen.height - screenPos.y + offY, 0f, Screen.height - 50f);
            _popupRect = new Rect(guiX, guiY, w, 10f); // высота = auto via GUILayout
        }

        private void RefreshTrajectory()
        {
            if (_npcPathRenderer == null) return;

            var ship = _pinnedInfo?.Ship;
            if (ship == null || ship.IsPlayer)
            {
                _npcPathRenderer.ClearPath();
                return;
            }

            var cfg = GalaxyManager.Instance?.Settings;
            if (!(cfg?.ShowNpcTrajectoryOnHover ?? true))
            {
                _npcPathRenderer.ClearPath();
                return;
            }

            // Будущая траектория: от текущей позиции корабля вперёд по запланированным waypoints
            // (ship.Waypoints[WaypointIndex..end]). Если будущих точек нет (например, корабль стоит)
            // — траектория не рисуется.
            if (ship.Waypoints != null && ship.WaypointIndex < ship.Waypoints.Count)
            {
                int remaining = ship.Waypoints.Count - ship.WaypointIndex;
                var chain = new List<Vector2>(remaining + 1);
                chain.Add(ship.Position);
                for (int i = ship.WaypointIndex; i < ship.Waypoints.Count; i++)
                    chain.Add(ship.Waypoints[i]);
                _npcPathRenderer.DrawPath(chain, SRUnits.ToWorld(ship.ActualSpeed), preSampled: true);
                return;
            }

            _npcPathRenderer.ClearPath();
        }

        public void Close()
        {
            if (!_showPopup && _pinnedInfo == null) return;
            _showPopup = false;
            _pinnedInfo = null;
            _npcPathRenderer?.ClearPath();
        }

        private void OnGUI()
        {
            if (_showContextMenu && _contextMenuShip != null)
            {
                _contextMenuRect = GUILayout.Window(9901, _contextMenuRect, DrawContextMenu,
                    _contextMenuShip.Name, GUILayout.MinWidth(160f));

                // Закрываем меню при клике ВНЕ окна; клик внутри обрабатывается кнопками.
                // Event.current уже в GUI-координатах, _contextMenuRect — тоже, сравнение корректно.
                if (Event.current.type == EventType.MouseDown &&
                    !_contextMenuRect.Contains(Event.current.mousePosition))
                {
                    _showContextMenu = false;
                }
            }

            if (!_showPopup || _pinnedInfo == null) return;
            _popupRect = GUILayout.Window(9900, _popupRect, DrawWindow, GetTitle(), GUILayout.MinWidth(_popupRect.width));

            // Автосдвиг по вертикали: если окно вылезает за нижний/верхний край экрана — сдвигаем,
            // чтобы всегда было видно целиком. GUILayout возвращает уже посчитанную высоту в _popupRect.
            float maxY = Screen.height - _popupRect.height - 4f;
            if (maxY < 0f) maxY = 0f;
            if (_popupRect.y > maxY) _popupRect.y = maxY;
            if (_popupRect.y < 0f)   _popupRect.y = 0f;
        }

        private void DrawContextMenu(int id)
        {
            // Порядок пунктов: 1) посадка на носитель (где возможно), 2) сканирование
            // (всегда, но неактивно для item-контейнеров), 3) все режимы преследования,
            // 4) прямые действия абордажа/буксира/подбора.
            DrawLandOnShipButton(_contextMenuShip);
            DrawScanButton(_contextMenuShip);
            DrawPursuitButtons(_contextMenuShip);
            DrawBoardingTowButtons(_contextMenuShip);

            if (GUILayout.Button("  Закрыть"))
                _showContextMenu = false;
            GUI.DragWindow();
        }

        /// <summary>Посадка на корабль-носитель — показывается только если операция доступна
        /// (тип цели с <c>CanBeLandedOn</c>, не враждебен, не item и т.д.).</summary>
        private void DrawLandOnShipButton(ShipData target)
        {
            var player = PlayerShip.Instance;
            if (player == null || player.ShipData == null || target == null) return;
            if (!player.CanUseFollowMode(target, PlayerShip.FollowMode.LandOnShip)) return;

            bool isActive = player.FollowShipUid == target.Uid
                         && player.CurrentFollowMode == PlayerShip.FollowMode.LandOnShip;
            string prefix = isActive ? "▶ " : "  ";
            string label = PlayerShip.GetFollowModeName(PlayerShip.FollowMode.LandOnShip);
            if (GUILayout.Button($"{prefix}{label}"))
            {
                player.SetFollow(target, PlayerShip.FollowMode.LandOnShip);
                _showContextMenu = false;
            }
        }

        /// <summary>Кнопка сканирования — показывается всегда для наглядности,
        /// но неактивна для item-контейнеров (у них нет корпуса/слотов/экипажа).</summary>
        private void DrawScanButton(ShipData target)
        {
            if (target == null) return;
            bool canScan = !target.IsItem;
            GUI.enabled = canScan;
            if (GUILayout.Button("  Сканировать") && canScan)
            {
                ShipScanUIController.Instance?.OpenFor(target);
                _showContextMenu = false;
            }
            GUI.enabled = true;
        }

        /// <summary>Все режимы преследования, доступные для цели (без LandOnShip — он выше).
        /// Активный режим помечен «▶».</summary>
        private void DrawPursuitButtons(ShipData target)
        {
            var player = PlayerShip.Instance;
            if (player == null || player.ShipData == null || target == null) return;
            if (target.IsItem || target == player.ShipData) return;

            for (int i = 0; i < PursuitModeOrder.Length; i++)
            {
                var mode = PursuitModeOrder[i];
                if (!player.CanUseFollowMode(target, mode)) continue;

                bool isActive = player.FollowShipUid == target.Uid && player.CurrentFollowMode == mode;
                string prefix = isActive ? "▶ " : "  ";
                string label = PlayerShip.GetFollowModeName(mode);

                if (GUILayout.Button($"{prefix}{label}"))
                {
                    player.SetFollow(target, mode);
                    _showContextMenu = false;
                }
            }
        }

        // Порядок кнопок преследования (LandOnShip — отдельно, выше как пункт «посадка»).
        private static readonly PlayerShip.FollowMode[] PursuitModeOrder =
        {
            PlayerShip.FollowMode.PursueAtMaxRange,
            PlayerShip.FollowMode.AttackWithLongRange,
            PlayerShip.FollowMode.AttackWithAllGuns,
            PlayerShip.FollowMode.FollowClose,
            PlayerShip.FollowMode.Board,
            PlayerShip.FollowMode.Tow,
        };

        /// <summary>
        /// Кнопки абордажа/буксира в правом-клик меню. Появляются только если у игрока
        /// есть нужное оборудование и операция доступна. Если связь уже установлена —
        /// показываем кнопку «Отпустить».
        /// </summary>
        private void DrawBoardingTowButtons(ShipData target)
        {
            var player = PlayerShip.Instance?.ShipData;
            if (player == null || target == null || target == player) return;

            var equipCfg = GalaxyManager.Instance?.Context?.ItemsConfig;

            // — Абордаж —
            bool playerHasHook = BoardingSystem.FindGrapplingHook(player) != null;
            if (playerHasHook && !target.IsItem)
            {
                if (player.BoardingTargetUid == target.Uid)
                {
                    if (GUILayout.Button("  Прекратить абордаж"))
                    {
                        BoardingSystem.EndBoarding(player, target);
                        GameConsoleController.AddEntry($"[Абордаж] {target.Name} отпущен.");
                        _showContextMenu = false;
                    }
                }
                else if (BoardingSystem.CanBoard(player, target, equipCfg, out var boardReason))
                {
                    if (GUILayout.Button("  Абордаж"))
                    {
                        if (BoardingSystem.TryBoard(player, target, equipCfg))
                            GameConsoleController.AddEntry($"[Абордаж] {target.Name} взят на абордаж.");
                        _showContextMenu = false;
                    }
                }
                else
                {
                    GUI.enabled = false;
                    GUILayout.Button($"  Абордаж: {boardReason}");
                    GUI.enabled = true;
                }
            }

            // — Буксир —
            bool playerHasRig = TowSystem.FindTowingRig(player) != null;
            if (playerHasRig)
            {
                if (target.TowedByUid == player.Uid)
                {
                    if (GUILayout.Button("  Отпустить буксир"))
                    {
                        // Передаём lookup + конфиг, чтобы система перепаковала якоря у родителя
                        // («ближайший по порядку переходит на освободившийся слот»).
                        var star = GalaxyManager.Instance?.CurrentStar;
                        Dictionary<string, ShipData> lookup = null;
                        if (star?.Ships != null)
                        {
                            lookup = new Dictionary<string, ShipData>(star.Ships.Count);
                            foreach (var s in star.Ships) lookup[s.Uid] = s;
                        }
                        TowSystem.ReleaseTow(player, target, lookup, equipCfg);
                        GameConsoleController.AddEntry($"[Буксир] {target.Name} отпущен.");
                        _showContextMenu = false;
                    }
                    // Блокировка пушек теперь автоматическая: см. правило в CombatSubTurn —
                    // враждебный буксируемый блокируется, союзник/нейтрал стреляет совместно с буксиром.
                }
                else if (target.PulledByUid == player.Uid)
                {
                    if (GUILayout.Button("  Прекратить притягивание"))
                    {
                        PickupSystem.EndPull(player, target);
                        GameConsoleController.AddEntry($"[Буксир] Притягивание {target.Name} прекращено.");
                        _showContextMenu = false;
                    }
                }
                else if (TowSystem.CanTow(player, target, equipCfg, out var towReason))
                {
                    // CanTow гарантирует, что цель в радиусе буксира. TryTow либо сразу прицепит
                    // (если близко), либо начнёт притягивание (несколько ходов).
                    if (GUILayout.Button("  Буксировать"))
                    {
                        var star = GalaxyManager.Instance?.CurrentStar;
                        Dictionary<string, ShipData> lookup = null;
                        if (star?.Ships != null)
                        {
                            lookup = new Dictionary<string, ShipData>(star.Ships.Count);
                            foreach (var s in star.Ships) lookup[s.Uid] = s;
                        }
                        if (TowSystem.TryTow(player, target, equipCfg, lookup))
                        {
                            if (target.TowedByUid == player.Uid)
                                GameConsoleController.AddEntry($"[Буксир] {target.Name} прицеплен.");
                            else if (target.PulledByUid == player.Uid)
                                GameConsoleController.AddEntry($"[Буксир] {target.Name} притягивается…");
                        }
                        _showContextMenu = false;
                    }
                }
                else
                {
                    GUI.enabled = false;
                    GUILayout.Button($"  Буксир: {towReason}");
                    GUI.enabled = true;
                }
            }

            // — Очередь захвата (CargoGrabber) для item-целей —
            if (target.IsItem && PickupSystem.FindCargoGrabber(player) != null)
            {
                bool alreadyQueued = player.PulledQueue != null && player.PulledQueue.Contains(target.Uid);
                if (alreadyQueued)
                {
                    if (GUILayout.Button("  Убрать из очереди захвата"))
                    {
                        player.PulledQueue.Remove(target.Uid);
                        GameConsoleController.AddEntry("[Захват] Цель удалена из очереди.");
                        _showContextMenu = false;
                    }
                }
                else
                {
                    if (GUILayout.Button("  В очередь захвата"))
                    {
                        PlayerShip.Instance?.EnqueuePullTarget(target.Uid);
                        _showContextMenu = false;
                    }
                }
            }
        }

        private string GetTitle()
        {
            if (_pinnedInfo.Star     != null) return $"Звезда: {_pinnedInfo.Star.Name}";
            if (_pinnedInfo.Planet   != null) return $"Планета: {_pinnedInfo.Planet.Name}";
            if (_pinnedInfo.Ship     != null)
                return _pinnedInfo.Ship.IsItem
                    ? $"Контейнер: {_pinnedInfo.Ship.Name}"
                    : $"Корабль: {_pinnedInfo.Ship.Name}";
            if (_pinnedInfo.Asteroid != null) return "Астероид";
            return "Объект";
        }

        private void DrawWindow(int id)
        {
            // Крестик в правом углу шапки. IMGUI не рисует системный close-control,
            // поэтому вешаем кнопку поверх области заголовка.
            var closeRect = new Rect(_popupRect.width - 22f, 2f, 20f, 18f);
            if (GUI.Button(closeRect, "×"))
            {
                _hoveredInfo = null;
                Close();
                return;
            }

            if      (_pinnedInfo.Star     != null) DrawStar(_pinnedInfo.Star);
            else if (_pinnedInfo.Planet   != null) DrawPlanet(_pinnedInfo.Planet);
            else if (_pinnedInfo.Ship     != null)
            {
                var cfg = GalaxyManager.Instance?.Settings;
                bool showTraj = !_pinnedInfo.Ship.IsPlayer && (cfg?.ShowNpcTrajectoryOnHover ?? true);
                DrawShip(_pinnedInfo.Ship, _pinnedInfo.NpcController, showTraj);
            }
            else if (_pinnedInfo.Asteroid != null) DrawAsteroid(_pinnedInfo.Asteroid);

            GUI.DragWindow();
        }

        // ── Звезда ────────────────────────────────────────────────────────────────

        private static GUIStyle _richLabelStyle;

        private static GUIStyle GetRichLabelStyle()
        {
            if (_richLabelStyle == null)
                _richLabelStyle = new GUIStyle(GUI.skin.label) { richText = true };
            return _richLabelStyle;
        }

        private static void DrawStar(StarData star)
        {
            GUILayout.Label($"Тип: {star.Type}   Цвет: {star.Color}");
            GUILayout.Label($"Owner: {star.Owner ?? "—"}   Race: {star.Race ?? "—"}");
            GUILayout.Label($"Планет: {star.Planets.Count}   Кораблей: {star.Ships.Count}");
            GUILayout.Space(4f);
            GUILayout.Label("— Планеты —");
            var ctx = GalaxyManager.Instance?.Context;
            var style = GetRichLabelStyle();
            foreach (var p in star.Planets)
            {
                Color c = ctx != null
                    ? OwnershipDisplayResolver.ResolvePlanetMinimapColor(p, ctx)
                    : Color.white;
                string hex = ColorUtility.ToHtmlStringRGB(c);
                GUILayout.Label($"  • <color=#{hex}>{p.Name}</color>  ({p.Type}, {p.Size})", style);
            }
        }

        // ── Планета ───────────────────────────────────────────────────────────────

        private static string TfArrow(PlanetData p, string key, float current)
        {
            if (!p.IsTerraformed || !p.TerraformOriginals.TryGetValue(key, out float orig)) return "";
            if (current > orig + 0.001f) return " ↑";
            if (current < orig - 0.001f) return " ↓";
            return "";
        }

        private static void DrawPlanet(PlanetData planet)
        {
            GUILayout.Label($"Тип: {planet.Type}   Размер: {planet.Size}");
            if (planet.IsTerraformed)
                GUILayout.Label("Терраформирована");
            GUILayout.Label($"Race: {planet.Race ?? "—"}   Owner: {planet.Owner ?? "—"}");
            DrawRelationToPlayer(planet);
            GUILayout.Label($"Орбита: {planet.OrbitRadius:F0}   Индекс: {planet.OrbitIndex}");
            GUILayout.Label($"Скор. орбиты: {planet.OrbitSpeed:F1}   Эксц.: {planet.OrbitEccentricity:F2}");
            GUILayout.Label($"Наклон: {planet.OrbitTiltDeg:F1}°");
            GUILayout.Label($"Вращение (день): {planet.DaySpeed}");
            GUILayout.Label($"Плотность: {planet.Density:F2} г/см³   Гравитация: {planet.SurfaceGravity:F2} м/с²  (по размеру: {planet.Size})");
            GUILayout.Label($"Геоактивность: {planet.GeoActivity:F2}  (плотн.={planet.Density:F2} г/см³, спутн.={planet.SatellitesCount})");
            GUILayout.Label($"Звёздный поток: {planet.SolarFlux:F2}  (орбита={planet.OrbitRadius:F0})");
            GUILayout.Label($"Маг. поле: {planet.MagneticField:F2}  (g={planet.SurfaceGravity:F2} м/с², геоакт.={planet.GeoActivity:F2}, спутн.={planet.SatellitesCount})");
            GUILayout.Label($"Атм. давление: {planet.AtmPressure:F2} атм{TfArrow(planet, "AtmPressure", planet.AtmPressure)}  (g={planet.SurfaceGravity:F2}, геоакт.={planet.GeoActivity:F2}, B={planet.MagneticField:F2}, flux={planet.SolarFlux:F2})");
            GUILayout.Label($"Радиация: {planet.SurfaceRadiation:F2}{TfArrow(planet, "SurfaceRadiation", planet.SurfaceRadiation)}  (flux={planet.SolarFlux:F2}, P={planet.AtmPressure:F2}, B={planet.MagneticField:F2})");
            GUILayout.Label($"Вода: {planet.WaterAbundance:F2}{TfArrow(planet, "WaterAbundance", planet.WaterAbundance)}  (геоакт.={planet.GeoActivity:F2}, спутн.={planet.SatellitesCount}, B={planet.MagneticField:F2}, flux={planet.SolarFlux:F2})");
            GUILayout.Label($"Равн. темп.: {planet.SurfaceTemp:F1} К{TfArrow(planet, "SurfaceTemp", planet.SurfaceTemp)}  (flux={planet.SolarFlux:F2}, P={planet.AtmPressure:F2}, вода={planet.WaterAbundance:F2})");
            GUILayout.Label($"O₂: {planet.OxygenPercent:F1}%{TfArrow(planet, "OxygenPercent", planet.OxygenPercent)}  (вода={planet.WaterAbundance:F2}, B={planet.MagneticField:F2}, P={planet.AtmPressure:F2}, flux={planet.SolarFlux:F2}, геоакт.={planet.GeoActivity:F2})");

            var ctx = GalaxyManager.Instance?.Context;
            string hab = GalaxyLogger.FormatRaceHabitability(planet, ctx?.Config?.Races, ctx?.AvailableRaces, richText: false);
            if (!string.IsNullOrEmpty(hab))
            {
                GUILayout.Space(4f);
                GUILayout.Label("— Пригодность для рас —");
                GUILayout.Label(hab);
            }

            if (!string.IsNullOrEmpty(planet.Atmosphere))
                GUILayout.Label($"Атмосфера: {planet.Atmosphere}");
            if (planet.SatellitesCount > 0)
                GUILayout.Label($"Спутников: {planet.SatellitesCount}");
            if (planet.CustomProperties?.Count > 0)
            {
                GUILayout.Space(4f);
                GUILayout.Label("— Свойства —");
                foreach (var kv in planet.CustomProperties)
                    GUILayout.Label($"  {kv.Key}: {kv.Value}");
            }
        }

        // ── Корабль ───────────────────────────────────────────────────────────────

        private static void DrawShip(ShipData ship, NpcController npc, bool showTrajectory = false)
        {
            if (ship.IsItem)
            {
                DrawItemContainer(ship);
                return;
            }
            GUILayout.Label($"Тип: {ship.ShipTypeId}   Размер спрайта: {ship.SpriteWorldSize:F2} ед.");
            GUILayout.Label($"Owner: {ship.Owner ?? "—"}   Race: {ship.Race ?? "—"}");
            if (!ship.IsPlayer) DrawRelationToPlayer(ship);
            GUILayout.Label($"Корпус: {ship.CurrentHull} / {ship.MaxHull}   Криминал: {ship.CrimeRating:F0}");
            GUILayout.Label($"Деньги: {ship.Money:N0} кр.");
            GUILayout.Label($"Скорость: {ship.ActualSpeed:F2}   Разворот: {ship.TurnSpeedDeg:F0}°/ход");
            GUILayout.Label($"Позиция: ({ship.Position.x:F1}, {ship.Position.y:F1})");
            if (!ship.IsPlayer) DrawShipCombatAndPersonality(ship);

            var shield = EquipmentSystem.GetEquipped(ship, SlotKeys.Shield);
            if (shield != null && shield.IsWorking)
                GUILayout.Label($"Щит: блок {shield.GetParam("BlockPercent", 0f):F0}%   прочн {shield.Durability}/{shield.MaxDurability}");

            // Снаряжение
            GUILayout.Space(4f);
            GUILayout.Label("— Снаряжение —");
            foreach (var kv in ship.Equipment.Slots)
            {
                if (kv.Value == null) continue;
                if (!ship.AllItems.TryGetValue(kv.Value, out var item)) continue;
                string dur = item.NoWear ? "∞" : $"{item.Durability}/{item.MaxDurability}";
                GUILayout.Label($"  [{kv.Key}] {item.Name}  ({dur})");
            }

            // Приказ и цель
            GUILayout.Space(4f);
            if (ship.IsPlayer)
            {
                GUILayout.Label("Приказ: (игрок)");
                GUILayout.Label($"Цель движения: ({ship.TargetPosition.x:F1}, {ship.TargetPosition.y:F1})");
            }
            else
            {
                string order = npc != null ? npc.CurrentOrder : "—";
                string activity = ship.Brain?.CurrentActivity?.DebugName ?? "—";
                GUILayout.Label($"Приказ: {order}   Активность: {activity}");
                DrawNpcTarget(ship);
                DrawShipDebugExtras(ship);
                if (showTrajectory)
                    GUILayout.Label("(траектория показана на сцене)");
            }

            // Активные эффекты
            if (ship.ActiveEffects?.Count > 0)
            {
                GUILayout.Space(4f);
                GUILayout.Label("— Эффекты —");
                foreach (var e in ship.ActiveEffects)
                    GUILayout.Label($"  {e.Type}  ×{e.Magnitude:F1}  [{e.TurnsLeft} т.]");
            }
        }

        private static void DrawRelationToPlayer(ShipData ship)
        {
            int val = Relations.GetToPlayer(ship);
            RelationLevel lvl = OwnerRaceRelationsManager.ToLevel(val);
            GUILayout.Label($"Отн. к игроку: {val} ({lvl})");
        }

        private static void DrawRelationToPlayer(PlanetData planet)
        {
            int val = Relations.GetToPlayer(planet);
            RelationLevel lvl = OwnerRaceRelationsManager.ToLevel(val);
            GUILayout.Label($"Отн. к игроку: {val} ({lvl})");
        }

        private static void DrawShipCombatAndPersonality(ShipData ship)
        {
            var brain = ship.Brain;
            var p = brain?.Personality ?? ship.Personality;
            GUILayout.Space(4f);
            string combatClass = brain != null
                ? brain.CombatClass.ToString()
                : NpcBrain.ResolveCombatClass(ship.ShipTypeId).ToString();
            GUILayout.Label($"Класс боя: {combatClass}   Тип: {ship.ShipTypeId}");
            if (p != null)
            {
                GUILayout.Label("— Характер —");
                GUILayout.Label(
                    $"  Агр {p.Aggression:F0}  Ост {p.Caution:F0}  Жад {p.Greed:F0}  " +
                    $"Дсп {p.Discipline:F0}  Трб {p.Tribalism:F0}  Вдт {p.Vendetta:F0}");
                GUILayout.Label(
                    $"  Фрустрация {p.Frustration:F0}   " +
                    $"EngageThr {p.EngageThreshold:F2}   FleeHP {p.FleeHullPercent * 100f:F0}%");
            }

            // Оценка боя относительно игрока (полезно для дебага AI-решений).
            var player = PlayerShip.Instance?.ShipData;
            if (player != null && brain != null && player != ship)
            {
                float ctw = brain.ChanceToWin(player);
                GUILayout.Label($"Шанс победы vs игрок: {ctw * 100f:F0}%");
            }
        }

        private static void DrawShipDebugExtras(ShipData ship)
        {
            // Захват/буксир/абордаж — важное состояние, влияющее на поведение AI.
            bool anyCarry = !string.IsNullOrEmpty(ship.BoardedByUid)
                         || !string.IsNullOrEmpty(ship.BoardingTargetUid)
                         || !string.IsNullOrEmpty(ship.TowedByUid)
                         || !string.IsNullOrEmpty(ship.PulledByUid);
            if (anyCarry)
            {
                var star = GalaxyManager.Instance?.CurrentStar;
                if (!string.IsNullOrEmpty(ship.BoardedByUid))
                    GUILayout.Label($"Абордирован: {ResolveShipName(star, ship.BoardedByUid)}");
                if (!string.IsNullOrEmpty(ship.BoardingTargetUid))
                    GUILayout.Label($"Абордирует: {ResolveShipName(star, ship.BoardingTargetUid)}");
                if (!string.IsNullOrEmpty(ship.TowedByUid))
                    GUILayout.Label($"Буксируется: {ResolveShipName(star, ship.TowedByUid)}");
                if (!string.IsNullOrEmpty(ship.PulledByUid))
                    GUILayout.Label($"Притягивается: {ResolveShipName(star, ship.PulledByUid)} ({ship.PullMode})");
            }

            // Последний агрессор — критично для дебага реактивной AI-логики.
            if (!string.IsNullOrEmpty(ship.LastAttackerUid))
            {
                var star = GalaxyManager.Instance?.CurrentStar;
                int currentTurn = GalaxyManager.Instance?.GeneratedGalaxy?.CurrentTurn ?? 0;
                int turnsAgo = currentTurn - ship.LastAttackerTurn;
                GUILayout.Label($"Последний агрессор: {ResolveShipName(star, ship.LastAttackerUid)}  ({turnsAgo} т. назад)");
            }

            // Гиперпрыжок / посадка.
            if (!string.IsNullOrEmpty(ship.HyperjumpTargetStarUid))
            {
                string starName = ship.HyperjumpTargetStarUid;
                var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
                if (galaxy != null && galaxy.StarsMap.TryGetValue(ship.HyperjumpTargetStarUid, out var target))
                    starName = target.Name;
                GUILayout.Label($"Гиперпрыжок: {ship.HyperjumpPhase} → {starName}");
            }
            if (!string.IsNullOrEmpty(ship.LandingPlanetUid) || !string.IsNullOrEmpty(ship.LandedPlanetUid))
                GUILayout.Label($"Посадка: {ship.LandingPhase}   Landing={ship.LandingPlanetUid ?? "—"}   Landed={ship.LandedPlanetUid ?? "—"}");
            if (!string.IsNullOrEmpty(ship.LandedOnShipUid))
            {
                var carrier = ship.CurrentStar?.Ships?.Find(s => s.Uid == ship.LandedOnShipUid);
                GUILayout.Label($"Стыковка: на борту {carrier?.Name ?? SpriteUtility.ShortId(ship.LandedOnShipUid)}");
            }
        }

        private static void DrawNpcTarget(ShipData ship)
        {
            var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
            StarData currentStar = null;
            if (galaxy != null && !string.IsNullOrEmpty(ship.CurrentStarUid))
                galaxy.StarsMap.TryGetValue(ship.CurrentStarUid, out currentStar);

            var activity = ship.Brain?.CurrentActivity;
            if (activity == null)
            {
                GUILayout.Label($"Цель: ({ship.TargetPosition.x:F1}, {ship.TargetPosition.y:F1})");
                return;
            }

            switch (activity)
            {
                case ActionPursueAndAttack pursue:
                {
                    string name = ResolveShipName(currentStar, pursue.TargetShipUid);
                    GUILayout.Label($"Цель: корабль  {name}");
                    break;
                }
                case ActionDisable disable:
                {
                    string name = ResolveShipName(currentStar, disable.TargetShipUid);
                    GUILayout.Label($"Цель: корабль  {name}  (вывести из строя)");
                    break;
                }
                case ActionRob rob:
                {
                    string name = ResolveShipName(currentStar, rob.TargetShipUid);
                    GUILayout.Label($"Цель: корабль  {name}  (ограбить)");
                    break;
                }
                case ActionFlee flee:
                {
                    string name = ResolveShipName(currentStar, flee.ThreatShipUid);
                    GUILayout.Label($"Цель: бегство от  {name}");
                    break;
                }
                case ActionEscort escort:
                {
                    string name = ResolveShipName(currentStar, escort.EscortedShipUid);
                    GUILayout.Label($"Цель: эскорт  {name}");
                    break;
                }
                case ActionRequestCeasefire ceasefire:
                {
                    string name = ResolveShipName(currentStar, ceasefire.AggressorShipUid);
                    GUILayout.Label($"Цель: перемирие с  {name}");
                    break;
                }
                case ActionHyperJumpTo jump:
                {
                    string starName = null;
                    if (galaxy != null && galaxy.StarsMap.TryGetValue(jump.TargetStarUid, out var targetStar))
                        starName = targetStar.Name;
                    GUILayout.Label($"Цель: гиперпрыжок → {starName ?? SpriteUtility.ShortId(jump.TargetStarUid)}");
                    break;
                }
                default:
                    GUILayout.Label($"Цель: ({ship.TargetPosition.x:F1}, {ship.TargetPosition.y:F1})");
                    break;
            }
        }

        private static void DrawItemContainer(ShipData container)
        {
            // Item-контейнер — не корабль: показываем содержимое (имя/тип/стат предмета),
            // а не корпус/слоты/приказ. Сканирование item запрещено отдельно.
            var items = container.Inventory?.Items;
            if (items == null || items.Count == 0)
            {
                GUILayout.Label("Пустой контейнер");
                return;
            }
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it == null) continue;
                GUILayout.Label(ShipFormView.BuildItemDescription(it));
                if (i < items.Count - 1) GUILayout.Space(4f);
            }
        }

        private static string ResolveShipName(StarData star, string uid)
        {
            if (star == null || string.IsNullOrEmpty(uid)) return SpriteUtility.ShortId(uid);
            foreach (var s in star.Ships)
                if (s.Uid == uid) return s.Name;
            return SpriteUtility.ShortId(uid);
        }

        // ── Астероид ──────────────────────────────────────────────────────────────

        private static void DrawAsteroid(AsteroidData asteroid)
        {
            GUILayout.Label($"ID: {SpriteUtility.ShortId(asteroid.Uid)}");
            GUILayout.Label($"Тип: {asteroid.TypeId ?? "—"}");
            GUILayout.Label($"Масса: {asteroid.Mass:F2}");
            GUILayout.Label($"Скорость: {asteroid.Velocity.magnitude:F3}");
            GUILayout.Label($"Радиус коллизии: {asteroid.CollisionRadius:F2}");
            GUILayout.Label($"Позиция: ({asteroid.Position.x:F1}, {asteroid.Position.y:F1})");
        }
    }
}
