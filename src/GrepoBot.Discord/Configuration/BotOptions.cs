namespace GrepoBot.Discord.Configuration;

/// <summary>
/// Connection settings, bound from the <c>Discord</c> section (or <c>Discord__*</c> environment variables).
/// </summary>
public sealed class DiscordOptions
{
    #region Properties

    public const string SECTION = "Discord";

    /// <summary>Bot token from the Developer Portal. Never commit it, set <c>Discord__Token</c> on the Pi.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>The community server the bot serves. Commands are registered to it so they appear instantly.</summary>
    public ulong GuildId { get; set; }

    /// <summary>Custom status shown under the bot's name.</summary>
    public string Status { get; set; } = string.Empty;

    #endregion
}

/// <summary>
/// Bot behaviour settings, bound from the <c>Bot</c> section.
/// </summary>
public sealed class BotOptions
{
    #region Properties

    public const string SECTION = "Bot";

    /// <summary>Folder for <c>state.json</c>. Relative paths are resolved against the app folder.</summary>
    public string DataDirectory { get; set; } = "data";

    public string WebsiteUrl { get; set; } = "https://grepobot.net";

    public string ChangelogUrl { get; set; } = "https://grepobot.net/changelog/";

    #endregion

    #region Public Methods

    public string ResolveDataDirectory()
    {
        if (Path.IsPathRooted(DataDirectory))
        {
            return DataDirectory;
        }

        return Path.Combine(AppContext.BaseDirectory, DataDirectory);
    }

    #endregion
}
