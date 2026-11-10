using Discord;
using Discord.Net;
using Discord.WebSocket;
using GrepoBot.Discord.Configuration;
using GrepoBot.Discord.Logic;
using GrepoBot.Discord.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GrepoBot.Discord.Services;

/// <summary>
/// The feedback forum (features, ideas, bugs): sets up its tags and guidelines, greets every new post
/// with ✅ / ❌ votes, lets the team set a status (Planned, Done, Declined, Duplicate) that closes the post
/// like a GitHub issue, and deletes declined and duplicate posts after a while.
/// </summary>
public sealed class ForumService
{
    #region Private Fields

    private static readonly TimeSpan _newPostAge = TimeSpan.FromMinutes(2);

    /// <summary>Discord sends the thread event again when the bot joins the post (by reacting), greet once.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, DateTimeOffset> _greeted = new();

    private readonly DiscordSocketClient _client;
    private readonly StateStore _store;
    private readonly TicketService _tickets;
    private readonly ulong _guildId;
    private readonly ILogger<ForumService> _logger;

    #endregion

    #region Constructor

    public ForumService(DiscordSocketClient client, StateStore store, TicketService tickets, IOptions<DiscordOptions> options, ILogger<ForumService> logger)
    {
        _client = client;
        _store = store;
        _tickets = tickets;
        _guildId = options.Value.GuildId;
        _logger = logger;
    }

    #endregion

    #region Public Methods

    /// <summary>Team = the owner, the Moderator / GrepoBot Team roles or the ticket staff roles.</summary>
    public bool IsStaff(SocketGuildUser user)
    {
        return _tickets.IsStaff(user);
    }

    /// <summary>
    /// Applies the forum layout: guidelines, tags (existing ones are kept by name, others removed),
    /// no default reaction (the bot adds ✅ / ❌), 2 minutes between new posts, posts hidden after a week.
    /// </summary>
    public async Task SetupAsync(IForumChannel forum, int deleteAfterDays)
    {
        var tags = FeedbackForum.MergeTags(forum.Tags.Select(t => (t.Id, t.Name)))
            .Select(t => new ForumTagBuilder(t.Tag.Name, t.ExistingId, t.Tag.Moderated, new Emoji(t.Tag.Emoji)).Build())
            .ToList();

        // The tags first: Discord only accepts "require a tag" once the forum has tags members can pick.
        var audit = new RequestOptions { AuditLogReason = "GrepoBot forum setup" };
        await forum.ModifyAsync(p => p.Tags = tags, audit);
        await forum.ModifyAsync(p =>
        {
            p.Topic = FeedbackForum.GUIDELINES;
            p.DefaultReactionEmoji = null;
            p.ThreadCreationInterval = FeedbackForum.POST_SLOWMODE_SECONDS;
            p.AutoArchiveDuration = ThreadArchiveDuration.OneWeek;
            p.DefaultSortOrder = ForumSortOrder.LatestActivity;
            p.Flags = ChannelFlags.RequireTag;
        }, audit);

        _store.Update(state =>
        {
            state.Forum.ChannelId = forum.Id;
            state.Forum.DeleteClosedAfterDays = deleteAfterDays;
        });

        _logger.LogInformation("Set up the feedback forum #{Forum}.", forum.Name);
    }

    /// <summary>A new post: add the ✅ / ❌ votes and a short thank-you with the next steps.</summary>
    public async Task OnThreadCreatedAsync(SocketThreadChannel thread)
    {
        var forumId = _store.Read(state => state.Forum.ChannelId);
        if (thread.Guild.Id != _guildId
            || forumId is null
            || thread.ParentChannel?.Id != forumId
            || DateTimeOffset.UtcNow - thread.CreatedAt > _newPostAge
            || !_greeted.TryAdd(thread.Id, DateTimeOffset.UtcNow))
        {
            return;
        }

        foreach (var old in _greeted.Where(g => DateTimeOffset.UtcNow - g.Value > _newPostAge).Select(g => g.Key).ToList())
        {
            _greeted.TryRemove(old, out _);
        }

        var starter = await GetStarterMessageAsync(thread);
        if (starter is null || starter.Author.IsBot)
        {
            return;
        }

        await starter.AddReactionAsync(new Emoji(FeedbackForum.VOTE_YES));
        await starter.AddReactionAsync(new Emoji(FeedbackForum.VOTE_NO));

        var text =
            $"**Thanks for the post, {starter.Author.Mention}!** 🙌\n" +
            $"Everyone can vote on it: {FeedbackForum.VOTE_YES} if you want it too, {FeedbackForum.VOTE_NO} if not. Comments are welcome right here.\n" +
            "The team marks the status with a tag: 📌 Planned · ✅ Done · 🚫 Declined · 🔁 Duplicate.";

        if (IsBugPost(thread) && starter.Attachments.Count == 0)
        {
            text += "\n\n🪲 **Reporting a bug?** Please add what you did, what you expected and what happened instead. " +
                "A screenshot helps a lot: `Win` + `Shift` + `S` on Windows, `Cmd` + `Ctrl` + `Shift` + `4` on macOS, then paste it here.";
        }

        var embed = new EmbedBuilder()
            .WithColor(BotBranding.Accent)
            .WithDescription(text)
            .Build();

        await thread.SendMessageAsync(embed: embed, allowedMentions: AllowedMentions.None);
        _logger.LogInformation("Greeted the new forum post '{Post}' by {User}.", thread.Name, starter.Author.Username);
    }

