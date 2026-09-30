using UnityEngine;
using SRG.NpcAI;
using SRG.Simulation;
using SRG.Galaxy;
using SRG.Presentation.World;
using SRG.UI.Common;

namespace SRG.Controllers
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

        /// <summary>Повесить AI-контроллер на GameObject корабля (вызывается SystemViewManager при создании визуала).</summary>
        public static void Attach(GameObject obj, ShipData ship, StarData star)
        {
            if (obj == null || ship == null || star == null) return;

            var ctrl = obj.GetComponent<NpcController>() ?? obj.AddComponent<NpcController>();
            ctrl.Init(ship, star); // lazy-создаёт Brain; ship.Personality проставляет конструктор NpcBrain

            Debug.Log($"[NpcController] Attached AI to '{ship.Name}' ({ship.ShipTypeId}) " +
                      $"CombatClass={ship.Brain.CombatClass} " +
                      $"Aggr={ship.Brain.Personality.Aggression:F0} " +
                      $"Caution={ship.Brain.Personality.Caution:F0} " +
                      $"Greed={ship.Brain.Personality.Greed:F0} " +
                      $"Disc={ship.Brain.Personality.Discipline:F0}");
        }

        public void Init(ShipData ship, StarData star)
        {
            ShipData = ship;
            // Visual опционален: если на GO нет компонента — _visual останется null,
            // UpdateVisual это учитывает (no-op).
            TryGetComponent(out _visual);
            ship.Brain ??= new NpcBrain(ship, star);
        }

        private void OnEnable()  => GameWorld.OnTurnCalculate += OnTurnDone;
        private void OnDisable() => GameWorld.OnTurnCalculate -= OnTurnDone;

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
