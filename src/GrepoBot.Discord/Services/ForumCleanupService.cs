using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GrepoBot.Discord.Services;

/// <summary>
/// Once an hour deletes declined and duplicate forum posts whose time is up (see <see cref="ForumService"/>).
/// </summary>
public sealed class ForumCleanupService : BackgroundService
{
    #region Private Fields

    private static readonly TimeSpan _interval = TimeSpan.FromHours(1);

    private readonly DiscordSocketClient _client;
    private readonly ForumService _forum;
    private readonly ILogger<ForumCleanupService> _logger;

    #endregion

    #region Constructor

    public ForumCleanupService(DiscordSocketClient client, ForumService forum, ILogger<ForumCleanupService> logger)
    {
        _client = client;
        _forum = forum;
        _logger = logger;
    }

    #endregion

    #region Private Methods

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        do
        {
            if (_client.ConnectionState != ConnectionState.Connected)
            {
                continue;
            }

            try
            {
                await _forum.CleanupAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Cleaning up closed forum posts failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    #endregion
}
