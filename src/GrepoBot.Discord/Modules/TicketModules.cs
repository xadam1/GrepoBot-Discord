using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.WebSocket;
using GrepoBot.Discord.Logic;
using GrepoBot.Discord.Services;
using GrepoBot.Discord.State;

namespace GrepoBot.Discord.Modules;

/// <summary>
/// The ticket buttons and forms members and staff click. Who may do what is checked in
/// <see cref="TicketService"/> (owner or staff), so this module has no permission attributes.
/// </summary>
[CommandContextType(InteractionContextType.Guild)]
public sealed class TicketButtonsModule : InteractionModuleBase<SocketInteractionContext>
{
    #region Private Fields

    private const string CREATE_ID = "ticket-create";
    private const string CLOSE_SAVE_ID = "ticket-close-save";

    private readonly TicketService _tickets;

    #endregion

    #region Constructor

    public TicketButtonsModule(TicketService tickets)
    {
        _tickets = tickets;
    }

    #endregion

    #region Public Methods

    [ComponentInteraction(TicketService.OPEN_BUTTON_ID)]
    public Task OnOpenClickedAsync()
    {
        return OpenFormAsync();
    }

    /// <summary>Panels posted before the single button had one button per topic.</summary>
    [ComponentInteraction(TicketService.OPEN_BUTTON_ID + ":*")]
    public Task OnLegacyOpenClickedAsync(string topicSlug)
    {
        return OpenFormAsync();
    }

    [ModalInteraction(CREATE_ID)]
    public async Task OnCreateAsync(TicketOpenModal modal)
    {
        await DeferAsync(ephemeral: true);
        try
        {
            var topic = TicketTopics.Find(modal.Topic);
            var channel = await _tickets.OpenAsync((SocketGuildUser)Context.User, topic, modal.Subject.Trim(), modal.Details?.Trim());
            await FollowupAsync($"✅ Your ticket is ready: {channel.Mention}", ephemeral: true);
        }
        catch (Exception ex) when (ex is TicketException or HttpException)
        {
            await FollowupAsync($"⚠️ {ErrorText(ex)}", ephemeral: true);
        }
    }

    [ComponentInteraction(TicketService.CLOSE_BUTTON_ID)]
    public async Task OnCloseClickedAsync()
    {
        var ticket = _tickets.GetTicket(Context.Channel.Id);
        if (ticket is null || ticket.IsClosed)
        {
            await RespondAsync("⚠️ This ticket is already closed.", ephemeral: true);
            return;
        }

        await RespondWithModalAsync<TicketCloseModal>(CLOSE_SAVE_ID);
    }

    [ModalInteraction(CLOSE_SAVE_ID)]
    public async Task OnCloseAsync(TicketCloseModal modal)
    {
        await DeferAsync(ephemeral: true);
        await RunAsync(() => _tickets.CloseAsync((SocketGuildUser)Context.User, (ITextChannel)Context.Channel, modal.Reason), "🔒 Ticket closed.");
    }

    [ComponentInteraction(TicketService.CLAIM_BUTTON_ID)]
    public async Task OnClaimAsync()
    {
        await DeferAsync(ephemeral: true);
        try
        {
            var text = await _tickets.ClaimAsync((SocketGuildUser)Context.User, (ITextChannel)Context.Channel);
            await FollowupAsync(text, ephemeral: true, allowedMentions: AllowedMentions.None);
        }
        catch (Exception ex) when (ex is TicketException or HttpException)
        {
            await FollowupAsync($"⚠️ {ErrorText(ex)}", ephemeral: true);
        }
    }

    [ComponentInteraction(TicketService.REOPEN_BUTTON_ID)]
    public async Task OnReopenAsync()
    {
        await DeferAsync(ephemeral: true);
        await RunAsync(() => _tickets.ReopenAsync((SocketGuildUser)Context.User, (ITextChannel)Context.Channel), "🔓 Ticket reopened.");
    }

    [ComponentInteraction(TicketService.DELETE_BUTTON_ID)]
    public async Task OnDeleteAsync()
    {
        var user = (SocketGuildUser)Context.User;
        if (!_tickets.IsStaff(user))
        {
            await RespondAsync("⚠️ Only the team can delete tickets.", ephemeral: true);
            return;
        }

        await RespondAsync("🗑️ This ticket will be deleted in 5 seconds.");
        try
        {
            await _tickets.DeleteAsync(user, (ITextChannel)Context.Channel);
        }
        catch (Exception ex) when (ex is TicketException or HttpException)
        {
            await FollowupAsync($"⚠️ {ErrorText(ex)}", ephemeral: true);
        }
    }

