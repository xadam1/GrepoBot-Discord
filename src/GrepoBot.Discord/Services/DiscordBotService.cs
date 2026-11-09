using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using GrepoBot.Discord.Configuration;
using GrepoBot.Discord.Logic;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GrepoBot.Discord.Services;

/// <summary>
/// Logs the bot in, registers the commands to the community server and routes interactions
/// and gateway events to the feature services. Discord.Net reconnects on its own after the
/// Pi's network drops (Wi-Fi hiccups, router restarts).
/// </summary>
public sealed class DiscordBotService : IHostedService
{
    #region Private Fields

    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly IServiceProvider _services;
    private readonly DiscordOptions _options;
    private readonly AutoReplyService _autoReply;
    private readonly WelcomeService _welcome;
    private readonly ModLogService _modLog;
    private readonly TicketService _tickets;
    private readonly ForumService _forum;
    private readonly DirectMessageService _directMessages;
    private readonly ILogger<DiscordBotService> _logger;
    private bool _commandsRegistered;
    private int _statusRotationStarted;

    #endregion

    #region Constructor

    public DiscordBotService(
        DiscordSocketClient client,
        InteractionService interactions,
        IServiceProvider services,
        IOptions<DiscordOptions> options,
        AutoReplyService autoReply,
        WelcomeService welcome,
        ModLogService modLog,
        TicketService tickets,
        ForumService forum,
        DirectMessageService directMessages,
        ILogger<DiscordBotService> logger)
    {
        _client = client;
        _interactions = interactions;
        _services = services;
        _options = options.Value;
        _autoReply = autoReply;
        _welcome = welcome;
        _modLog = modLog;
        _tickets = tickets;
        _forum = forum;
        _directMessages = directMessages;
        _logger = logger;
    }

    #endregion

    #region Public Methods

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            throw new InvalidOperationException(
                "Discord token is missing. Set Discord__Token (environment) or Discord:Token in appsettings.Local.json.");
        }

        if (_options.GuildId == 0)
        {
            throw new InvalidOperationException(
                "Server ID is missing. Set Discord__GuildId (environment) or Discord:GuildId in appsettings.Local.json.");
        }

        _client.Log += OnDiscordLogAsync;
        _interactions.Log += OnDiscordLogAsync;
        _client.Ready += OnReadyAsync;
        _client.InteractionCreated += OnInteractionCreatedAsync;
        _interactions.InteractionExecuted += OnInteractionExecutedAsync;

        _client.MessageReceived += message => RunSafe(() => _autoReply.HandleMessageAsync(message), "auto-reply");
        _client.MessageReceived += message => RunSafe(() => _directMessages.HandleMessageAsync(message), "direct message");
        _client.UserJoined += user => RunSafe(() => OnUserJoinedAsync(user), "member join");
        _client.UserLeft += (guild, user) => RunSafe(() => _modLog.OnUserLeftAsync(guild, user), "member leave");
        _client.UserBanned += (user, guild) => RunSafe(() => _modLog.OnUserBannedAsync(user, guild), "ban");
        _client.UserUnbanned += (user, guild) => RunSafe(() => _modLog.OnUserUnbannedAsync(user, guild), "unban");
        _client.MessageDeleted += (message, channel) => RunSafe(() => _modLog.OnMessageDeletedAsync(message, channel), "message delete");
        _client.MessageUpdated += (before, after, channel) => RunSafe(() => _modLog.OnMessageUpdatedAsync(before, after, channel), "message edit");
        _client.ChannelDestroyed += channel => RunSafe(() => _tickets.OnChannelDestroyedAsync(channel), "channel delete");
        _client.ThreadCreated += thread => RunSafe(() => _forum.OnThreadCreatedAsync(thread), "forum post");

        await _interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), _services);
        await _client.LoginAsync(TokenType.Bot, _options.Token);
        await _client.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _client.StopAsync();
        await _client.LogoutAsync();
    }

    #endregion

    #region Private Methods

    /// <summary>Cycles through <see cref="BotStatuses"/> for as long as the bot runs (the status survives reconnects).</summary>
    private async Task RotateStatusAsync()
    {
        using var timer = new PeriodicTimer(BotStatuses.Interval);
        var step = 0L;
        do
        {
            try
            {
                await _client.SetCustomStatusAsync(BotStatuses.At(step++));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Setting the bot status failed.");
            }
        }
        while (await timer.WaitForNextTickAsync());
    }

    private async Task OnReadyAsync()
    {
        _logger.LogInformation("Connected as {User}.", _client.CurrentUser);

        if (!string.IsNullOrWhiteSpace(_options.Status))
        {
            await _client.SetCustomStatusAsync(_options.Status);
        }
        else if (Interlocked.Exchange(ref _statusRotationStarted, 1) == 0)
        {
            _ = RotateStatusAsync();
        }

        // Ready fires again after every reconnect, the commands only need registering once.
        if (_commandsRegistered)
        {
            return;
        }

        if (_client.GetGuild(_options.GuildId) is null)
        {
            _logger.LogError("The bot is not a member of server {GuildId}. Invite it with the link from the README.", _options.GuildId);
            return;
        }

        var commands = await _interactions.RegisterCommandsToGuildAsync(_options.GuildId, deleteMissing: true);
        _commandsRegistered = true;
        _logger.LogInformation("Registered {Count} commands to server {GuildId}.", commands.Count, _options.GuildId);
    }

    private async Task OnInteractionCreatedAsync(SocketInteraction interaction)
    {
        // Another instance with the same token (e.g. a local test run for the test server) answers
        // interactions of its own server, two answers to one click would fail.
        if (interaction.GuildId is { } guildId && guildId != _options.GuildId)
        {
            return;
        }

        try
        {
            var context = new SocketInteractionContext(_client, interaction);
            await _interactions.ExecuteCommandAsync(context, _services);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Interaction {Type} failed before executing.", interaction.Type);
        }
    }

    private async Task OnInteractionExecutedAsync(ICommandInfo command, IInteractionContext context, IResult result)
    {
        if (result.IsSuccess)
        {
            return;
        }

        if (result is ExecuteResult { Exception: not null } execute)
        {
            _logger.LogError(execute.Exception, "Command {Command} failed.", command?.Name);
        }
        else
        {
            _logger.LogWarning("Command {Command} failed: {Error} {Reason}", command?.Name, result.Error, result.ErrorReason);
        }

        var message = result.Error switch
        {
            InteractionCommandError.UnmetPrecondition => $"⛔ {result.ErrorReason}",
            InteractionCommandError.Exception => "❌ Something went wrong. The details are in the bot's log.",
            _ => $"❌ {result.ErrorReason}",
        };

        try
        {
            if (context.Interaction.HasResponded)
            {
                await context.Interaction.FollowupAsync(message, ephemeral: true);
            }
            else
            {
                await context.Interaction.RespondAsync(message, ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not report the failure to the user.");
        }
    }

    private async Task OnUserJoinedAsync(SocketGuildUser user)
    {
        await _welcome.OnUserJoinedAsync(user);
        await _modLog.OnUserJoinedAsync(user);
    }

    private Task OnDiscordLogAsync(LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            _ => LogLevel.Trace,
        };

        _logger.Log(level, message.Exception, "[{Source}] {Message}", message.Source, message.Message);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs a gateway handler off the gateway thread. Discord.Net warns (and can disconnect) when a
    /// handler blocks, and an exception must never reach the gateway loop.
    /// </summary>
    private Task RunSafe(Func<Task> handler, string name)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await handler();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Handling {Event} failed.", name);
            }
        });

        return Task.CompletedTask;
    }

    #endregion
}
