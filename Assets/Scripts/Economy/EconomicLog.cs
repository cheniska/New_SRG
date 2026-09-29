using SRG.Utils;

namespace SRG.Economy
{
    /// <summary>
    /// Лог экономических событий галактики: сделки транспортов, инфляция, обновления магазинов,
    /// дрейф рынка. Пишется в &lt;project&gt;/logs/economic_log.txt.
    /// Буферизованный вывод — flush раз в FlushThreshold строк или по требованию.
    ///
    /// Формат: одна строка на событие, `T{turn} {CATEGORY} {actor} {action} key=value key=value ...`
    /// Удобно грепать и парсить (awk/Python).
    ///
    /// Категории можно индивидуально включать/выключать через флаги Log* (рантайм).
    /// </summary>
    public static class EconomicLog
    {
        private static readonly BufferedFileLog _log = new(
            fileName: "economic_log.txt",
            header:   "# Economic log —\n# Format: T<turn> CATEGORY actor action key=value ...",
            tag:      "EconomicLog",
            flushThreshold: 200);

        /// <summary>Глобальный выключатель. false — ничего не пишется.</summary>
        public static bool Enabled { get => _log.Enabled; set => _log.Enabled = value; }

        /// <summary>Сделки трейдеров (BUY/SELL/LAUNCH/LAND/PLAN/IDLE). Самое полезное для анализа.</summary>
        public static bool LogTrade { get; set; } = true;

        /// <summary>Инфляция (ежемесячный тик + шоки).</summary>
        public static bool LogInflation { get; set; } = true;

        /// <summary>Обновления магазина оборудования (1 строка на planet/week — сводка).</summary>
        public static bool LogShop { get; set; } = true;

        /// <summary>Per-planet per-good поток ProductionDelta. ВЫСОКАЯ ВОЛЮМ — по умолчанию выключен,
        /// включать только когда профилируешь движение цен/стока.</summary>
        public static bool LogMarket { get; set; } = false;

        /// <summary>Спавн NPC.</summary>
        public static bool LogSpawn { get; set; } = true;

        /// <summary>Принудительный сброс буфера в файл. Стоит вызывать при завершении симуляции/сейве.</summary>
        public static void Flush() => _log.Flush();

        // ── Публичные хелперы — по одному на категорию ────────────────────────────

        public static void Trade(int turn, string actor, string action, string fields)
        {
            if (!LogTrade) return;
            _log.Append($"T{turn} TRADE {actor} {action} {fields}");
        }

        public static void Inflation(int turn, string action, string fields)
        {
            if (!LogInflation) return;
            _log.Append($"T{turn} INFLATION Galaxy {action} {fields}");
        }

        public static void Shop(int turn, string planet, string action, string fields)
        {
            if (!LogShop) return;
            _log.Append($"T{turn} SHOP {planet} {action} {fields}");
        }

        public static void Market(int turn, string planet, string action, string fields)
        {
            if (!LogMarket) return;
            _log.Append($"T{turn} MARKET {planet} {action} {fields}");
        }

        public static void Spawn(int turn, string actor, string action, string fields)
        {
            if (!LogSpawn) return;
            _log.Append($"T{turn} SPAWN {actor} {action} {fields}");
        }

        /// <summary>Произвольная запись — для одноразовых событий, не вписывающихся в категории.</summary>
        public static void Custom(int turn, string category, string actor, string action, string fields)
        {
            _log.Append($"T{turn} {category} {actor} {action} {fields}");
        }

        // ── Утилиты для форматирования полей key=value ────────────────────────────

        /// <summary>Безопасное представление: заменяет пробелы на «_», чтобы не ломать формат key=value.</summary>
        public static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "-";
            return s.Replace(' ', '_').Replace('\n', '_').Replace('\t', '_');
        }
    }
}
