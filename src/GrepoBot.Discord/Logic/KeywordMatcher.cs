using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GrepoBot.Discord.State;

namespace GrepoBot.Discord.Logic;

/// <summary>
/// Decides whether a message triggers an auto-reply rule. Pure, so it is fully unit tested.
/// <para>
/// Keywords are matched as whole words, case- and accent-insensitive ("licence" matches "Licence",
/// "pripojeni" matches "připojení"). A trailing <c>*</c> matches any word ending ("instal*" matches
/// "install", "installation"). A keyword can be a phrase ("how to install").
/// </para>
/// </summary>
public static class KeywordMatcher
{
    #region Private Fields

    private static readonly TimeSpan _regexTimeout = TimeSpan.FromMilliseconds(100);

    #endregion

    #region Public Methods

    /// <summary>Returns true when <paramref name="text"/> triggers <paramref name="rule"/>.</summary>
    public static bool IsMatch(AutoReplyRule rule, string text)
    {
        if (rule.Keywords.Count == 0 || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (rule.Mode == MatchMode.Regex)
        {
            return rule.Keywords.Any(pattern => IsRegexMatch(pattern, text));
        }

        var normalized = Normalize(text);
        var matches = rule.Keywords.Select(keyword => IsKeywordMatch(keyword, normalized));

        return rule.Mode == MatchMode.All
            ? matches.All(m => m)
            : matches.Any(m => m);
    }

    /// <summary>Returns the first enabled rule that matches, or null.</summary>
    public static AutoReplyRule? FindMatch(IEnumerable<AutoReplyRule> rules, string text)
    {
        return rules.FirstOrDefault(rule => rule.Enabled && IsMatch(rule, text));
    }

    /// <summary>Splits the text typed in the keywords box into keywords (one per line or comma separated).</summary>
    public static List<string> ParseKeywords(string input, MatchMode mode)
    {
        // Regex patterns may contain commas, so they are one per line only.
        var separators = mode == MatchMode.Regex
            ? new[] { '\n' }
            : new[] { '\n', ',' };

        return input
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Returns the first invalid regex pattern with the reason, or null when all are valid.</summary>
    public static string? ValidatePatterns(IEnumerable<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            try
            {
                _ = new Regex(pattern, RegexOptions.IgnoreCase, _regexTimeout);
            }
            catch (ArgumentException ex)
            {
                return $"`{pattern}`: {ex.Message}";
            }
        }

        return null;
    }

    /// <summary>Lower case, without accents and with single spaces.</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            builder.Append(c);
            lastWasSpace = false;
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).Trim();
    }

    #endregion

    #region Private Methods

    private static bool IsKeywordMatch(string keyword, string normalizedText)
    {
        var normalizedKeyword = Normalize(keyword);
        if (normalizedKeyword.Length == 0)
        {
            return false;
        }

        var isPrefix = normalizedKeyword.EndsWith('*');
        var word = Regex.Escape(normalizedKeyword.TrimEnd('*'));
        if (word.Length == 0)
        {
            return false;
        }

        var pattern = isPrefix
            ? $@"(?<![\p{{L}}\p{{N}}]){word}"
            : $@"(?<![\p{{L}}\p{{N}}]){word}(?![\p{{L}}\p{{N}}])";

        return Regex.IsMatch(normalizedText, pattern, RegexOptions.CultureInvariant, _regexTimeout);
    }

    private static bool IsRegexMatch(string pattern, string text)
    {
        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, _regexTimeout);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    #endregion
}
