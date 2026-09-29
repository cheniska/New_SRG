using SRG.Galaxy;

namespace SRG.Ships.Services
{
    /// <summary>
    /// Техническая подмена графики корабля, не связанная с маскировкой. Обёртка над
    /// <see cref="ShipData.SetCustomBodyGraphic"/> — единая точка, которую могут дёргать
    /// сюжет/квесты/консоль/cutscene, не завязываясь на систему камуфляжа.
    /// </summary>
    /// <remarks>
    /// Камуфляж (<see cref="Disguise.DisguiseService"/>) использует этот же API под капотом:
    /// техническая функция → бизнес-фича. Если и то и другое одновременно попытаются
    /// установить override — победит последний вызов; при снятии маскировки восстанавливается
    /// значение, сохранённое в <see cref="Disguise.DisguiseState.SavedVisualPath"/>.
    /// </remarks>
    public static class ShipVisualService
    {
        /// <summary>Установить override спрайта корабля. Path — путь без расширения в Resources.
        /// Передайте null / пустую строку, чтобы <see cref="ClearOverride"/>.</summary>
        public static void SetOverride(ShipData ship, string path)
        {
            if (ship == null) return;
            if (string.IsNullOrEmpty(path)) { ClearOverride(ship); return; }
            ship.SetCustomBodyGraphic(path);
        }

        /// <summary>Убрать override, вернуться к дефолтной графике корпуса.</summary>
        public static void ClearOverride(ShipData ship)
        {
            if (ship == null) return;
            ship.SetCustomBodyGraphic(null);
        }
    }
}
