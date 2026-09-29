using System.Collections.Generic;
using UnityEngine;
using SRG.Combat;
using SRG.Config;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.NpcAI.Actions;

namespace SRG.Ships.Services
{
    /// <summary>
    /// Универсальные рейтинги кораблей, декларативно описываемые конфигом
    /// (GalaxyConfig.json → "Ratings", см. <see cref="ShipRatingConfig"/>).
    /// Рейтинг — просто упорядоченный список кораблей по взвешенной сумме метрик
    /// (Kills*, TradeProfit, CrimeRating). Никаких наград и штрафов: периодически
    /// публикуется новость с отличившимися (топ списка), полный список доступен
    /// через <see cref="Build"/> для будущего UI.
    ///
    /// Метрики копятся на ShipData независимо от состава рейтингов:
    ///   1) <see cref="ShipDeathBus.OnShipDestroyed"/> — инкремент Kills* у killer'а;
    ///   2) <see cref="ActionGoodsTrader"/> — TradeProfit при завершении торгового лега;
    ///   3) CrimeRating — существующая механика криминала.
    /// Какие корабли в каком рейтинге участвуют — целиком решает конфиг
    /// (селекторы ShipTypes/Owners), так что новый рейтинг = новая запись в JSON.
    /// </summary>
    public static class ShipRatingService
    {
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            ShipDeathBus.OnShipDestroyed += OnShipDestroyed;
        }

        // ── Сбор метрик: подписка на смерти ────────────────────────

        private static void OnShipDestroyed(ShipData victim, ShipData killer, string cause)
        {
            if (killer == null || victim == null) return;
            // Классификация жертвы по классу — инкремент соответствующего счётчика убийцы.
            // Астероид/столкновение сюда «сбитием» не приходят (эмитят CAUSE_COLLISION без killer-корабля).
            if (ShipUtils.IsDominator(victim))      killer.KillsDominator++;
            else if (ShipUtils.IsPirate(victim))    killer.KillsPirate++;
            else if (ShipUtils.IsTransport(victim)) killer.KillsTransport++;
            else if (ShipUtils.IsRanger(victim))    killer.KillsRanger++;
        }

        // ── Построение рейтинга ────────────────────────────────────

        /// <summary>Счёт корабля в конкретном рейтинге: взвешенная сумма метрик из cfg.Score.</summary>
        public static float GetScore(ShipData s, ShipRatingConfig cfg)
        {
            float total = 0f;
            if (cfg.Score == null) return total;
            foreach (var kv in cfg.Score)
                total += kv.Value * GetMetric(s, kv.Key);
            return total;
        }

        private static float GetMetric(ShipData s, string metric) => metric switch
        {
            "KillsDominator" => s.KillsDominator,
            "KillsPirate"    => s.KillsPirate,
            "KillsTransport" => s.KillsTransport,
            "KillsRanger"    => s.KillsRanger,
            "KillsTotal"     => s.KillsDominator + s.KillsPirate + s.KillsTransport + s.KillsRanger,
            "TradeProfit"    => s.TradeProfit,
            "CrimeRating"    => s.CrimeRating,
            _                => 0f
        };

        /// <summary>Селекторы ShipTypes/Owners объединяются по ИЛИ: достаточно попасть по любому.</summary>
        private static bool Participates(ShipData s, ShipRatingConfig cfg)
        {
            if (s == null || s.CurrentHull <= 0) return false;
            if (s.IsPlayer && !cfg.IncludePlayer) return false;
            if (cfg.ShipTypes != null && cfg.ShipTypes.Contains(s.ShipTypeId)) return true;
            if (cfg.Owners != null && cfg.Owners.Contains(s.Owner)) return true;
            return false;
        }