    /// <summary>
    /// Sets the status tag of a post and tells its author. Done, Declined and Duplicate lock and archive the post;
    /// Declined and Duplicate are also deleted later. Returns the text for the staff member.
    /// </summary>
    public async Task<string> SetStatusAsync(SocketGuildUser staff, IThreadChannel post, ForumStatus status, string? note, string? duplicateOf)
    {
        var forum = RequireForumPost(staff, post);
        var statusTags = FindStatusTags(forum);
        if (!statusTags.ContainsKey(status))
        {
            throw new ForumException($"The forum has no **{FeedbackForum.StatusTags[status].Name}** tag. Run `/forum setup` first.");
        }

        var duplicateId = FeedbackForum.ParsePostId(duplicateOf);
        if (status == ForumStatus.Duplicate && duplicateId is null)
        {
            throw new ForumException("Please give the link of the original post in `duplicate-of`.");
        }

        var deleteAfterDays = _store.Read(state => state.Forum.DeleteClosedAfterDays);
        var deleteAt = FeedbackForum.IsDeletedLater(status) && deleteAfterDays > 0
            ? DateTimeOffset.UtcNow.AddDays(deleteAfterDays)
            : (DateTimeOffset?)null;

        var tag = FeedbackForum.StatusTags[status];
        var lines = new List<string>
        {
            status switch
            {
                ForumStatus.Planned => $"{tag.Emoji} **Planned** – the team plans to work on this.",
                ForumStatus.Done => $"{tag.Emoji} **Done** – this is now in GrepoBot. Thanks for the post!",
                ForumStatus.Declined => $"{tag.Emoji} **Declined** – the team decided not to do this.",
                _ => $"{tag.Emoji} **Duplicate** – this is already discussed in <#{duplicateId}>, please vote and continue there.",
            },
        };

        if (!string.IsNullOrWhiteSpace(note))
        {
            lines.Add($"> {note.Trim().Replace("\n", "\n> ")}");
        }

        if (FeedbackForum.Closes(status))
        {
            lines.Add(deleteAt is { } at
                ? $"-# This post is now closed and will be deleted <t:{at.ToUnixTimeSeconds()}:R>."
                : "-# This post is now closed.");
        }

        var embed = new EmbedBuilder()
            .WithColor(status switch
            {
                ForumStatus.Planned => BotBranding.Accent,
                ForumStatus.Done => BotBranding.Success,
                ForumStatus.Declined => BotBranding.Danger,
                _ => BotBranding.Warning,
            })
            .WithDescription(string.Join("\n", lines))
            .WithFooter($"Status set by {staff.DisplayName}")
            .Build();

        // Archived posts must be unarchived before anything can be posted or changed.
        if (post.IsArchived)
        {
            await post.ModifyAsync(p => p.Archived = false);
        }

        var ownerId = post is SocketThreadChannel { Owner: { } owner } ? owner.Id : (ulong?)null;
        await post.SendMessageAsync(
            ownerId is null ? null : $"<@{ownerId}>",
            embed: embed,
            allowedMentions: ownerId is null ? AllowedMentions.None : new AllowedMentions { UserIds = [ownerId.Value] });

        var tags = FeedbackForum.ApplyStatus(post.AppliedTags, statusTags, status);
        var closes = FeedbackForum.Closes(status);
        await post.ModifyAsync(p =>
        {
            p.AppliedTags = tags;
            if (closes)
            {
                p.Locked = true;
                p.Archived = true;
            }
        }, new RequestOptions { AuditLogReason = $"Forum status {status} by {staff.Username}" });

        _store.Update(state =>
        {
            state.Forum.ClosedPosts.RemoveAll(p => p.ThreadId == post.Id);
            if (deleteAt is { } at)
            {
                state.Forum.ClosedPosts.Add(new ClosedForumPost { ThreadId = post.Id, Status = status, DeleteAt = at });
            }
        });

        _logger.LogInformation("{User} set '{Post}' to {Status}.", staff.Username, post.Name, status);
        return closes
            ? $"✅ Marked as **{tag.Name}**, the post is locked{(deleteAt is null ? "." : " and will be deleted in " + deleteAfterDays + " days.")}"
            : $"✅ Marked as **{tag.Name}**.";
    }

