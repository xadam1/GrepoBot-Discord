namespace GrepoBot.Discord.Logic;

/// <summary>
/// The GrepoBot server layout <c>/server setup</c> applies: roles, categories, channels, the rules and the
/// templates forum. Existing roles and channels are found by name (the part after "・", without emoji) or by
/// an older name, so running it again or on a server with the old layout renames instead of duplicating.
/// </summary>
public static partial class ServerLayout
{
    #region Properties

    public const string LEFTOVER_CATEGORY = "╭──・🗑️Delete me🗑️・──╮";

    public const string TEAM_ROLE = "team";
    public const string MODERATOR_ROLE = "moderator";
    public const string SUPPORTER_ROLE = "supporter";

    /// <summary>Managed roles of the server, top to bottom. They sit right below the bot's own role.</summary>
    public static readonly IReadOnlyList<RoleSpec> Roles =
    [
        new(TEAM_ROLE, "GrepoBot Team", ["grepobot team", "grepobot developer"], 0xF1C40F, AddPermissions: []),
        new(MODERATOR_ROLE, "Moderator", ["moderator", "discord moderator"], 0x2ECC71, AddPermissions: ["ManageThreads", "ManageMessages", "ModerateMembers"]),
        new(SUPPORTER_ROLE, "Supporter", ["supporter", "patreon", "patron"], 0xF47FFF, AddPermissions: []),
    ];

    /// <summary>Old roles nobody needs any more, deleted by the setup (by their name, never managed roles).</summary>
    public static readonly IReadOnlyList<string> ObsoleteRoles = ["contributor", "webmaster", "bot"];

    /// <summary>Color of the bot's own role, the visor cyan of the icon.</summary>
    public const uint BOT_ROLE_COLOR = 0x3FD8F5;

    public static readonly IReadOnlyList<CategorySpec> Categories =
    [
        new("╭──・💡Important💡・──╮", ["important"], CategoryAccess.Keep,
        [
            new("welcome", "👋・welcome", ["welcome"], ChannelKind.Text, null, ReadOnly: false),
            new("rules", "📑・rules", ["rules"], ChannelKind.Text, "Please read the rules, they apply everywhere on this server.", ReadOnly: true),
            new("updates", "🔔・updates", ["updates"], ChannelKind.Announcement,
                "New GrepoBot releases and important news. The full changelog is on https://grepobot.net/changelog/", ReadOnly: true),
            new("status", "📡・bot-status", ["bot-status", "status"], ChannelKind.Text,
                "Is GrepoBot working right now? The team keeps the status message here up to date.", ReadOnly: true),
            new("links", "🔗・links", ["links", "website"], ChannelKind.Text,
                "Website, download, features and changelog of GrepoBot.", ReadOnly: true),
        ]),
        new("╭──・🔥General🔥・──╮", ["general"], CategoryAccess.Keep,
        [
            new("general", "💬・general", ["general"], ChannelKind.Text,
                "Chat about Grepolis and GrepoBot. Be nice and have fun!", ReadOnly: false),
            new("help", "🙋・help", ["help", "bot-farming"], ChannelKind.Text,
                "Quick questions about farming, building, culture, scripts or attacks, the community and the team help here. Account or private problems: open a ticket.", ReadOnly: false),
            new("feedback", "💡・features-and-bugs", ["features-and-bugs"], ChannelKind.Forum, null, ReadOnly: false),
            new("templates", "🏗️・templates", ["templates", "templates-and-ideas"], ChannelKind.Forum, null, ReadOnly: false),
            new("showcase", "📸・showcase", ["showcase"], ChannelKind.Text,
                "Show off your cities, empires, conquests and the stats GrepoBot got you.", ReadOnly: false),
            new("offtopic", "🎲・off-topic", ["off-topic", "offtopic"], ChannelKind.Text,
                "Anything that is not about GrepoBot. Keep it friendly.", ReadOnly: false),
        ]),
        new("╭──・💎Supporters💎・──╮", ["supporters"], CategoryAccess.Supporters,
        [
            new("lounge", "💎・supporter-lounge", ["supporter-lounge"], ChannelKind.Text,
                "Thank you for supporting GrepoBot! A cosy corner for supporters and the team.", ReadOnly: false),
            new("early", "🧪・early-access", ["early-access"], ChannelKind.Text,
                "Sneak peeks and features to try before everyone else.", ReadOnly: true),
        ]),
        new("╭──・🛟Support🛟・──╮", ["support"], CategoryAccess.Keep,
        [
            new("tickets", "🆘・support-ticket", ["support-ticket"], ChannelKind.Text, null, ReadOnly: false),
        ]),
    ];

