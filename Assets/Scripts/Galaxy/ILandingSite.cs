using UnityEngine;

namespace SRG.Galaxy
{
    /// <summary>Тип посадочной цели. Используется UI/сервисами для дифференциации поведения
    /// (набор вкладок в PlanetUIController, подпись кнопки связи и т.д.).</summary>
    public enum LandingSiteKind
    {
        Planet,
        Station,
        Carrier,
    }

    /// <summary>
    /// Единый интерфейс объекта, на который можно сесть/пристыковаться. Реализуется
    /// <see cref="PlanetData"/> (Kind=Planet) и <see cref="ShipData"/> (Kind=Station/Carrier
    /// в зависимости от <see cref="ShipData.IsStation"/>). Служит для замены транзитного
    /// PlanetData-view при стыковке с кораблями: сервисы (Fuel/Repair/Ammo/Trade/Shop) и UI
    /// работают с ILandingSite, а не с реальным типом объекта.
    ///
    /// Основной payload — <see cref="Settlement"/> (общий для планет, станций и линкоров):
    /// магазины/правительство/экономика. <see cref="CenterPosition"/> и <see cref="LandingRadius"/>
    /// дают единый геометрический контракт для landing pipeline (сравнение «прибыл ли», рендер
    /// end-маркера маршрута). Поверхность/орбита планеты, корпус/оружие корабля не абстрагируются
    /// — их достают через pattern-match (<c>if (site is PlanetData planet) …</c>) в местах,
    /// где это действительно нужно.
    /// </summary>
    public interface ILandingSite
    {
        string Uid { get; }
        string Name { get; }
        string Owner { get; }
        string Race { get; }
        SettlementData Settlement { get; }
        LandingSiteKind Kind { get; }

        /// <summary>Текущий центр объекта в мировых координатах. Для планеты — позиция на орбите
        /// (пересчитывается по углу), для корабля/станции — <see cref="ShipData.Position"/>.</summary>
        Vector2 CenterPosition { get; }

        /// <summary>Радиус захвата: корабль внутри этого расстояния от <see cref="CenterPosition"/>
        /// в конце хода → срабатывает <see cref="LandingPhase"/>.Fading (двухфазная посадка).
        /// Для планеты — <c>PlanetGeometry.GetLandingRadius</c>, для корабля — <c>ShipDockingService.GetDockRadius</c>.</summary>
        float LandingRadius { get; }
    }

    /// <summary>Extension-хелперы для <see cref="ILandingSite"/>.</summary>
    public static class LandingSiteExtensions
    {
        public static float GetLandingRadiusSq(this ILandingSite site)
        {
            float r = site?.LandingRadius ?? 0f;
            return r * r;
        }

        /// <summary>Достаточно ли близко <paramref name="pos"/> для срабатывания посадки на site.</summary>
        public static bool IsWithinLandingRange(this ILandingSite site, Vector2 pos)
        {
            if (site == null) return false;
            float r = site.LandingRadius;
            return (pos - site.CenterPosition).sqrMagnitude <= r * r;
        }
    }
}
