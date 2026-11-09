using GrepoBot.Discord.State;

namespace GrepoBot.Discord.Logic;

/// <summary>
/// The look of the feedback forum: post guidelines, the tags members pick (Feature, Bug, Idea) and the
/// status tags only the team sets (Planned, Done, Declined, Duplicate).
/// </summary>
public static class FeedbackForum
{
    #region Properties

    public const string VOTE_YES = "✅";
    public const string VOTE_NO = "❌";

    /// <summary>Discord allows at most 5 tags on a post.</summary>
    public const int MAX_TAGS_PER_POST = 5;

    /// <summary>Seconds a member waits between two new posts.</summary>
    public const int POST_SLOWMODE_SECONDS = 120;

    public const string GUIDELINES =
        "**One post = one feature, idea or bug.** Search first. If it already exists, vote on it and add your comments there instead of opening a new post.\n\n" +
        "**Title:** briefly say what it is about, e.g. *Farming: skip villages with low loyalty* or *Login window stays empty*.\n" +
        "**Tag:** pick 🆕 Feature, 🪲 Bug or 💡 Idea.\n" +
        "**Description:**\n" +
        "- Feature / Idea: what should GrepoBot do, and how would it help you?\n" +
        "- Bug: what you did, what you expected and what happened instead.\n" +
        "**Screenshots:** add screenshots or a short video whenever you can, they help a lot.\n\n" +
        "**Vote** with ✅ (I want this too) or ❌ (not needed). The team looks at the most wanted posts first and marks the status with a tag: 📌 Planned, ✅ Done, 🚫 Declined or 🔁 Duplicate.\n" +
        "Need personal 1-on-1 help with download, login or your account? Please open a ticket in the support channel instead.";

    /// <summary>Tags members choose when they create a post.</summary>
    public static readonly IReadOnlyList<ForumTagDefinition> TypeTags =
    [
        new("Feature", "🆕", Moderated: false),
        new("Bug", "🪲", Moderated: false),
        new("Idea", "💡", Moderated: false),
    ];

    /// <summary>Status tags, set by the team through <c>/forum status</c> (or by hand with Manage Threads).</summary>
    public static readonly IReadOnlyDictionary<ForumStatus, ForumTagDefinition> StatusTags = new Dictionary<ForumStatus, ForumTagDefinition>
    {
        [ForumStatus.Planned] = new("Planned", "📌", Moderated: true),
        [ForumStatus.Done] = new("Done", "✅", Moderated: true),
        [ForumStatus.Declined] = new("Declined", "🚫", Moderated: true),
        [ForumStatus.Duplicate] = new("Duplicate", "🔁", Moderated: true),
    };

    #endregion

    #region Public Methods

    /// <summary>All tags of the forum in display order.</summary>
    public static IReadOnlyList<ForumTagDefinition> AllTags()
    {
        return TypeTags.Concat(StatusTags.Values).ToList();
    }

    /// <summary>
    /// Matches the wanted tags to the forum's existing ones by name, so posts keep their tags.
    /// Existing tags that are not wanted (e.g. an old "In progress") are left out and get removed.
    /// </summary>
    public static List<(ForumTagDefinition Tag, ulong? ExistingId)> MergeTags(IEnumerable<(ulong Id, string Name)> existing)
    {
        var byName = existing
            .GroupBy(t => Normalize(t.Name))
            .ToDictionary(g => g.Key, g => g.First().Id);

        return AllTags()
            .Select(tag => (tag, byName.TryGetValue(Normalize(tag.Name), out var id) ? (ulong?)id : null))
            .ToList();
    }

    /// <summary>
    /// The tags of a post after setting <paramref name="status"/>: other status tags are removed,
    /// the member's own tags stay. Null status only removes the status tags (reopen).
    /// </summary>
    public static List<ulong> ApplyStatus(IEnumerable<ulong> currentTagIds, IReadOnlyDictionary<ForumStatus, ulong> statusTagIds, ForumStatus? status)
    {
        var statusIds = statusTagIds.Values.ToHashSet();
        var result = currentTagIds.Where(id => !statusIds.Contains(id)).Distinct().ToList();

        if (status is { } wanted && statusTagIds.TryGetValue(wanted, out var tagId))
        {
            // The status must fit, drop the member's last tag when the post is full.
            while (result.Count >= MAX_TAGS_PER_POST)
            {
                result.RemoveAt(result.Count - 1);
            }

            result.Add(tagId);
        }

        return result;
    }

    /// <summary>Done, Declined and Duplicate close the post (locked and archived), Planned keeps it open.</summary>
    public static bool Closes(ForumStatus status)
    {
        return status != ForumStatus.Planned;
    }

    /// <summary>Declined and Duplicate posts are deleted after a while, done ones stay as a record.</summary>
    public static bool IsDeletedLater(ForumStatus status)
    {
        return status is ForumStatus.Declined or ForumStatus.Duplicate;
    }

    /// <summary>Reads the post ID from a post link (…/channels/guild/post) or a bare ID.</summary>
    public static ulong? ParsePostId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // https://discord.com/channels/<guild>/<post>[/<message>], or <#post>, or the bare ID.
        var parts = text.Trim().Trim('<', '>', '#').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var channels = Array.IndexOf(parts, "channels");
        var candidate = channels >= 0 && parts.Length > channels + 2 ? parts[channels + 2] : parts.LastOrDefault();
        return ulong.TryParse(candidate, out var id) ? id : null;
    }

    #endregion

    #region Helpers

    private static string Normalize(string name)
    {
        return name.Trim().ToLowerInvariant();
    }

    #endregion
}

/// <summary>A forum tag the bot creates. Moderated tags can only be set by members with Manage Threads.</summary>
public sealed record ForumTagDefinition(string Name, string Emoji, bool Moderated);
