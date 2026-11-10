namespace GrepoBot.Discord.Logic;

/// <summary>
/// The buttons of an automatic answer, typed one per line as <c>Label | target</c>. The target is a web address
/// (<c>https://grepobot.net/download/</c>) or a channel placeholder (<c>{#tickets}</c>).
/// </summary>
public static class AutoReplyButtons
{
    #region Private Fields

    /// <summary>Discord allows 5 buttons in one row.</summary>
    public const int MAX_BUTTONS = 5;

    /// <summary>Discord's limit for a button label.</summary>
    public const int MAX_LABEL_LENGTH = 80;

    #endregion

    #region Public Methods

    /// <summary>Parses the buttons box. Returns the buttons, or an error for the first line that is not valid.</summary>
    public static (List<AutoReplyButtonSpec> Buttons, string? Error) Parse(string? input)
    {
        var buttons = new List<AutoReplyButtonSpec>();
        if (string.IsNullOrWhiteSpace(input))
        {
            return (buttons, null);
        }

        foreach (var line in input.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.LastIndexOf('|');
            if (separator <= 0)
            {
                return (buttons, $"`{line}`: write it as `Label | https://…` or `Label | {{#tickets}}`.");
            }

            var label = line[..separator].Trim();
            var target = line[(separator + 1)..].Trim();

            if (label.Length == 0 || label.Length > MAX_LABEL_LENGTH)
            {
                return (buttons, $"`{line}`: the label must have 1-{MAX_LABEL_LENGTH} characters.");
            }

            if (!IsWebAddress(target) && ServerLayout.ChannelPlaceholderKey(target) is null)
            {
                return (buttons, $"`{line}`: the target must start with `https://` or be a channel like `{{#tickets}}`.");
            }

            buttons.Add(new AutoReplyButtonSpec(label, target));
        }

        if (buttons.Count > MAX_BUTTONS)
        {
            return (buttons, $"Use at most {MAX_BUTTONS} buttons.");
        }

        return (buttons, null);
    }

    /// <summary>The buttons as they are typed in the form, one per line.</summary>
    public static string Format(IEnumerable<AutoReplyButtonSpec> buttons)
    {
        return string.Join("\n", buttons.Select(b => $"{b.Label} | {b.Target}"));
    }

    /// <summary>
    /// The address the button opens: the web address, or the channel link for a channel placeholder.
    /// Null when the channel does not exist on the server.
    /// </summary>
    public static string? ResolveUrl(AutoReplyButtonSpec button, ulong guildId, IEnumerable<(ulong Id, string Name)> channels)
    {
        if (IsWebAddress(button.Target))
        {
            return button.Target;
        }

        var key = ServerLayout.ChannelPlaceholderKey(button.Target);
        var channelId = key is null ? null : ServerLayout.FindChannelId(key, channels);
        return channelId is null ? null : $"https://discord.com/channels/{guildId}/{channelId}";
    }

    public static bool IsWebAddress(string? text)
    {
        return Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    #endregion
}

/// <summary>One button of an automatic answer.</summary>
public sealed record AutoReplyButtonSpec(string Label, string Target);
