using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SRG.Dialog.PlanetGreetings
{
    // Правила «приветствий» — общего вида, для двух каналов:
    //   • правительство планеты  → DialogsConfig.PlanetGreetings.Rules
    //   • капитан корабля        → DialogsConfig.ShipGreetings.Rules
    // Схема единая: все поля опциональные. PlanetGreetingSelector фильтрует
    // правила по всем ненулевым условиям (AND), выбирает случайное с макс. Priority.
    //
    // Планета-only фильтры (Planet*)      — актуальны для PlanetGreetings.
    // Корабль-only фильтры (Ship*, ToShip*, LastPlanet*, FlyType, AutoTalk, …) — для ShipGreetings.
    // Общие фильтры (Player*, ToPlanet*, ToStar*, Faction*, Ships*, Goods) — для обоих.

    public class PlanetGreetingsConfig
    {
        [JsonProperty("Rules")] public List<GreetingRule> Rules { get; set; } = new();
    }

    public class ShipGreetingsConfig
    {
        [JsonProperty("Rules")] public List<GreetingRule> Rules { get; set; } = new();
    }

    public class GreetingRule
    {
        [JsonProperty("Id")]        public string Id { get; set; }
        [JsonProperty("Priority")]  public int    Priority { get; set; } = 1;

        // ─── CurPlanet (только для PlanetGreetings) ───────────────────
        // Каждое поле имеет две формы: короткую (PlanetX) и полную (CurPlanetX) — принимаются обе.
        // В любом строковом массиве поддерживается отрицание элемента через префикс "!" (например ["!Agro"]).
        [JsonProperty("PlanetRelation")]         public string[] PlanetRelation { get; set; }
        [JsonProperty("PlanetRace")]             public string[] PlanetRace { get; set; }
        [JsonProperty("PlanetRaceIsPlayerRace")] public string   PlanetRaceIsPlayerRace { get; set; }
        [JsonProperty("PlanetGovernment")]       public string[] PlanetGovernment { get; set; }
        [JsonProperty("PlanetEconomy")]          public string[] PlanetEconomy { get; set; }
        [JsonProperty("PlanetGoodsPermit")]      public string   PlanetGoodsPermit { get; set; }
        [JsonProperty("PlanetGoodsCnt")]         public string[] PlanetGoodsCnt { get; set; }
        [JsonProperty("PlanetGoodsSale")]        public string[] PlanetGoodsSale { get; set; }
        [JsonProperty("PlanetGoodsBuy")]         public string[] PlanetGoodsBuy { get; set; }

        // Полные имена. Мержатся с короткими в OnDeserialized.
        [JsonProperty("CurPlanetRelations")]        public string[] CurPlanetRelationsAlias        { set => PlanetRelation           = value; }
        [JsonProperty("CurPlanetRelation")]         public string[] CurPlanetRelationAlias         { set => PlanetRelation           = value; }
        [JsonProperty("CurPlanetRace")]             public string[] CurPlanetRaceAlias             { set => PlanetRace               = value; }
        [JsonProperty("CurPlanetRaceIsPlayerRace")] public string   CurPlanetRaceIsPlayerRaceAlias { set => PlanetRaceIsPlayerRace   = value; }
        [JsonProperty("CurPlanetGovernment")]       public string[] CurPlanetGovernmentAlias       { set => PlanetGovernment         = value; }
        [JsonProperty("CurPlanetGoverment")]        public string[] CurPlanetGovermentAlias        { set => PlanetGovernment         = value; }
        [JsonProperty("CurPlanetEconomy")]          public string[] CurPlanetEconomyAlias          { set => PlanetEconomy            = value; }
        [JsonProperty("CurPlanetGoodsPermit")]      public string   CurPlanetGoodsPermitAlias      { set => PlanetGoodsPermit        = value; }
        [JsonProperty("CurPlanetGoodsCnt")]         public string[] CurPlanetGoodsCntAlias         { set => PlanetGoodsCnt           = value; }
        [JsonProperty("CurPlanetGoodsSale")]        public string[] CurPlanetGoodsSaleAlias        { set => PlanetGoodsSale          = value; }
        [JsonProperty("CurPlanetGoodsBuy")]         public string[] CurPlanetGoodsBuyAlias         { set => PlanetGoodsBuy           = value; }

        // ─── Новые условия ────────────────────────────────────────────
        [JsonProperty("QuestGiver")]                public string   QuestGiver { get; set; }
        [JsonProperty("PlayerMoney")]               public string[] PlayerMoney { get; set; }
        [JsonProperty("CurStarHasWormHole")]        public string   CurStarHasWormHole { get; set; }
        [JsonProperty("CurPlanetVisited")]          public string   CurPlanetVisited { get; set; }
        [JsonProperty("CurPlanetPopulation")]       public string[] CurPlanetPopulation { get; set; }
        // TechLevel может быть точным числом или его кантом. Массив строк — "1"/"2"/...".
        [JsonProperty("CurPlanetTechLevel")]        public string[] CurPlanetTechLevel { get; set; }
        [JsonProperty("CurPlanetIsHomeworld")]      public string   CurPlanetIsHomeworld { get; set; }

        // ─── Игрок ────────────────────────────────────────────────────
        [JsonProperty("PlayerRace")]      public string[] PlayerRace { get; set; }
        [JsonProperty("PlayerStatus")]    public string[] PlayerStatus { get; set; }
        [JsonProperty("PlayerRank")]      public string[] PlayerRank { get; set; }
        [JsonProperty("PlayerRating")]    public string[] PlayerRating { get; set; }
        [JsonProperty("PlayerStrength")]  public string[] PlayerStrength { get; set; }
        [JsonProperty("PlayerStructure")] public string[] PlayerStructure { get; set; }
        [JsonProperty("PlayerHaveGoods")] public string   PlayerHaveGoods { get; set; }
        [JsonProperty("PlayerGoodsCnt")]  public string[] PlayerGoodsCnt { get; set; }
        [JsonProperty("PlayerGoodsTypeCnt")] public string[] PlayerGoodsTypeCnt { get; set; }

        // ─── Товар-контекст ───────────────────────────────────────────
        [JsonProperty("Goods")]  public string   Goods { get; set; }

        // ─── ToPlanet (общее — куда указываем в реплике) ──────────────
        [JsonProperty("ToPlanetRace")]                public string[] ToPlanetRace { get; set; }
        [JsonProperty("ToPlanetInCurStar")]           public string   ToPlanetInCurStar { get; set; }
        [JsonProperty("ToPlanetRaceIsCurPlanetRace")] public string   ToPlanetRaceIsCurPlanetRace { get; set; }
        [JsonProperty("ToPlanetRaceIsPlayerRace")]    public string   ToPlanetRaceIsPlayerRace { get; set; }
        [JsonProperty("ToPlanetRaceIsShipRace")]      public string   ToPlanetRaceIsShipRace { get; set; }
        [JsonProperty("ToPlanetGovernment")]          public string[] ToPlanetGovernment { get; set; }
        [JsonProperty("ToPlanetEconomy")]             public string[] ToPlanetEconomy { get; set; }
        [JsonProperty("ToPlanetGoodsPermit")]         public string   ToPlanetGoodsPermit { get; set; }
        [JsonProperty("ToPlanetGoodsCnt")]            public string[] ToPlanetGoodsCnt { get; set; }
        [JsonProperty("ToPlanetGoodsSale")]           public string[] ToPlanetGoodsSale { get; set; }
        [JsonProperty("ToPlanetGoodsBuy")]            public string[] ToPlanetGoodsBuy { get; set; }
        [JsonProperty("ToPlanetRelation")]            public string[] ToPlanetRelation { get; set; }
        [JsonProperty("ToPlanetRelations")]           public string[] ToPlanetRelationsAlias         { set => ToPlanetRelation   = value; }
        [JsonProperty("ToPlanetGoverment")]           public string[] ToPlanetGovermentAlias         { set => ToPlanetGovernment = value; }
        [JsonProperty("ToPlanetIsHomePlanet")]        public string   ToPlanetIsHomePlanet { get; set; }
        [JsonProperty("ToPlanetIsLastPlanet")]        public string   ToPlanetIsLastPlanet { get; set; }

        // ─── Битвы / оккупация в звёздах ──────────────────────────────
        [JsonProperty("CurStarInBattle")]      public string CurStarInBattle { get; set; }
        [JsonProperty("ToStarInBattle")]       public string ToStarInBattle { get; set; }
        [JsonProperty("ToStarControlByEnemy")] public string ToStarControlByEnemy { get; set; }

        // ─── Ship-in-star счётчики / битвы с фракциями ────────────────
        [JsonProperty("ShipsInCurStar")]           public Dictionary<string, string[]> ShipsInCurStar { get; set; }
        [JsonProperty("ShipsInToStar")]            public Dictionary<string, string[]> ShipsInToStar { get; set; }
        [JsonProperty("StarBattleWithInCurStar")]  public Dictionary<string, string>   StarBattleWithInCurStar { get; set; }
        [JsonProperty("StarBattleWithInToStar")]   public Dictionary<string, string>   StarBattleWithInToStar { get; set; }

        // ─── Побеждённые фракции ──────────────────────────────────────
        [JsonProperty("FactionDefeated")]         public Dictionary<string, string> FactionDefeated { get; set; }
        [JsonProperty("AnyMajorFactionDefeated")] public string AnyMajorFactionDefeated { get; set; }

        // ─── Собеседник-корабль (для ShipGreetings) ──────────────────
        [JsonProperty("Relations")]              public string[] Relations { get; set; }        // отношение ship→player
        [JsonProperty("ShipRace")]               public string[] ShipRace { get; set; }
        [JsonProperty("ShipRaceIsPlayerRace")]   public string   ShipRaceIsPlayerRace { get; set; }
        [JsonProperty("ShipType")]               public string[] ShipType { get; set; }
        [JsonProperty("ShipStatus")]             public string[] ShipStatus { get; set; }
        [JsonProperty("ShipStrength")]           public string[] ShipStrength { get; set; }
        [JsonProperty("ShipStructure")]          public string[] ShipStructure { get; set; }
        [JsonProperty("ShipHaveGoods")]          public string   ShipHaveGoods { get; set; }
        [JsonProperty("ShipGoodsCnt")]           public string[] ShipGoodsCnt { get; set; }
        [JsonProperty("ShipGoodsTypeCnt")]       public string[] ShipGoodsTypeCnt { get; set; }
        [JsonProperty("ShipMayScanPlayer")]      public string   ShipMayScanPlayer { get; set; }
        [JsonProperty("ShipFlyToPlayer")]        public string   ShipFlyToPlayer { get; set; }
        [JsonProperty("ShipNeedInItem")]         public string   ShipNeedInItem { get; set; }
        [JsonProperty("ItemType")]               public string[] ItemType { get; set; }
        [JsonProperty("ShipTurnBeforeEndOrder")] public string   ShipTurnBeforeEndOrder { get; set; }
        [JsonProperty("ShipBadTurnBeforeEndOrder")] public string ShipBadTurnBeforeEndOrder { get; set; }
        [JsonProperty("ShipBadFlyToShip")]       public string   ShipBadFlyToShip { get; set; }
        [JsonProperty("ShipBadType")]            public string[] ShipBadType { get; set; }
        [JsonProperty("ShipBadRace")]            public string[] ShipBadRace { get; set; }

        // Ship-flight контекст
        [JsonProperty("FlyType")] public string[] FlyType { get; set; }   // Any/ToPlanet/ToShip/ToStar/…
        [JsonProperty("AutoTalk")] public string  AutoTalk { get; set; }  // корабль сам инициирует

        // Дополнительные Player→Ship и Ship состояния (только для ShipGreetings).
        [JsonProperty("PlayerIsShipBad")]      public string   PlayerIsShipBad { get; set; }
        [JsonProperty("PlayerFlyToShip")]      public string   PlayerFlyToShip { get; set; }
        [JsonProperty("PlayerAttackGoodShip")] public string   PlayerAttackGoodShip { get; set; }
        [JsonProperty("InFear")]               public string[] InFear { get; set; }

        // Сравнение сил и рангов игрока и корабля
        [JsonProperty("RankShipWithPlayer")]     public string RankShipWithPlayer { get; set; }
        [JsonProperty("StrengthShipWithPlayer")] public string StrengthShipWithPlayer { get; set; }

        // ─── Другой корабль-«цель» (ToShip*) ─────────────────────────
        [JsonProperty("ToShipType")]       public string[] ToShipType { get; set; }
        [JsonProperty("ToShipRace")]       public string[] ToShipRace { get; set; }
        [JsonProperty("ToShipRelations")]  public string[] ToShipRelations { get; set; }
        [JsonProperty("ToShipBad")]        public string   ToShipBad { get; set; }
        [JsonProperty("ToShipInPlanet")]   public string   ToShipInPlanet { get; set; }

        // ─── LastPlanet (откуда прилетел корабль) ────────────────────
        [JsonProperty("LastPlanetRace")]             public string[] LastPlanetRace { get; set; }
        [JsonProperty("LastPlanetRaceIsPlayerRace")] public string   LastPlanetRaceIsPlayerRace { get; set; }
        [JsonProperty("LastPlanetRaceIsShipRace")]   public string   LastPlanetRaceIsShipRace { get; set; }
        [JsonProperty("LastPlanetInCurStar")]        public string   LastPlanetInCurStar { get; set; }
        [JsonProperty("LastPlanetIsHomePlanet")]     public string   LastPlanetIsHomePlanet { get; set; }
        [JsonProperty("LastPlanetGovernment")]       public string[] LastPlanetGovernment { get; set; }
        [JsonProperty("LastPlanetEconomy")]          public string[] LastPlanetEconomy { get; set; }
        [JsonProperty("LastPlanetRelations")]        public string[] LastPlanetRelations { get; set; }
        [JsonProperty("LastPlanetGoodsCnt")]         public string[] LastPlanetGoodsCnt { get; set; }
        [JsonProperty("LastPlanetGoodsSale")]        public string[] LastPlanetGoodsSale { get; set; }
        [JsonProperty("LastPlanetGoodsBuy")]         public string[] LastPlanetGoodsBuy { get; set; }
        [JsonProperty("LastPlanetDistToShipInTurn")] public string   LastPlanetDistToShipInTurn { get; set; }

        // ─── HomePlanet ──────────────────────────────────────────────
        [JsonProperty("HomePlanetInCurStar")] public string HomePlanetInCurStar { get; set; }
        [JsonProperty("HomePlanetInToStar")]  public string HomePlanetInToStar { get; set; }

        // ─── Тексты ──────────────────────────────────────────────────
        [JsonProperty("Texts")] public List<string> Texts { get; set; } = new();

        // ─── Плоские ключи (динамические) ─────────────────────────────
        // Ключи, не покрытые явными полями, попадают сюда и разбираются селектором:
        //   • <SideName>Defeated              — уничтожена ли фракция (Yes/No)
        //   • <SideName>InCurStar             — счётчик кораблей стороны в текущей системе
        //   • <SideName>InToStar              — то же для системы ToPlanet
        //   • <ShipType>InToStar / InCurStar  — счётчик кораблей по ShipTypeId
        //   • CurStarInBattle<SideName>       — идёт ли бой стороны в текущей системе (Yes/No)
        //   • ToStarInBattle<SideName>        — то же для ToStar
        //   • CurPlanetOccupiedBy<SideName>   — планета оккупирована этой стороной (Yes/No)
        //   • ToStarControledBy<SideName>     — оккупирована ли ToStar (Yes/No)
        [JsonExtensionData]
        public Dictionary<string, JToken> Extras { get; set; }
    }
}
