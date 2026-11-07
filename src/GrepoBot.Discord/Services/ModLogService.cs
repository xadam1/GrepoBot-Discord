using Discord;
using Discord.WebSocket;
using GrepoBot.Discord.Configuration;
using GrepoBot.Discord.Logic;
using GrepoBot.Discord.State;
using Microsoft.Extensions.Options;

namespace GrepoBot.Discord.Services;

/// <summary>
/// Writes moderation events (deleted and edited messages, joins, leaves, bans) to the log channel.
/// Deleted message content is only known for messages the bot saw since its last start.
/// </summary>
public sealed class ModLogService
{
    #region Private Fields

    private readonly DiscordSocketClient _client;
    private readonly StateStore _store;
    private readonly PurgeService _purge;
    private readonly ulong _guildId;

    #endregion

    #region Constructor

    public ModLogService(DiscordSocketClient client, StateStore store, PurgeService purge, IOptions<DiscordOptions> options)
    {
        _client = client;
        _store = store;
        _purge = purge;
        _guildId = options.Value.GuildId;
    }

    #endregion

    #region Public Methods

    public Task OnUserJoinedAsync(SocketGuildUser user)
    {
        var accountAge = DateTimeOffset.UtcNow - user.CreatedAt;
        var embed = UserEmbed(user, "Member joined", BotBranding.Success)
            .AddField("Account created", $"<t:{user.CreatedAt.ToUnixTimeSeconds()}:R>", inline: true)
            .AddField("Members", user.Guild.MemberCount.ToString("N0"), inline: true);

        if (accountAge < TimeSpan.FromDays(7))
        {
            embed.WithColor(BotBranding.Warning).WithDescription("⚠️ New account (younger than 7 days).");
        }

        return SendAsync(user.Guild.Id, embed);
    }

    public Task OnUserLeftAsync(SocketGuild guild, SocketUser user)
    {
        var embed = UserEmbed(user, "Member left", BotBranding.Navy);
        if (user is SocketGuildUser { JoinedAt: { } joinedAt })
        {
            embed.AddField("Joined", $"<t:{joinedAt.ToUnixTimeSeconds()}:R>", inline: true);
        }

        return SendAsync(guild.Id, embed);
    }

    public Task OnUserBannedAsync(SocketUser user, SocketGuild guild)
    {
        return SendAsync(guild.Id, UserEmbed(user, "Member banned", BotBranding.Danger));
    }

    public Task OnUserUnbannedAsync(SocketUser user, SocketGuild guild)
    {
        return SendAsync(guild.Id, UserEmbed(user, "Member unbanned", BotBranding.Success));
    }

    public async Task OnMessageDeletedAsync(Cacheable<IMessage, ulong> cached, Cacheable<IMessageChannel, ulong> cachedChannel)
    {
        if (await cachedChannel.GetOrDownloadAsync() is not SocketGuildChannel channel || IsLogChannel(channel.Id) || _purge.IsBeingPurged(channel.Id))
        {
            return;
        }

        var message = cached.HasValue ? cached.Value : null;
        if (message?.Author.IsBot == true)
        {
            return;
        }

        var embed = new EmbedBuilder()
            .WithColor(BotBranding.Danger)
            .WithTitle("Message deleted")
            .AddField("Channel", $"<#{channel.Id}>", inline: true)
            .WithCurrentTimestamp();

        if (message is null)
        {
            embed.WithDescription("*The message was sent before the bot started, its content is unknown.*");
        }
        else
        {
            embed.WithAuthor(message.Author.Username, message.Author.GetDisplayAvatarUrl())
                .WithDescription(Shorten(message.Content, 4000))
                .AddField("Author", message.Author.Mention, inline: true);

            if (message.Attachments.Count > 0)
            {
                embed.AddField("Attachments", Shorten(string.Join("\n", message.Attachments.Select(a => a.Filename)), 1024));
            }
        }

        await SendAsync(channel.Guild.Id, embed);
    }

    public async Task OnMessageUpdatedAsync(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
    {
        if (channel is not SocketGuildChannel guildChannel
            || IsLogChannel(channel.Id)
            || after.Author.IsBot
            || !before.HasValue
            || before.Value.Content == after.Content)
        {
            return;
        }

        var embed = new EmbedBuilder()
            .WithColor(BotBranding.Warning)
            .WithTitle("Message edited")
            .WithAuthor(after.Author.Username, after.Author.GetDisplayAvatarUrl())
            .WithUrl(after.GetJumpUrl())
            .AddField("Before", Shorten(before.Value.Content, 1024))
            .AddField("After", Shorten(after.Content, 1024))
            .AddField("Channel", $"<#{channel.Id}>", inline: true)
            .AddField("Author", after.Author.Mention, inline: true)
            .WithCurrentTimestamp();

        await SendAsync(guildChannel.Guild.Id, embed);
    }

    #endregion

    #region Private Methods

    private static EmbedBuilder UserEmbed(IUser user, string title, Color color)
    {
        return new EmbedBuilder()
            .WithColor(color)
            .WithTitle(title)
            .WithAuthor(user.Username, user.GetDisplayAvatarUrl())
            .WithThumbnailUrl(user.GetDisplayAvatarUrl())
            .WithDescription(user.Mention)
            .WithFooter($"User ID {user.Id}")
            .WithCurrentTimestamp();
    }

    private bool IsLogChannel(ulong channelId)
    {
        return _store.Read(state => state.LogChannelId) == channelId;
    }

    private async Task SendAsync(ulong guildId, EmbedBuilder embed)
    {
        var logChannelId = _store.Read(state => state.LogChannelId);
        if (guildId != _guildId || logChannelId is null)
        {
            return;
        }

        if (_client.GetGuild(guildId)?.GetTextChannel(logChannelId.Value) is { } channel)
        {
            await channel.SendMessageAsync(embed: embed.Build(), allowedMentions: AllowedMentions.None);
        }
    }

    private static string Shorten(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "*(no text)*";
        }

        return text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";
    }

    #endregion
}
