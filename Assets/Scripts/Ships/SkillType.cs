namespace SRG.Ships
{
    public enum SkillType
    {
        Accuracy    = 0,
        Mobility    = 1,
        Technical   = 2,
        Trader      = 3,
        Charm       = 4,
        Leadership  = 5,
    }

    public enum ExpCategory
    {
        None           = 0,
        DominatorKill  = 1,
        PirateKill     = 2,
        GoodShipKill   = 3,
        Trade          = 4,
    }

    public static class SkillTypeExtensions
    {
        public const int SkillCount = 6;

        public static readonly SkillType[] All =
        {
            SkillType.Accuracy, SkillType.Mobility, SkillType.Technical,
            SkillType.Trader,   SkillType.Charm,    SkillType.Leadership,
        };

        public static string DisplayNameRu(this SkillType s) => s switch
        {
            SkillType.Accuracy   => "Точность",
            SkillType.Mobility   => "Манёвр",
            SkillType.Technical  => "Техника",
            SkillType.Trader     => "Торговля",
            SkillType.Charm      => "Обаяние",
            SkillType.Leadership => "Лидерство",
            _ => s.ToString(),
        };
    }
}