    #endregion

    #region Private Methods

    private async Task OpenFormAsync()
    {
        var user = (SocketGuildUser)Context.User;
        var problem = _tickets.GetSetupProblem(Context.Guild) ?? _tickets.GetLimitProblem(user);
        if (problem is not null)
        {
            await RespondAsync($"⚠️ {problem}", ephemeral: true);
            return;
        }

        await RespondWithModalAsync<TicketOpenModal>(CREATE_ID);
    }

    private async Task RunAsync(Func<Task> action, string success)
    {
        try
        {
            await action();
            await FollowupAsync(success, ephemeral: true);
        }
        catch (Exception ex) when (ex is TicketException or HttpException)
        {
            await FollowupAsync($"⚠️ {ErrorText(ex)}", ephemeral: true);
        }
    }

    internal static string ErrorText(Exception ex)
    {
        return ex switch
        {
            HttpException http => $"Discord refused the request: {http.Reason ?? http.Message}",
            _ => ex.Message,
        };
    }

    #endregion
}

/// <summary>
/// <c>/ticket …</c>: commands used inside a ticket channel.
/// </summary>
[Group("ticket", "Manage the ticket you are in")]
[CommandContextType(InteractionContextType.Guild)]
public sealed class TicketModule : InteractionModuleBase<SocketInteractionContext>
{
    #region Private Fields

    private readonly TicketService _tickets;

    #endregion

    #region Constructor

    public TicketModule(TicketService tickets)
    {
        _tickets = tickets;
    }

    #endregion

    #region Public Methods

    [SlashCommand("add", "Give another member access to this ticket (team only)")]
    public Task AddAsync([Summary(description: "Member to add")] IGuildUser member)
    {
        return RunAsync(() => _tickets.AddMemberAsync(User, Channel, member), $"✅ Added {member.Mention}.");
    }

    [SlashCommand("remove", "Remove a member you added from this ticket (team only)")]
    public Task RemoveAsync([Summary(description: "Member to remove")] IGuildUser member)
    {
        return RunAsync(() => _tickets.RemoveMemberAsync(User, Channel, member), $"✅ Removed {member.Mention}.");
    }

    [SlashCommand("close", "Close this ticket")]
    public Task CloseAsync([Summary(description: "Why the ticket is closed (shown to the member)"), MaxLength(500)] string? reason = null)
    {
        return RunAsync(() => _tickets.CloseAsync(User, Channel, reason), "🔒 Ticket closed.");
    }

    [SlashCommand("claim", "Take care of this ticket (team only)")]
    public async Task ClaimAsync()
    {
        await DeferAsync(ephemeral: true);
        try
        {
            var text = await _tickets.ClaimAsync(User, Channel);
            await FollowupAsync(text, ephemeral: true, allowedMentions: AllowedMentions.None);
        }
        catch (Exception ex) when (ex is TicketException or HttpException)
        {
            await FollowupAsync($"⚠️ {TicketButtonsModule.ErrorText(ex)}", ephemeral: true);
        }
    }

    #endregion

    #region Private Methods

    private SocketGuildUser User => (SocketGuildUser)Context.User;

    private ITextChannel Channel => Context.Channel as ITextChannel
        ?? throw new TicketException("This is not a ticket channel.");

    private async Task RunAsync(Func<Task> action, string success)
    {
        await DeferAsync(ephemeral: true);
        try
        {
            await action();
            await FollowupAsync(success, ephemeral: true, allowedMentions: AllowedMentions.None);
        }
        catch (Exception ex) when (ex is TicketException or HttpException)
        {
            await FollowupAsync($"⚠️ {TicketButtonsModule.ErrorText(ex)}", ephemeral: true);
        }
    }

    #endregion
}

