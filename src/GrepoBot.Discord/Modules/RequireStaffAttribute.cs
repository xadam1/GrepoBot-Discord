using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using GrepoBot.Discord.Logic;

namespace GrepoBot.Discord.Modules;

/// <summary>
/// Lets only the server owner and the Moderator, GrepoBot Team and GrepoBot roles run the command, its buttons and
/// forms (see <see cref="StaffAccess"/>). Applies on top of the command's Discord permission.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireStaffAttribute : PreconditionAttribute
{
    #region Public Methods

    public override Task<PreconditionResult> CheckRequirementsAsync(IInteractionContext context, ICommandInfo commandInfo,
        IServiceProvider services)
    {
        if (context.User is not SocketGuildUser user)
        {
            return Task.FromResult(PreconditionResult.FromError(StaffAccess.DENIED_MESSAGE));
        }

        var isStaff = StaffAccess.IsStaff(user.Guild.OwnerId == user.Id, user.Roles.Select(r => r.Name));

        return Task.FromResult(isStaff
            ? PreconditionResult.FromSuccess()
            : PreconditionResult.FromError(StaffAccess.DENIED_MESSAGE));
    }

    #endregion
}
