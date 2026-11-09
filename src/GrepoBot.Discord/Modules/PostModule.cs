using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.WebSocket;
using GrepoBot.Discord.Logic;
using GrepoBot.Discord.Services;
using GrepoBot.Discord.State;

namespace GrepoBot.Discord.Modules;

/// <summary>
/// Right click a message → Apps → "Post as GrepoBot": pick a channel and a color and the bot posts a copy of
/// the message (text, attachments, emojis, server stickers) in an embed with that colored line, or as a plain message. "Update GrepoBot post" re-applies later
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
    private const string COLOR_ID = "post-color";
    private const string PING_NONE = "0";
    private const string PING_ROLE = "1";
    private const string PING_EVERYONE = "e";
    private const string RELEASE_TARGET_ID = "release-target";
    private const string RELEASE_SEND_ID = "release-send";

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

    /// <summary>
    /// Right click a draft → Apps → "Post as release": shows the release card built from the draft (version banner,
    /// sections, images, thanks and download buttons), then posts it to the chosen channel.
    /// </summary>
    [MessageCommand("Post as release")]
    public async Task PostReleaseAsync(IMessage message)
    {
        if (message is not IUserMessage draft)
        {
            await RespondAsync("❌ System messages cannot be posted.", ephemeral: true);
            return;
        }

        if (ReleaseNotes.Parse(draft.Content, out var problem) is null)
        {
            await RespondAsync($"❌ {problem}\nWrite the draft like this, then try again:\n```\n{ReleaseNotes.TEMPLATE}\n```", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);
        try
        {
            var (card, files) = await _repost.BuildReleaseAsync(draft, Context.Guild, null, DateTimeOffset.UtcNow);
            try
            {
                await FollowupWithFilesAsync(files, components: card, ephemeral: true, flags: MessageFlags.ComponentsV2);
            }
            finally
            {
                MessageCards.DisposeAll(files);
            }
        }
        catch (Exception ex) when (ex is RepostException or HttpException)
        {
            await FollowupAsync($"❌ {ErrorText(ex)}", ephemeral: true);
            return;
        }

        var updates = Context.Guild.TextChannels.FirstOrDefault(c => ServerLayout.Matches(c.Name, ServerLayout.Channel("updates").Aliases));
        var (content, components) = BuildReleaseControls(draft.Channel.Id, draft.Id, updates);
        await FollowupAsync(content, components: components, ephemeral: true);
    }

    [ComponentInteraction(RELEASE_TARGET_ID + ":*:*")]
    public async Task OnReleaseTargetSelectedAsync(string sourceChannelId, string sourceMessageId, string[] selected)
    {
        var component = (SocketMessageComponent)Context.Interaction;
        var (content, components) = BuildReleaseControls(ulong.Parse(sourceChannelId), ulong.Parse(sourceMessageId), Context.Guild.GetTextChannel(ulong.Parse(selected[0])));
        await component.UpdateAsync(p =>
        {
            p.Content = content;
            p.Components = components;
        });
    }

    [ComponentInteraction(RELEASE_SEND_ID + ":*:*:*:*")]
    public async Task OnReleaseSendAsync(string sourceChannelId, string sourceMessageId, string targetChannelId, string ping)
    {
        var component = (SocketMessageComponent)Context.Interaction;
        await component.UpdateAsync(p =>
        {
            p.Content = "⏳ Posting the release…";
            p.Components = new ComponentBuilder().Build();
        });

        string result;
        if (Context.Client.GetChannel(ulong.Parse(sourceChannelId)) is not IMessageChannel sourceChannel
            || await sourceChannel.GetMessageAsync(ulong.Parse(sourceMessageId)) is not IUserMessage source)
        {
            result = "❌ The draft was deleted.";
        }
        else if (Context.Guild.GetTextChannel(ulong.Parse(targetChannelId)) is not { } target)
        {
            result = "❌ The target channel no longer exists.";
        }
        else
        {
            try
            {
                var posted = await _repost.PostReleaseAsync(source, target, ping == PING_ROLE ? GetUpdatesRole() : null, ping == PING_EVERYONE);
                result = $"✅ Release posted to {target.Mention}: {posted.GetJumpUrl()}\n" +
                    "-# Edited the draft? Right click it → Apps → **Update GrepoBot post**.";
            }
            catch (Exception ex) when (ex is RepostException or HttpException)
            {
                result = $"❌ {ErrorText(ex)}";
            }
        }

        await ModifyOriginalResponseAsync(p => p.Content = result);
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
        var color = _store.Read(state => state.PostColor);
        await ShowConfirmAsync(ulong.Parse(sourceChannelId), ulong.Parse(sourceMessageId), ulong.Parse(selected[0]), color);
    }

    [ComponentInteraction(COLOR_ID + ":*:*:*")]
    public async Task OnColorSelectedAsync(string sourceChannelId, string sourceMessageId, string targetChannelId, string[] selected)
    {
        var color = RepostLayout.FindColor(selected.FirstOrDefault()).Value;
        _store.Update(state => state.PostColor = color);
        await ShowConfirmAsync(ulong.Parse(sourceChannelId), ulong.Parse(sourceMessageId), ulong.Parse(targetChannelId), color);
    }

    [ComponentInteraction(SEND_ID + ":*:*:*:*:*")]
    public async Task OnSendAsync(string sourceChannelId, string sourceMessageId, string targetChannelId, string ping, string color)
    {
        var component = (SocketMessageComponent)Context.Interaction;
        await component.UpdateAsync(p =>
        {
            p.Content = "⏳ Posting…";
            p.Components = new ComponentBuilder().Build();
        });

        var result = await SendAsync(ulong.Parse(sourceChannelId), ulong.Parse(sourceMessageId), ulong.Parse(targetChannelId), ping, RepostLayout.FindColor(color));
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

    /// <summary>Channel picker, color picker and the post buttons for the chosen channel and color.</summary>
    private async Task ShowConfirmAsync(ulong sourceChannel, ulong sourceMessage, ulong targetId, string colorValue)
    {
        var component = (SocketMessageComponent)Context.Interaction;
        var target = Context.Guild.GetTextChannel(targetId);
        if (target is null)
        {
            await component.UpdateAsync(p => p.Content = "❌ That channel no longer exists.");
            return;
        }

        var color = RepostLayout.FindColor(colorValue);
        var missing = GetMissingPermissions(target);
        var updatesRole = GetUpdatesRole();

        var content = $"📮 Post the message to {target.Mention}?\n🎨 {color.Emoji} {color.Label}";
        if (target is INewsChannel && _store.Read(s => s.AutoPublish))
        {
            content += "\n📣 It is an announcement channel, the post will be published to following servers.";
        }

        if (missing.Count > 0)
        {
            content += $"\n⚠️ I am missing these permissions in {target.Mention}: **{string.Join(", ", missing)}**.";
        }

        var colors = new SelectMenuBuilder()
            .WithCustomId($"{COLOR_ID}:{sourceChannel}:{sourceMessage}:{target.Id}")
            .WithPlaceholder("Color of the line on the left")
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var option in RepostLayout.Colors)
        {
            colors.AddOption(option.Label, option.Value, emote: new Emoji(option.Emoji), isDefault: option.Value == color.Value);
        }

        var sendId = $"{SEND_ID}:{sourceChannel}:{sourceMessage}:{target.Id}";
        var buttons = BuildPicker(sourceChannel, sourceMessage, target.Id)
            .WithSelectMenu(colors, row: 1)
            .WithButton("Post & ping @everyone", $"{sendId}:{PING_EVERYONE}:{color.Value}", ButtonStyle.Success, new Emoji("📢"), row: 2)
            .WithButton("Post", $"{sendId}:{PING_NONE}:{color.Value}", ButtonStyle.Primary, new Emoji("📮"), row: 2);

        if (updatesRole is not null)
        {
            buttons.WithButton($"Post & ping @{updatesRole.Name}", $"{sendId}:{PING_ROLE}:{color.Value}", ButtonStyle.Secondary, new Emoji("🔔"), row: 2);
        }

        buttons.WithButton("Cancel", CANCEL_ID, ButtonStyle.Secondary, row: 2);

        await component.UpdateAsync(p =>
        {
            p.Content = content;
            p.Components = buttons.Build();
        });
    }

    /// <summary>The channel picker and post buttons under the release preview.</summary>
    private (string Content, MessageComponent Components) BuildReleaseControls(ulong sourceChannelId, ulong sourceMessageId, SocketTextChannel? target)
    {
        var menu = new SelectMenuBuilder()
            .WithCustomId($"{RELEASE_TARGET_ID}:{sourceChannelId}:{sourceMessageId}")
            .WithType(ComponentType.ChannelSelect)
            .WithChannelTypes(ChannelType.Text, ChannelType.News)
            .WithPlaceholder("Choose a channel")
            .WithMinValues(1)
            .WithMaxValues(1);
        if (target is not null)
        {
            menu.WithDefaultValues(new SelectMenuDefaultValue(target.Id, SelectDefaultValueType.Channel));
        }

        var builder = new ComponentBuilder().WithSelectMenu(menu, row: 0);
        if (target is null)
        {
            builder.WithButton("Cancel", CANCEL_ID, ButtonStyle.Secondary, row: 1);
            return ("🚀 **This is how the release will look.** Where should I post it?", builder.Build());
        }

        var content = $"🚀 **This is how the release will look.** Post it to {target.Mention}?";
        if (target is INewsChannel && _store.Read(s => s.AutoPublish))
        {
            content += "\n📣 It is an announcement channel, the release will be published to following servers.";
        }

        var missing = GetMissingPermissions(target);
        if (missing.Count > 0)
        {
            content += $"\n⚠️ I am missing these permissions in {target.Mention}: **{string.Join(", ", missing)}**.";
        }

        var sendId = $"{RELEASE_SEND_ID}:{sourceChannelId}:{sourceMessageId}:{target.Id}";
        builder
            .WithButton("Post & ping @everyone", $"{sendId}:{PING_EVERYONE}", ButtonStyle.Success, new Emoji("📢"), row: 1)
            .WithButton("Post", $"{sendId}:{PING_NONE}", ButtonStyle.Primary, new Emoji("📮"), row: 1);
        if (GetUpdatesRole() is { } role)
        {
            builder.WithButton($"Post & ping @{role.Name}", $"{sendId}:{PING_ROLE}", ButtonStyle.Secondary, new Emoji("🔔"), row: 1);
        }

        builder.WithButton("Cancel", CANCEL_ID, ButtonStyle.Secondary, row: 1);
        return (content, builder.Build());
    }

    private async Task<string> SendAsync(ulong sourceChannelId, ulong sourceMessageId, ulong targetChannelId, string ping, RepostColor color)
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

        var pingRole = ping == PING_ROLE ? GetUpdatesRole() : null;
        try
        {
            var posted = await _repost.PostAsync(source, target, pingRole, pingEveryone: ping == PING_EVERYONE, color: color.Rgb);
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
