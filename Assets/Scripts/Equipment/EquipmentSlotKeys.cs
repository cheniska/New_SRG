using SRG.Config;
using SRG.Galaxy;
using SRG.Ships.Services;

namespace SRG.Equipment
{
    /// <summary>
    /// Константы ключей слотов снаряжения (вида "Category_index")
    /// и категорий снаряжения. Используются вместо магических строк во всём проекте.
    /// </summary>
    public static class SlotKeys
    {
        // ── Одиночные слоты (всегда индекс 0) ────────────────────────────────────
        public const string Hull         = "Hull_0";
        public const string Engine       = "Engine_0";
        public const string FuelTank     = "FuelTank_0";
        public const string Forsage      = "Forsage_0";
        public const string Shield       = "Shield_0";
        public const string Radar        = "Radar_0";
        public const string Scanner      = "Scanner_0";

        /// <summary>Псевдо-слот «выбросить»: предмет, перенесённый сюда, выкидывается в космос
        /// контейнером (CosmicItem с IsItem=true). Сам слот никогда не «заполняется» — после drop
        /// он сразу очищается. Не входит в DisplayOrder/Equipment.Slots.</summary>
        public const string Throw        = "Throw_0";

        /// <summary>Псевдо-слот «активировать»: появляется только когда в руке предмет с
        /// Activatable=true (см. ItemInstance.Activatable). Drop сюда возвращает предмет
        /// в исходную позицию и запускает его активируемый код. Сам слот ничего не хранит,
        /// в Equipment.Slots не входит.</summary>
        public const string Activate     = "Activate_0";
    }

    /// <summary>
    /// Константы названий категорий снаряжения.
    /// Должны совпадать с ключами в ItemsConfig.json → Equipment.<cat> и EquipmentTemplates.<cat>.
    /// </summary>
    public static class EquipmentCategory
    {
        public const string Hull         = "Hull";
        public const string Engine       = "Engine";
        public const string FuelTank     = "FuelTank";
        public const string Forsage      = "Forsage";
        public const string Shield       = "Shield";
        public const string Radar        = "Radar";
        public const string Scanner      = "Scanner";
        public const string Droid        = "Droid";
        public const string CargoGrabber = "CargoGrabber";
        public const string Weapons      = "Weapons";
        public const string Artefacts    = "Artefacts";
        // Абордажный крюк и буксировочная установка ставятся в слот CargoGrabber
        // (буксир — также в Weapons) через ItemInstance.CompatibleSlots. Отдельные
        // слот-категории пока не выделяются: спецслот «BoardingHook» отложен.
        public const string BoardingHook = "BoardingHook";
        public const string TowingRig    = "TowingRig";
        /// <summary>Упакованный дрон-компаньон. Активируется через слот Activate:
        /// извлекается ShipData и разворачивается в звезду через <c>DroneService.Deploy</c>.</summary>
        public const string CompanionDrone = "CompanionDrone";

        /// <summary>Порядок категорий для отображения в UI (Inventory/ShipScan).</summary>
        public static readonly string[] DisplayOrder =
        {
            Hull, Engine, FuelTank, Forsage, Shield, Radar, Scanner,
            Droid, CargoGrabber, Weapons, Artefacts
        };
    }
}
