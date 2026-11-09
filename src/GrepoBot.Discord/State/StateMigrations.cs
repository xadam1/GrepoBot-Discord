namespace GrepoBot.Discord.State;

/// <summary>
/// Replaces texts that were saved while they were still the bot's defaults, so improved default texts
/// reach existing installations. Texts an admin changed are never touched.
/// </summary>
internal static class StateMigrations
{
    #region Private Fields

    private static readonly string[] _oldWelcomeDefaults =
    [
        "Hey {user}, welcome to **{server}**! 👋\n\n" +
        "Please read and accept the rules in {rules} to unlock the rest of the server.\n" +
        "You are our member **#{membercount}**.",
        "Hey {user}, welcome to **{server}**! 👋\n\n" +
        "Please have a look at {rules}. Questions about GrepoBot? Ask in the chat, and for licence or payment problems open a ticket.\n" +
        "You are our member **#{membercount}**.",
        "Hey {user}, welcome to **{server}**! 👋\n\n" +
        "Please have a look at {rules}. Questions about GrepoBot? Ask in the chat, and if something does not work for you, open a ticket.\n" +
        "You are our member **#{membercount}**.",
    ];

    private static readonly string[] _oldTicketDefaults =
    [
        "Hi {user}, thanks for reaching out! 👋\n\n" +
        "A team member will be with you soon. Meanwhile, please describe your problem in as much detail as you can: " +
        "your GrepoBot version, what you did and what happened. Screenshots help a lot.",
    ];

    #endregion

    #region Public Methods

    public static void Apply(BotState state)
    {
        if (_oldWelcomeDefaults.Contains(state.Welcome.Message))
        {
            state.Welcome.Message = WelcomeSettings.DEFAULT_MESSAGE;
        }

        if (_oldTicketDefaults.Contains(state.Tickets.Message))
        {
            state.Tickets.Message = TicketSettings.DEFAULT_MESSAGE;
        }
    }

    #endregion
}
