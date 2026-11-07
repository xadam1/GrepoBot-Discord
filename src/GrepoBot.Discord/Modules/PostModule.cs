using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.WebSocket;
using GrepoBot.Discord.Services;
using GrepoBot.Discord.State;

namespace GrepoBot.Discord.Modules;

/// <summary>
/// Right click a message → Apps → "Post as GrepoBot": pick a channel and the bot posts a copy of the
/// message (text, attachments, emojis, server stickers). "Update GrepoBot post" re-applies later
/// edits of the draft to the posted copies.
/// </summary>
[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
[RequireUserPermission(GuildPermission.ManageGuild)]
public sealed class PostModule : InteractionModuleBase<SocketInteractionContext>
{
    #region Private Fields

    private const string TARGET_ID = "post-target";
    private const string SEND_ID = "post-send";
    private const string CANCEL_ID = "post-cancel";

    private readonly RepostService _repost;
    private readonly StateStore _store;

    #endregion

    #region Constructor

    public PostModule(RepostService repost, StateStore store)
    {
        _repost = repost;
        _store = store;
    }

    #endregion

    #region Public Methods

    [MessageCommand("Post as GrepoBot")]
    public async Task PostAsync(IMessage message)
    {
        var problem = RepostService.GetRepostProblem(message);
        if (problem is not null)
        {
            await RespondAsync($"❌ {problem}", ephemeral: true);
            return;
        }

        await RespondAsync(
            "📮 **Where should I post this message?**\n" + Describe(message),
            components: BuildPicker(message.Channel.Id, message.Id, null).Build(),
            ephemeral: true);
    }

    [MessageCommand("Update GrepoBot post")]
    public async Task UpdateAsync(IMessage message)
    {
        if (message is not IUserMessage draft || !_repost.HasCopies(message.Id))
        {
            await RespondAsync("❌ Nothing was posted from this message yet. Use **Apps → Post as GrepoBot** first.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);
        try
        {
            var results = await _repost.UpdateCopiesAsync(draft, Context.Guild);
            await FollowupAsync(string.Join("\n", results), ephemeral: true);
        }
        catch (Exception ex) when (ex is RepostException or HttpException)
        {
            await FollowupAsync($"❌ {ErrorText(ex)}", ephemeral: true);
        }
    }

    [ComponentInteraction(TARGET_ID + ":*:*")]
    public async Task OnTargetSelectedAsync(string sourceChannelId, string sourceMessageId, string[] selected)
    {
        var component = (SocketMessageComponent)Context.Interaction;
        var target = Context.Guild.GetTextChannel(ulong.Parse(selected[0]));
        if (target is null)
        {
            await component.UpdateAsync(p => p.Content = "❌ That channel no longer exists.");
            return;
        }

        var sourceChannel = ulong.Parse(sourceChannelId);
        var sourceMessage = ulong.Parse(sourceMessageId);
        var missing = GetMissingPermissions(target);
        var updatesRole = GetUpdatesRole();

        var content = $"📮 Post the message to {target.Mention}?";
        if (target is INewsChannel && _store.Read(s => s.AutoPublish))
        {
            content += "\n📣 It is an announcement channel, the post will be published to following servers.";
        }

        if (missing.Count > 0)
        {
            content += $"\n⚠️ I am missing these permissions in {target.Mention}: **{string.Join(", ", missing)}**.";
        }

        var buttons = BuildPicker(sourceChannel, sourceMessage, target.Id)
            .WithButton("Post", $"{SEND_ID}:{sourceChannel}:{sourceMessage}:{target.Id}:0", ButtonStyle.Primary, new Emoji("📮"), row: 1);

        if (updatesRole is not null)
        {
            buttons.WithButton($"Post & ping @{updatesRole.Name}", $"{SEND_ID}:{sourceChannel}:{sourceMessage}:{target.Id}:1", ButtonStyle.Success, new Emoji("🔔"), row: 1);
        }

        buttons.WithButton("Cancel", CANCEL_ID, ButtonStyle.Secondary, row: 1);

        await component.UpdateAsync(p =>
        {
            p.Content = content;
            p.Components = buttons.Build();
        });
    }

    [ComponentInteraction(SEND_ID + ":*:*:*:*")]
    public async Task OnSendAsync(string sourceChannelId, string sourceMessageId, string targetChannelId, string ping)
    {
        var component = (SocketMessageComponent)Context.Interaction;
        await component.UpdateAsync(p =>
        {
            p.Content = "⏳ Posting…";
            p.Components = new ComponentBuilder().Build();
        });

        var result = await SendAsync(ulong.Parse(sourceChannelId), ulong.Parse(sourceMessageId), ulong.Parse(targetChannelId), ping == "1");
        await ModifyOriginalResponseAsync(p => p.Content = result);
    }

    [ComponentInteraction(CANCEL_ID)]
    public async Task OnCancelAsync()
    {
        var component = (SocketMessageComponent)Context.Interaction;
        await component.UpdateAsync(p =>
        {
            p.Content = "Cancelled, nothing was posted.";
            p.Components = new ComponentBuilder().Build();
        });
    }

    #endregion

    #region Private Methods

    private async Task<string> SendAsync(ulong sourceChannelId, ulong sourceMessageId, ulong targetChannelId, bool ping)
    {
        if (Context.Client.GetChannel(sourceChannelId) is not IMessageChannel sourceChannel
            || await sourceChannel.GetMessageAsync(sourceMessageId) is not IUserMessage source)
        {
            return "❌ The original message was deleted.";
        }

        if (Context.Guild.GetTextChannel(targetChannelId) is not { } target)
        {
            return "❌ The target channel no longer exists.";
        }

        var pingRole = ping ? GetUpdatesRole() : null;
        try
        {
            var posted = await _repost.PostAsync(source, target, pingRole);
            return $"✅ Posted to {target.Mention}: {posted.GetJumpUrl()}\n" +
                "-# Edited the draft? Right click it → Apps → **Update GrepoBot post**.";
        }
        catch (Exception ex) when (ex is RepostException or HttpException)
        {
            return $"❌ {ErrorText(ex)}";
        }
    }

    private static ComponentBuilder BuildPicker(ulong sourceChannelId, ulong sourceMessageId, ulong? selectedChannelId)
    {
        var menu = new SelectMenuBuilder()
            .WithCustomId($"{TARGET_ID}:{sourceChannelId}:{sourceMessageId}")
            .WithType(ComponentType.ChannelSelect)
            .WithChannelTypes(ChannelType.Text, ChannelType.News)
            .WithPlaceholder("Choose a channel")
            .WithMinValues(1)
            .WithMaxValues(1);

        if (selectedChannelId is not null)
        {
            menu.WithDefaultValues(new SelectMenuDefaultValue(selectedChannelId.Value, SelectDefaultValueType.Channel));
        }

        return new ComponentBuilder().WithSelectMenu(menu, row: 0);
    }

    private static string Describe(IMessage message)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            parts.Add($"{message.Content.Length} characters");
        }

        if (message.Attachments.Count > 0)
        {
            parts.Add($"{message.Attachments.Count} attachment(s)");
        }

        if (message.Stickers.Count > 0)
        {
            parts.Add($"{message.Stickers.Count} sticker(s)");
        }

        return "-# " + string.Join(" · ", parts);
    }

    private IRole? GetUpdatesRole()
    {
        var roleId = _store.Read(state => state.UpdatesRoleId);
        return roleId is null ? null : Context.Guild.GetRole(roleId.Value);
    }

    private List<string> GetMissingPermissions(SocketTextChannel target)
    {
        var permissions = Context.Guild.CurrentUser.GetPermissions(target);
        var missing = new List<string>();

        if (!permissions.ViewChannel)
        {
            missing.Add("View Channel");
        }

        if (!permissions.SendMessages)
        {
            missing.Add("Send Messages");
        }

        if (!permissions.AttachFiles)
        {
            missing.Add("Attach Files");
        }

        if (!permissions.EmbedLinks)
        {
            missing.Add("Embed Links");
        }

        if (!permissions.MentionEveryone)
        {
            missing.Add("Mention @everyone and All Roles");
        }

        return missing;
    }

    private static string ErrorText(Exception ex)
    {
        return ex switch
        {
            HttpException http => $"Discord refused the request: {http.Reason ?? http.Message}",
            _ => ex.Message,
        };
    }

    #endregion
}
