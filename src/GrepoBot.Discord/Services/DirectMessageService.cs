using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using GrepoBot.Discord.Configuration;
using GrepoBot.Discord.Logic;
using GrepoBot.Discord.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GrepoBot.Discord.Services;

/// <summary>
/// Answers direct messages to the bot politely: it is only a bot, 1-on-1 help goes through a ticket,
/// ideas and bugs into the forum. Each member gets the answer at most once per cooldown.
/// </summary>
public sealed class DirectMessageService
{
    #region Private Fields

    private static readonly TimeSpan _cooldown = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<ulong, DateTimeOffset> _lastReplies = new();
    private readonly DiscordSocketClient _client;
    private readonly StateStore _store;
    private readonly ulong _guildId;
    private readonly ILogger<DirectMessageService> _logger;

    #endregion

    #region Constructor

    public DirectMessageService(DiscordSocketClient client, StateStore store, IOptions<DiscordOptions> options, ILogger<DirectMessageService> logger)
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
        if (message.Channel is not IDMChannel
            || message.Author.IsBot
            || message.Author.IsWebhook
            || message.Source != MessageSource.User)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (_lastReplies.TryGetValue(message.Author.Id, out var last) && now - last < _cooldown)
        {
            return;
        }

        _lastReplies[message.Author.Id] = now;

        var guild = _client.GetGuild(_guildId);
        var links = _store.Read(state => new { Tickets = state.Tickets.PanelChannelId, Forum = state.Forum.ChannelId });
        var name = (message.Author as IGuildUser)?.DisplayName ?? message.Author.GlobalName ?? message.Author.Username;

        var embed = new EmbedBuilder()
            .WithColor(BotBranding.Accent)
            .WithTitle($"Hi {name}, thank you for your message! 👋")
            .WithDescription(BuildText(guild?.Name ?? "GrepoBot", links.Tickets, links.Forum))
            .WithFooter("This is an automatic reply, messages sent here are not read.")
            .Build();

        await message.Channel.SendMessageAsync(embed: embed, allowedMentions: AllowedMentions.None);
        _logger.LogInformation("Answered a direct message from {User}.", message.Author.Username);
    }

    #endregion

    #region Private Methods

    internal static string BuildText(string serverName, ulong? ticketChannelId, ulong? forumChannelId)
    {
        var tickets = ticketChannelId is { } t ? $"<#{t}>" : "the support channel";
        var forum = forumChannelId is { } f ? $"<#{f}>" : "the feedback forum";

        return
            $"I'm the helper bot of the **{serverName}** Discord server, so I can't read or answer messages here. " +
            "But we'd love to help you:\n\n" +
            $"🎫 **Need 1-on-1 help?** Open a ticket in {tickets}. Only you and the team can see it, and we'll answer you there as soon as we can.\n\n" +
            $"💡 **Have an idea or found a bug?** Create a post in {forum}, so others can vote on it and the team can track it.\n\n" +
            "💬 **Just a quick question?** Feel free to ask in the server chat, the community is happy to help.\n\n" +
            "Thanks for using GrepoBot! 🙂";
    }

    #endregion
}
