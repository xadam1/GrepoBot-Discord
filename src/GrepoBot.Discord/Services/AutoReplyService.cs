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
/// Answers common questions: when a member's message matches an auto-reply rule, the bot replies
/// with the predefined answer. Each rule has a cooldown per channel so a busy chat is not flooded.
/// </summary>
public sealed class AutoReplyService
{
    #region Private Fields

    private readonly ConcurrentDictionary<(string Rule, ulong Channel), DateTimeOffset> _lastReplies = new();
    private readonly StateStore _store;
    private readonly ulong _guildId;
    private readonly ILogger<AutoReplyService> _logger;

    #endregion

    #region Constructor

    public AutoReplyService(StateStore store, IOptions<DiscordOptions> options, ILogger<AutoReplyService> logger)
    {
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
            || message.Channel is not SocketGuildChannel channel
            || channel.Guild.Id != _guildId
            || string.IsNullOrWhiteSpace(message.Content))
        {
            return;
        }

        var settings = _store.Read(state => new
        {
            Rules = state.AutoReply.Rules.Where(r => r.Enabled).ToList(),
            Ignored = state.AutoReply.IgnoredChannelIds.ToHashSet(),
            state.AutoReply.IgnoreStaff,
            state.AutoReply.Footer,
        });

        if (settings.Rules.Count == 0 || IsIgnoredChannel(channel, settings.Ignored))
        {
            return;
        }

        if (settings.IgnoreStaff && message.Author is SocketGuildUser { GuildPermissions.ManageMessages: true })
        {
            return;
        }

        var rule = KeywordMatcher.FindMatch(settings.Rules, message.Content);
        if (rule is null || IsOnCooldown(rule, channel.Id))
        {
            return;
        }

        var response = TextTemplate.Render(rule.Response, new Dictionary<string, string>
        {
            ["user"] = message.Author.Mention,
            ["username"] = message.Author.GlobalName ?? message.Author.Username,
            ["server"] = channel.Guild.Name,
        });

        var embed = new EmbedBuilder()
            .WithColor(BotBranding.Accent)
            .WithDescription(response)
            .WithFooter(settings.Footer)
            .Build();

        await userMessage.ReplyAsync(embed: embed, allowedMentions: AllowedMentions.None);

        _store.Update(state =>
        {
            var stored = state.AutoReply.Rules.FirstOrDefault(r => r.Name == rule.Name);
            if (stored is not null)
            {
                stored.TimesUsed++;
            }
        });

        _logger.LogInformation("Auto-reply '{Rule}' answered {User} in #{Channel}.", rule.Name, message.Author.Username, channel.Name);
    }

    #endregion

    #region Private Methods

    private static bool IsIgnoredChannel(SocketGuildChannel channel, HashSet<ulong> ignored)
    {
        if (ignored.Contains(channel.Id))
        {
            return true;
        }

        return channel switch
        {
            SocketThreadChannel { ParentChannel: SocketGuildChannel parent } =>
                ignored.Contains(parent.Id) || (parent is INestedChannel { CategoryId: { } parentCategory } && ignored.Contains(parentCategory)),
            INestedChannel { CategoryId: { } category } => ignored.Contains(category),
            _ => false,
        };
    }

    private bool IsOnCooldown(AutoReplyRule rule, ulong channelId)
    {
        var key = (rule.Name, channelId);
        var now = DateTimeOffset.UtcNow;

        if (_lastReplies.TryGetValue(key, out var last) && now - last < TimeSpan.FromSeconds(rule.CooldownSeconds))
        {
            return true;
        }

        _lastReplies[key] = now;
        return false;
    }

    #endregion
}
