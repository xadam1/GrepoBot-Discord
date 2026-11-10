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
/// Support tickets: a member clicks the panel button and fills in a form (topic, subject, description),
/// the bot creates a private channel that only the
/// member and the staff roles see. Staff can claim, close (the member loses access), reopen and delete it.
/// Every step is logged to the ticket log channel. No transcripts are kept.
/// </summary>
public sealed class TicketService
{
    #region Private Fields

    public const string OPEN_BUTTON_ID = "ticket-open";
    public const string CLOSE_BUTTON_ID = "ticket-close";
    public const string CLAIM_BUTTON_ID = "ticket-claim";
    public const string REOPEN_BUTTON_ID = "ticket-reopen";
    public const string DELETE_BUTTON_ID = "ticket-delete";

    private static readonly TimeSpan _deleteDelay = TimeSpan.FromSeconds(5);

    private static readonly OverwritePermissions _memberPermissions = new(
        viewChannel: PermValue.Allow,
        sendMessages: PermValue.Allow,
        readMessageHistory: PermValue.Allow,
        attachFiles: PermValue.Allow,
        embedLinks: PermValue.Allow,
        addReactions: PermValue.Allow,
        useApplicationCommands: PermValue.Allow);

    private static readonly OverwritePermissions _staffPermissions = _memberPermissions.Modify(
        manageMessages: PermValue.Allow);

    private static readonly OverwritePermissions _botPermissions = _staffPermissions.Modify(
        manageChannel: PermValue.Allow,
        manageRoles: PermValue.Allow);

    private readonly StateStore _store;
    private readonly ulong _guildId;
    private readonly SemaphoreSlim _openLock = new(1, 1);
    private readonly ILogger<TicketService> _logger;

    #endregion

    #region Constructor

    public TicketService(StateStore store, IOptions<DiscordOptions> options, ILogger<TicketService> logger)
    {
        _store = store;
        _guildId = options.Value.GuildId;
        _logger = logger;
    }

    #endregion

    #region Public Methods

    /// <summary>Returns a copy of the ticket that lives in <paramref name="channelId"/>, or null.</summary>
    public TicketRecord? GetTicket(ulong channelId)
    {
        return _store.Read(state => state.Tickets.Tickets.FirstOrDefault(t => t.ChannelId == channelId)?.Clone());
    }

    /// <summary>Staff = one of the ticket staff roles, or the owner and the Moderator / GrepoBot Team roles.</summary>
    public bool IsStaff(SocketGuildUser user)
    {
        if (StaffAccess.IsStaff(user.Guild.OwnerId == user.Id, user.Roles.Select(r => r.Name)))
        {
            return true;
        }

        var staff = _store.Read(state => state.Tickets.StaffRoleIds.ToHashSet());
        return user.Roles.Any(r => staff.Contains(r.Id));
    }

    /// <summary>Returns what is missing before members can open tickets, or null when it works.</summary>
    public string? GetSetupProblem(SocketGuild guild)
    {
        var categoryId = _store.Read(state => state.Tickets.CategoryId);
        if (categoryId is null || guild.GetCategoryChannel(categoryId.Value) is null)
        {
            return "Tickets are not set up yet. Please tell a moderator.";
        }

        return null;
    }

    /// <summary>Open tickets of <paramref name="userId"/>, for the "you already have a ticket" check.</summary>
    public List<TicketRecord> GetOpenTickets(ulong userId)
    {
        return _store.Read(state => state.Tickets.Tickets
            .Where(t => t.OwnerId == userId && !t.IsClosed)
            .Select(t => t.Clone())
            .ToList());
    }

    /// <summary>Returns the text for a member who has reached the open ticket limit, or null when they may open one.</summary>
    public string? GetLimitProblem(SocketGuildUser user)
    {
        var max = _store.Read(state => state.Tickets.MaxOpenPerUser);
        var open = GetOpenTickets(user.Id);
        if (max <= 0 || open.Count < max)
        {
            return null;
        }

        return "You already have an open ticket: " + string.Join(", ", open.Select(t => $"<#{t.ChannelId}>")) +
            "\nPlease continue there, the team will answer you as soon as possible.";
    }

