using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using GrepoBot.Discord.Logic;
using GrepoBot.Discord.Services;

namespace GrepoBot.Discord.Modules;

/// <summary>
/// <c>/server setup</c>: shows what the layout would change, the Apply button does it.
/// </summary>
[Group("server", "Server layout")]
[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.Administrator)]
[RequireUserPermission(GuildPermission.Administrator)]
public sealed class ServerModule : InteractionModuleBase<SocketInteractionContext>
{
    #region Private Fields

    public const string APPLY_ID = "server-setup-apply";
    public const string CANCEL_ID = "server-setup-cancel";

    private const int EMBED_LIMIT = 4000;

    private readonly ServerSetupService _setup;

    #endregion

    #region Constructor

    public ServerModule(ServerSetupService setup)
    {
        _setup = setup;
    }

    #endregion

    #region Public Methods

    [SlashCommand("setup", "Set up roles, channels, rules, forums, tickets and links (shows a preview first)")]
    public async Task SetupAsync()
    {
        await DeferAsync(ephemeral: true);
        var preview = await _setup.RunAsync(Context.Guild, apply: false);

        var buttons = new ComponentBuilder()
            .WithButton("Apply", APPLY_ID, ButtonStyle.Success, new Emoji("✅"))
            .WithButton("Cancel", CANCEL_ID, ButtonStyle.Secondary)
            .Build();
        await FollowupAsync(
            embed: BuildEmbed("🧰 Server setup: preview", preview,
                "No channel is deleted: channels that do not fit move into a Delete me category for you to check. Staff, voice and ticket channels stay as they are. Only the old roles listed above are deleted."),
            components: buttons,
            ephemeral: true);
    }

    [ComponentInteraction(APPLY_ID, ignoreGroupNames: true)]
    public async Task OnApplyAsync()
    {
        await DeferAsync(ephemeral: true);
        await ModifyOriginalResponseAsync(p =>
        {
            p.Embed = new EmbedBuilder().WithColor(BotBranding.Warning).WithTitle("⏳ Setting up the server…").WithDescription("This takes up to a minute.").Build();
            p.Components = new ComponentBuilder().Build();
        });

        var result = await _setup.RunAsync(Context.Guild, apply: true);
        await ModifyOriginalResponseAsync(p => p.Embed = BuildEmbed("✅ Server setup done", result, "Run `/server setup` again any time, it only fixes what differs."));
    }

    [ComponentInteraction(CANCEL_ID, ignoreGroupNames: true)]
    public async Task OnCancelAsync()
    {
        await ((SocketMessageComponent)Context.Interaction).UpdateAsync(p =>
        {
            p.Embed = new EmbedBuilder().WithColor(BotBranding.Navy).WithTitle("Server setup cancelled").Build();
            p.Components = new ComponentBuilder().Build();
        });
    }

    #endregion

    #region Private Methods

    private static Embed BuildEmbed(string title, List<string> lines, string footer)
    {
        var text = lines.Count == 0 ? "Everything is already set up. 🎉" : string.Join("\n", lines);
        if (text.Length > EMBED_LIMIT)
        {
            text = text[..EMBED_LIMIT] + "\n…";
        }

        return new EmbedBuilder()
            .WithColor(lines.Any(l => l.StartsWith("⚠️")) ? BotBranding.Warning : BotBranding.Accent)
            .WithTitle(title)
            .WithDescription(text)
            .WithFooter(footer)
            .Build();
    }

    #endregion
}

/// <summary>
/// <c>/status set</c>: whether the GrepoBot app works right now, shown in the bot-status channel.
/// </summary>
[Group("status", "GrepoBot app status in the bot-status channel")]
[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.ManageThreads)]
[RequireUserPermission(GuildPermission.ManageThreads)]
public sealed class StatusModule : InteractionModuleBase<SocketInteractionContext>
{
    #region Private Fields

    private readonly AppStatusService _status;
    private readonly GrepoBot.Discord.State.StateStore _store;

    #endregion

    #region Constructor

    public StatusModule(AppStatusService status, GrepoBot.Discord.State.StateStore store)
    {
        _status = status;
        _store = store;
    }

    #endregion

    #region Public Methods

    [SlashCommand("set", "Set whether GrepoBot works right now")]
    public async Task SetAsync(
        [Summary(description: "Working, not working or maintenance")] AppState state,
        [Summary(description: "What happened or what users should do (optional)"), MaxLength(1000)] string? note = null,
        [Summary(description: "Also post a new message so followers of the channel are notified (default: no)")] bool announce = false)
    {
        await DeferAsync(ephemeral: true);
        var channelId = _store.Read(s => s.Status.ChannelId);
        if (channelId is null || Context.Guild.GetTextChannel(channelId.Value) is not { } channel)
        {
            await FollowupAsync("❌ There is no status channel yet. Run `/server setup` first.", ephemeral: true);
            return;
        }

        var message = await _status.SetAsync(channel, state, note, announce);
        var look = AppStatus.Look(state);
        await FollowupAsync($"{look.Emoji} Status is now **{look.Label}**: {message.GetJumpUrl()}", ephemeral: true);
    }

    #endregion
}
