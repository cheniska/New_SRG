using SRG.Utils;

namespace SRG.Galaxy.Politics
{
    /// <summary>
    /// Диагностический лог публикаций <see cref="GalaxyNewsService"/> — пишется в
    /// logs/news_log.txt. Одна строка на решение фильтра «показать/скрыть» для новости.
    /// Формат: `T{turn} NEWS <verdict> cat=<c> player=<owner> sides=<a>[,b] reason=<r> text="..."`.
    ///
    /// verdict:
    ///   POST     — новость опубликована (без скоупа либо дружественная сторона).
    ///   DROP     — фильтр отбросил (все стороны hostile для игрока).
    ///   VARIANT  — выбор варианта текста для <see cref="GalaxyNewsService.CAT_DEFEAT"/>.
    /// </summary>
    public static class NewsLog
    {
        private static readonly BufferedFileLog _log = new(
            fileName: "news_log.txt",
            header:   "# News log —\n# Format: T<turn> NEWS <POST|DROP|VARIANT> cat=<c> player=<owner> sides=<a[,b]> reason=<r> text=\"...\"",
            tag:      "NewsLog",
            flushThreshold: 32);

        public static bool Enabled { get => _log.Enabled; set => _log.Enabled = value; }

        public static void Flush() => _log.Flush();

        /// <summary>Записать решение фильтра. verdict: POST/DROP/VARIANT.</summary>
        public static void Decision(int turn, string verdict, string category, string playerSide,
            string sideA, string sideB, string reason, string text)
        {
            if (!_log.Enabled) return;
            string sides = FormatSides(sideA, sideB);
            _log.Append($"T{turn} NEWS {verdict} cat={Safe(category)} player={Safe(playerSide)} sides={sides} reason={Safe(reason)} text=\"{Trim(text)}\"");
        }

        private static string FormatSides(string a, string b)
        {
            bool aEmpty = string.IsNullOrEmpty(a);
            bool bEmpty = string.IsNullOrEmpty(b);
            if (aEmpty && bEmpty) return "-";
            if (bEmpty) return Safe(a);
            if (aEmpty) return Safe(b);
            return $"{Safe(a)},{Safe(b)}";
        }

        private static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "-";
            return s.Replace(' ', '_').Replace('\n', '_').Replace('\t', '_');
        }

        private static string Trim(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Replace("\"", "'");
            if (s.Length > 200) s = s.Substring(0, 200) + "…";
            return s;
        }
    }
}
