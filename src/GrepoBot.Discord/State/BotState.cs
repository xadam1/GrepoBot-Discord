namespace GrepoBot.Discord.State;

/// <summary>
/// Everything the admins configure through commands. Saved as <c>state.json</c> in the data folder.
/// </summary>
public sealed class BotState
{
    #region Properties

    public WelcomeSettings Welcome { get; set; } = new();

    public RulesSettings Rules { get; set; } = new();

    public AutoReplySettings AutoReply { get; set; } = new();

    /// <summary>Channel for the moderation log (deleted/edited messages, joins, leaves, bans). Null disables it.</summary>
    public ulong? LogChannelId { get; set; }

    /// <summary>Role pinged by "Post & ping" when reposting an update.</summary>
    public ulong? UpdatesRoleId { get; set; }

    /// <summary>Publish reposts in announcement channels to following servers automatically.</summary>
    public bool AutoPublish { get; set; } = true;

    /// <summary>Recent reposts, so "Update GrepoBot post" can edit the copies of a draft.</summary>
    public List<PostRecord> Posts { get; set; } = [];

    #endregion
}

public sealed class WelcomeSettings
{
    #region Properties

    public const string DEFAULT_MESSAGE =
        "Hey {user}, welcome to **{server}**! 👋\n\n" +
        "Please read and accept the rules in {rules} to unlock the rest of the server.\n" +
        "You are our member **#{membercount}**.";

    public ulong? ChannelId { get; set; }

    public string Message { get; set; } = DEFAULT_MESSAGE;

    #endregion
}

public sealed class RulesSettings
{
    #region Properties

    public const string DEFAULT_TITLE = "📜 Server Rules";

    public const string DEFAULT_TEXT =
        "**1. Be respectful.** No insults, harassment, hate speech or discrimination.\n" +
        "**2. No spam.** No flooding, mass mentions or unsolicited advertising (including DMs).\n" +
        "**3. Keep it on topic.** Use the right channel for your question.\n" +
        "**4. No piracy or account sharing.** Do not share cracked versions of GrepoBot or other people's accounts.\n" +
        "**5. Support goes through tickets.** For problems with your licence or payments open a ticket.\n" +
        "**6. Follow Discord's Terms of Service** and Community Guidelines.\n\n" +
        "Moderators may remove content or members that break these rules.\n\n" +
        "Click the button below to confirm that you have read and accept the rules.";

    public ulong? ChannelId { get; set; }

    public ulong? MessageId { get; set; }

    /// <summary>Role given after accepting the rules.</summary>
    public ulong? MemberRoleId { get; set; }

    public string Title { get; set; } = DEFAULT_TITLE;

    public string Text { get; set; } = DEFAULT_TEXT;

    #endregion
}

public sealed class AutoReplySettings
{
    #region Properties

    public const string DEFAULT_FOOTER = "Automatic answer · Still stuck? Open a ticket.";

    public List<AutoReplyRule> Rules { get; set; } = [];

    /// <summary>Channels or categories where the bot never auto-replies (e.g. ticket categories, staff channels).</summary>
    public List<ulong> IgnoredChannelIds { get; set; } = [];

    /// <summary>Do not auto-reply to members who can manage messages.</summary>
    public bool IgnoreStaff { get; set; } = true;

    public string Footer { get; set; } = DEFAULT_FOOTER;

    #endregion
}

public enum MatchMode
{
    /// <summary>At least one keyword appears.</summary>
    Any,

    /// <summary>Every keyword appears.</summary>
    All,

    /// <summary>Each line is a regular expression, at least one matches.</summary>
    Regex,
}

public sealed class AutoReplyRule
{
    #region Properties

    public string Name { get; set; } = string.Empty;

    public List<string> Keywords { get; set; } = [];

    public MatchMode Mode { get; set; } = MatchMode.Any;

    public string Response { get; set; } = string.Empty;

    /// <summary>Minimum time between two answers of this rule in the same channel.</summary>
    public int CooldownSeconds { get; set; } = 300;

    public bool Enabled { get; set; } = true;

    public int TimesUsed { get; set; }

    #endregion
}

public sealed class PostRecord
{
    #region Properties

    public ulong SourceChannelId { get; set; }

    public ulong SourceMessageId { get; set; }

    public ulong TargetChannelId { get; set; }

    /// <summary>All messages of the copy (long drafts are split), the last one carries the attachments.</summary>
    public List<ulong> MessageIds { get; set; } = [];

    public ulong? PingRoleId { get; set; }

    public DateTimeOffset PostedAt { get; set; }

    #endregion
}
