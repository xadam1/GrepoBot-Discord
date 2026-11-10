using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using GrepoBot.Discord.Configuration;
using GrepoBot.Discord.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GrepoBot.Discord.Services;

/// <summary>
/// Answers members who tag the bot (<c>@GrepoBot</c> or its role) on the server with the same
/// "I'm only a bot" card as a direct message: help goes through a ticket, ideas and bugs into the forum.
/// Each member gets it at most once per cooldown, the team and ticket channels are left alone.
/// </summary>
public sealed class MentionReplyService
{
    #region Private Fields

    private static readonly TimeSpan _cooldown = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<ulong, DateTimeOffset> _lastReplies = new();
    private readonly DiscordSocketClient _client;
    private readonly StateStore _store;
    private readonly ulong _guildId;
    private readonly ILogger<MentionReplyService> _logger;

    #endregion

    #region Constructor

    public MentionReplyService(DiscordSocketClient client, StateStore store, IOptions<DiscordOptions> options, ILogger<MentionReplyService> logger)
    {
        _client = client;
        _store = store;
        _guildId = options.Value.GuildId;
        _logger = logger;
    }

    #endregion

    #region Public Methods

    public async Task HandleMessageAsync(SocketMessage message)
    {
        if (message is not SocketUserMessage userMessage
            || message.Author.IsBot
            || message.Author.IsWebhook
            || message.Source != MessageSource.User
            || message.Channel is not SocketGuildChannel channel
            || channel.Guild.Id != _guildId
            || message.Author is not SocketGuildUser author
            || author.GuildPermissions.ManageMessages)
        {
            return;
        }

        var self = channel.Guild.CurrentUser;
        var botRoleIds = self.Roles.Where(r => r.IsManaged).Select(r => r.Id).ToList();
        if (!IsBotTagged(message.Content, self.Id, botRoleIds))
        {
            return;
        }

        var settings = _store.Read(state => new
        {
            Tickets = state.Tickets.PanelChannelId,
            TicketCategory = state.Tickets.CategoryId,
            Forum = state.Forum.ChannelId,
            Rules = state.AutoReply.Rules.ToList(),
        });

        // A tagged question with an automatic answer gets that answer instead
        if (Logic.KeywordMatcher.FindMatch(settings.Rules, message.Content) is not null)
        {
            return;
        }

        // Inside a ticket the team answers
        if (settings.TicketCategory is { } ticketCategory && channel is INestedChannel { CategoryId: { } category } && category == ticketCategory)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (_lastReplies.TryGetValue(author.Id, out var last) && now - last < _cooldown)
        {
            return;
        }

        _lastReplies[author.Id] = now;

        var card = BotIntroCard.Build(
            MessageCards.Logo(channel.Guild),
            author.DisplayName,
            channel.Guild.Name,
            channel.Guild.Id,
            settings.Tickets,
            settings.Forum,
            isMention: true);

        await userMessage.ReplyAsync(components: card, allowedMentions: AllowedMentions.None, flags: MessageFlags.ComponentsV2);
        _logger.LogInformation("Answered a tag from {User} in #{Channel}.", author.Username, channel.Name);
    }

    /// <summary>
    /// Whether the text tags the bot itself or one of its roles. Only explicit tags count, so replying to
    /// a message of the bot (which notifies it too) does not trigger the answer.
    /// </summary>
    internal static bool IsBotTagged(string content, ulong botId, IEnumerable<ulong> botRoleIds)
    {
        if (string.IsNullOrEmpty(content))
        {
            return false;
        }

        if (content.Contains($"<@{botId}>") || content.Contains($"<@!{botId}>"))
        {
            return true;
        }

        return botRoleIds.Any(roleId => content.Contains($"<@&{roleId}>"));
    }

    #endregion
}
