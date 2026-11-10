using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using GrepoBot.Discord.Services;

namespace GrepoBot.Discord.Modules;

/// <summary>
/// <c>/purge messages</c> and <c>/purge all</c>.
/// </summary>
[Group("purge", "Delete messages in this channel")]
[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.ManageMessages)]
[RequireUserPermission(GuildPermission.ManageMessages)]
[RequireStaff]
public sealed class PurgeModule : InteractionModuleBase<SocketInteractionContext>
{
    #region Private Fields

    private readonly PurgeService _purge;

    #endregion

    #region Constructor

    public PurgeModule(PurgeService purge)
    {
        _purge = purge;
    }

    #endregion

    #region Public Methods

    [SlashCommand("messages", "Delete the latest messages in this channel")]
    public async Task MessagesAsync(
        [Summary(description: "How many messages to delete"), MinValue(1), MaxValue(1000)] int count,
        [Summary(description: "Only delete messages of this member")] IUser? user = null)
    {
        if (Context.Channel is not ITextChannel channel)
        {
            await RespondAsync("❌ This only works in text channels.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);
        var deleted = await _purge.PurgeLatestAsync(channel, count, user?.Id);
        var whose = user is null ? string.Empty : $" from {user.Mention}";
        await FollowupAsync($"🧹 Deleted {deleted} message(s){whose}.", ephemeral: true);
    }

    [SlashCommand("all", "Delete EVERY message in this channel")]
    public async Task AllAsync()
    {
        if (Context.Channel is not ITextChannel channel)
        {
            await RespondAsync("❌ This only works in text channels.", ephemeral: true);
            return;
        }

        var buttons = new ComponentBuilder()
            .WithButton("Delete all messages", $"{PurgeConfirmModule.ALL_KEEP_ID}:{channel.Id}", ButtonStyle.Danger, new Emoji("🧹"));

        var text = $"⚠️ **Delete every message in {channel.Mention}?** This cannot be undone.\n\n" +
            "**Delete all messages** keeps the channel. Messages older than 14 days are deleted one by one, " +
            "which takes about a second each.";

        if (channel is SocketTextChannel and not SocketThreadChannel)
        {
            buttons.WithButton("Recreate channel", $"{PurgeConfirmModule.ALL_RECREATE_ID}:{channel.Id}", ButtonStyle.Danger, new Emoji("♻️"));
            text += "\n**Recreate channel** is instant: it creates an empty copy with the same name, topic, category and permissions " +
                "and deletes this one. Links to old messages, webhooks and integrations pointing at the channel stop working.";
        }

        buttons.WithButton("Cancel", PurgeConfirmModule.CANCEL_ID, ButtonStyle.Secondary);
        await RespondAsync(text, components: buttons.Build(), ephemeral: true);
    }

    #endregion
}

/// <summary>
/// Right click a message → Apps → "Purge from here", and the confirmation buttons of all purges.
/// Context menu commands cannot live in a command group, so they are in their own module.
/// </summary>
[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.ManageMessages)]
[RequireUserPermission(GuildPermission.ManageMessages)]
[RequireStaff]
public sealed class PurgeConfirmModule : InteractionModuleBase<SocketInteractionContext>
{
    #region Private Fields

    public const string ALL_KEEP_ID = "purge-all-keep";
    public const string ALL_RECREATE_ID = "purge-all-recreate";
    public const string FROM_ID = "purge-from";
    public const string CANCEL_ID = "purge-cancel";

    private readonly PurgeService _purge;

    #endregion

    #region Constructor

    public PurgeConfirmModule(PurgeService purge)
    {
        _purge = purge;
    }

    #endregion

    #region Public Methods

    [MessageCommand("Purge from here")]
    public async Task PurgeFromAsync(IMessage message)
    {
        var buttons = new ComponentBuilder()
            .WithButton("Delete", $"{FROM_ID}:{message.Channel.Id}:{message.Id}", ButtonStyle.Danger, new Emoji("🧹"))
            .WithButton("Cancel", CANCEL_ID, ButtonStyle.Secondary);

        await RespondAsync(
            $"⚠️ **Delete this message and every message after it?** {message.GetJumpUrl()}",
            components: buttons.Build(),
            ephemeral: true);
    }

    [ComponentInteraction(FROM_ID + ":*:*")]
    public async Task OnPurgeFromAsync(string channelId, string messageId)
    {
        if (Context.Guild.GetChannel(ulong.Parse(channelId)) is not ITextChannel channel)
        {
            await UpdateAsync("❌ The channel no longer exists.");
            return;
        }

        await UpdateAsync("⏳ Deleting…");
        var deleted = await _purge.PurgeFromAsync(channel, ulong.Parse(messageId));
        await TryModifyAsync($"🧹 Deleted {deleted} message(s).");
    }

    [ComponentInteraction(ALL_KEEP_ID + ":*")]
    public async Task OnPurgeAllAsync(string channelId)
    {
        if (Context.Guild.GetChannel(ulong.Parse(channelId)) is not ITextChannel channel)
        {
            await UpdateAsync("❌ The channel no longer exists.");
            return;
        }

        await UpdateAsync("⏳ Deleting every message… Old messages take about a second each, you can leave this running.");
        var deleted = await _purge.PurgeAllAsync(channel);
        await TryModifyAsync($"🧹 Deleted {deleted} message(s).");
    }

    [ComponentInteraction(ALL_RECREATE_ID + ":*")]
    public async Task OnRecreateAsync(string channelId)
    {
        if (Context.Guild.GetChannel(ulong.Parse(channelId)) is not SocketTextChannel channel)
        {
            await UpdateAsync("❌ The channel no longer exists.");
            return;
        }

        await UpdateAsync("⏳ Recreating the channel…");
        var copy = await _purge.RecreateAsync(channel);

        // The confirmation lived in the deleted channel, so tell the admin in the new one.
        var notice = await copy.SendMessageAsync($"🧹 This channel was cleared by {Context.User.Mention}.", allowedMentions: AllowedMentions.None);
        _ = Task.Delay(TimeSpan.FromSeconds(15)).ContinueWith(_ => notice.DeleteAsync());
    }

    [ComponentInteraction(CANCEL_ID)]
    public async Task OnCancelAsync()
    {
        await UpdateAsync("Cancelled, nothing was deleted.");
    }

    #endregion

    #region Private Methods

    private Task UpdateAsync(string text)
    {
        var component = (SocketMessageComponent)Context.Interaction;
        return component.UpdateAsync(p =>
        {
            p.Content = text;
            p.Components = new ComponentBuilder().Build();
        });
    }

    /// <summary>The interaction token expires after 15 minutes, a long purge may outlive it.</summary>
    private async Task TryModifyAsync(string text)
    {
        try
        {
            await ModifyOriginalResponseAsync(p => p.Content = text);
        }
        catch
        {
            await Context.User.SendMessageAsync(text);
        }
    }

    #endregion
}
