using Discord;

namespace GrepoBot.Discord.Logic;

/// <summary>
/// Who may use the bot's management commands (settings, posts, purge, forum status, …): the server owner and
/// members with the Moderator, GrepoBot Team or GrepoBot role. Discord permissions alone are not enough, so a role
/// that got Manage Server or Manage Messages by mistake cannot change the bot.
/// </summary>
/// <remarks>
/// Roles are matched by name like the server layout ("🛡️・Moderator" and "Discord Moderator" count), so it works
/// before <c>/server setup</c> ran. The commands' own permission (e.g. Administrator for <c>/server setup</c>)
/// is still required on top.
/// </remarks>
public static class StaffAccess
{
    #region Private Fields

    /// <summary>The bot's own role; members cannot get it, but a human role named the same is staff too.</summary>
    private const string BOT_ROLE = "grepobot";

    #endregion

    #region Properties

    /// <summary>Role names (slugs) that may use the management commands.</summary>
    public static IReadOnlyList<string> RoleNames { get; } = ServerLayout.Roles
        .Where(r => r.Key is ServerLayout.TEAM_ROLE or ServerLayout.MODERATOR_ROLE)
        .SelectMany(r => r.Aliases)
        .Append(BOT_ROLE)
        .ToArray();

    /// <summary>The message members without access get.</summary>
    public const string DENIED_MESSAGE = "Only the Moderator and GrepoBot Team roles can use this command.";

    #endregion

    #region Public Methods

    public static bool IsStaffRole(string? roleName)
    {
        return ServerLayout.Matches(roleName, RoleNames);
    }

    public static bool IsStaff(bool isOwner, IEnumerable<string?> roleNames)
    {
        return isOwner || roleNames.Any(IsStaffRole);
    }

    /// <summary>
    /// Roles that are not staff but have a permission that shows management commands or lets them moderate
    /// without the bot (Administrator, Manage Server, Manage Messages, …). <c>@everyone</c> and integrations count.
    /// </summary>
    public static IReadOnlyList<(string Name, GuildPermission Permission)> FindRisks(
        IEnumerable<(string Name, GuildPermissions Permissions, bool IsManaged)> roles)
    {
        var risks = new List<(string, GuildPermission)>();

        foreach (var (name, permissions, isManaged) in roles)
        {
            if (isManaged || IsStaffRole(name))
            {
                continue;
            }

            var permission = RoleSafety.FindDangerous(permissions);
            if (permission is not null)
            {
                risks.Add((name, permission.Value));
            }
        }

        return risks;
    }

    #endregion
}
