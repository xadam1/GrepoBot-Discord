namespace GrepoBot.Discord.Logic;

/// <summary>
/// The topics members pick in the ticket form's dropdown. The value goes into the modal, the label
/// and emoji are shown in the ticket and the log.
/// </summary>
public static class TicketTopics
{
    #region Properties

    public const string INSTALL = "install";
    public const string LOGIN = "login";
    public const string FEATURE = "feature";
    public const string OTHER = "other";

    public static readonly IReadOnlyList<TicketTopic> All =
    [
        new(INSTALL, "📥", "Download & installation", "Downloading, installing or updating GrepoBot"),
        new(LOGIN, "🔑", "Login", "Signing in to GrepoBot or to the game through it"),
        new(FEATURE, "🧩", "Feature problem", "A feature does not work as expected"),
        new(OTHER, "❓", "Something else", "Anything that does not fit above"),
    ];

    #endregion

    #region Public Methods

    /// <summary>The topic for a dropdown value, "Something else" when it is unknown or missing.</summary>
    public static TicketTopic Find(string? value)
    {
        return All.FirstOrDefault(t => string.Equals(t.Value, value?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? All.First(t => t.Value == OTHER);
    }

    #endregion
}

/// <summary>A ticket topic, e.g. 📥 Download &amp; installation.</summary>
public sealed record TicketTopic(string Value, string Emoji, string Label, string Description)
{
    public string Display => $"{Emoji} {Label}";
}
