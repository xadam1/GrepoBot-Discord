using System.Diagnostics;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using GrepoBot.Discord.Configuration;
using GrepoBot.Discord.Logic;
using GrepoBot.Discord.Services;
using Microsoft.Extensions.Options;

namespace GrepoBot.Discord.Modules;

/// <summary>
/// Everything regular members can use: <c>/about</c>, the rules accept button and role panel buttons.
/// </summary>
[CommandContextType(InteractionContextType.Guild)]
public sealed class PublicModule : InteractionModuleBase<SocketInteractionContext>
{
    #region Private Fields

    public const string ROLE_TOGGLE_ID = "role-toggle";

    private readonly RulesService _rules;
    private readonly BotOptions _options;

    #endregion

    #region Constructor

    public PublicModule(RulesService rules, IOptions<BotOptions> options)
    {
        _rules = rules;
        _options = options.Value;
    }

    #endregion

    #region Public Methods

    [SlashCommand("about", "What is GrepoBot and where to get it")]
    public async Task AboutAsync()
    {
        var uptime = DateTime.Now - Process.GetCurrentProcess().StartTime;

        var embed = new EmbedBuilder()
            .WithColor(BotBranding.Accent)
            .WithTitle("GrepoBot")
            .WithThumbnailUrl(Context.Client.CurrentUser.GetDisplayAvatarUrl())
            .WithDescription(
                "Automation tool and helper for **Grepolis**: farming, building, culture and more, " +
                "with the game embedded right in the app.")
            .WithFooter($"Bot online for {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m")
            .Build();

        var links = new ComponentBuilder()
            .WithButton("Website", url: _options.WebsiteUrl, style: ButtonStyle.Link, emote: new Emoji("🌐"))
            .WithButton("Changelog", url: _options.ChangelogUrl, style: ButtonStyle.Link, emote: new Emoji("📝"))
            .Build();

        await RespondAsync(embed: embed, components: links, ephemeral: true);
    }

    [ComponentInteraction(RulesService.ACCEPT_BUTTON_ID)]
    public async Task OnAcceptRulesAsync()
    {
        await DeferAsync(ephemeral: true);
        var text = await _rules.AcceptAsync((SocketGuildUser)Context.User);
        await FollowupAsync(text, ephemeral: true);
    }

    [ComponentInteraction(ROLE_TOGGLE_ID + ":*")]
    public async Task OnToggleRoleAsync(string roleId)
    {
        await DeferAsync(ephemeral: true);

        var user = (SocketGuildUser)Context.User;
        var role = Context.Guild.GetRole(ulong.Parse(roleId));
        if (role is null)
        {
            await FollowupAsync("❌ This role no longer exists.", ephemeral: true);
            return;
        }

        // Checked again on every click, the role's permissions may have changed since the panel was posted.
        if (RoleSafety.GetProblem(role, Context.Guild.CurrentUser.Hierarchy) is not null)
        {
            await FollowupAsync("⚠️ I cannot give this role right now. Please tell a moderator.", ephemeral: true);
            return;
        }

        if (user.Roles.Any(r => r.Id == role.Id))
        {
            await user.RemoveRoleAsync(role, new RequestOptions { AuditLogReason = "Role panel" });
            await FollowupAsync($"➖ Removed {role.Mention}.", ephemeral: true, allowedMentions: AllowedMentions.None);
        }
        else
        {
            await user.AddRoleAsync(role, new RequestOptions { AuditLogReason = "Role panel" });
            await FollowupAsync($"➕ You now have {role.Mention}.", ephemeral: true, allowedMentions: AllowedMentions.None);
        }
    }

    #endregion
}
