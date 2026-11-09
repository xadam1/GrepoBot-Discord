using System.Text;
using System.Text.RegularExpressions;

namespace GrepoBot.Discord.Logic;

/// <summary>
/// Reads a release draft written as a normal Discord message:
/// <code>
/// 1.4.0 - Building is back!
/// Optional intro text.
///
/// New
/// - Auto farming for all islands
/// Improved
/// - Faster building queue
/// Fixed
/// - Login works again
/// </code>
/// The first line holds the version (optionally "GrepoBot", "v" and a headline after a dash). Section headings
/// are recognized by their word (New, Features, Added, Improved, Changes, Fixed, Fixes, Bugs, Removed, with or
/// without #, ** or a colon); any other line starting with # is a custom heading. Bullets may use -, *, • or +.
/// </summary>
public static partial class ReleaseNotes
{
    #region Properties

    /// <summary>Discord allows 4000 characters of text per card, the rest is banner text, thanks and buttons.</summary>
    public const int MAX_LENGTH = 3300;

    public const string THANKS_TEXT =
        "💙 **Thank you** for using GrepoBot and for all your ideas and bug reports, they make every update better!";

    public const string TEMPLATE =
        "1.4.0 - Building is back!\n" +
        "A short intro (optional).\n\n" +
        "New\n- Something new\n" +
        "Improved\n- Something better\n" +
        "Fixed\n- Something that works again";

    private static readonly (string Title, string[] Words)[] _knownSections =
    [
        ("✨ New features", ["new", "new features", "features", "added", "whats new", "new stuff"]),
        ("⚡ Improvements", ["improved", "improvements", "changes", "changed", "updated", "better"]),
        ("🪲 Bug fixes", ["fixed", "fixes", "bug fixes", "bugfixes", "bugs", "bug fix"]),
        ("🧹 Removed", ["removed"]),
    ];

    #endregion

    #region Public Methods

    /// <summary>Parses the draft. Returns null and a problem text when it has no version on the first line.</summary>
    public static ReleaseDraft? Parse(string? text, out string? problem)
    {
        var lines = (text ?? string.Empty).Replace("\r", string.Empty).Split('\n').ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
        {
            lines.RemoveAt(0);
        }

        var match = lines.Count == 0 ? null : VersionLine().Match(lines[0].Trim());
        if (match is null || !match.Success)
        {
            problem = "The first line of the draft must be the version, e.g. `1.4.0` or `1.4.0 - Building is back!`.";
            return null;
        }

        if ((text ?? string.Empty).Length > MAX_LENGTH)
        {
            problem = $"The draft is {text!.Length} characters long, a release card fits {MAX_LENGTH}. Please shorten it.";
            return null;
        }

        var version = match.Groups["version"].Value;
        var headline = Clean(match.Groups["headline"].Value);

        var intro = new StringBuilder();
        var sections = new List<ReleaseSection>();
        StringBuilder? body = null;
        string? title = null;

        foreach (var raw in lines.Skip(1))
        {
            var line = raw.TrimEnd();
            var heading = GetHeading(line);
            if (heading is not null)
            {
                AddSection(sections, title, body);
                title = heading;
                body = new StringBuilder();
                continue;
            }

            var target = body ?? intro;
            target.AppendLine(Bullet(line));
        }

        AddSection(sections, title, body);
        problem = null;
        return new ReleaseDraft(version, headline.Length == 0 ? null : headline, NullIfEmpty(intro.ToString()), sections);
    }

    /// <summary>"New:", "## Fixed", "**Bug fixes**" → the section title, null when the line is no heading.</summary>
    public static string? GetHeading(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 40)
        {
            return null;
        }

        var startsWithHash = trimmed.StartsWith('#');
        var words = new string(trimmed.ToLowerInvariant().Where(c => char.IsLetter(c) || c == ' ').ToArray()).Trim();
        words = MultipleSpaces().Replace(words, " ");

        foreach (var (title, known) in _knownSections)
        {
            if (known.Contains(words))
            {
                return title;
            }
        }

        if (startsWithHash)
        {
            var custom = trimmed.TrimStart('#').Trim().Trim('*', '_').TrimEnd(':').Trim();
            return custom.Length == 0 ? null : custom;
        }

        return null;
    }

    #endregion

    #region Private Methods

    private static void AddSection(List<ReleaseSection> sections, string? title, StringBuilder? body)
    {
        if (title is null || body is null)
        {
            return;
        }

        var text = NullIfEmpty(body.ToString());
        if (text is not null)
        {
            sections.Add(new ReleaseSection(title, text));
        }
    }

    /// <summary>"• text", "* text", "+ text" → "- text" (Discord's list), other lines stay as they are.</summary>
    private static string Bullet(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length > 1 && trimmed[0] is '-' or '*' or '•' or '+' && trimmed[1] == ' ')
        {
            return "- " + trimmed[2..].Trim();
        }

        return line;
    }

    private static string Clean(string text)
    {
        return text.Trim().Trim('*', '_').Trim();
    }

    private static string? NullIfEmpty(string text)
    {
        var trimmed = text.Trim('\n', ' ');
        return trimmed.Length == 0 ? null : trimmed;
    }

    [GeneratedRegex(@"^(?:#+\s*)?(?:\*\*)?(?:GrepoBot\s+)?v?(?<version>\d+(?:\.\d+){1,3})(?:\*\*)?\s*(?:[-–—:|]\s*)?(?<headline>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionLine();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleSpaces();

    #endregion
}

/// <summary>A parsed release draft. <paramref name="Sections"/> keep the draft's order.</summary>
public sealed record ReleaseDraft(string Version, string? Headline, string? Intro, IReadOnlyList<ReleaseSection> Sections);

public sealed record ReleaseSection(string Title, string Body);
