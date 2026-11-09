using Discord;
using Discord.WebSocket;
using GrepoBot.Discord.Configuration;
using GrepoBot.Discord.Logic;
using GrepoBot.Discord.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GrepoBot.Discord.Services;

/// <summary>
/// Greets new members in the welcome channel with the configured message and a personal welcome card
/// (their avatar and name on the GrepoBot background, see <see cref="WelcomeCard"/>).
/// </summary>
public sealed class WelcomeService
{
    #region Private Fields

    private readonly StateStore _store;
    private readonly HttpClient _http;
    private readonly ulong _guildId;
    private readonly ILogger<WelcomeService> _logger;

    #endregion

    #region Constructor

    public WelcomeService(StateStore store, HttpClient http, IOptions<DiscordOptions> options, ILogger<WelcomeService> logger)
    {
        _store = store;
        _http = http;
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

        // Without the bot's /rules, {rules} points to the server's own rules channel (Community servers).
        var text = RenderMessage(settings.Message, user, settings.RulesChannelId ?? user.Guild.RulesChannel?.Id);
        // Mentions inside a card ping too, the new member gets a notification.
        var mentions = new AllowedMentions { UserIds = [user.Id] };
        if (!text.Contains(user.Mention, StringComparison.Ordinal))
        {
            text = $"👋 {user.Mention}\n{text}";
        }

        var card = await RenderCardAsync(user);
        Func<IReadOnlyList<FileAttachment>>? file = card is not null
            ? MessageCards.One(() => new FileAttachment(new MemoryStream(card), WelcomeCard.FILE_NAME))
            : BotBranding.HasBanner ? MessageCards.One(BotBranding.OpenBanner) : null;
        var imageUrl = card is not null ? "attachment://" + WelcomeCard.FILE_NAME : BotBranding.BannerAttachmentUrl;

        var container = new ContainerBuilder()
            .WithAccentColor(BotBranding.Accent)
            .WithTextDisplay(text);
        if (file is not null)
        {
            container.WithMediaGallery(new MediaGalleryBuilder().AddItem(imageUrl, $"Welcome card of {user.DisplayName}", false));
        }

        container.WithTextDisplay($"-# {user.Guild.Name} · member #{user.Guild.MemberCount:N0} · <t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:f>");
        var components = new ComponentBuilderV2().WithContainer(container).Build();
        await MessageCards.UpsertAsync(channel, null, components, file, mentions);

        _logger.LogInformation("Welcomed {User} in #{Channel}.", user.Username, channel.Name);
        return true;
    }

    #endregion

    #region Private Methods

    /// <summary>The personal card, or null when drawing it fails (the banner is used then).</summary>
    private async Task<byte[]?> RenderCardAsync(SocketGuildUser user)
    {
        try
        {
            byte[]? avatar = null;
            try
            {
                avatar = await _http.GetByteArrayAsync(user.GetDisplayAvatarUrl(ImageFormat.Png, 256));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not download the avatar of {User}.", user.Username);
            }

            return WelcomeCard.Render(avatar, user.DisplayName, user.Guild.Name, user.Guild.MemberCount, WelcomeQuips.Pick());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Drawing the welcome card for {User} failed, using the banner.", user.Username);
            return null;
        }
    }

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
