using UnityEngine;
using SRG.Core;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.NpcAI.Orders;
using SRG.Utils;

namespace SRG.NpcAI.Actions
{
    public class ActionPursueAndAttack : NpcAction
    {
        private readonly string _targetUid;
        // Прекращать погоню за целью дальше NpcBalance.MaxEngageRange. Включается для
        // «инициативных» атак (NpcBrain.EvaluateSituation, ActionDefend); военные директивы
        // создают без лимита — в захватываемой системе врага преследуют где угодно.
        private readonly bool _limitEngageRange;
        private OrderAttack _attackOrder;
        private int _ticks;
        private int _targetHpAtLastCheck = -1;

        public string TargetShipUid => _targetUid;

        public ActionPursueAndAttack(string targetUid, bool limitEngageRange = false)
        {
            _targetUid = targetUid;
            _limitEngageRange = limitEngageRange;
        }

        public override string CombatTargetUid => _targetUid;

        public override bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            ShipData target = FindAliveShip(star, _targetUid);
            if (target == null || target.CurrentHull <= 0)
            {
                // Цель уничтожена — снимаем фрустрацию (награда за упорство).
                ship.Personality?.ReduceFrustration(20f);
                IsCompleted = true;
                return true;
            }

            if (_limitEngageRange
                && (target.Position - ship.Position).sqrMagnitude
                   > NpcBalance.MaxEngageRange * NpcBalance.MaxEngageRange)
            {
                // Цель ушла за дистанцию преследования — бросаем, EvaluateSituation переоценит.
                IsCompleted = true;
                return true;
            }

            if (_attackOrder == null)
            {
                _attackOrder = new OrderAttack(_targetUid);
                _targetHpAtLastCheck = target.CurrentHull;
            }

            bool done = _attackOrder.Execute(ship, star, ctx);
            _ticks++;
            // Раз в NpcBalance.FrustrationCheckInterval ходов: если HP цели не упало — растёт фрустрация.
            if (_ticks % NpcBalance.FrustrationCheckInterval == 0 && ship.Personality != null)
            {
                int dmg = _targetHpAtLastCheck - target.CurrentHull;
                if (dmg <= 0) ship.Personality.AddFrustration(8f);
                else if (dmg < target.MaxHull * 0.05f) ship.Personality.AddFrustration(3f);
                _targetHpAtLastCheck = target.CurrentHull;

                // Retarget-проверка: если появился свежий агрессор, отличный от текущей цели,
                // и он ближе — прерываем action, чтобы NpcBrain.EvaluateSituation переоценил
                // (может выбрать новую цель или Flee). Иначе NPC долбит старую цель, пока
                // его расстреливает более близкий враг.
                if (ShouldSwitchToNewAttacker(ship, star, target))
                {
                    IsCompleted = true;
                    return true;
                }
            }
            if (done) { IsCompleted = true; return true; }
            return false;
        }

        private bool ShouldSwitchToNewAttacker(ShipData ship, StarData star, ShipData currentTarget)
        {
            string atk = ship.LastAttackerUid;
            if (string.IsNullOrEmpty(atk) || atk == _targetUid) return false;

            int currentTurn = GalaxyManager.Instance?.GeneratedGalaxy?.CurrentTurn ?? 0;
            if (currentTurn - ship.LastAttackerTurn > NpcBalance.LastAttackerMemoryTurns) return false;

            ShipData newAttacker = FindAliveShip(star, atk);
            if (newAttacker == null) return false;

            float distNewSq = (newAttacker.Position - ship.Position).sqrMagnitude;
            float distCurSq = (currentTarget.Position - ship.Position).sqrMagnitude;
            return distNewSq < distCurSq;
        }

        public override string DebugName => $"PursueAndAttack({SpriteUtility.ShortId(_targetUid)})";
    }
}
