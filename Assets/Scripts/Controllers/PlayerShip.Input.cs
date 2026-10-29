using UnityEngine;
using UnityEngine.EventSystems;
using SRG.Core;
using SRG.Dialog;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.UI.Common;
using SRG.UI.HUD;
using SRG.UI.Screens;
using SRG.Utils;
using SRG.Ships;
using SRG.Presentation.Common;
using SRG.Presentation.World;

namespace SRG.Controllers
{
    // PlayerShip — Input/UI-режимы (этап T2 рефакторинга, июнь 2026). См. PlayerShip.cs.
    public partial class PlayerShip
    {

        public void ToggleWeaponMode()
        {
            _weaponModeActive = !_weaponModeActive;
            if (_weaponModeActive)
            {
                if (_dialogModeActive) DisableDialogMode(silent: true);
                GameLog.Add("[Оружие] Наведение включено.");
            }
            else
            {
                GameLog.Add("[Оружие] Наведение выключено.");
            }
        }

        public void ToggleDialogMode()
        {
            _dialogModeActive = !_dialogModeActive;
            if (_dialogModeActive)
            {
                if (_weaponModeActive) _weaponModeActive = false;
                DialogCursor.Apply();
                GameLog.Add("[Связь] Укажите цель для связи.");
            }
            else
            {
                DialogCursor.Reset();
                GameLog.Add("[Связь] Режим связи выключен.");
            }
        }

        public void ToggleForsage() => Pilot?.ToggleForsage();

        private void DisableDialogMode(bool silent = false)
        {
            if (!_dialogModeActive) return;
            _dialogModeActive = false;
            DialogCursor.Reset();
            if (!silent) GameLog.Add("[Связь] Режим связи выключен.");


        }
        private void HandleInput()
        {
            if (GameConsoleController.IsOpen) return;

            if (HandleKeyInput()) return;

            if (!Input.GetMouseButtonDown(0)) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (GameConsoleController.IsOpen) return;
            if (ObjectInfoPopup.IsMouseBlocked) return;

            if (!WorldPointer.TryScreenToWorld(_cam, Input.mousePosition, out var clickPos)) return;
            var hit = WorldPointer.PickAt(clickPos);

            // В режиме связи — выбор корабля для диалога
            if (_dialogModeActive)
            {
                HandleDialogClick(hit);
                return;
            }

            // В режиме стрельбы — выбор цели; клик не по цели снимает режим и обрабатывается как обычный
            if (_weaponModeActive && TryHandleWeaponModeClick(hit)) return;

            HandleNavigationClick(hit, clickPos);
        }

        /// <summary>Обработка клавиатурного ввода (W/D/F — модусы). Возвращает true, если клавиша
        /// перехвачена и дальнейший ввод этого кадра обрабатывать не нужно.</summary>
        private bool HandleKeyInput()
        {
            KeyCode weaponKey = _settings != null ? _settings.WeaponModeKey : KeyCode.Q;
            if (Input.GetKeyDown(weaponKey)) { ToggleWeaponMode(); return true; }

            KeyCode dialogKey = _settings != null ? _settings.DialogModeKey : KeyCode.T;
            if (Input.GetKeyDown(dialogKey)) { ToggleDialogMode(); return true; }

            KeyCode forsageKey = _settings != null ? _settings.ForsageKey : KeyCode.F;
            if (Input.GetKeyDown(forsageKey)) { ToggleForsage(); return true; }

            return false;
        }

        /// <summary>Клик в режиме стрельбы. Возвращает true, если клик «съеден»
        /// (попадание по врагу/астероиду — открыт огонь); false, если режим стрельбы снят и
        /// нужно продолжить обработку как обычный навигационный клик.</summary>
        private bool TryHandleWeaponModeClick(WorldPick hit)
        {
            bool isEnemy    = hit?.Ship != null && !hit.Ship.IsPlayer;
            bool isAsteroid = hit?.Asteroid != null && !hit.Asteroid.IsDestroyed;
            if (isEnemy || isAsteroid)
            {
                Pilot.SelectShotTarget(isEnemy ? hit.Ship : null, isAsteroid ? hit.Asteroid : null);
                _weaponModeActive = false;
                return true;
            }
            _weaponModeActive = false;
            return false;
        }