    /// <summary>Creates the ticket channel. Returns the channel, or throws <see cref="TicketException"/>.</summary>
    public async Task<ITextChannel> OpenAsync(SocketGuildUser user, TicketTopic ticketTopic, string subject, string? details)
    {
        var topic = ticketTopic.Display;
        await _openLock.WaitAsync();
        try
        {
            if (GetSetupProblem(user.Guild) is { } setup)
            {
                throw new TicketException(setup);
            }

            if (GetLimitProblem(user) is { } limit)
            {
                throw new TicketException(limit);
            }

            var settings = _store.Read(state => new
            {
                CategoryId = state.Tickets.CategoryId!.Value,
                Staff = state.Tickets.StaffRoleIds.ToList(),
                state.Tickets.PingStaff,
                state.Tickets.Message,
            });

            var number = _store.Update(state => ++state.Tickets.Counter);
            var record = new TicketRecord
            {
                Number = number,
                OwnerId = user.Id,
                OwnerName = user.Username,
                Topic = topic,
                Subject = subject,
                OpenedAt = DateTimeOffset.UtcNow,
            };

            var channel = await user.Guild.CreateTextChannelAsync(TicketNaming.OpenChannelName(number, user.Username), properties =>
            {
                properties.CategoryId = settings.CategoryId;
                properties.Topic = $"Ticket #{TicketNaming.FormatNumber(number)} · {topic} · opened by {user.Username} ({user.Id})";
                properties.PermissionOverwrites = BuildOverwrites(user.Guild, settings.Staff, record, withMembers: true);
            }, new RequestOptions { AuditLogReason = $"Ticket opened by {user.Username}" });

            record.ChannelId = channel.Id;
            _store.Update(state => state.Tickets.Tickets.Add(record.Clone()));

            var text = TextTemplate.Render(settings.Message, new Dictionary<string, string>
            {
                ["user"] = user.Mention,
                ["topic"] = topic,
                ["server"] = user.Guild.Name,
            });

            // The member's own words are the big part, so nobody has to ask for them again.
            var request = new EmbedBuilder()
                .WithColor(BotBranding.Accent)
                .WithAuthor($"Ticket #{TicketNaming.FormatNumber(number)} · {topic}", user.GetDisplayAvatarUrl())
                .WithTitle(Limit(subject, 256))
                .WithDescription(string.IsNullOrWhiteSpace(details) ? "*No description given.*" : Limit(details, 4000))
                .WithFooter($"Opened by {user.DisplayName}")
                .WithCurrentTimestamp()
                .Build();

            var info = new EmbedBuilder()
                .WithColor(BotBranding.Navy)
                .WithDescription(text)
                .Build();

            var staffMentions = settings.PingStaff
                ? string.Concat(settings.Staff.Select(id => $" <@&{id}>"))
                : string.Empty;

            var control = await channel.SendMessageAsync(
                user.Mention + staffMentions,
                embeds: [request, info],
                components: BuildControlButtons(null),
                allowedMentions: new AllowedMentions { UserIds = [user.Id], RoleIds = settings.PingStaff ? settings.Staff : [] });

            await TryAsync(() => control.PinAsync(), "pin the ticket message");
            _store.Update(state => Find(state, channel.Id)?.ControlMessageId = control.Id);

            await LogAsync(user.Guild, new EmbedBuilder()
                .WithColor(BotBranding.Success)
                .WithTitle($"Ticket #{TicketNaming.FormatNumber(number)} opened")
                .AddField("Ticket", ChannelText(channel), inline: true)
                .AddField("By", user.Mention, inline: true)
                .AddField("Topic", topic, inline: true)
                .AddField("Subject", Limit(subject, 1024)));

            _logger.LogInformation("{User} opened ticket #{Number} ({Topic}).", user.Username, number, topic);
            return channel;
        }
        finally
        {
            _openLock.Release();
        }
    }

