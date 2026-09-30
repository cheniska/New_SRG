using SRG.Utils;

namespace SRG.NpcAI
{
    /// <summary>
    /// Логгер решений фракционных генштабов (<see cref="FactionHighCommand"/>).
    /// Пишет в logs/factions_log.txt (относительно корня проекта). Формат совместим с
    /// <see cref="SRG.Economy.EconomicLog"/>: одна строка на событие, `T{turn} HC {factionKey} {action} k=v ...`.
    ///
    /// Категории (можно выключать отдельно):
    /// - INIT/RESTORE — бутстрап/загрузка ГШ (стратегия, счётчик).
    /// - EVAL — ход оценки: START, TARGET, STAGING, NOT_READY, NO_TARGET.
    /// - DIRECTIVE — выданные Attack/Defend + причина.
    /// - EXPIRE — истечение директивы (событие приходит из Directive.Tick через явный вызов).
    /// </summary>
    public static class HighCommandLog
    {
        private static readonly BufferedFileLog _log = new(
            fileName: "factions_log.txt",
            header:   "# Factions log —\n# Format: T<turn> HC <faction> <action> key=value ...\n# faction: Owner либо Owner|Race (для per-race ГШ синтетов).",
            tag:      "HighCommandLog",
            flushThreshold: 50);

        public static bool Enabled { get => _log.Enabled; set => _log.Enabled = value; }
        public static bool LogInit { get; set; } = true;
        public static bool LogEval { get; set; } = true;
        public static bool LogDirective { get; set; } = true;
        public static bool LogExpire { get; set; } = true;
        public static bool LogProgress { get; set; } = true;

        public static void Flush() => _log.Flush();

        // ── Категории ────────────────────────────────────────────────────────────

        public static void Init(int turn, string factionKey, string action, string fields)
        {
            if (!LogInit) return;
            _log.Append($"T{turn} HC {Safe(factionKey)} {action} {fields}");
        }

        public static void Eval(int turn, string factionKey, string action, string fields)
        {
            if (!LogEval) return;
            _log.Append($"T{turn} HC {Safe(factionKey)} {action} {fields}");
        }

        public static void Directive(int turn, string factionKey, string action, string fields)
        {
            if (!LogDirective) return;
            _log.Append($"T{turn} HC {Safe(factionKey)} {action} {fields}");
        }

        public static void Expire(int turn, string factionKey, string action, string fields)
        {
            if (!LogExpire) return;
            _log.Append($"T{turn} HC {Safe(factionKey)} {action} {fields}");
        }

        /// <summary>Периодический снапшот состояния активной директивы (staging power, target hostile,
        /// распределение своих кораблей и т.п.) — чтобы понимать, почему атака не завершается.</summary>
        public static void Progress(int turn, string factionKey, string action, string fields)
        {
            if (!LogProgress) return;
            _log.Append($"T{turn} HC {Safe(factionKey)} {action} {fields}");
        }

        public static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "-";
            return s.Replace(' ', '_').Replace('\n', '_').Replace('\t', '_');
        }

        /// <summary>Короткий UID (6 знаков) для читаемости в логе.</summary>
        public static string ShortId(string uid) => string.IsNullOrEmpty(uid) || uid.Length < 6 ? uid : uid.Substring(0, 6);
    }
}
