using Discord;
using Discord.WebSocket;
using GrepoBot.Discord.Logic;
using GrepoBot.Discord.State;
using Microsoft.Extensions.Logging;

namespace GrepoBot.Discord.Services;

/// <summary>
/// Posts the rules as a card (optionally with an "I accept" button) and gives the member role to whoever accepts.
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
    /// With <paramref name="memberRole"/> the message has an accept button giving that role; without it the rules
    /// are only shown (Discord's own rules screening then handles accepting).
    /// </summary>
    public async Task<IUserMessage> PublishAsync(ITextChannel channel, IRole? memberRole, string title, string text)
    {
        var existing = _store.Read(state => new { state.Rules.ChannelId, state.Rules.MessageId });

        _store.Update(state =>
        {
            state.Rules.ChannelId = channel.Id;
            state.Rules.MemberRoleId = memberRole?.Id;
            state.Rules.Title = title;
            state.Rules.Text = text;
        });

        IUserMessage? existingMessage = null;
        if (existing.ChannelId == channel.Id && existing.MessageId is { } messageId)
        {
            existingMessage = await channel.GetMessageAsync(messageId) as IUserMessage;
        }

        var card = BuildCard(channel.Guild, title, text, memberRole is not null);
        var message = await MessageCards.UpsertAsync(channel, existingMessage, card, BotBranding.HasBanner ? MessageCards.One(BotBranding.OpenBanner) : null);
        if (message.Id != existingMessage?.Id)
        {
            _store.Update(state => state.Rules.MessageId = message.Id);
        }

        _logger.LogInformation("Published the rules in #{Channel}.", channel.Name);
        return message;
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

    /// <summary>The rules card: banner, title, rules and (with a member role) the accept button.</summary>
    private static MessageComponent BuildCard(IGuild guild, string title, string text, bool withAcceptButton)
    {
        var container = new ContainerBuilder().WithAccentColor(BotBranding.Accent);
        if (BotBranding.HasBanner)
        {
            container.WithMediaGallery(MessageCards.Banner());
        }

        container
            .WithTextDisplay("# " + title)
            .WithSeparator(SeparatorSpacingSize.Small, true)
            .WithTextDisplay(text)
            .WithSeparator(SeparatorSpacingSize.Small, true)
            .WithTextDisplay($"-# {guild.Name} · these rules apply everywhere on the server");

        if (withAcceptButton)
        {
            container.WithActionRow(new ActionRowBuilder()
                .WithButton("I have read and accept the rules", ACCEPT_BUTTON_ID, ButtonStyle.Success, new Emoji("✅")));
        }

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    #endregion
}
