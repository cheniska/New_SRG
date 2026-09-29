using UnityEngine;
using SRG.Core;
using SRG.Galaxy;
using SRG.Presentation.World;
using SRG.UI.Common;

namespace SRG.NpcAI
{
    /// <summary>
    /// MonoBehaviour-обёртка для NPC корабля: владеет ShipData/Brain, по событию OnTurnCalculate
    /// обновляет визуал (если он привязан к тому же GameObject).
    ///
    /// Этап T4 рефакторинга (июнь 2026): убран жёсткий <c>[RequireComponent(typeof(ShipVisualController))]</c>.
    /// Контроллер теперь работает БЕЗ визуала (например, для тестов AI или для ситуаций, когда
    /// визуал ещё не назначен или удалён). UpdateVisual безопасен при отсутствии _visual —
    /// ИИ-симуляция и Brain.Tick не зависят от рендера.
    /// </summary>
    public class NpcController : MonoBehaviour
    {
        public ShipData ShipData { get; private set; }

        // Делегируем в Brain — UI читает это поле через ClickableInfo
        public string CurrentOrder => ShipData?.Brain?.CurrentOrder ?? "—";

        private ShipVisualController _visual;

        public void Init(ShipData ship, StarData star)
        {
            ShipData = ship;
            // Visual опционален: если на GO нет компонента — _visual останется null,
            // UpdateVisual это учитывает (no-op).
            TryGetComponent(out _visual);
            ship.Brain ??= new NpcBrain(ship, star);
        }

        private void OnEnable()  => GalaxyManager.OnTurnCalculate += OnTurnDone;
        private void OnDisable() => GalaxyManager.OnTurnCalculate -= OnTurnDone;

        private void OnTurnDone(TurnAnimationData _) => UpdateVisual();

        private void UpdateVisual()
        {
            if (_visual == null || ShipData == null) return;
            var delta = ShipData.Position - ShipData.PreviousPosition;
            bool isMoving = delta.sqrMagnitude > 0.0001f;
            _visual.SetThrustersActive(isMoving);
            if (isMoving)
            {
                _visual.SetMovementDirection(delta);
                _visual.SetAnimation("Move");
            }
            else
            {
                _visual.SetAnimation("Idle");
            }
        }
    }
}