        /// <summary>Обычный навигационный клик: планета → посадка, свой корабль с маршрутом →
        /// сброс маршрута, чужой корабль → follow или enqueue (если item), астероид → курс,
        /// пустое пространство → waypoint.</summary>
        private void HandleNavigationClick(WorldPick info, Vector2 clickPos)
        {
            // Пристыкован к носителю: любой навигационный клик = взлёт
            // (аналог кнопки «взлететь» при посадке на планету). Идёт через PlayerManager,
            // чтобы OnLeft закрыл посадочный UI и обнулил LandedSite — иначе форма носителя
            // осталась бы «открытой» в состоянии PlayerManager, хотя корабль уже в космосе.
            if (!string.IsNullOrEmpty(ShipData.LandedOnShipUid))
            {
                if (PlayerManager.Instance != null) PlayerManager.Instance.LeavePlanet();
                else Pilot.UndockFromCarrier();
                return;
            }

            KeyCode shiftKey = _settings != null ? _settings.MultiWaypointKey : KeyCode.LeftShift;
            bool append = Input.GetKey(shiftKey);

            if (info == null)
            {
                Pilot.FlyTo(clickPos, append);
                return;
            }

            if (info.Planet != null)
            {
                Pilot.SetLandingCourse(info.Planet);
                return;
            }
            if (info.Wormhole != null)
            {
                Pilot.SetWormholeCourse(info.Wormhole, info.WormholeStar);
                return;
            }
            // Клик по СВОЕМУ кораблю при распланированном маршруте — сброс маршрута.
            // На следующей симуляции корабль останется на месте.
            if (info.Ship != null && info.Ship.IsPlayer)
            {
                if (Pilot.HasPlannedRoute())
                {
                    Pilot.ClearRoute();
                    GameLog.Add("[Навигация] Маршрут сброшен.");
                }
                return;
            }
            if (info.Ship != null)
            {
                // Item-цели (контейнеры в космосе) — левый клик ставит их в очередь захвата,
                // без открытия follow-цикла. Обычные корабли — следование / смена режима.
                if (info.Ship.IsItem) Pilot.EnqueuePullTarget(info.Ship.Uid);
                else Pilot.FollowOrCycle(info.Ship);
                return;
            }
            if (info.Asteroid != null)
                Pilot.FlyToAsteroid(info.Asteroid, append);
            // Звезда — без действия.
        }

        private void HandleDialogClick(WorldPick hit)
        {
            var targetShip = hit?.Ship;
            if (targetShip == null || targetShip.IsPlayer)
            {
                DisableDialogMode();
                return;
            }

            float radarRange = EquipmentSystem.GetRadarRange(ShipData);
            float dist = (targetShip.Position - ShipData.Position).magnitude;
            if (radarRange <= 0f || dist > radarRange)
            {
                GameLog.Add(
                    $"[Связь] {targetShip.Name}: объект вне зоны действия радара.");
                HudMessageController.Show("Объект вне зоны радара");
                DisableDialogMode(silent: true);
                return;
            }

            string dialogId = SRG.Dialog.DialogService.ResolveShipDialogId(targetShip);
            if (string.IsNullOrEmpty(dialogId))
            {
                GameLog.Add($"[Связь] {targetShip.Name} не отвечает.");
                HudMessageController.Show($"{targetShip.Name} не отвечает");
                DisableDialogMode(silent: true);
                return;
            }

            if (DialogUIController.Instance != null)
                DialogUIController.Instance.OpenSpaceDialog(dialogId, targetShip);
            DisableDialogMode(silent: true);
        }
    }
}
