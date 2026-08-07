namespace SiNiSistar2.Difficulty.Core;

/// <summary>
/// The default membership of the two status-ailment groups (SPEC002 6.1). The game has no such
/// grouping; these lists are read off the <c>AbnormalType</c> enumerator names and are therefore
/// an inference, which is why they are configuration rather than a table baked into the code
/// (SPEC002 DEC-107).
/// </summary>
public static class AbnormalTypeDefaults
{
    /// <summary>
    /// Statuses that stand for the will to resist being eroded. <c>Defilement</c> is deliberately
    /// absent: escalating escape difficulty with defilement is the game's own axis and the MOD
    /// must not act on the same quantity (SPEC002 FR-112, DEC-102).
    /// </summary>
    public static readonly IReadOnlyList<string> Pleasure = new[]
    {
        "Lustfull",
        "Lustfull_Forever",
        "LustMarkCurse",
        "MindControl",
        "MindIntegration",
        "Breast",
        "BreastSuper",
        "Milk",
        "WetNurse",
        "Drunk",
        "FallSleep",
        "Semen",
        "Semen_mucus",
    };

    /// <summary>Statuses that stand for the body being made heavy.</summary>
    public static readonly IReadOnlyList<string> Burden = new[]
    {
        "Pregnant",
        "Pregnant_Demi",
        "MotherBody",
        "FrogEgg",
        "FrogLEgg",
        "TentacleEgg",
        "TentacleEgg_GO",
        "SpiderEggSac",
        "LeechEgg",
        "LeechEgg_Boss",
        "LeechInfestation",
        "MeatBud",
        "MeatBuding",
        "Parasite",
        "ParasiteLv13",
        "LivestockParasite",
        "Assimilation_Seed",
        "EvilWoodSeed",
    };

    /// <summary>
    /// The status the MOD refuses to treat as a pleasure status. Named here so the rule and its
    /// enforcement do not drift apart.
    /// </summary>
    public const string Defilement = "Defilement";
}
