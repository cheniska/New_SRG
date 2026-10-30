using UnityEngine;
using SRG.Core;
using SRG.Galaxy;
using SRG.Utils;

namespace SRG.Controllers
{
    // PlayerShip — визуальная часть хода: подготовка, анимация, конец. Маршрут по итогам хода
    // доводит PlayerPilot.OnTurnCompleted. См. PlayerShip.cs.
    public partial class PlayerShip
    {
        private void OnTurnCalculate(TurnAnimationData anim)
        {
            _isTurnInProgress = true;
            IsMovingThisTurn = false;
            _pathRenderer.ClearPath();
            _weaponModeActive = false;
            if (_dialogModeActive) DisableDialogMode(silent: true);
            _visual?.PrepareForTurn(anim, GalaxyManager.Instance?.CurrentStar);

            // На arrival-симуляции корабль должен появиться сразу прозрачным — на случай если
            // предыдущий ход оставил alpha в другом состоянии. HyperArrive держит его невидимым
            // всё время открытия портала; HyperExit стартует fade-in c 0.
            // А также сразу повёрнут в нужную сторону (иначе плавный поворот 180° за 1 сек).
            if (ShipData != null && (ShipData.HyperjumpPhase == HyperjumpPhase.HyperArrive
                                     || ShipData.HyperjumpPhase == HyperjumpPhase.HyperExit))
            {
                _visual?.SetAlpha(0f);
                _visual?.SnapRotationToHyperjumpHeading();
            }

            Pilot?.OnTurnCalculated();
        }

        private void OnTurnAnimate(float progress, int currentSubTurn)
        {
            var animData = GalaxyManager.Instance?.LastTurnData;
            if (animData == null || _visual == null) return;

            var dir = _visual.AnimateTurn(progress, currentSubTurn, animData);
            if (dir.sqrMagnitude > 0.0001f) IsMovingThisTurn = true;
            if (progress >= 0.99f && dir.sqrMagnitude > 0.0001f)
                ShipData.CurrentHeading = Angles.Of(dir);
        }

        private void OnTurnComplete(TurnAnimationData anim)
        {
            _visual?.EndTurn();
            _isTurnInProgress = false;
            transform.position = new Vector3(ShipData.Position.x, ShipData.Position.y, 0f);

            Pilot?.OnTurnCompleted();
        }
    }
}
