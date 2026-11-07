using Discord;
using Discord.WebSocket;
using GrepoBot.Discord.Logic;
using GrepoBot.Discord.State;
using Microsoft.Extensions.Logging;

namespace GrepoBot.Discord.Services;

/// <summary>
/// Posts the rules with an "I accept" button and gives the member role to whoever accepts.
/// </summary>
public sealed class RulesService
{
    #region Private Fields

    public const string ACCEPT_BUTTON_ID = "rules-accept";

    private readonly StateStore _store;
    private readonly ILogger<RulesService> _logger;

    #endregion

    #region Constructor

    public RulesService(StateStore store, ILogger<RulesService> logger)
    {
        _store = store;
        _logger = logger;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Saves the rules and posts them to <paramref name="channel"/>, or edits the existing rules message there.
    /// </summary>
    public async Task<IUserMessage> PublishAsync(ITextChannel channel, IRole memberRole, string title, string text)
    {
        var existing = _store.Read(state => new { state.Rules.ChannelId, state.Rules.MessageId });

        _store.Update(state =>
        {
            state.Rules.ChannelId = channel.Id;
            state.Rules.MemberRoleId = memberRole.Id;
            state.Rules.Title = title;
            state.Rules.Text = text;
        });

        var embed = BuildEmbed(title, text, withBanner: false);
        var components = BuildComponents();

        if (existing.ChannelId == channel.Id
            && existing.MessageId is { } messageId
            && await channel.GetMessageAsync(messageId) is IUserMessage message)
        {
            var hasBanner = message.Attachments.Any(a => a.Filename == BotBranding.BANNER_FILE_NAME);
            await message.ModifyAsync(properties =>
            {
                properties.Embed = BuildEmbed(title, text, hasBanner);
                properties.Components = components;
            });

            _logger.LogInformation("Updated the rules message in #{Channel}.", channel.Name);
            return message;
        }

        IUserMessage posted;
        if (BotBranding.HasBanner)
        {
            using var banner = BotBranding.OpenBanner();
            posted = await channel.SendFileAsync(banner, embed: BuildEmbed(title, text, withBanner: true), components: components);
        }
        else
        {
            posted = await channel.SendMessageAsync(embed: embed, components: components);
        }

        _store.Update(state => state.Rules.MessageId = posted.Id);
        _logger.LogInformation("Posted the rules in #{Channel}.", channel.Name);
        return posted;
    }

    /// <summary>Gives the member role. Returns the text shown to the member.</summary>
    public async Task<string> AcceptAsync(SocketGuildUser user)
    {
        var roleId = _store.Read(state => state.Rules.MemberRoleId);
        if (roleId is null || user.Guild.GetRole(roleId.Value) is not { } role)
        {
            return "⚠️ The member role is not set up yet. Please tell a moderator.";
        }

        if (user.Roles.Any(r => r.Id == role.Id))
        {
            return "✅ You have already accepted the rules. Have fun!";
        }

        var problem = RoleSafety.GetProblem(role, user.Guild.CurrentUser.Hierarchy);
        if (problem is not null)
        {
            _logger.LogWarning("Cannot give the rules role: {Problem}", problem);
            return "⚠️ I cannot give you the member role right now. Please tell a moderator.";
        }

        await user.AddRoleAsync(role, new RequestOptions { AuditLogReason = "Accepted the rules" });
        _logger.LogInformation("{User} accepted the rules.", user.Username);
        return $"🎉 Thanks for accepting the rules, {user.DisplayName}! The rest of the server is now unlocked.";
    }

    #endregion

    #region Private Methods

    private static Embed BuildEmbed(string title, string text, bool withBanner)
    {
        var embed = new EmbedBuilder()
            .WithColor(BotBranding.Accent)
            .WithTitle(title)
            .WithDescription(text)
            .WithFooter("GrepoBot community");

        if (withBanner)
        {
            embed.WithImageUrl(BotBranding.BannerAttachmentUrl);
        }

        return embed.Build();
    }

    private static MessageComponent BuildComponents()
    {
        return new ComponentBuilder()
            .WithButton("I have read and accept the rules", ACCEPT_BUTTON_ID, ButtonStyle.Success, new Emoji("✅"))
            .Build();
    }

    #endregion
}
