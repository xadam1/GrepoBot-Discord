using System.Text.RegularExpressions;

namespace GrepoBot.Discord.Logic;

/// <summary>
/// How a reposted draft is laid out: as plain messages, or as embeds with a colored line on the left.
/// Embeds hold up to 4096 characters, plain messages 2000. Pings go into the message text because
/// mentions inside embeds never notify anyone. Discord draws all text next to a colored line (embeds and
/// containers alike) at 14px instead of 16px, a bot cannot change that.
/// </summary>
public static partial class RepostLayout
{
    #region Properties

    public const int EMBED_TEXT_LIMIT = 4096;

    public const string NO_COLOR = "none";

    /// <summary>The colors offered in the post picker. "none" posts a plain message without a line.</summary>
    public static readonly IReadOnlyList<RepostColor> Colors =
    [
        new("cyan", "GrepoBot cyan", "🩵", 0x3FD8F5),
        new("blue", "Blue", "🔵", 0x5865F2),
        new("green", "Green", "🟢", 0x43B581),
        new("gold", "Gold", "🟡", 0xF0B232),
        new("orange", "Orange", "🟠", 0xE67E22),
        new("red", "Red", "🔴", 0xE54B4B),
        new("purple", "Purple", "🟣", 0x9B59B6),
        new(NO_COLOR, "No color line (plain message)", "⚪", null),
    ];

    public static string DefaultColor => Colors[0].Value;

    #endregion

    #region Public Methods

    /// <summary>The color for a picker value, the GrepoBot cyan when it is unknown.</summary>
    public static RepostColor FindColor(string? value)
    {
        return Colors.FirstOrDefault(c => c.Value == value) ?? Colors[0];
    }

    /// <summary>
    /// Splits the draft into the messages of the copy. With a color every message is one embed and
    /// the ping goes into the first message's text; without a color the ping starts the text.
    /// Always returns at least one part.
    /// </summary>
    public static List<RepostPart> Build(string? content, ulong? pingRoleId, bool pingEveryone, uint? color)
    {
        var ping = pingEveryone ? "@everyone" : pingRoleId is { } roleId ? $"<@&{roleId}>" : null;
        var text = content ?? string.Empty;

        if (color is null)
        {
            var plain = MessageSplitter.Split(ping is null ? text : $"{ping}\n{text}".TrimEnd());
            return plain.Count == 0
                ? [new RepostPart(null, null)]
                : plain.Select(p => new RepostPart(p, null)).ToList();
        }

        var parts = MessageSplitter.Split(text, EMBED_TEXT_LIMIT)
            .Select((p, i) => new RepostPart(i == 0 ? ping : null, p))
            .ToList();

        return parts.Count == 0 ? [new RepostPart(ping, null)] : parts;
    }

    /// <summary>
    /// A file name Discord keeps as is, so <c>attachment://name</c> in an embed finds it
    /// (Discord turns spaces and other characters into underscores).
    /// </summary>
    public static string SafeFileName(string fileName)
    {
        var safe = UnsafeFileNameCharacters().Replace(fileName ?? string.Empty, "_");
        return string.IsNullOrWhiteSpace(safe.Trim('_', '.')) ? "file" : safe;
    }

    public static bool IsImage(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp";
    }

    #endregion

    #region Helpers

    [GeneratedRegex("[^A-Za-z0-9._-]")]
    private static partial Regex UnsafeFileNameCharacters();

    #endregion
}

/// <summary>A color of the post picker. <paramref name="Rgb"/> is null for a plain message.</summary>
public sealed record RepostColor(string Value, string Label, string Emoji, uint? Rgb);

/// <summary>One message of a copy: its text (ping or plain text) and the text of its colored embed.</summary>
public sealed record RepostPart(string? Content, string? EmbedText);
