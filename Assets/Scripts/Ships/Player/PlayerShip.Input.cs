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

namespace SRG.Ships.Player
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
                GameConsoleController.AddEntry("[Оружие] Наведение включено.");
            }
            else
            {
                GameConsoleController.AddEntry("[Оружие] Наведение выключено.");
            }
        }

        public void ToggleDialogMode()
        {
            _dialogModeActive = !_dialogModeActive;
            if (_dialogModeActive)
            {
                if (_weaponModeActive) _weaponModeActive = false;
                DialogCursor.Apply();
                GameConsoleController.AddEntry("[Связь] Укажите цель для связи.");
            }
            else
            {
                DialogCursor.Reset();
                GameConsoleController.AddEntry("[Связь] Режим связи выключен.");
            }
        }

        public void ToggleForsage()
        {
            if (ShipData == null) return;
            var forsage = EquipmentSystem.GetEquipped(ShipData, SlotKeys.Forsage);
            if (forsage == null)
            {
                GameConsoleController.AddEntry("[Форсаж] Не установлен.");
                return;
            }
            if (!forsage.IsWorking)
            {
                GameConsoleController.AddEntry($"[Форсаж] {forsage.Name}: повреждён, активация невозможна.");
                return;
            }

            // При включении — проверяем условия (износ двигателя, близость к посадке).
            // При выключении — всегда разрешаем.
            if (!ShipData.ForsageActive
                && !EquipmentSystem.CanActivateForsage(ShipData, out string reason))
            {
                GameConsoleController.AddEntry($"[Форсаж] Активация невозможна: {reason}.");
                HudMessageController.Show($"Форсаж: {reason}");
                return;
            }

            ShipData.ForsageActive = !ShipData.ForsageActive;
            GameConsoleController.AddEntry(ShipData.ForsageActive
                ? $"[Форсаж] Включён: {forsage.Name}."
                : "[Форсаж] Выключен.");
            HudMessageController.Show(ShipData.ForsageActive
                ? "Форсаж активирован"
                : "Форсаж деактивирован");

            // Скорость кораля изменилась — пересчитываем планируемый маршрут,
            // чтобы метки дней на нём отражали новый шаг.
            RefreshPlannedRoute();


        }
        private void DisableDialogMode(bool silent = false)
        {
            if (!_dialogModeActive) return;
            _dialogModeActive = false;
            DialogCursor.Reset();
            if (!silent) GameConsoleController.AddEntry("[Связь] Режим связи выключен.");


        }
        private void HandleInput()
        {
            if (GameConsoleController.IsOpen) return;

            if (HandleKeyInput()) return;

            if (!Input.GetMouseButtonDown(0)) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (GameConsoleController.IsOpen) return;
            if (ObjectInfoPopup.IsMouseBlocked) return;

            var worldPos = _cam.ScreenToWorldPoint(Input.mousePosition);
            var clickPos = new Vector2(worldPos.x, worldPos.y);
            var hit = Physics2D.Raycast(clickPos, Vector2.zero);

            // В режиме связи — выбор корабля для диалога
            if (_dialogModeActive)
            {
                HandleDialogClick(hit);
                return;
            }

            // В режиме стрельбы — выбор цели; клик не по цели снимает режим и обрабатывается как обычный
            if (_weaponModeActive && TryHandleWeaponModeClick(hit, clickPos)) return;

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
        private bool TryHandleWeaponModeClick(RaycastHit2D hit, Vector2 clickPos)
        {
            if (hit.collider != null)
            {
                var info = hit.collider.GetComponentInParent<ClickableInfo>();
                bool isEnemy    = info?.Ship != null && !info.Ship.IsPlayer;
                bool isAsteroid = info?.Asteroid != null && !info.Asteroid.IsDestroyed;
                if (isEnemy || isAsteroid)
                {
                    HandleWeaponClick(clickPos);
                    return true;
                }
            }
            _weaponModeActive = false;
            return false;
        }

        /// <summary>Обычный навигационный клик: планета → посадка, свой корабль с маршрутом →
        /// сброс маршрута, чужой корабль → follow или enqueue (если item), астероид → курс,
        /// пустое пространство → waypoint.</summary>
        private void HandleNavigationClick(RaycastHit2D hit, Vector2 clickPos)
        {
            // Пристыкован к носителю: любой навигационный клик = взлёт
            // (аналог кнопки «взлететь» при посадке на планету). Идёт через PlayerManager,
            // чтобы OnLeft закрыл посадочный UI и обнулил LandedSite — иначе форма носителя
            // осталась бы «открытой» в состоянии PlayerManager, хотя корабль уже в космосе.
            if (!string.IsNullOrEmpty(ShipData.LandedOnShipUid))
            {
                if (PlayerManager.Instance != null) PlayerManager.Instance.LeavePlanet();
                else UndockFromCarrier();
                return;
            }

            if (hit.collider != null)
            {
                var info = hit.collider.GetComponentInParent<ClickableInfo>();
                if (info?.Planet != null)
                {
                    SetLandingCourse(info.Planet);
                    return;
                }
                if (info?.Wormhole != null)
                {
                    SetWormholeCourse(info.Wormhole, info.WormholeStar);
                    return;
                }
                // Клик по СВОЕМУ кораблю при распланированном маршруте — сброс маршрута.
                // На следующей симуляции корабль останется на месте.
                if (info?.Ship != null && info.Ship.IsPlayer)
                {
                    if (HasPlannedRoute())
                    {
                        ClearRoute();
                        GameConsoleController.AddEntry("[Навигация] Маршрут сброшен.");
                    }
                    return;
                }
                if (info?.Ship != null && !info.Ship.IsPlayer)
                {
                    // Item-цели (контейнеры в космосе) — левый клик ставит их в очередь захвата,
                    // без открытия follow-цикла. Обычные корабли — BeginOrCycleFollow.
                    if (info.Ship.IsItem)
                    {
                        EnqueuePullTarget(info.Ship.Uid);
                        return;
                    }
                    BeginOrCycleFollow(info.Ship);
                    return;
                }

                if (info?.Asteroid != null && !info.Asteroid.IsDestroyed)
                {
                    var targetAsteroid = info.Asteroid;
                    ClearFollow();
                    SetWaypointToPosition(targetAsteroid.Position);
                    _trackedTargetFunc = () => targetAsteroid.Position;
                    _lastTrackedPos = targetAsteroid.Position;
                    GameConsoleController.AddEntry($"[Навигация] Курс на астероид {SpriteUtility.ShortId(targetAsteroid.Uid)}.");
                    return;
                }
                return;
            }

            ClearFollow();
            _trackedTargetFunc = null;
            SetWaypointToPosition(clickPos);


        }
        private void HandleWeaponClick(Vector2 clickPos)
        {
            var hit = Physics2D.Raycast(clickPos, Vector2.zero);
            if (hit.collider == null) return;

            var info = hit.collider.GetComponentInParent<ClickableInfo>();
            if (info == null) return;

            bool isShip     = info.Ship != null && !info.Ship.IsPlayer;
            bool isAsteroid = info.Asteroid != null && !info.Asteroid.IsDestroyed;
            if (!isShip && !isAsteroid) return;

            float maxRange = GetMaxWeaponRange();
            if (maxRange <= 0f)
            {
                GameConsoleController.AddEntry("[Оружие] На корабле нет исправного оружия.");
                _weaponModeActive = false;
                return;
            }

            Vector2 targetPos = isShip ? info.Ship.Position : info.Asteroid.Position;
            float dist = (targetPos - ShipData.Position).magnitude;
            if (dist > maxRange)
            {
                string name = isShip ? info.Ship.Name : $"астероид {SpriteUtility.ShortId(info.Asteroid.Uid)}";
                GameConsoleController.AddEntry(
                    $"[Оружие] {name} вне радиуса поражения ({dist:F1} > {maxRange:F1}).");
                _weaponModeActive = false;
                return;
            }

            // Ручной выстрел отменяет активный режим следования (иначе UpdateFollowAutoFire перепишет цель).
            ClearFollow();

            // Фиксируем цель
            if (isShip)
            {
                ShipData.ManualShootTargetUid = info.Ship.Uid;
                ShipData.ManualShootTargetIsAsteroid = false;
                GameConsoleController.AddEntry(
                    $"[Оружие] Цель выбрана: {info.Ship.Name}. Выстрел — в следующем ходу.");
            }
            else
            {
                ShipData.ManualShootTargetUid = info.Asteroid.Uid;
                ShipData.ManualShootTargetIsAsteroid = true;
                GameConsoleController.AddEntry(
                    $"[Оружие] Цель: астероид {SpriteUtility.ShortId(info.Asteroid.Uid)}. Выстрел — в следующем ходу.");
            }

            _weaponModeActive = false;
        }

        private void HandleDialogClick(RaycastHit2D hit)
        {
            var info = hit.collider != null ? hit.collider.GetComponentInParent<ClickableInfo>() : null;
            var targetShip = info?.Ship;
            if (targetShip == null || targetShip.IsPlayer)
            {
                DisableDialogMode();
                return;
            }

            float radarRange = EquipmentSystem.GetRadarRange(ShipData);
            float dist = (targetShip.Position - ShipData.Position).magnitude;
            if (radarRange <= 0f || dist > radarRange)
            {
                GameConsoleController.AddEntry(
                    $"[Связь] {targetShip.Name}: объект вне зоны действия радара.");
                HudMessageController.Show("Объект вне зоны радара");
                DisableDialogMode(silent: true);
                return;
            }

            string dialogId = SRG.Dialog.DialogService.ResolveShipDialogId(targetShip);
            if (string.IsNullOrEmpty(dialogId))
            {
                GameConsoleController.AddEntry($"[Связь] {targetShip.Name} не отвечает.");
                HudMessageController.Show($"{targetShip.Name} не отвечает");
                DisableDialogMode(silent: true);
                return;
            }

            if (DialogUIController.Instance != null)
                DialogUIController.Instance.OpenSpaceDialog(dialogId, targetShip);
            DisableDialogMode(silent: true);
        }

        private float GetMaxWeaponRange()
        {
            float maxRange = 0f;
            var ship = ShipData;
            if (ship == null) return 0f;
            foreach (var slotKey in ship.Equipment.GetOccupiedSlotsOfCategory(EquipmentCategory.Weapons))
            {
                string uid = ship.Equipment.GetItemUid(slotKey);
                if (uid == null || !ship.AllItems.TryGetValue(uid, out var weapon)) continue;
                if (!weapon.IsWorking) continue;
                float range = SRUnits.ToWorld(weapon.GetParam("Range", 0f));
                if (range > maxRange) maxRange = range;
            }
            return maxRange;
        }
    }
}
