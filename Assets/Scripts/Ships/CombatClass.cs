using SRG.Config;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.NpcAI;
using SRG.NpcAI.Orders;

namespace SRG.Ships
{
    /// <summary>
    /// Боевой класс корабля — категориальная ось «какую роль тип играет в боевой экосистеме».
    /// Задаётся в конфиге per-type: <c>ShipTypeConfig.CombatClass</c> (GalaxyConfig.json → Ships.ShipTypes),
    /// резолвится через <see cref="NpcBrain.ResolveCombatClass"/> (фолбэк — Mercenary).
    /// Определяет ветку AI-логики (NpcBrain), участие в директивах ГШ (Military+Mercenary),
    /// разбивку силы фракций Combat/Civilian (StarData.RebuildPowerCache), правила
    /// оккупации (OccupationAutoRule), пороги выкупа (OrderOfferMoneyRansom) и матрицу
    /// таргетинга (DecisionTable). Не путать с ShipPersonality — случайными чертами
    /// конкретного корабля внутри класса.
    /// </summary>
    public enum CombatClass
    {
        Civilian,   // Транспорты, Лайнеры, Дипломаты, Дроны
        Mercenary,  // Рейнджеры, Наёмники
        Military,   // Военные, Крейсеры, Доминаторы
        Pirate,     // Пираты, Мародёры
    }
}
