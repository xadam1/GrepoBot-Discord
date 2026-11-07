using Discord;
using Discord.WebSocket;
using GrepoBot.Discord.Configuration;
using GrepoBot.Discord.Logic;
using GrepoBot.Discord.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GrepoBot.Discord.Services;

/// <summary>
/// Greets new members in the welcome channel with the configured message and the GrepoBot banner.
/// </summary>
public sealed class WelcomeService
{
    #region Private Fields

    private readonly StateStore _store;
    private readonly ulong _guildId;
    private readonly ILogger<WelcomeService> _logger;

    #endregion

    #region Constructor

    public WelcomeService(StateStore store, IOptions<DiscordOptions> options, ILogger<WelcomeService> logger)
    {
        _store = store;
        _guildId = options.Value.GuildId;
        _logger = logger;
    }

    #endregion

    #region Public Methods

    public async Task OnUserJoinedAsync(SocketGuildUser user)
    {
        if (user.Guild.Id != _guildId || user.IsBot)
        {
            return;
        }

        await SendWelcomeAsync(user);
    }

    /// <summary>Sends the welcome message for <paramref name="user"/>. Returns false when no welcome channel is set.</summary>
    public async Task<bool> SendWelcomeAsync(SocketGuildUser user)
    {
        var settings = _store.Read(state => new
        {
            state.Welcome.ChannelId,
            state.Welcome.Message,
            RulesChannelId = state.Rules.ChannelId,
        });

        if (settings.ChannelId is null || user.Guild.GetTextChannel(settings.ChannelId.Value) is not { } channel)
        {
            return false;
        }

        var text = RenderMessage(settings.Message, user, settings.RulesChannelId);
        var embed = new EmbedBuilder()
            .WithColor(BotBranding.Accent)
            .WithAuthor($"Welcome, {user.DisplayName}!", user.GetDisplayAvatarUrl())
            .WithDescription(text)
            .WithThumbnailUrl(user.GetDisplayAvatarUrl(size: 256))
            .WithFooter(user.Guild.Name, user.Guild.IconUrl)
            .WithCurrentTimestamp();

        // Mention in the content so the new member gets a notification, embeds never ping.
        var mentions = new AllowedMentions { UserIds = [user.Id] };
        if (BotBranding.HasBanner)
        {
            using var banner = BotBranding.OpenBanner();
            embed.WithImageUrl(BotBranding.BannerAttachmentUrl);
            await channel.SendFileAsync(banner, user.Mention, embed: embed.Build(), allowedMentions: mentions);
        }
        else
        {
            await channel.SendMessageAsync(user.Mention, embed: embed.Build(), allowedMentions: mentions);
        }

        _logger.LogInformation("Welcomed {User} in #{Channel}.", user.Username, channel.Name);
        return true;
    }

    #endregion

    #region Private Methods

    private static string RenderMessage(string template, SocketGuildUser user, ulong? rulesChannelId)
    {
        return TextTemplate.Render(template, new Dictionary<string, string>
        {
            ["user"] = user.Mention,
            ["username"] = user.DisplayName,
            ["server"] = user.Guild.Name,
            ["membercount"] = user.Guild.MemberCount.ToString("N0"),
            ["rules"] = rulesChannelId is null ? "the rules channel" : $"<#{rulesChannelId}>",
        });
    }

    #endregion
}
