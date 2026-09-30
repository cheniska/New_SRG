namespace SRG.Utils
{
    /// <summary>
    /// Хэш строки, одинаковый на всех платформах и во всех запусках (FNV-1a, 32 бита).
    /// <c>string.GetHashCode()</c> для этого не годится: в .NET Core он рандомизирован
    /// на процесс, а в разных рантаймах Unity (Mono/IL2CPP) реализован по-разному.
    /// Использовать везде, где из строки (обычно UID) выводится сид или слот.
    /// </summary>
    public static class StableHash
    {
        public static int Of(string s)
        {
            if (s == null) return 0;
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= s[i];
                    h *= 16777619u;
                }
                return (int)h;
            }
        }
    }
}