        /// <summary>Полный отсортированный (по убыванию счёта) список участников рейтинга —
        /// для новостей и будущего UI. Корабли со счётом ниже cfg.MinScore отбрасываются.</summary>
        public static List<(ShipData Ship, float Score)> Build(GalaxyData galaxy, ShipRatingConfig cfg)
        {
            var result = new List<(ShipData, float)>();
            if (galaxy == null || cfg == null) return result;
            foreach (var star in galaxy.StarsMap.Values)
            {
                var ships = star.Ships;
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i];
                    if (!Participates(s, cfg)) continue;
                    float score = GetScore(s, cfg);
                    if (score < cfg.MinScore) continue;
                    result.Add((s, score));
                }
            }
            result.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            return result;
        }

        /// <summary>Рейтинг по Id из конфига (null, если такого нет) — вход для будущего UI.</summary>
        public static ShipRatingConfig FindRating(GalaxyConfig config, string id)
        {
            var ratings = config?.Ratings;
            if (ratings == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < ratings.Count; i++)
                if (ratings[i]?.Id == id) return ratings[i];
            return null;
        }

        // ── Периодическая публикация «отличившихся» ────────────────

        public static void TickIfDue(GalaxyData galaxy, GalaxyConfig config)
        {
            if (galaxy == null || galaxy.CurrentTurn <= 0) return;
            var ratings = config?.Ratings;
            if (ratings == null) return;

            for (int r = 0; r < ratings.Count; r++)
            {
                var cfg = ratings[r];
                if (cfg == null || !cfg.Enabled || cfg.NewsStrideTurns <= 0) continue;
                if (galaxy.CurrentTurn % cfg.NewsStrideTurns != 0) continue;
                PublishNews(galaxy, cfg);
            }
        }

        private static void PublishNews(GalaxyData galaxy, ShipRatingConfig cfg)
        {
            var list = Build(galaxy, cfg);
            if (list.Count == 0) return;

            string title = string.IsNullOrEmpty(cfg.DisplayName) ? cfg.Id : cfg.DisplayName;
            // Строим TOP-часть отдельно, чтобы шаблон был компактным и локализуемым:
            // "{title} {updatedLabel} {topLabel} {items}", где items — "; "-разделённые записи.
            var sb = new System.Text.StringBuilder();
            int n = Mathf.Min(cfg.TopN, list.Count);
            string sep = NewsTexts.Format("rating.item_separator");
            if (string.IsNullOrEmpty(sep)) sep = "; ";
            string itemTpl = NewsTexts.Format("rating.item",
                ("rank", "{rank}"), ("name", "{name}"), ("score", "{score}"));
            if (string.IsNullOrEmpty(itemTpl)) itemTpl = "{rank}. {name} — {score} очк.";
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(sep);
                sb.Append(itemTpl
                    .Replace("{rank}", (i + 1).ToString())
                    .Replace("{name}", SafeName(list[i].Ship))
                    .Replace("{score}", list[i].Score.ToString("F0")));
            }
            string headline = NewsTexts.Format("rating.updated",
                ("title", title), ("items", sb.ToString()));
            if (string.IsNullOrEmpty(headline)) headline = $"{title} обновлён. Отличившиеся: {sb}";
            string category = string.IsNullOrEmpty(cfg.NewsCategory) ? title : cfg.NewsCategory;
            // Рейтинг привязан к конкретным сторонам (cfg.Owners). Если не указаны — рейтинг
            // «свободный» (напр. рейнджерский), считаем его новостью Coalition. Показываем
            // игроку только если хотя бы одна из сторон рейтинга ему дружественна.
            string sideA = cfg.Owners != null && cfg.Owners.Count > 0 ? cfg.Owners[0] : "Coalition";
            string sideB = cfg.Owners != null && cfg.Owners.Count > 1 ? cfg.Owners[1] : null;
            GalaxyNewsService.PostForSide(category, headline, sideA, sideB);
        }

        private static string SafeName(ShipData s) =>
            s == null || string.IsNullOrEmpty(s.Name) ? "неизвестный корабль" : s.Name;
    }
}
