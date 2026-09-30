using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SRG.Combat;
using SRG.Dialog;
using SRG.Dialog.PlanetGreetings;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.NpcAI;
using SRG.NpcAI.Spawning;
using SRG.Science;
using SRG.Ships;
using SRG.Ships.Services;
using SRG.Simulation;

namespace SRG.Config
{
    // ──────────────────────────────────────────────
    // Рейтинги кораблей (рейнджерский, пиратский, торговый, …)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Декларативное описание одного рейтинга (GalaxyConfig.json → "Ratings", обрабатывает
    /// <see cref="ShipRatingService"/>). Участники выбираются селекторами ShipTypes/Owners
    /// (объединение по ИЛИ), счёт — взвешенная сумма метрик ShipData. Доступные ключи Score:
    /// KillsDominator, KillsPirate, KillsTransport, KillsRanger, KillsTotal, TradeProfit, CrimeRating.
    /// </summary>
    public class ShipRatingConfig
    {
        [JsonProperty("Id")] public string Id { get; set; }
        [JsonProperty("DisplayName")] public string DisplayName { get; set; }
        [JsonProperty("Enabled")] public bool Enabled { get; set; } = true;
        /// <summary>Участники по ShipTypeId ("Ranger", "Pirate", "Transport", …).</summary>
        [JsonProperty("ShipTypes")] public List<string> ShipTypes { get; set; } = new();
        /// <summary>Участники по Owner ("Pirates", "Dominators", …) — дополняет ShipTypes.</summary>
        [JsonProperty("Owners")] public List<string> Owners { get; set; } = new();
        /// <summary>Метрика → вес. Счёт корабля = Σ вес × значение метрики.</summary>
        [JsonProperty("Score")] public Dictionary<string, float> Score { get; set; } = new();
        /// <summary>Включать ли корабль игрока в список.</summary>
        [JsonProperty("IncludePlayer")] public bool IncludePlayer { get; set; }
        /// <summary>Минимальный счёт для попадания в список (отсекает «нулевых» новичков).</summary>
        [JsonProperty("MinScore")] public float MinScore { get; set; } = 1f;
        /// <summary>Раз в сколько ходов публиковать новость с отличившимися. 0 — не публиковать
        /// (рейтинг остаётся доступен через ShipRatingService.Build, например для UI).</summary>
        [JsonProperty("NewsStrideTurns")] public int NewsStrideTurns { get; set; } = 90;
        /// <summary>Сколько лидеров упоминать в новости.</summary>
        [JsonProperty("TopN")] public int TopN { get; set; } = 3;
        /// <summary>Категория новости; по умолчанию — DisplayName.</summary>
        [JsonProperty("NewsCategory")] public string NewsCategory { get; set; }
    }
}
