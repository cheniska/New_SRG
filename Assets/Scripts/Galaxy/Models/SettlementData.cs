using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System;
using SRG.Combat;
using SRG.Config;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy.Generation;
using SRG.Galaxy.Politics;
using SRG.Galaxy.Simulation;
using SRG.NpcAI;
using SRG.NpcAI.Actions;
using SRG.NpcAI.Spawning;
using SRG.Science;
using SRG.Ships;
using SRG.Ships.Movement;
using SRG.Ships.Services;
using SRG.Simulation;

namespace SRG.Galaxy
{
    public class ActivePlanetEvent
    {
        public string EventId { get; set; }
        public int RemainingMonths { get; set; } = -1; // -1 = бессрочное (до внешнего условия)
    }

    /// <summary>
    /// «Обитаемая инфраструктура» — общая сущность для планет и станций: правительство, экономика,
    /// население, магазины (товаров и оборудования), техуровень, наука и состояние оккупации.
    /// Вынесена из <see cref="PlanetData"/>, чтобы станции (<see cref="ShipData.Settlement"/>) получали
    /// все «планетные» возможности при стыковке без дублирования логики.
    /// </summary>
    public class SettlementData
    {
        public int Population { get; set; }
        public string EconomyType { get; set; }
        public string Government { get; set; }

        /// <summary>
        /// Кто держит объект под контролем сейчас. Пусто/null = не оккупирован (управляется Owner хоста).
        /// Работать через <see cref="Galaxy.Politics.OccupationService"/>, а не выставлять напрямую.
        /// </summary>
        public string OccupiedByOwner { get; set; }
        /// <summary>Ход, когда была установлена текущая оккупация. 0 если не оккупирован.</summary>
        public int OccupationStartTurn { get; set; }
        /// <summary>Посадка/стыковка запрещена всем, кроме членов стороны-контроллёра.</summary>
        public bool LandingBlocked { get; set; }

        public int TechLevel { get; set; } = 1;
        /// <summary>УСТАРЕВШЕЕ. Осталось ради совместимости; PTU производна от <see cref="InventionCredits"/>.</summary>
        public float PtuProgress { get; set; } = 0f;
        /// <summary>Сколько изобретений закрыто как MainContributor (≥40% поинтов). Из этого выводится PTU.</summary>
        public int InventionCredits { get; set; } = 0;
        /// <summary>Накопленные научные поинты по категориям (см. ScienceSystem.Distribute).</summary>
        public Dictionary<string, float> ResearchPoints { get; set; } = new();
        /// <summary>Социальный капитал (0..100). Гейтит участие в дорогих изобретениях.</summary>
        public float SocialCapital { get; set; } = 30f;
        public List<ActivePlanetEvent> ActiveEvents { get; set; } = new();

        public PlanetShop Shop { get; set; } = new();
        public PlanetEquipmentShop EquipmentShop { get; set; } = new();

        /// <summary>История BuyPrice по каждому товару (последние ~10 семплов). Для триггеров PriceAbove/Below.</summary>
        public Dictionary<string, List<int>> PriceHistory { get; set; } = new();
        /// <summary>Кулдаун событий: ход последнего истечения для каждого EventId.</summary>
        public Dictionary<string, int> EventCooldowns { get; set; } = new();
        /// <summary>Кэш базового стока товаров. Заполняется лениво в TradeSystem; не сериализуется.</summary>
        [JsonIgnore] public Dictionary<string, int> BaseStockCache { get; set; }
    }
}
