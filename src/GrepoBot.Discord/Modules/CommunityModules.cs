using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using GrepoBot.Discord.Logic;
using GrepoBot.Discord.Services;
using GrepoBot.Discord.State;

namespace GrepoBot.Discord.Modules;

/// <summary>
/// <c>/rules post</c> and <c>/rules edit</c>.
/// </summary>
[Group("rules", "Server rules with an accept button that gives the member role")]
[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
[RequireUserPermission(GuildPermission.ManageGuild)]
public sealed class RulesModule : InteractionModuleBase<SocketInteractionContext>
{
    #region Private Fields

    private const string SAVE_ID = "rules-save";

    private readonly RulesService _rules;
    private readonly StateStore _store;

    #endregion

    #region Constructor

    public RulesModule(RulesService rules, StateStore store)
    {
        _rules = rules;
        _store = store;
    }

    #endregion

    #region Public Methods

    [SlashCommand("post", "Post the rules to a channel (a form for the text opens)")]
    public async Task PostAsync(
        [Summary(description: "Channel for the rules"), ChannelTypes(ChannelType.Text, ChannelType.News)] ITextChannel channel,
        [Summary(description: "Role given after accepting, e.g. Member")] IRole role)
    {
        var problem = RoleSafety.GetProblem(role, Context.Guild.CurrentUser.Hierarchy);
        if (problem is not null)
        {
            await RespondAsync($"❌ {problem}", ephemeral: true);
            return;
        }

        await RespondWithRulesModalAsync(channel.Id, role.Id);
    }

    [SlashCommand("edit", "Edit the posted rules")]
    public async Task EditAsync()
    {
        var rules = _store.Read(state => new { state.Rules.ChannelId, state.Rules.MemberRoleId });
        if (rules.ChannelId is null || rules.MemberRoleId is null)
        {
            await RespondAsync("❌ The rules are not posted yet. Use `/rules post`.", ephemeral: true);
            return;
        }

        await RespondWithRulesModalAsync(rules.ChannelId.Value, rules.MemberRoleId.Value);
    }

    [ModalInteraction(SAVE_ID + ":*:*", ignoreGroupNames: true)]
    public async Task OnSaveAsync(string channelId, string roleId, RulesModal modal)
    {
        await DeferAsync(ephemeral: true);

        var channel = Context.Guild.GetTextChannel(ulong.Parse(channelId));
        var role = Context.Guild.GetRole(ulong.Parse(roleId));
        if (channel is null || role is null)
        {
            await FollowupAsync("❌ The channel or the role no longer exists.", ephemeral: true);
            return;
        }

        var message = await _rules.PublishAsync(channel, role, modal.RulesTitle.Trim(), modal.Text.Trim());
        await FollowupAsync(
            $"✅ Rules are live: {message.GetJumpUrl()}\n\n" +
            $"**Tip:** to lock the server until the rules are accepted, deny *View Channel* for @everyone on your categories, " +
            $"allow it for {role.Mention}, and leave {channel.Mention} and the welcome channel visible to @everyone.",
            ephemeral: true,
            allowedMentions: AllowedMentions.None);
    }

    #endregion

    #region Private Methods

    private Task RespondWithRulesModalAsync(ulong channelId, ulong roleId)
    {
        var current = _store.Read(state => new { state.Rules.Title, state.Rules.Text });
        return RespondWithModalAsync<RulesModal>(
            $"{SAVE_ID}:{channelId}:{roleId}",
            modifyModal: builder =>
            {
                builder.UpdateTextInput(RulesModal.TITLE_ID, input => input.Value = current.Title);
                builder.UpdateTextInput(RulesModal.TEXT_ID, input => input.Value = current.Text);
            });
    }

    #endregion
}

public sealed class RulesModal : IModal
{
    #region Properties

    public const string TITLE_ID = "title";
    public const string TEXT_ID = "text";

    public string Title => "Server rules";

    [InputLabel("Title")]
    [ModalTextInput(TITLE_ID, TextInputStyle.Short, "📜 Server Rules", maxLength: 256)]
    public string RulesTitle { get; set; } = string.Empty;

    [InputLabel("Rules (markdown)")]
    [ModalTextInput(TEXT_ID, TextInputStyle.Paragraph, "**1. Be respectful.** …", maxLength: 4000)]
    public string Text { get; set; } = string.Empty;

    #endregion
}

/// <summary>
/// <c>/welcome …</c>: greeting for new members.
/// </summary>
[Group("welcome", "Welcome message for new members")]
[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
[RequireUserPermission(GuildPermission.ManageGuild)]
public sealed class WelcomeModule : InteractionModuleBase<SocketInteractionContext>
{
    #region Private Fields

    private const string SAVE_ID = "welcome-save";

    private readonly WelcomeService _welcome;
    private readonly StateStore _store;

    #endregion

    #region Constructor

    public WelcomeModule(WelcomeService welcome, StateStore store)
    {
        _welcome = welcome;
        _store = store;
    }

    #endregion

    #region Public Methods

    [SlashCommand("setup", "Choose the welcome channel and write the message")]
    public async Task SetupAsync([Summary(description: "Channel for welcome messages"), ChannelTypes(ChannelType.Text)] ITextChannel channel)
    {
        var message = _store.Read(state => state.Welcome.Message);
        await RespondWithModalAsync<WelcomeModal>(
            $"{SAVE_ID}:{channel.Id}",
            modifyModal: builder => builder.UpdateTextInput(WelcomeModal.MESSAGE_ID, input => input.Value = message));
    }

    [ModalInteraction(SAVE_ID + ":*", ignoreGroupNames: true)]
    public async Task OnSaveAsync(string channelId, WelcomeModal modal)
    {
        _store.Update(state =>
        {
            state.Welcome.ChannelId = ulong.Parse(channelId);
            state.Welcome.Message = modal.Message.Trim();
        });

        await RespondAsync($"✅ New members will be welcomed in <#{channelId}>. Try it with `/welcome test`.", ephemeral: true);
    }

    [SlashCommand("test", "Send the welcome message for yourself")]
    public async Task TestAsync()
    {
        await DeferAsync(ephemeral: true);
        var sent = await _welcome.SendWelcomeAsync((SocketGuildUser)Context.User);
        await FollowupAsync(sent ? "✅ Sent." : "❌ No welcome channel is set. Use `/welcome setup`.", ephemeral: true);
    }

    [SlashCommand("disable", "Stop welcoming new members")]
    public async Task DisableAsync()
    {
        _store.Update(state => state.Welcome.ChannelId = null);
        await RespondAsync("⏸️ Welcome messages are off.", ephemeral: true);
    }

    #endregion
}

public sealed class WelcomeModal : IModal
{
    #region Properties

    public const string MESSAGE_ID = "message";

    public string Title => "Welcome message";

    [InputLabel("Message", "Placeholders: {user} {username} {server} {rules} {membercount}")]
    [ModalTextInput(MESSAGE_ID, TextInputStyle.Paragraph, "Hey {user}, welcome to **{server}**!", maxLength: 2000)]
    public string Message { get; set; } = string.Empty;

    #endregion
}

/// <summary>
/// <c>/roles panel</c>: buttons members click to get or remove roles (e.g. update notifications, language).
/// </summary>
[Group("roles", "Self-assignable roles")]
[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.ManageRoles)]
[RequireUserPermission(GuildPermission.ManageRoles)]
public sealed class RolesModule : InteractionModuleBase<SocketInteractionContext>
{
    #region Public Methods

    [SlashCommand("panel", "Post buttons that give or remove roles")]
    public async Task PanelAsync(
        [Summary(description: "Where to post the panel"), ChannelTypes(ChannelType.Text, ChannelType.News)] ITextChannel channel,
        [Summary(description: "Panel title, e.g. Notifications"), MaxLength(256)] string title,
        IRole role1,
        IRole? role2 = null,
        IRole? role3 = null,
        IRole? role4 = null,
        IRole? role5 = null,
        [Summary(description: "Text above the buttons"), MaxLength(2000)] string? description = null)
    {
        var roles = new[] { role1, role2, role3, role4, role5 }
            .OfType<IRole>()
            .DistinctBy(r => r.Id)
            .ToList();

        var problems = roles
            .Select(r => RoleSafety.GetProblem(r, Context.Guild.CurrentUser.Hierarchy))
            .OfType<string>()
            .ToList();

        if (problems.Count > 0)
        {
            await RespondAsync("❌ " + string.Join("\n❌ ", problems), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var text = (description ?? "Click a button to get the role, click it again to remove it.") + "\n\n" +
            string.Join("\n", roles.Select(r => $"• {r.Mention}"));

        var embed = new EmbedBuilder()
            .WithColor(BotBranding.Accent)
            .WithTitle(title)
            .WithDescription(text)
            .Build();

        var buttons = new ComponentBuilder();
        foreach (var role in roles)
        {
            buttons.WithButton(role.Name, $"{PublicModule.ROLE_TOGGLE_ID}:{role.Id}", ButtonStyle.Secondary);
        }

        var message = await channel.SendMessageAsync(embed: embed, components: buttons.Build(), allowedMentions: AllowedMentions.None);
        await RespondAsync($"✅ Posted: {message.GetJumpUrl()}", ephemeral: true);
    }

    #endregion
}

/// <summary>
/// <c>/setup …</c>: general settings and the bot's look.
/// </summary>
[Group("setup", "GrepoBot settings")]
[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
[RequireUserPermission(GuildPermission.ManageGuild)]
public sealed class SetupModule : InteractionModuleBase<SocketInteractionContext>
{
    #region Private Fields

    private readonly StateStore _store;

    #endregion

    #region Constructor

    public SetupModule(StateStore store)
    {
        _store = store;
    }

    #endregion

    #region Public Methods

    [SlashCommand("show", "Show the current settings")]
    public async Task ShowAsync()
    {
        var s = _store.Read(state => new
        {
            state.LogChannelId,
            state.UpdatesRoleId,
            state.AutoPublish,
            WelcomeChannelId = state.Welcome.ChannelId,
            RulesChannelId = state.Rules.ChannelId,
            state.Rules.MemberRoleId,
            AutoReplies = state.AutoReply.Rules.Count,
            Posts = state.Posts.Count,
        });

        var embed = new EmbedBuilder()
            .WithColor(BotBranding.Accent)
            .WithTitle("⚙️ GrepoBot settings")
            .AddField("Rules", s.RulesChannelId is null ? "not posted" : $"<#{s.RulesChannelId}> → <@&{s.MemberRoleId}>", inline: true)
            .AddField("Welcome", Channel(s.WelcomeChannelId), inline: true)
            .AddField("Mod log", Channel(s.LogChannelId), inline: true)
            .AddField("Updates role", s.UpdatesRoleId is null ? "not set" : $"<@&{s.UpdatesRoleId}>", inline: true)
            .AddField("Auto-publish", s.AutoPublish ? "on" : "off", inline: true)
            .AddField("Automatic answers", s.AutoReplies.ToString(), inline: true)
            .WithFooter($"{s.Posts} reposts remembered")
            .Build();

        await RespondAsync(embed: embed, ephemeral: true);
    }

    [SlashCommand("log-channel", "Channel for the moderation log (leave empty to turn it off)")]
    public async Task LogChannelAsync([ChannelTypes(ChannelType.Text)] ITextChannel? channel = null)
    {
        _store.Update(state => state.LogChannelId = channel?.Id);
        await RespondAsync(channel is null ? "⏸️ Moderation log is off." : $"✅ Moderation log goes to {channel.Mention}.", ephemeral: true);
    }

    [SlashCommand("updates-role", "Role pinged by \"Post & ping\" for new versions (leave empty to remove)")]
    public async Task UpdatesRoleAsync(IRole? role = null)
    {
        _store.Update(state => state.UpdatesRoleId = role?.Id);
        var text = role is null
            ? "⏸️ No updates role."
            : $"✅ {role.Mention} will be pinged by **Post & ping**. Make it self-assignable with `/roles panel`.";
        await RespondAsync(text, ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    [SlashCommand("auto-publish", "Publish posts in announcement channels to following servers")]
    public async Task AutoPublishAsync(bool enabled)
    {
        _store.Update(state => state.AutoPublish = enabled);
        await RespondAsync(enabled ? "✅ Posts in announcement channels are published." : "⏸️ Posts are not published.", ephemeral: true);
    }

    [SlashCommand("branding", "Set the bot's avatar and profile banner to the GrepoBot look")]
    public async Task BrandingAsync()
    {
        await DeferAsync(ephemeral: true);

        if (!File.Exists(BotBranding.AvatarPath) || !BotBranding.HasBanner)
        {
            await FollowupAsync("❌ The Assets folder with avatar.png and banner.png is missing next to the bot.", ephemeral: true);
            return;
        }

        using var avatar = new Image(BotBranding.AvatarPath);
        using var banner = new Image(BotBranding.BannerPath);
        await Context.Client.CurrentUser.ModifyAsync(properties =>
        {
            properties.Avatar = avatar;
            properties.Banner = banner;
        });

        await FollowupAsync("✅ Avatar and banner updated. Discord may take a few minutes to show them everywhere.", ephemeral: true);
    }

    #endregion

    #region Private Methods

    private static string Channel(ulong? id)
    {
        return id is null ? "off" : $"<#{id}>";
    }

    #endregion
}
