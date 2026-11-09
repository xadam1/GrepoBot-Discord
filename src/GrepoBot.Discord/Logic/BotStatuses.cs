namespace GrepoBot.Discord.Logic;

/// <summary>
/// The custom statuses the bot rotates through: GrepoBot at work for its players.
/// Discord shows at most 128 characters.
/// </summary>
public static class BotStatuses
{
    #region Properties

    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    public static readonly IReadOnlyList<string> All =
    [
        "Running cities for the players 🏛️",
        "Farming villages while players sleep 🌾",
        "Queueing buildings in every city 🔨",
        "Celebrating city festivals 🎉",
        "Hiding silver in the cave 🪙",
        "Clearing the Bandit Camp ⚔️",
        "Collecting wood, stone and silver 🪵",
        "Keeping an eye on the warehouses 📦",
    ];

    #endregion

    #region Public Methods

    /// <summary>The status for the given rotation step, wrapping around the list.</summary>
    public static string At(long step)
    {
        return All[(int)(Math.Abs(step) % All.Count)];
    }

    #endregion
}
