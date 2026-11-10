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

    public TicketSettings Tickets { get; set; } = new();

    public ForumSettings Forum { get; set; } = new();

    public AppStatusSettings Status { get; set; } = new();

    /// <summary>The server's permanent invite the bot created (shown in the links card and on grepobot.net).</summary>
    public string? InviteUrl { get; set; }

    /// <summary>Channel for the moderation log (deleted/edited messages, joins, leaves, bans). Null disables it.</summary>
    public ulong? LogChannelId { get; set; }

    /// <summary>Role pinged by "Post & ping" when reposting an update.</summary>
    public ulong? UpdatesRoleId { get; set; }

    /// <summary>Color last picked in "Post as GrepoBot", preselected next time.</summary>
    public string PostColor { get; set; } = GrepoBot.Discord.Logic.RepostLayout.DefaultColor;

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
        "Please have a look at {rules}. Questions about GrepoBot? Ask in the chat, " +
        "and if something does not work for you, open a ticket. Have fun! 🎉";

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
        "**4. No piracy or account sharing.** Do not share cracked copies of GrepoBot or other people's accounts.\n" +
        "**5. Support goes through tickets.** If GrepoBot does not work for you, open a ticket.\n" +
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

public sealed class TicketSettings
{
    #region Properties

    public const string DEFAULT_MESSAGE =
        "Thanks for reaching out, {user}! 👋 A team member will be with you soon.\n\n" +
        "📸 **Screenshots are very welcome**, they help us understand the problem much faster. " +
        "Paste them right here:\n" +
        "> **Windows:** `Win` + `Shift` + `S`, select the area, then `Ctrl` + `V` here\n" +
        "> **macOS:** `Cmd` + `Ctrl` + `Shift` + `4`, select the area, then `Cmd` + `V` here\n" +
        "You can also drag and drop images or short videos into this channel.";

    /// <summary>Category the ticket channels are created in. Null means tickets are not set up.</summary>
    public ulong? CategoryId { get; set; }

    /// <summary>Roles that see every ticket and can claim, close and delete them.</summary>
    public List<ulong> StaffRoleIds { get; set; } = [];

    /// <summary>Channel where opened, closed and deleted tickets are logged. Null disables it.</summary>
    public ulong? LogChannelId { get; set; }

    /// <summary>Mention the staff roles when a ticket is opened.</summary>
    public bool PingStaff { get; set; } = true;

    /// <summary>How many tickets one member may have open at the same time.</summary>
    public int MaxOpenPerUser { get; set; } = 1;

    /// <summary>Text posted in a new ticket. Placeholders: {user} {topic} {server}.</summary>
    public string Message { get; set; } = DEFAULT_MESSAGE;

    /// <summary>Texts of the ticket panel, prefilled in <c>/tickets panel</c>.</summary>
    public string PanelTitle { get; set; } = "🎫 Support";

    public string PanelDescription { get; set; } = GrepoBot.Discord.Modules.TicketPanelModal.DEFAULT_TEXT;

    /// <summary>Channel of the ticket panel, linked in the bot's DM auto-reply.</summary>
    public ulong? PanelChannelId { get; set; }

    /// <summary>Number of the last opened ticket.</summary>
    public int Counter { get; set; }

    /// <summary>Ticket channels that still exist (open or closed). Removed when the channel is deleted.</summary>
    public List<TicketRecord> Tickets { get; set; } = [];

    #endregion
}

public sealed class TicketRecord
{
    #region Properties

    public ulong ChannelId { get; set; }

    public int Number { get; set; }

    public ulong OwnerId { get; set; }

    public string OwnerName { get; set; } = string.Empty;

    public string Topic { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public DateTimeOffset OpenedAt { get; set; }

    /// <summary>The ticket's first message with the Close and Claim buttons.</summary>
    public ulong? ControlMessageId { get; set; }

    public ulong? ClaimedById { get; set; }

    /// <summary>Members added with <c>/ticket add</c>, they lose access on close and get it back on reopen.</summary>
    public List<ulong> AddedUserIds { get; set; } = [];

    public bool IsClosed { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public ulong? ClosedById { get; set; }

    public string? CloseReason { get; set; }

    #endregion

    #region Public Methods

    public TicketRecord Clone()
    {
        var copy = (TicketRecord)MemberwiseClone();
        copy.AddedUserIds = AddedUserIds.ToList();
        return copy;
    }

    #endregion
}

public sealed class ForumSettings
{
    #region Properties

    /// <summary>The feedback forum (features, ideas, bugs). Null until <c>/forum setup</c>.</summary>
    public ulong? ChannelId { get; set; }

    /// <summary>Posts closed as Declined or Duplicate are deleted this many days after closing. 0 keeps them.</summary>
    public int DeleteClosedAfterDays { get; set; } = 14;

    /// <summary>Posts waiting for deletion.</summary>
    public List<ClosedForumPost> ClosedPosts { get; set; } = [];

    #endregion
}

public sealed class ClosedForumPost
{
    #region Properties

    public ulong ThreadId { get; set; }

    public ForumStatus Status { get; set; }

    public DateTimeOffset DeleteAt { get; set; }

    #endregion
}

/// <summary>Status tags of the feedback forum, only the team can set them.</summary>
public enum ForumStatus
{
    Planned,
    Done,
    Declined,
    Duplicate,
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

    /// <summary>Optional headline of the answer card.</summary>
    public string? Title { get; set; }

    public string Response { get; set; } = string.Empty;

    /// <summary>Link buttons under the answer (web addresses or <c>{#channel}</c> placeholders).</summary>
    public List<GrepoBot.Discord.Logic.AutoReplyButtonSpec> Buttons { get; set; } = [];

    /// <summary>Optional image shown in the answer card (https address of a png, jpg, gif or webp).</summary>
    public string? ImageUrl { get; set; }

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

    /// <summary>The copy starts with @everyone.</summary>
    public bool PingEveryone { get; set; }

    /// <summary>Color of the embed line, null for a plain message.</summary>
    public uint? Color { get; set; }

    /// <summary>Posted with "Post as release": a release card instead of a copy.</summary>
    public bool IsRelease { get; set; }

    public DateTimeOffset PostedAt { get; set; }

    #endregion
}

public sealed class AppStatusSettings
{
    #region Properties

    public ulong? ChannelId { get; set; }

    public ulong? MessageId { get; set; }

    public GrepoBot.Discord.Logic.AppState State { get; set; } = GrepoBot.Discord.Logic.AppState.Working;

    /// <summary>Extra text from the team, null shows the state's default text.</summary>
    public string? Note { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    #endregion
}
