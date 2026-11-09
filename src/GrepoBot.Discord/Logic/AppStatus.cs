namespace GrepoBot.Discord.Logic;

/// <summary>Whether the GrepoBot app works right now, set by the team with <c>/status set</c>.</summary>
public enum AppState
{
    Working,
    Down,
    Maintenance,
}

/// <summary>How each <see cref="AppState"/> looks in the status message and in the channel name.</summary>
public static class AppStatus
{
    #region Properties

    public static readonly IReadOnlyDictionary<AppState, AppStatusLook> Looks = new Dictionary<AppState, AppStatusLook>
    {
        [AppState.Working] = new("✅", "Working", "GrepoBot is working", 0x43B581,
            "Everything runs normally. Happy farming! 🌾"),
        [AppState.Down] = new("❌", "Not working", "GrepoBot is not working", 0xE54B4B,
            "GrepoBot does not work at the moment, usually after a Grepolis update. We're already working on a fix, no need to open a ticket."),
        [AppState.Maintenance] = new("🔧", "Maintenance", "GrepoBot is under maintenance", 0x5865F2,
            "We're working on GrepoBot or the website. Things will be back shortly."),
    };

    #endregion

    #region Public Methods

    public static AppStatusLook Look(AppState state)
    {
        return Looks[state];
    }

    #endregion
}

/// <summary><paramref name="Headline"/> is the big line of the status card.</summary>
public sealed record AppStatusLook(string Emoji, string Label, string Headline, uint Color, string DefaultText);
