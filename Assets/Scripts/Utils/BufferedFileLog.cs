using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace SRG.Utils
{
    /// <summary>
    /// Общая инфраструктура файлового лога с буферизацией. До этого PerfLog / EconomicLog /
    /// HighCommandLog / NewsLog повторяли одну и ту же реализацию (буфер, lock, EnsureInit
    /// с записью заголовка, flush по порогу / по требованию, disable при ошибке I/O).
    /// Инстансируется один раз на файл, публичный API специализированных логгеров остаётся
    /// прежним — они лишь делегируют Append/Flush сюда.
    /// </summary>
    public sealed class BufferedFileLog
    {
        private readonly string _fileName;
        private readonly string _header;
        private readonly string _tag;
        private readonly int _flushThreshold;
        private readonly StringBuilder _buffer = new();

        private string _filePath;
        private int _bufferLines;
        private bool _initialized;

        /// <summary>Глобальный выключатель. false — Append становится no-op.</summary>
        public bool Enabled { get; set; } = true;

        /// <param name="fileName">Имя файла в logs/, например "perf_log.txt".</param>
        /// <param name="header">Стартовые строки без trailing \n; каждая должна начинаться с '#'.
        /// Автодополняется меткой времени и переводом строки в конце.</param>
        /// <param name="tag">Метка для сообщений в Debug.LogWarning ("PerfLog", "EconomicLog", …).</param>
        /// <param name="flushThreshold">Число строк в буфере, при котором происходит авто-flush.</param>
        public BufferedFileLog(string fileName, string header, string tag, int flushThreshold)
        {
            _fileName = fileName;
            _header = header ?? "";
            _tag = tag ?? fileName;
            _flushThreshold = Mathf.Max(1, flushThreshold);
        }

        private string FilePath
        {
            get
            {
                if (_filePath == null) _filePath = RuntimePaths.LogFile(_fileName);
                return _filePath;
            }
        }

        private void EnsureInit()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                RuntimePaths.EnsureDirFor(FilePath);
                File.WriteAllText(FilePath, $"{_header}# session started {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{_tag}] cannot init at {FilePath}: {e.Message}");
                Enabled = false;
            }
        }

        public void Append(string line)
        {
            if (!Enabled) return;
            EnsureInit();
            if (!Enabled) return;
            lock (_buffer)
            {
                _buffer.AppendLine(line);
                _bufferLines++;
                if (_bufferLines >= _flushThreshold) FlushInternal();
            }
        }

        public void Flush() { lock (_buffer) { FlushInternal(); } }

        private void FlushInternal()
        {
            if (_buffer.Length == 0) return;
            try { File.AppendAllText(FilePath, _buffer.ToString()); }
            catch (Exception e) { Debug.LogWarning($"[{_tag}] flush failed: {e.Message}"); }
            _buffer.Clear();
            _bufferLines = 0;
        }
    }
}
