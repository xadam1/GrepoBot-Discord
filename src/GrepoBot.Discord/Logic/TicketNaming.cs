using System.Globalization;
using System.Text;

namespace GrepoBot.Discord.Logic;

/// <summary>
/// Names of the ticket system: channel names and slugs.
/// </summary>
public static class TicketNaming
{
    #region Private Fields

    private const int MAX_SLUG_LENGTH = 40;
    private const int MAX_USER_PART = 20;

    #endregion

    #region Public Methods

    /// <summary>Channel name of an open ticket, e.g. <c>ticket-0007-jadam</c>.</summary>
    public static string OpenChannelName(int number, string userName)
    {
        return BuildName("ticket", number, userName);
    }

    /// <summary>Channel name of a closed ticket, e.g. <c>closed-0007-jadam</c>.</summary>
    public static string ClosedChannelName(int number, string userName)
    {
        return BuildName("closed", number, userName);
    }

    public static string FormatNumber(int number)
    {
        return number.ToString("0000", CultureInfo.InvariantCulture);
    }

    /// <summary>Lowercase a-z, 0-9 and dashes, accents removed. Safe for custom IDs and channel names.</summary>
    public static string Slug(string text, int maxLength = MAX_SLUG_LENGTH)
    {
        var normalized = (text ?? string.Empty).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        var lastWasDash = true;

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(c);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(lower);
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                builder.Append('-');
                lastWasDash = true;
            }

            if (builder.Length >= maxLength)
            {
                break;
            }
        }

        return builder.ToString().Trim('-');
    }

    #endregion

    #region Private Methods

    private static string BuildName(string prefix, int number, string userName)
    {
        var user = Slug(userName, MAX_USER_PART);
        var name = $"{prefix}-{FormatNumber(number)}";
        return user.Length == 0 ? name : $"{name}-{user}";
    }


    #endregion
}
