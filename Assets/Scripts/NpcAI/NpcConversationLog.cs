using SRG.Dialog;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.Simulation;

namespace SRG.NpcAI
{
    /// <summary>
    /// Публикатор коротких NPC↔NPC «диалогов» в панель уведомлений игрока.
    /// Одна запись = один переговор (одна попытка Rob/Extort/Ceasefire/OfferMoney):
    /// заголовок «имя1 → имя2», в теле — реплика инициатора и ответ получателя.
    ///
    /// Реплики берутся рандомно из <see cref="DialogsConfig.StringPools"/> — тех же пулов,
    /// что использует диалог игрока (Goods.Send, Goods.<Type>Ok/No, Money.Send, Money.<Type>Ok/No и т.п.).
    /// Так реплики NPC-переговоров звучат тем же голосом, что и реплики в игровых диалогах.
    ///
    /// Публикуется только если оба участника не игрок (диалоги с игроком идут через DialogService)
    /// и хотя бы один из них в звезде игрока (иначе — шум за галактикой).
    /// </summary>
    public static class NpcConversationLog
    {
        /// <summary>Опубликовать переговор с пулами. Каждый ключ — имя pool'а в StringPools;
        /// если pool пуст или отсутствует — используется fallback-строка. Плейсхолдеры
        /// в pool-строках подставляются вручную (см. `substitutions` — набор ("&lt;Tag&gt;", value)).
        /// null-пары в substitutions игнорируются.</summary>
        public static void Post(ShipData initiator, ShipData recipient,
            string initiatorPoolKey, string initiatorFallback,
            string recipientPoolKey, string recipientFallback,
            params (string placeholder, string value)[] substitutions)
        {
            if (initiator == null || recipient == null) return;
            if (initiator.IsPlayer || recipient.IsPlayer) return;
            var playerStar = GameWorld.CurrentStar;
            if (playerStar == null) return;
            if (initiator.CurrentStarUid != playerStar.Uid
                && recipient.CurrentStarUid != playerStar.Uid) return;

            string a = ResolveLine(initiatorPoolKey, initiatorFallback, substitutions);
            string b = ResolveLine(recipientPoolKey, recipientFallback, substitutions);
            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return;

            string aName = string.IsNullOrEmpty(initiator.Name) ? "?" : initiator.Name;
            string bName = string.IsNullOrEmpty(recipient.Name) ? "?" : recipient.Name;
            string header = $"{aName} → {bName}";
            string body = FormatLines(a, b);
            string text = string.IsNullOrEmpty(body) ? header : $"{header}\n{body}";
            GalaxyNewsService.Post(GalaxyNewsService.CAT_SYSTEM, text);
        }

        private static string ResolveLine(string poolKey, string fallback,
            (string placeholder, string value)[] substitutions)
        {
            string raw = DialogService.PickFromPool(poolKey);
            if (string.IsNullOrEmpty(raw)) raw = fallback;
            if (string.IsNullOrEmpty(raw)) return null;
            if (substitutions != null && substitutions.Length > 0)
            {
                for (int i = 0; i < substitutions.Length; i++)
                {
                    var (ph, val) = substitutions[i];
                    if (string.IsNullOrEmpty(ph)) continue;
                    raw = raw.Replace(ph, val ?? "");
                }
            }
            return raw;
        }

        private static string FormatLines(string a, string b)
        {
            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return "";
            if (string.IsNullOrEmpty(a)) return $"— {b}";
            if (string.IsNullOrEmpty(b)) return $"— {a}";
            return $"— {a}\n— {b}";
        }

        /// <summary>Утилита: маппинг ShipTypeId → короткое имя типа для ключей пулов
        /// (Transport/Liner/Diplomat/Ranger/Warrior/Pirate; всё остальное → Transport).</summary>
        public static string PoolTypeOf(ShipData ship)
        {
            if (ship == null) return "Transport";
            return ship.ShipTypeId switch
            {
                "Transport" => "Transport",
                "Liner"     => "Liner",
                "Diplomat"  => "Diplomat",
                "Ranger"    => "Ranger",
                "Warrior"   => "Warrior",
                "Pirate"    => "Pirate",
                _           => "Transport",
            };
        }
    }
}
