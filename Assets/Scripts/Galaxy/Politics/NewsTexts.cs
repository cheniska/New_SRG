using System.Collections.Generic;
using System.Text;
using SRG.Config;
using SRG.Core;

namespace SRG.Galaxy.Politics
{
    /// <summary>
    /// Локализованные тексты новостей — фасад над <see cref="TextConfig.News"/>.
    /// Отделяет ключи новостей (латинские идентификаторы, стабильные — используются в
    /// <see cref="GalaxyNewsEntry.Category"/> и для сравнения в <see cref="GalaxyNewsService.ClassifyKind"/>)
    /// от отображаемых имён (могут переводиться, меняться без правки кода).
    ///
    /// <para>Плейсхолдеры в шаблонах — именованные: <c>{starName}</c>, <c>{newController}</c> и т.п.
    /// Пример: <c>NewsTexts.Format("system_control.new", ("starName", star.Name), ("sectorName", sector.Name), ("newController", ctrl))</c>.</para>
    /// </summary>
    public static class NewsTexts
    {
        /// <summary>Отображаемое имя категории по ключу. Если ключ не найден в
        /// TextConfig.News.Categories — возвращает сам ключ (visible-но-broken индикатор).</summary>
        public static string Category(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            var cats = TryGetConfig()?.Categories;
            if (cats != null && cats.TryGetValue(key, out var display) && !string.IsNullOrEmpty(display))
                return display;
            return key;
        }

        /// <summary>Форматированный текст новости по ключу шаблона. Плейсхолдеры <c>{name}</c>
        /// подставляются из args. Отсутствующий ключ шаблона → пустая строка (Post это отфильтрует).</summary>
        public static string Format(string templateKey, params (string name, object value)[] args)
        {
            if (string.IsNullOrEmpty(templateKey)) return "";
            var tpls = TryGetConfig()?.Templates;
            if (tpls == null || !tpls.TryGetValue(templateKey, out var tpl) || string.IsNullOrEmpty(tpl))
                return "";
            if (args == null || args.Length == 0) return tpl;
            var sb = new StringBuilder(tpl);
            for (int i = 0; i < args.Length; i++)
            {
                var (name, value) = args[i];
                if (string.IsNullOrEmpty(name)) continue;
                sb.Replace("{" + name + "}", value?.ToString() ?? "");
            }
            return sb.ToString();
        }

        /// <summary>Есть ли ключ шаблона в конфиге. Используется вызывающим кодом, чтобы
        /// решить, писать ли новость (без ключа — молча пропустить).</summary>
        public static bool HasTemplate(string templateKey)
        {
            if (string.IsNullOrEmpty(templateKey)) return false;
            var tpls = TryGetConfig()?.Templates;
            return tpls != null && tpls.ContainsKey(templateKey);
        }

        private static NewsTextsConfig TryGetConfig() =>
            GalaxyManager.Instance?.Context?.TextConfig?.News;
    }
}
