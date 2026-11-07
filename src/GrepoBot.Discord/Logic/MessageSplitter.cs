namespace GrepoBot.Discord.Logic;

/// <summary>
/// Splits text longer than Discord's 2000 characters per bot message. Nitro users can write
/// drafts of up to 4000 characters, the bot then posts them as several messages.
/// Splits on a blank line, then a line break, then a space, and never inside a code block fence.
/// </summary>
public static class MessageSplitter
{
    #region Private Fields

    public const int MAX_LENGTH = 2000;

    #endregion

    #region Public Methods

    public static List<string> Split(string text, int maxLength = MAX_LENGTH)
    {
        var parts = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return parts;
        }

        var remaining = text;
        while (remaining.Length > maxLength)
        {
            var cut = FindCut(remaining, maxLength);
            parts.Add(remaining[..cut].TrimEnd());
            remaining = remaining[cut..].TrimStart('\n', '\r', ' ');
        }

        if (remaining.Length > 0)
        {
            parts.Add(remaining);
        }

        return parts;
    }

    #endregion

    #region Private Methods

    private static int FindCut(string text, int maxLength)
    {
        var window = text[..maxLength];
        var minimum = maxLength / 2;

        foreach (var separator in new[] { "\n\n", "\n", " " })
        {
            var index = window.LastIndexOf(separator, StringComparison.Ordinal);
            if (index >= minimum)
            {
                return index;
            }
        }

        return maxLength;
    }

    #endregion
}