    /// <summary>Categories the setup never touches, by a word in their name: staff channels and voice.</summary>
    public static readonly IReadOnlyList<string> UntouchedCategoryWords = ["private", "voice"];

    public const string RULES_TITLE = "📜 GrepoBot Community Rules";

    /// <summary>The rules. {help}, {feedback}, {tickets} and {templates} become channel mentions.</summary>
    public const string RULES_TEXT =
        "Welcome to the GrepoBot community! These rules keep it friendly and useful for everyone.\n\n" +
        "**1. Be respectful.** Treat everyone kindly. No insults, harassment, hate speech, discrimination or personal attacks.\n\n" +
        "**2. Keep it clean.** No NSFW, gore or shocking content, in messages, names, avatars or links.\n\n" +
        "**3. No spam or advertising.** No flooding, mass mentions, chain messages or self-promotion, and no unwanted DMs to members.\n\n" +
        "**4. Use the right channel.** Quick questions go to {help}, ideas and bugs to {feedback}, building templates to {templates}, " +
        "and personal problems (download, login, your account) to a ticket in {tickets}.\n\n" +
        "**5. Only official downloads.** Get GrepoBot only from https://grepobot.net. Never share cracked, modified or re-uploaded builds, " +
        "and never run files strangers send you.\n\n" +
        "**6. Protect your accounts.** The team will never ask for your Grepolis or Discord password. Never post passwords or login data, " +
        "not even in tickets or screenshots.\n\n" +
        "**7. No trading.** No buying, selling or sharing of Grepolis or Discord accounts, and no real-money trading.\n\n" +
        "**8. Keep Grepolis drama out.** Don't use this server to flame, threaten or recruit against other players or alliances.\n\n" +
        "**9. English in public channels**, so everyone can follow along and the team can help.\n\n" +
        "**10. Follow Discord's [Terms of Service](https://discord.com/terms) and [Community Guidelines](https://discord.com/guidelines).**\n\n" +
        "Moderators may warn, mute or remove anyone who breaks these rules. Something wrong? Open a ticket or ping a moderator. Thank you! 💙";

    public const string TEMPLATES_GUIDELINES =
        "**Share what works for you:** building templates, farming setups, culture plans and handy scripts.\n\n" +
        "**Title:** what it is and what it is for, e.g. *Offensive city: full LS build order* or *Farming setup for a new island*.\n" +
        "**Tag:** pick what it is about (Building, Farming, Culture, Scripts, Settings or Other).\n" +
        "**Content:** explain how to use it, paste the template or settings, and add screenshots if you can.\n\n" +
        "Like a template? React with 👍 and say thanks in the post. Questions about it go into the post itself.";

    /// <summary>The templates forum's tags, anyone can pick them.</summary>
    public static readonly IReadOnlyList<ForumTagDefinition> TemplateTags =
    [
        new("Building", "🏛️", Moderated: false),
        new("Farming", "🌾", Moderated: false),
        new("Culture", "🎭", Moderated: false),
        new("Scripts", "📜", Moderated: false),
        new("Settings", "⚙️", Moderated: false),
        new("Other", "🧩", Moderated: false),
    ];

    #endregion

    #region Public Methods