    /// <summary>Marks the ticket as handled by <paramref name="staff"/>. Returns the text for the clicker.</summary>
    public async Task<string> ClaimAsync(SocketGuildUser staff, ITextChannel channel)
    {
        var ticket = RequireTicket(channel);
        RequireStaff(staff);

        if (ticket.ClaimedById is { } claimedBy)
        {
            return claimedBy == staff.Id
                ? "You have already claimed this ticket."
                : $"<@{claimedBy}> has already claimed this ticket.";
        }

        _store.Update(state => Find(state, channel.Id)?.ClaimedById = staff.Id);
        await UpdateControlMessageAsync(channel, ticket.ControlMessageId, staff);
        await channel.SendMessageAsync(
            embed: new EmbedBuilder()
                .WithColor(BotBranding.Accent)
                .WithDescription($"🙋 {staff.Mention} will take care of this ticket.")
                .Build(),
            allowedMentions: AllowedMentions.None);

        return "✅ You claimed this ticket.";
    }

    /// <summary>
    /// Closes the ticket: the owner and added members lose access, the channel is renamed to
    /// <c>closed-…</c> and staff get Reopen / Delete buttons.
    /// </summary>
    public async Task CloseAsync(SocketGuildUser closer, ITextChannel channel, string? reason)
    {
        var ticket = RequireTicket(channel);
        if (ticket.IsClosed)
        {
            throw new TicketException("This ticket is already closed.");
        }

        if (ticket.OwnerId != closer.Id && !IsStaff(closer))
        {
            throw new TicketException("Only the ticket owner or the team can close this ticket.");
        }

        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var closedAt = DateTimeOffset.UtcNow;
        _store.Update(state =>
        {
            if (Find(state, channel.Id) is { } stored)
            {
                stored.IsClosed = true;
                stored.ClosedAt = closedAt;
                stored.ClosedById = closer.Id;
                stored.CloseReason = reason;
            }
        });

        var staffRoles = _store.Read(state => state.Tickets.StaffRoleIds.ToList());
        ticket.IsClosed = true;
        await channel.ModifyAsync(
            properties => properties.PermissionOverwrites = BuildOverwrites(closer.Guild, staffRoles, ticket, withMembers: false),
            new RequestOptions { AuditLogReason = $"Ticket closed by {closer.Username}" });
        await TryRenameAsync(channel, TicketNaming.ClosedChannelName(ticket.Number, ticket.OwnerName));

        var embed = new EmbedBuilder()
            .WithColor(BotBranding.Warning)
            .WithDescription($"🔒 Ticket closed by {closer.Mention}." + (reason is null ? string.Empty : $"\n**Reason:** {Limit(reason, 1000)}"))
            .WithFooter("Team: reopen the ticket, or delete it when it is done.")
            .Build();
        var buttons = new ComponentBuilder()
            .WithButton("Reopen", REOPEN_BUTTON_ID, ButtonStyle.Secondary, new Emoji("🔓"))
            .WithButton("Delete", DELETE_BUTTON_ID, ButtonStyle.Danger, new Emoji("🗑️"))
            .Build();
        await channel.SendMessageAsync(embed: embed, components: buttons, allowedMentions: AllowedMentions.None);

        await NotifyOwnerAsync(closer.Guild, ticket, closer, reason);
        await LogAsync(closer.Guild, new EmbedBuilder()
            .WithColor(BotBranding.Warning)
            .WithTitle($"Ticket #{TicketNaming.FormatNumber(ticket.Number)} closed")
            .AddField("Ticket", ChannelText(channel), inline: true)
            .AddField("Owner", $"<@{ticket.OwnerId}>", inline: true)
            .AddField("Closed by", closer.Mention, inline: true)
            .AddField("Topic", ticket.Topic, inline: true)
            .AddField("Claimed by", ticket.ClaimedById is { } c ? $"<@{c}>" : "nobody", inline: true)
            .AddField("Open for", FormatDuration(closedAt - ticket.OpenedAt), inline: true)
            .AddField("Subject", Limit(ticket.Subject, 1024))
            .AddField("Reason", reason is null ? "—" : Limit(reason, 1024)));

        _logger.LogInformation("{User} closed ticket #{Number}.", closer.Username, ticket.Number);
    }

