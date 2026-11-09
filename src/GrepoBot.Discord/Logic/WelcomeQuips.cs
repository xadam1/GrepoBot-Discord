namespace GrepoBot.Discord.Logic;

/// <summary>
/// Playful Grepolis-themed lines for the welcome card, like Discord's own join messages.
/// They follow the member's name: "Bob just docked in the harbour."
/// </summary>
public static class WelcomeQuips
{
    #region Properties

    public static readonly IReadOnlyList<string> All =
    [
        "just docked in the harbour.",
        "sailed in with a fleet of biremes.",
        "was sent here by Zeus himself.",
        "arrived. The farmers can finally rest.",
        "joined the empire. Hide your silver!",
        "made it past the city walls.",
        "brought plenty of wood, stone and silver.",
        "was spotted by our scouts. Say hi!",
        "founded a new city right here.",
        "has entered the Agora.",
        "rowed all the way from Athens.",
        "is ready to let the bot do the farming.",
    ];

    #endregion

    #region Public Methods

    public static string Pick(Random? random = null)
    {
        return All[(random ?? Random.Shared).Next(All.Count)];
    }

    #endregion
}