    /// <summary>"💬・general" → "general", "╭──・💡Important💡・──╮" → "important", "Discord Moderator" → "discord moderator".</summary>
    public static string Slug(string name)
    {
        var dot = name.IndexOf('・');
        var text = dot >= 0 ? name[(dot + 1)..] : name;
        return new string(text.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c is '-' or ' ').ToArray()).Trim();
    }

    public static bool Matches(string existingName, IEnumerable<string> aliases)
    {
        var slug = Slug(existingName);
        return aliases.Any(a => a == slug);
    }

    /// <summary>The channel spec with this key.</summary>
    public static ChannelSpec Channel(string key)
    {
        return Categories.SelectMany(c => c.Channels).First(c => c.Key == key);
    }

    /// <summary>
    /// Replaces <c>{#key}</c> placeholders with channel mentions, e.g. <c>{#tickets}</c> → <c>&lt;#123&gt;</c>.
    /// A key is a layout key (<c>tickets</c>, <c>status</c>, <c>rules</c>, …), one of its aliases or the channel's
    /// name without the emoji (<c>support-ticket</c>). Unknown placeholders are left untouched so a typo stays visible.
    /// </summary>
    public static string RenderChannelMentions(string text, IEnumerable<(ulong Id, string Name)> channels)
    {
        var channelList = channels.ToList();

        return ChannelPlaceholder().Replace(text, match =>
        {
            var id = FindChannelId(match.Groups[1].Value, channelList);
            return id is null ? match.Value : $"<#{id}>";
        });
    }

    /// <summary>
    /// The channel a placeholder key stands for: a layout key (<c>tickets</c>), one of its aliases or the channel's
    /// name without the emoji. Null when the server has no such channel.
    /// </summary>
    public static ulong? FindChannelId(string key, IEnumerable<(ulong Id, string Name)> channels)
    {
        key = key.Trim().ToLowerInvariant();
        var spec = Categories.SelectMany(c => c.Channels).FirstOrDefault(c => c.Key == key || c.Aliases.Contains(key));
        var aliases = spec is null ? [key] : spec.Aliases.Append(key).ToArray();

        foreach (var (id, name) in channels)
        {
            if (Matches(name, aliases))
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>The key of a <c>{#key}</c> placeholder that is the whole text, or null.</summary>
    public static string? ChannelPlaceholderKey(string text)
    {
        var match = ChannelPlaceholder().Match(text.Trim());
        return match.Success && match.Length == text.Trim().Length ? match.Groups[1].Value : null;
    }

    /// <summary>The rules with the channel mentions filled in.</summary>
    public static string RenderRules(IReadOnlyDictionary<string, ulong> channelIds)
    {
        return TextTemplate.Render(RULES_TEXT, channelIds.ToDictionary(p => p.Key, p => $"<#{p.Value}>"));
    }

    /// <summary>A status channel's name with the status emoji in front, e.g. "✅・bot-status".</summary>
    public static string StatusChannelName(string emoji)
    {
        return emoji + "・bot-status";
    }


    [System.Text.RegularExpressions.GeneratedRegex(@"\{#([a-z0-9_-]{1,40})\}", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex ChannelPlaceholder();

    #endregion
}

public enum ChannelKind
{
    Text,
    Announcement,
    Forum,
}

public enum CategoryAccess
{
    /// <summary>Keep the category's permissions as they are.</summary>
    Keep,

    /// <summary>Only supporters and the team see it.</summary>
    Supporters,
}

/// <summary>A role of the layout. <paramref name="AddPermissions"/> are added to what the role already has.</summary>
public sealed record RoleSpec(string Key, string Name, string[] Aliases, uint Color, string[] AddPermissions);

public sealed record ChannelSpec(string Key, string Name, string[] Aliases, ChannelKind Kind, string? Topic, bool ReadOnly);

public sealed record CategorySpec(string Name, string[] Aliases, CategoryAccess Access, IReadOnlyList<ChannelSpec> Channels);
