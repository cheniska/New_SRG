using SRG.Galaxy;

namespace SRG.Ships
{
    /// <summary>
    /// Классификаторы корабля по ShipTypeId/Owner — единая точка правды. До этого
    /// одни и те же строки-литералы ("Warrior"/"Ranger"/"Pirate"/"Transport"/"Liner"/"Diplomat"/…)
    /// повторялись в ShipRatingService, StarPresenceService, ExtortionService, CargoRobberyService,
    /// NpcBrain. Централизация — чтобы ввод нового ShipTypeId не требовал ловить все места.
    /// </summary>
    public static class ShipUtils
    {
        // ── Классификаторы по типу/владельцу ──────────────────────────

        public static bool IsDominator(ShipData s) =>
            s != null && s.Owner == "Dominators";

        public static bool IsPirate(ShipData s) =>
            s != null && (s.ShipTypeId == "Pirate" || s.Owner == "Pirates");

        public static bool IsWarrior(ShipData s) =>
            s != null && s.ShipTypeId == "Warrior";

        public static bool IsRanger(ShipData s) =>
            s != null && s.ShipTypeId == "Ranger";

        public static bool IsTransport(ShipData s) =>
            s != null && (s.ShipTypeId == "Transport" || s.ShipTypeId == "Liner" || s.ShipTypeId == "Diplomat");

        // ── Иммунитет к «жёстким» криминальным взаимодействиям ────────
        // Военные / вольные пилоты / пираты / синтеты не сдаются на вымогательство и не отдают груз.

        public static bool IsHardTargetForCriminalAct(ShipData target)
        {
            if (target == null || string.IsNullOrEmpty(target.ShipTypeId)) return false;
            return IsWarrior(target) || IsRanger(target) || IsPirate(target) || IsDominator(target);
        }
    }
}
