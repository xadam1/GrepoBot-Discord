namespace GrepoBot.Discord.Logic;

/// <summary>
/// Fills <c>{placeholder}</c>s in admin-written texts (welcome message, auto-replies).
/// Unknown placeholders are left untouched so a typo stays visible.
/// </summary>
public static class TextTemplate
{
    #region Public Methods

    public static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        var result = template;
        foreach (var (key, value) in values)
        {
            result = result.Replace("{" + key + "}", value, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    #endregion
}