    /// <summary>Removes the status tag, unlocks the post and cancels its deletion.</summary>
    public async Task<string> ReopenAsync(SocketGuildUser staff, IThreadChannel post)
    {
        var forum = RequireForumPost(staff, post);
        var tags = FeedbackForum.ApplyStatus(post.AppliedTags, FindStatusTags(forum), status: null);

        await post.ModifyAsync(p =>
        {
            p.Archived = false;
            p.Locked = false;
            p.AppliedTags = tags;
        }, new RequestOptions { AuditLogReason = $"Forum post reopened by {staff.Username}" });

        _store.Update(state => state.Forum.ClosedPosts.RemoveAll(p => p.ThreadId == post.Id));

        await post.SendMessageAsync(
            embed: new EmbedBuilder()
                .WithColor(BotBranding.Accent)
                .WithDescription($"🔓 Reopened by {staff.Mention}, the discussion continues.")
                .Build(),
            allowedMentions: AllowedMentions.None);

        return "✅ Reopened, the status tag is removed.";
    }

    /// <summary>Deletes declined and duplicate posts whose time is up. Called by <see cref="ForumCleanupService"/>.</summary>
    public async Task CleanupAsync(CancellationToken cancellationToken)
    {
        var due = _store.Read(state => state.Forum.ClosedPosts
            .Where(p => p.DeleteAt <= DateTimeOffset.UtcNow)
            .Select(p => p.ThreadId)
            .ToList());

        foreach (var threadId in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await _client.Rest.GetChannelAsync(threadId) is IThreadChannel thread)
                {
                    await thread.DeleteAsync(new RequestOptions { AuditLogReason = "Closed forum post, deletion time reached" });
                    _logger.LogInformation("Deleted the closed forum post '{Post}'.", thread.Name);
                }
            }
            catch (HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.NotFound)
            {
                // Already deleted by hand.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete forum post {Post}, trying again later.", threadId);
                continue;
            }

            _store.Update(state => state.Forum.ClosedPosts.RemoveAll(p => p.ThreadId == threadId));
        }
    }

    #endregion

    #region Private Methods

    private IForumChannel RequireForumPost(SocketGuildUser staff, IThreadChannel post)
    {
        if (!IsStaff(staff))
        {
            throw new ForumException("Only the team can change the status of posts.");
        }

        var forumId = _store.Read(state => state.Forum.ChannelId);
        var parentId = (post as SocketThreadChannel)?.ParentChannel?.Id ?? post.CategoryId;
        if (forumId is null || parentId != forumId || staff.Guild.GetChannel(forumId.Value) is not IForumChannel forum)
        {
            throw new ForumException("Use this command inside a post of the feedback forum.");
        }

        return forum;
    }

    private static Dictionary<ForumStatus, ulong> FindStatusTags(IForumChannel forum)
    {
        var result = new Dictionary<ForumStatus, ulong>();
        foreach (var (status, definition) in FeedbackForum.StatusTags)
        {
            var tag = forum.Tags.FirstOrDefault(t => string.Equals(t.Name, definition.Name, StringComparison.OrdinalIgnoreCase));
            if (tag.Id != 0)
            {
                result[status] = tag.Id;
            }
        }

        return result;
    }

    private static bool IsBugPost(SocketThreadChannel thread)
    {
        if (thread.ParentChannel is not IForumChannel forum)
        {
            return false;
        }

        var bugTag = forum.Tags.FirstOrDefault(t => string.Equals(t.Name, "Bug", StringComparison.OrdinalIgnoreCase));
        return bugTag.Id != 0 && thread.AppliedTags.Contains(bugTag.Id);
    }

    /// <summary>The starter message of a forum post has the post's ID, it can arrive a moment after the thread.</summary>
    private static async Task<IUserMessage?> GetStarterMessageAsync(SocketThreadChannel thread)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (await thread.GetMessageAsync(thread.Id) is IUserMessage message)
            {
                return message;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        return null;
    }

    #endregion
}

/// <summary>A forum action that is not possible right now. The message is shown to the staff member as is.</summary>
public sealed class ForumException(string message) : Exception(message);