    /// <summary>Gives the owner and added members their access back.</summary>
    public async Task ReopenAsync(SocketGuildUser staff, ITextChannel channel)
    {
        var ticket = RequireTicket(channel);
        RequireStaff(staff);
        if (!ticket.IsClosed)
        {
            throw new TicketException("This ticket is open.");
        }

        _store.Update(state =>
        {
            if (Find(state, channel.Id) is { } stored)
            {
                stored.IsClosed = false;
                stored.ClosedAt = null;
                stored.ClosedById = null;
                stored.CloseReason = null;
            }
        });

        var staffRoles = _store.Read(state => state.Tickets.StaffRoleIds.ToList());
        ticket.IsClosed = false;
        await channel.ModifyAsync(
            properties => properties.PermissionOverwrites = BuildOverwrites(staff.Guild, staffRoles, ticket, withMembers: true),
            new RequestOptions { AuditLogReason = $"Ticket reopened by {staff.Username}" });
        await TryRenameAsync(channel, TicketNaming.OpenChannelName(ticket.Number, ticket.OwnerName));

        await channel.SendMessageAsync(
            $"<@{ticket.OwnerId}>",
            embed: new EmbedBuilder()
                .WithColor(BotBranding.Success)
                .WithDescription($"🔓 Ticket reopened by {staff.Mention}.")
                .Build(),
            allowedMentions: new AllowedMentions { UserIds = [ticket.OwnerId] });

        await LogAsync(staff.Guild, new EmbedBuilder()
            .WithColor(BotBranding.Success)
            .WithTitle($"Ticket #{TicketNaming.FormatNumber(ticket.Number)} reopened")
            .AddField("Ticket", ChannelText(channel), inline: true)
            .AddField("By", staff.Mention, inline: true));
    }

    /// <summary>Deletes the ticket channel after a short delay. The caller has already told the user.</summary>
    public async Task DeleteAsync(SocketGuildUser staff, ITextChannel channel)
    {
        var ticket = RequireTicket(channel);
        RequireStaff(staff);

        await Task.Delay(_deleteDelay);
        await channel.DeleteAsync(new RequestOptions { AuditLogReason = $"Ticket deleted by {staff.Username}" });
        Forget(channel.Id);

        await LogAsync(staff.Guild, new EmbedBuilder()
            .WithColor(BotBranding.Danger)
            .WithTitle($"Ticket #{TicketNaming.FormatNumber(ticket.Number)} deleted")
            .AddField("Ticket", $"#{channel.Name}", inline: true)
            .AddField("Owner", $"<@{ticket.OwnerId}>", inline: true)
            .AddField("Deleted by", staff.Mention, inline: true)
            .AddField("Topic", ticket.Topic, inline: true)
            .AddField("Subject", Limit(ticket.Subject, 1024)));

        _logger.LogInformation("{User} deleted ticket #{Number}.", staff.Username, ticket.Number);
    }

    /// <summary>Gives another member access to the ticket.</summary>
    public async Task AddMemberAsync(SocketGuildUser staff, ITextChannel channel, IGuildUser member)
    {
        var ticket = RequireTicket(channel);
        RequireStaff(staff);

        if (member.Id == ticket.OwnerId || ticket.AddedUserIds.Contains(member.Id))
        {
            throw new TicketException($"{member.Mention} is already in this ticket.");
        }

        _store.Update(state => Find(state, channel.Id)?.AddedUserIds.Add(member.Id));
        if (!ticket.IsClosed)
        {
            await channel.AddPermissionOverwriteAsync(member, _memberPermissions, new RequestOptions { AuditLogReason = $"Added to the ticket by {staff.Username}" });
        }

        await channel.SendMessageAsync($"➕ {staff.Mention} added {member.Mention} to the ticket.", allowedMentions: new AllowedMentions { UserIds = [member.Id] });
    }

    /// <summary>Takes the access of a member added with <see cref="AddMemberAsync"/> away again.</summary>
    public async Task RemoveMemberAsync(SocketGuildUser staff, ITextChannel channel, IGuildUser member)
    {
        var ticket = RequireTicket(channel);
        RequireStaff(staff);

        if (member.Id == ticket.OwnerId)
        {
            throw new TicketException("The owner cannot be removed. Close the ticket instead.");
        }

        if (!ticket.AddedUserIds.Contains(member.Id))
        {
            throw new TicketException($"{member.Mention} was not added to this ticket.");
        }

        _store.Update(state => Find(state, channel.Id)?.AddedUserIds.Remove(member.Id));
        await channel.RemovePermissionOverwriteAsync(member, new RequestOptions { AuditLogReason = $"Removed from the ticket by {staff.Username}" });
        await channel.SendMessageAsync($"➖ {staff.Mention} removed {member.Mention} from the ticket.", allowedMentions: AllowedMentions.None);
    }

