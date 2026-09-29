using System.Globalization;
using SRG.Galaxy;

namespace SRG.Dialog
{
    /// <summary>
    /// Общие операции над <see cref="DialogContext"/>, которые раньше лежали приватными копиями
    /// в каждом модуле действий (<c>ReadInt</c> был продублирован четырежды, <c>Pair</c> — дважды,
    /// чтение строки из <c>Data</c> — трижды).
    ///
    /// <para>Все чтения устойчивы к <c>null</c>-контексту: действия и теги вызываются в том числе
    /// до старта диалога и после его закрытия.</para>
    /// </summary>
    public static class DialogContextExt
    {
        /// <summary>Пара «цель разговора / корабль игрока». Половина действий начинается с неё,
        /// и обе половины почти всегда проверяются на null вместе.</summary>
        public static (ShipData Target, ShipData Player) Pair(this DialogContext ctx)
            => (ctx?.TargetShip, ctx?.PlayerShip);

        /// <summary>Строка из <c>ctx.Data</c>. <paramref name="fallback"/> — когда ключа нет.</summary>
        public static string ReadStr(this DialogContext ctx, string key, string fallback = "")
            => ctx?.Data != null && ctx.Data.TryGetValue(key, out var v) ? v : fallback;

        /// <summary>Целое из <c>ctx.Data</c>. Разбор всегда инвариантный: значения кладутся туда
        /// через <c>CultureInfo.InvariantCulture</c>, и на локали с другим разделителем
        /// культурно-зависимый парсинг молча вернул бы fallback.</summary>
        public static int ReadInt(this DialogContext ctx, string key, int fallback = 0)
        {
            if (ctx?.Data == null) return fallback;
            return ctx.Data.TryGetValue(key, out var s)
                && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                ? v : fallback;
        }

        /// <summary>Записать целое в <c>ctx.Data</c> в том же инвариантном формате, в котором его
        /// прочитает <see cref="ReadInt"/>.</summary>
        public static void WriteInt(this DialogContext ctx, string key, int value)
        {
            if (ctx?.Data == null) return;
            ctx.Data[key] = value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Первое непустое значение из перечня ключей <c>ctx.Data</c>.
        /// Нужно составным тегам (<c>{money_amount}</c>, <c>{third_party_name}</c>), где одна и та же
        /// фраза пула переиспользуется ветками, хранящими значение под разными именами.</summary>
        public static string FirstPresent(this DialogContext ctx, string[] keys)
        {
            if (ctx?.Data == null || keys == null) return null;
            for (int i = 0; i < keys.Length; i++)
                if (ctx.Data.TryGetValue(keys[i], out var v) && !string.IsNullOrEmpty(v)) return v;
            return null;
        }
    }
}
