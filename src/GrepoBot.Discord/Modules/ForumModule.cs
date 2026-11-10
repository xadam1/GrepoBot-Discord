using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.WebSocket;
using GrepoBot.Discord.Services;
using GrepoBot.Discord.State;

namespace GrepoBot.Discord.Modules;

/// <summary>
/// <c>/forum …</c>: the feedback forum. Hidden for members without Manage Threads; the team check
/// (Manage Threads or ticket staff role) runs again in <see cref="ForumService"/>.
/// </summary>
[Group("forum", "Feedback forum: post status and setup")]
[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.ManageThreads)]
[RequireStaff]
public sealed class ForumModule : InteractionModuleBase<SocketInteractionContext>
{
    #region Private Fields

    private readonly ForumService _forum;

    #endregion

    #region Constructor

    public ForumModule(ForumService forum)
    {
        _forum = forum;
    }

    #endregion

    #region Public Methods

    [SlashCommand("status", "Set the status of this post (Done, Declined and Duplicate close it)")]
    public async Task StatusAsync(
        [Summary(description: "New status of the post")] ForumStatus status,
        [Summary(description: "Short note for the author (optional)"), MaxLength(500)] string? note = null,
        [Summary("duplicate-of", "Link to the original post (for Duplicate)")] string? duplicateOf = null)
    {
        await DeferAsync(ephemeral: true);
        if (Context.Channel is not IThreadChannel post)
        {
            await FollowupAsync("⚠️ Use this command inside a post of the feedback forum.", ephemeral: true);
            return;
        }

        try
        {
            var text = await _forum.SetStatusAsync((SocketGuildUser)Context.User, post, status, note, duplicateOf);
            await FollowupAsync(text, ephemeral: true);
        }
        catch (Exception ex) when (ex is ForumException or HttpException)
        {
            await FollowupAsync($"⚠️ {TicketButtonsModule.ErrorText(ex)}", ephemeral: true);
        }
    }

    [SlashCommand("reopen", "Reopen this post and remove its status")]
    public async Task ReopenAsync()
    {
        await DeferAsync(ephemeral: true);
        if (Context.Channel is not IThreadChannel post)
        {
            await FollowupAsync("⚠️ Use this command inside a post of the feedback forum.", ephemeral: true);
            return;
        }

        try
        {
            var text = await _forum.ReopenAsync((SocketGuildUser)Context.User, post);
            await FollowupAsync(text, ephemeral: true);
        }
        catch (Exception ex) when (ex is ForumException or HttpException)
        {
            await FollowupAsync($"⚠️ {TicketButtonsModule.ErrorText(ex)}", ephemeral: true);
        }
    }

    [SlashCommand("setup", "Set up the feedback forum: guidelines, tags, votes and slowmode")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task SetupAsync(
        [Summary(description: "The forum channel"), ChannelTypes(ChannelType.Forum)] IForumChannel channel,
        [Summary("delete-after-days", "Delete Declined and Duplicate posts after this many days, 0 = never (default 14)"), MinValue(0), MaxValue(365)] int deleteAfterDays = 14)
    {
        await DeferAsync(ephemeral: true);
        try
        {
            await _forum.SetupAsync(channel, deleteAfterDays);
            await FollowupAsync(
                $"✅ {channel.Mention} is set up: guidelines, tags (🆕 Feature · 🪲 Bug · 💡 Idea, and the team-only 📌 Planned · ✅ Done · 🚫 Declined · 🔁 Duplicate), " +
                $"✅/❌ votes on every new post, one new post per member every 2 minutes. " +
                (deleteAfterDays > 0 ? $"Declined and duplicate posts are deleted {deleteAfterDays} days after closing." : "Closed posts are kept."),
                ephemeral: true);
        }
        catch (HttpException ex)
        {
            await FollowupAsync($"❌ {TicketButtonsModule.ErrorText(ex)}", ephemeral: true);
        }
    }

    #endregion
}
