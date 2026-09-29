using UnityEngine;
using SRG.UI.Common;

namespace SRG.Dialog
{
    /// <summary>
    /// Регистрирует диалоговые действия/теги для механики улучшения оборудования на
    /// научной базе (SB). Основное действие — <c>OpenImprovement</c>: открывает
    /// <see cref="ImprovementDialog"/> для игрока. Регистрируется из <c>DialogUIController.Awake</c>.
    ///
    /// В DialogsConfig.json достаточно узла реплики с <c>Action: OpenImprovement</c> — можно
    /// добавить в диалог станции SB, чтобы игрок из обычного разговора попадал в модальный
    /// экран улучшения. См. docs/modules/equipment_improvement.md.
    /// </summary>
    public static class DialogSbImprovementActions
    {
        public static void RegisterDefaults()
        {
            DialogService.RegisterAction("OpenImprovement", (ctx, _) =>
            {
                if (ctx?.PlayerShip == null) return;
                ImprovementDialog.Show(ctx.PlayerShip);
                ctx.ShouldClose = true; // закрываем разговор, чтобы модалка не перекрывалась им
            });

            DialogService.RegisterTag("player_nodes", ctx =>
            {
                var ship = ctx?.PlayerShip;
                if (ship == null) return "0";
                int fromStack = 0;
                if (ship.Inventory?.Stacks != null && ship.Inventory.Stacks.TryGetValue("Nod", out var s))
                    fromStack = s.TotalWeight;
                return (fromStack + Mathf.Max(0, ship.NodeAccount)).ToString();
            });

            DialogService.RegisterTag("player_node_account", ctx =>
                Mathf.Max(0, ctx?.PlayerShip?.NodeAccount ?? 0).ToString());
        }
    }
}
