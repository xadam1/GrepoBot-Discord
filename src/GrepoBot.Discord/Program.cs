using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using GrepoBot.Discord.Configuration;
using GrepoBot.Discord.Services;
using GrepoBot.Discord.State;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// appsettings.json < appsettings.Local.json (gitignored, for local testing) < environment variables (Discord__Token on the Pi).
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddSystemd();
builder.Services.Configure<DiscordOptions>(builder.Configuration.GetSection(DiscordOptions.SECTION));
builder.Services.Configure<BotOptions>(builder.Configuration.GetSection(BotOptions.SECTION));

builder.Services.AddSingleton(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.Guilds
        | GatewayIntents.GuildMembers
        | GatewayIntents.GuildMessages
        | GatewayIntents.MessageContent
        | GatewayIntents.GuildBans,
    MessageCacheSize = 500,
    AlwaysDownloadUsers = false,
    LogLevel = LogSeverity.Info,
});
builder.Services.AddSingleton<DiscordSocketClient>();
builder.Services.AddSingleton(provider => new InteractionService(
    provider.GetRequiredService<DiscordSocketClient>(),
    new InteractionServiceConfig
    {
        DefaultRunMode = RunMode.Async,
        LogLevel = LogSeverity.Info,
        UseCompiledLambda = false,
    }));

builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromMinutes(2) });
builder.Services.AddSingleton<StateStore>();
builder.Services.AddSingleton<RepostService>();
builder.Services.AddSingleton<PurgeService>();
builder.Services.AddSingleton<RulesService>();
builder.Services.AddSingleton<AutoReplyService>();
builder.Services.AddSingleton<WelcomeService>();
builder.Services.AddSingleton<ModLogService>();
builder.Services.AddHostedService<DiscordBotService>();

await builder.Build().RunAsync();
