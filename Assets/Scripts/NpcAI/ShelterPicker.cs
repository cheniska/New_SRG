using SRG.Galaxy;
using SRG.Simulation;

namespace SRG.NpcAI
{
    /// <summary>
    /// Общая логика «куда деваться»: станция → планета → соседняя система → fallback.
    /// До июля 2026 в NpcBrain.ChooseDefaultActivity и ActionSeekShelter.ChoosePhase
    /// жили две параллельные копии похожих цепочек.
    /// Возвращает enum-решение и найденный объект (Station/Planet/FriendlyStar) — вызывающий
    /// строит из этого action или order по своему сценарию.
    /// </summary>
    public static class ShelterPicker
    {
        public enum ShelterKind
        {
            None,
            Station,        // Payload = ShipData (станция в этой звезде)
            Planet,         // Payload = PlanetData
            FriendlyStar,   // Payload = StarData (кандидат на прыжок)
        }

        public readonly struct ShelterResult
        {
            public readonly ShelterKind Kind;
            public readonly object Payload;
            public ShelterResult(ShelterKind kind, object payload) { Kind = kind; Payload = payload; }
            public static readonly ShelterResult None = new ShelterResult(ShelterKind.None, null);
        }

        /// <summary>Выбирает первое доступное укрытие. Одинаково для Pirate/Military/Civilian.
        /// Если includeJump=false — пропускает шаг «прыгнуть в соседнюю систему»
        /// (для ChooseDefaultActivity, где стартовое решение не должно вести к прыжку).</summary>
        public static ShelterResult Pick(ShipData ship, StarData star, bool includeJump = true)
        {
            if (ship == null || star == null) return ShelterResult.None;

            var station = NpcTargeting.FindFriendlyStationInStar(star, ship);
            if (station != null) return new ShelterResult(ShelterKind.Station, station);

            var planet = NpcTargeting.FindFriendlyPlanetInStar(star, ship);
            if (planet != null) return new ShelterResult(ShelterKind.Planet, planet);

            if (includeJump)
            {
                var galaxy = GameWorld.GeneratedGalaxy;
                var friendlyStar = NpcTargeting.FindNearestFriendlyStar(star, ship, galaxy);
                if (friendlyStar != null) return new ShelterResult(ShelterKind.FriendlyStar, friendlyStar);
            }

            return ShelterResult.None;
        }
    }
}