/// <summary>
/// <c>/tickets …</c>: setting up the ticket system.
/// </summary>
[Group("tickets", "Support ticket settings")]
[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
[RequireUserPermission(GuildPermission.ManageGuild)]
[RequireStaff]
public sealed class TicketsAdminModule : InteractionModuleBase<SocketInteractionContext>
{
    #region Private Fields

    private const string PANEL_SAVE_ID = "tickets-panel-save";
    private const string MESSAGE_SAVE_ID = "tickets-message-save";

    private readonly TicketService _tickets;
    private readonly StateStore _store;

    #endregion

    #region Constructor

    public TicketsAdminModule(TicketService tickets, StateStore store)
    {
        _tickets = tickets;
        _store = store;
    }

    #endregion

    #region Public Methods

    [SlashCommand("setup", "Choose the ticket category, the team roles and the log channel")]
    public async Task SetupAsync(
        [Summary(description: "Category the ticket channels are created in")] ICategoryChannel category,
        [Summary("staff-role", "Role that sees and handles every ticket")] IRole staffRole,
        [Summary("log-channel", "Where opened and closed tickets are logged")] [ChannelTypes(ChannelType.Text)] ITextChannel? logChannel = null,
        [Summary("staff-role2", "Another team role")] IRole? staffRole2 = null,
        [Summary("ping-staff", "Mention the team roles in new tickets (default: yes)")] bool pingStaff = true,
        [Summary("max-open", "Open tickets per member at the same time (default: 1)"), MinValue(1), MaxValue(10)] int maxOpen = 1)
    {
        var staff = new[] { staffRole, staffRole2 }
            .OfType<IRole>()
            .Where(r => r.Id != Context.Guild.EveryoneRole.Id)
            .Select(r => r.Id)
            .Distinct()
            .ToList();

        _store.Update(state =>
        {
            state.Tickets.CategoryId = category.Id;
            state.Tickets.StaffRoleIds = staff;
            state.Tickets.LogChannelId = logChannel?.Id;
            state.Tickets.PingStaff = pingStaff;
            state.Tickets.MaxOpenPerUser = maxOpen;
        });

        await RespondAsync(
            $"✅ Tickets are created in **{category.Name}**, handled by {string.Join(", ", staff.Select(id => $"<@&{id}>"))}" +
            (logChannel is null ? ", no log." : $", logged in {logChannel.Mention}.") +
            "\nNext: `/tickets panel` posts the button members click.",
            ephemeral: true,
            allowedMentions: AllowedMentions.None);
    }

    [SlashCommand("panel", "Post or update the panel with the Open a ticket button (a form opens)")]
    public async Task PanelAsync([Summary(description: "Channel for the panel"), ChannelTypes(ChannelType.Text)] ITextChannel channel)
    {
        // initValue on the attributes is not sent by Discord.Net, the current texts are filled in here.
        var current = _store.Read(state => new { state.Tickets.PanelTitle, state.Tickets.PanelDescription });
        await RespondWithModalAsync<TicketPanelModal>(
            $"{PANEL_SAVE_ID}:{channel.Id}",
            modifyModal: builder =>
            {
                builder.UpdateTextInput(TicketPanelModal.TITLE_ID, input => input.Value = current.PanelTitle);
                builder.UpdateTextInput(TicketPanelModal.TEXT_ID, input => input.Value = current.PanelDescription);
            });
    }

    [ModalInteraction(PANEL_SAVE_ID + ":*", ignoreGroupNames: true)]
    public async Task OnPanelSaveAsync(string channelId, TicketPanelModal modal)
    {
        await DeferAsync(ephemeral: true);
        if (Context.Guild.GetTextChannel(ulong.Parse(channelId)) is not { } channel)
        {
            await FollowupAsync("❌ That channel no longer exists.", ephemeral: true);
            return;
        }

        try
        {
            var (message, updated) = await _tickets.PublishPanelAsync(channel, modal.PanelTitle.Trim(), modal.Text.Trim());
            var warning = _tickets.GetSetupProblem(Context.Guild) is null ? string.Empty : "\n⚠️ Run `/tickets setup` before members click it.";
            await FollowupAsync($"✅ Panel {(updated ? "updated" : "posted")}: {message.GetJumpUrl()}{warning}", ephemeral: true);
        }
        catch (HttpException ex)
        {
            await FollowupAsync($"❌ {TicketButtonsModule.ErrorText(ex)}", ephemeral: true);
        }
    }

    [SlashCommand("message", "Edit the text posted in every new ticket")]
    public async Task MessageAsync()
    {
        var current = _store.Read(state => state.Tickets.Message);
        await RespondWithModalAsync<TicketMessageModal>(
            MESSAGE_SAVE_ID,
            modifyModal: builder => builder.UpdateTextInput(TicketMessageModal.MESSAGE_ID, input => input.Value = current));
    }

    [ModalInteraction(MESSAGE_SAVE_ID, ignoreGroupNames: true)]
    public async Task OnMessageSaveAsync(TicketMessageModal modal)
    {
        _store.Update(state => state.Tickets.Message = modal.Message.Trim());
        await RespondAsync("✅ Saved. New tickets will use this text.", ephemeral: true);
    }

    [SlashCommand("list", "Show the open and closed ticket channels")]
    public async Task ListAsync()
    {
        var tickets = _store.Read(state => state.Tickets.Tickets.Select(t => t.Clone()).ToList());
        if (tickets.Count == 0)
        {
            await RespondAsync("No tickets right now. 🎉", ephemeral: true);
            return;
        }

        var lines = tickets
            .OrderBy(t => t.IsClosed)
            .ThenBy(t => t.Number)
            .Take(40)
            .Select(t => $"{(t.IsClosed ? "🔒" : "🟢")} <#{t.ChannelId}> · <@{t.OwnerId}> · {t.Topic}" +
                (t.ClaimedById is { } c ? $" · claimed by <@{c}>" : string.Empty) +
                $" · <t:{t.OpenedAt.ToUnixTimeSeconds()}:R>");

        var embed = new EmbedBuilder()
            .WithColor(BotBranding.Accent)
            .WithTitle($"🎫 Tickets ({tickets.Count(t => !t.IsClosed)} open, {tickets.Count(t => t.IsClosed)} closed)")
            .WithDescription(string.Join("\n", lines))
            .Build();

        await RespondAsync(embed: embed, ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    #endregion
}

public sealed class TicketOpenModal : IModal
{
    #region Properties

    public string Title => "Open a ticket";

    [InputLabel("Topic")]
    [ModalSelectMenu("topic", 1, 1)]
    [ModalSelectMenuOption("Download & installation", TicketTopics.INSTALL, "Downloading, installing or updating GrepoBot", "📥")]
    [ModalSelectMenuOption("Login", TicketTopics.LOGIN, "Signing in to GrepoBot or to the game through it", "🔑")]
    [ModalSelectMenuOption("Feature problem", TicketTopics.FEATURE, "A feature does not work as expected", "🧩")]
    [ModalSelectMenuOption("Something else", TicketTopics.OTHER, "Anything that does not fit above", "❓")]
    public string Topic { get; set; } = string.Empty;

    [InputLabel("What do you need help with?", "One short sentence, it becomes the ticket's title")]
    [ModalTextInput("subject", TextInputStyle.Short, "e.g. The installer stops with an error", minLength: 3, maxLength: 100)]
    public string Subject { get; set; } = string.Empty;

    [InputLabel("Description", "What you did and what happened instead")]
    [RequiredInput(false)]
    [ModalTextInput("details", TextInputStyle.Paragraph, "The more details, the faster we can help.", maxLength: 2000)]
    public string? Details { get; set; }

    #endregion
}

public sealed class TicketCloseModal : IModal
{
    #region Properties

    public string Title => "Close the ticket?";

    [InputLabel("Reason (optional)", "Shown to the member and in the ticket log")]
    [RequiredInput(false)]
    [ModalTextInput("reason", TextInputStyle.Paragraph, "e.g. Solved, the login works again.", maxLength: 500)]
    public string? Reason { get; set; }

    #endregion
}

public sealed class TicketPanelModal : IModal
{
    #region Properties

    public const string DEFAULT_TEXT =
        "Something is not working for you? Click **Open a ticket**, pick a topic and tell us what happened. " +
        "A private channel only you and the team can see will open.\n\n" +
        "📥 Download & installation · 🔑 Login · 🧩 Feature problem · ❓ Something else\n\n" +
        "-# Ideas and bug reports that concern everyone are welcome in the forum, so others can vote on them.";

    public const string DEFAULT_TITLE = "🎫 Support";

    public const string TITLE_ID = "title";
    public const string TEXT_ID = "text";

    public string Title => "Ticket panel";

    [InputLabel("Title")]
    [ModalTextInput(TITLE_ID, TextInputStyle.Short, DEFAULT_TITLE, maxLength: 256)]
    public string PanelTitle { get; set; } = string.Empty;

    [InputLabel("Text (markdown)")]
    [ModalTextInput(TEXT_ID, TextInputStyle.Paragraph, "Click the button below to open a ticket.", maxLength: 3600)]
    public string Text { get; set; } = string.Empty;

    #endregion
}

public sealed class TicketMessageModal : IModal
{
    #region Properties

    public const string MESSAGE_ID = "message";

    public string Title => "Ticket message";

    [InputLabel("Message", "Placeholders: {user} {topic} {server}")]
    [ModalTextInput(MESSAGE_ID, TextInputStyle.Paragraph, "Hi {user}, a team member will be with you soon.", maxLength: 2000)]
    public string Message { get; set; } = string.Empty;

    #endregion
}
