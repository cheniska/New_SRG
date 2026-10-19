namespace SRG.Utils
{
    /// <summary>
    /// Короткие идентификаторы для логов и UI. Процедурные спрайты — в
    /// <c>SRG.Presentation.Common.ProceduralSprites</c> (симуляция графику не создаёт).
    /// </summary>
    public static class SpriteUtility
    {
        /// <summary>
        /// Безопасно возвращает первые <paramref name="len"/> символов строки.
        /// Если строка короче — возвращает её целиком.
        /// </summary>
        public static string ShortId(string id, int len = 6)
            => id == null ? "null" : id.Length >= len ? id[..len] : id;
    }
}
