using Discord;

namespace GrepoBot.Discord.Logic;

/// <summary>
/// Guards the roles members can give themselves (rules role, role panel buttons), so a misconfigured
/// button never hands out moderator rights.
/// </summary>
public static class RoleSafety
{
    #region Private Fields

    private static readonly GuildPermission[] _dangerous =
    [
        GuildPermission.Administrator,
        GuildPermission.ManageGuild,
        GuildPermission.ManageRoles,
        GuildPermission.ManageChannels,
        GuildPermission.ManageMessages,
        GuildPermission.ManageWebhooks,
        GuildPermission.ManageNicknames,
        GuildPermission.ManageEmojisAndStickers,
        GuildPermission.ManageEvents,
        GuildPermission.ManageThreads,
        GuildPermission.KickMembers,
        GuildPermission.BanMembers,
        GuildPermission.ModerateMembers,
        GuildPermission.MentionEveryone,
        GuildPermission.ViewAuditLog,
    ];

    #endregion

    #region Public Methods

    /// <summary>Returns why the bot must not hand out the role, or null when it is safe.</summary>
    public static string? GetProblem(IRole role, int botTopRolePosition)
    {
        if (role.Id == role.Guild.EveryoneRole.Id)
        {
            return "@everyone cannot be assigned.";
        }

        if (role.IsManaged)
        {
            return $"{role.Mention} is managed by an integration.";
        }

        if (role.Position >= botTopRolePosition)
        {
            return $"{role.Mention} is above the bot's highest role. Move the GrepoBot role above it in Server Settings → Roles.";
        }

        var dangerous = _dangerous.FirstOrDefault(p => role.Permissions.Has(p));
        if (dangerous != default)
        {
            return $"{role.Mention} has the {dangerous} permission, members must not be able to give it to themselves.";
        }

        return null;
    }

    #endregion
}