    /// <summary>Forgets the ticket when its channel was deleted by hand.</summary>
    public Task OnChannelDestroyedAsync(SocketChannel channel)
    {
        if (channel is SocketGuildChannel guildChannel && guildChannel.Guild.Id == _guildId)
        {
            Forget(channel.Id);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Posts a panel with one button per ticket topic, or updates the bot's existing panel in that channel
    /// (found among the last 50 messages), so changing the topics never needs deleting the old panel.
    /// Returns the message and whether it was updated.
    /// </summary>
    public async Task<(IUserMessage Message, bool Updated)> PublishPanelAsync(ITextChannel channel, string title, string text)
    {
        _store.Update(state =>
        {
            state.Tickets.PanelTitle = title;
            state.Tickets.PanelDescription = text;
            state.Tickets.PanelChannelId = channel.Id;
        });

        var existing = await FindPanelAsync(channel);
        var posted = await MessageCards.UpsertAsync(channel, existing, BuildPanel(channel.Guild, title, text));
        return (posted, posted.Id == existing?.Id);
    }

    #endregion

    #region Private Methods

    /// <summary>The ticket panel card: logo, text and the button that opens a ticket.</summary>
    private static MessageComponent BuildPanel(IGuild guild, string title, string text)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(BotBranding.Accent)
            .WithSection(MessageCards.WithLogo(MessageCards.Logo(guild), "# " + title, text))
            .WithSeparator(SeparatorSpacingSize.Small, true)
            .WithSection(new SectionBuilder(
                new ButtonBuilder("Open a ticket", OPEN_BUTTON_ID, ButtonStyle.Primary, emote: new Emoji("🎫")),
                [new TextDisplayBuilder("**Need help?**\nOnly you and the team can see your ticket.")]))
            .WithTextDisplay("-# GrepoBot support · quick questions are welcome in the help channel too");

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    private static async Task<IUserMessage?> FindPanelAsync(ITextChannel channel)
    {
        var botId = ((SocketGuild)channel.Guild).CurrentUser.Id;
        var messages = await channel.GetMessagesAsync(50).FlattenAsync();
        return messages
            .OfType<IUserMessage>()
            .Where(m => m.Author.Id == botId)
            .FirstOrDefault(m => MessageCards.Buttons(m.Components)
                .Any(b => b.CustomId?.StartsWith(OPEN_BUTTON_ID, StringComparison.Ordinal) == true));
    }

    private TicketRecord RequireTicket(IChannel channel)
    {
        return GetTicket(channel.Id) ?? throw new TicketException("This is not a ticket channel.");
    }

    private void RequireStaff(SocketGuildUser user)
    {
        if (!IsStaff(user))
        {
            throw new TicketException("Only the team can do this.");
        }
    }

    private void Forget(ulong channelId)
    {
        _store.Update(state => state.Tickets.Tickets.RemoveAll(t => t.ChannelId == channelId));
    }

    private static TicketRecord? Find(BotState state, ulong channelId)
    {
        return state.Tickets.Tickets.FirstOrDefault(t => t.ChannelId == channelId);
    }

    /// <summary>Hidden for everyone, visible for staff and the bot, and for the owner and added members while open.</summary>
    private static List<Overwrite> BuildOverwrites(SocketGuild guild, IEnumerable<ulong> staffRoleIds, TicketRecord ticket, bool withMembers)
    {
        var overwrites = new List<Overwrite>
        {
            new(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Deny)),
            new(guild.CurrentUser.Id, PermissionTarget.User, _botPermissions),
        };

        foreach (var roleId in staffRoleIds.Where(id => guild.GetRole(id) is not null))
        {
            overwrites.Add(new Overwrite(roleId, PermissionTarget.Role, _staffPermissions));
        }

        if (withMembers)
        {
            foreach (var userId in ticket.AddedUserIds.Prepend(ticket.OwnerId).Distinct())
            {
                overwrites.Add(new Overwrite(userId, PermissionTarget.User, _memberPermissions));
            }
        }

        return overwrites;
    }

    private static MessageComponent BuildControlButtons(IUser? claimedBy)
    {
        var buttons = new ComponentBuilder()
            .WithButton("Close", CLOSE_BUTTON_ID, ButtonStyle.Danger, new Emoji("🔒"));

        if (claimedBy is null)
        {
            buttons.WithButton("Claim", CLAIM_BUTTON_ID, ButtonStyle.Secondary, new Emoji("🙋"));
        }
        else
        {
            var label = $"Claimed by {(claimedBy as IGuildUser)?.DisplayName ?? claimedBy.Username}";
            buttons.WithButton(Limit(label, 80), CLAIM_BUTTON_ID, ButtonStyle.Secondary, new Emoji("🙋"), disabled: true);
        }

        return buttons.Build();
    }

    private async Task UpdateControlMessageAsync(ITextChannel channel, ulong? messageId, IUser claimedBy)
    {
        if (messageId is null || await channel.GetMessageAsync(messageId.Value) is not IUserMessage message)
        {
            return;
        }

        await TryAsync(() => message.ModifyAsync(p => p.Components = BuildControlButtons(claimedBy)), "update the ticket buttons");
    }

    private async Task NotifyOwnerAsync(SocketGuild guild, TicketRecord ticket, IUser closer, string? reason)
    {
        if (ticket.OwnerId == closer.Id || guild.GetUser(ticket.OwnerId) is not { } owner)
        {
            return;
        }

        var embed = new EmbedBuilder()
            .WithColor(BotBranding.Accent)
            .WithTitle($"Your ticket #{TicketNaming.FormatNumber(ticket.Number)} was closed")
            .WithDescription($"**{guild.Name}** · {ticket.Topic}\n**Subject:** {Limit(ticket.Subject, 500)}" +
                (reason is null ? string.Empty : $"\n**Reason:** {Limit(reason, 1000)}") +
                "\n\nNeed more help? Just open a new ticket.")
            .WithFooter($"Closed by {closer.Username}")
            .WithCurrentTimestamp()
            .Build();

        // Members can turn off DMs from server members, that is fine.
        await TryAsync(() => owner.SendMessageAsync(embed: embed), "DM the ticket owner");
    }

    private async Task LogAsync(SocketGuild guild, EmbedBuilder embed)
    {
        var logChannelId = _store.Read(state => state.Tickets.LogChannelId);
        if (logChannelId is null || guild.GetTextChannel(logChannelId.Value) is not { } logChannel)
        {
            return;
        }

        await TryAsync(() => logChannel.SendMessageAsync(embed: embed.WithCurrentTimestamp().Build(), allowedMentions: AllowedMentions.None), "write the ticket log");
    }

    /// <summary>Discord allows two renames per channel in 10 minutes. Skip the rename rather than block for minutes.</summary>
    private async Task TryRenameAsync(ITextChannel channel, string name)
    {
        try
        {
            await channel.ModifyAsync(p => p.Name = name, new RequestOptions { RetryMode = RetryMode.AlwaysFail });
        }
        catch (Exception ex) when (ex is RateLimitedException or HttpException or TimeoutException)
        {
            _logger.LogInformation("Renaming #{Channel} to {Name} skipped: {Reason}", channel.Name, name, ex.Message);
        }
    }

    private async Task TryAsync(Func<Task> action, string what)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not {What}.", what);
        }
    }


    /// <summary>The mention shows "unknown" once the channel is deleted, the name stays readable in the log.</summary>
    private static string ChannelText(IChannel channel)
    {
        return $"<#{channel.Id}> (`{channel.Name}`)";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
        {
            return $"{(int)duration.TotalDays}d {duration.Hours}h";
        }

        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        return $"{Math.Max(1, (int)duration.TotalMinutes)}m";
    }

    private static string Limit(string text, int max)
    {
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }

    #endregion
}

/// <summary>A ticket action that is not possible right now. The message is shown to the member as is.</summary>
public sealed class TicketException(string message) : Exception(message);
