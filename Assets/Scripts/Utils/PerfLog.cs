namespace SRG.Utils
{
    /// <summary>Файловый лог для профайлинг-инструментации (тайминги подсистем хода).
    /// Пишет в logs/perf_log.txt — отдельно от Unity Console, чтобы не мешать остальным логам.</summary>
    public static class PerfLog
    {
        private static readonly BufferedFileLog _log = new(
            fileName: "perf_log.txt",
            header:   "# Perf log —",
            tag:      "PerfLog",
            flushThreshold: 20);

        public static void Log(string line) => _log.Append(line);
        public static void Flush() => _log.Flush();
    }
}
