using Discord;
using Discord.WebSocket;
using GrepoBot.Discord.Logic;
using GrepoBot.Discord.State;
using Microsoft.Extensions.Logging;

namespace GrepoBot.Discord.Services;

/// <summary>
/// Keeps one status message in the bot-status channel: whether the GrepoBot app works right now.
/// The message is edited in place (no notification spam) and the channel name starts with the status
/// emoji, so the state is visible in the channel list. <paramref name="announce"/> on <see cref="SetAsync"/>
/// additionally posts a short new message, which notifies followers of the channel.
/// </summary>
public sealed class AppStatusService
{
    #region Private Fields

    private readonly StateStore _store;
    private readonly ILogger<AppStatusService> _logger;

    #endregion

    #region Constructor

    public AppStatusService(StateStore store, ILogger<AppStatusService> logger)
    {
        _store = store;
        _logger = logger;
    }

    #endregion

    #region Public Methods

    /// <summary>Sets the status and updates the status card and the channel name. Returns the status card.</summary>
    public async Task<IUserMessage> SetAsync(ITextChannel channel, AppState state, string? note, bool announce)
    {
        _store.Update(s =>
        {
            s.Status.ChannelId = channel.Id;
            s.Status.State = state;
            s.Status.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            s.Status.UpdatedAt = DateTimeOffset.UtcNow;
        });

        var message = await PublishAsync(channel);

        if (announce)
        {
            await channel.SendMessageAsync(components: BuildAnnouncement(state, note), allowedMentions: AllowedMentions.None, flags: MessageFlags.ComponentsV2);
        }

        await RenameChannelAsync(channel, state);
        return message;
    }

    /// <summary>Posts the status card with the saved status, or updates the existing one.</summary>
    public async Task<IUserMessage> PublishAsync(ITextChannel channel)
    {
        var saved = _store.Read(s => new { s.Status.MessageId, s.Status.ChannelId, s.Status.State, s.Status.Note, s.Status.UpdatedAt });
        var card = BuildCard(channel.Guild, saved.State, saved.Note, saved.UpdatedAt ?? DateTimeOffset.UtcNow);

        IUserMessage? existing = null;
        if (saved.ChannelId == channel.Id && saved.MessageId is { } messageId)
        {
            existing = await channel.GetMessageAsync(messageId) as IUserMessage;
        }

        var message = await MessageCards.UpsertAsync(channel, existing, card);
        if (message.Id != existing?.Id)
        {
            await TryPinAsync(message);
            _store.Update(s =>
            {
                s.Status.ChannelId = channel.Id;
                s.Status.MessageId = message.Id;
            });
        }

        await RenameChannelAsync(channel, saved.State);
        return message;
    }

    #endregion

    #region Private Methods

    private static MessageComponent BuildCard(IGuild guild, AppState state, string? note, DateTimeOffset updatedAt)
    {
        var look = AppStatus.Look(state);
        var time = updatedAt.ToUnixTimeSeconds();
        var container = new ContainerBuilder()
            .WithAccentColor(new Color(look.Color))
            .WithSection(MessageCards.WithLogo(MessageCards.Logo(guild),
                $"# {look.Emoji} {look.Headline}",
                look.DefaultText));

        if (!string.IsNullOrWhiteSpace(note))
        {
            container.WithSeparator(SeparatorSpacingSize.Small, true)
                .WithTextDisplay("**📝 Note from the team**\n" + note.Trim());
        }

        container.WithSeparator(SeparatorSpacingSize.Small, true)
            .WithTextDisplay($"**Status:** {look.Emoji} {look.Label}\n**Last update:** <t:{time}:f> (<t:{time}:R>)")
            .WithTextDisplay("-# Something else broken? Open a ticket. This message is kept up to date by the team.");

        var buttons = new ActionRowBuilder()
            .WithButton(MessageCards.Link("Changelog", "https://grepobot.net/changelog/", "📜"))
            .WithButton(MessageCards.Link("Website", "https://grepobot.net/", "🌐"));
        var tickets = ((SocketGuild)guild).TextChannels.FirstOrDefault(c => ServerLayout.Matches(c.Name, ServerLayout.Channel("tickets").Aliases));
        if (tickets is not null)
        {
            buttons.WithButton(MessageCards.Link("Get help", $"https://discord.com/channels/{guild.Id}/{tickets.Id}", "🆘"));
        }

        container.WithActionRow(buttons);
        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    private static MessageComponent BuildAnnouncement(AppState state, string? note)
    {
        var look = AppStatus.Look(state);
        var container = new ContainerBuilder()
            .WithAccentColor(new Color(look.Color))
            .WithTextDisplay($"## {look.Emoji} {look.Headline}\n{Text(state, note)}");
        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    private static string Text(AppState state, string? note)
    {
        return string.IsNullOrWhiteSpace(note) ? AppStatus.Look(state).DefaultText : note.Trim();
    }

    /// <summary>Discord allows two channel renames per 10 minutes, a skipped rename only leaves the old emoji.</summary>
    private async Task RenameChannelAsync(ITextChannel channel, AppState state)
    {
        var name = ServerLayout.StatusChannelName(AppStatus.Look(state).Emoji);
        if (channel.Name == name)
        {
            return;
        }

        try
        {
            await channel.ModifyAsync(p => p.Name = name, new RequestOptions { RetryMode = RetryMode.AlwaysFail });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Renaming the status channel failed (Discord allows two renames per 10 minutes).");
        }
    }

    private async Task TryPinAsync(IUserMessage message)
    {
        try
        {
            await message.PinAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pinning the status message failed.");
        }
    }

    #endregion
}
