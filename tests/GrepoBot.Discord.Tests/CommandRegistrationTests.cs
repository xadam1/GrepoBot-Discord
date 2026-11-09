using Discord.Interactions;
using Discord.WebSocket;
using GrepoBot.Discord.Configuration;
using GrepoBot.Discord.Services;
using GrepoBot.Discord.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GrepoBot.Discord.Tests;

/// <summary>
/// Builds every command module without connecting to Discord, so wrong attributes, wildcard
/// parameters or missing services fail here instead of when the bot starts on the Pi.
/// </summary>
public class CommandRegistrationTests
{
    #region Tests

    [Fact]
    public async Task AllModules_Build()
    {
        await using var services = BuildServices();
        var interactions = services.GetRequiredService<InteractionService>();

        var modules = await interactions.AddModulesAsync(typeof(DiscordBotService).Assembly, services);

        Assert.NotEmpty(modules);
        Assert.Contains(interactions.ContextCommands, c => c.Name == "Post as GrepoBot");
        Assert.Contains(interactions.ContextCommands, c => c.Name == "Post as release");
        Assert.Contains(interactions.ContextCommands, c => c.Name == "Update GrepoBot post");
        Assert.Contains(interactions.ContextCommands, c => c.Name == "Purge from here");

        var slash = interactions.SlashCommands.Select(c => $"{c.Module.SlashGroupName} {c.Name}".Trim()).ToList();
        Assert.Contains("purge all", slash);
        Assert.Contains("autoreply add", slash);
        Assert.Contains("rules post", slash);
        Assert.Contains("welcome setup", slash);
        Assert.Contains("roles panel", slash);
        Assert.Contains("setup branding", slash);
        Assert.Contains("about", slash);
        Assert.Contains("tickets setup", slash);
        Assert.Contains("tickets panel", slash);
        Assert.Contains("ticket add", slash);
        Assert.Contains("ticket close", slash);
        Assert.Contains("forum status", slash);
        Assert.Contains("forum reopen", slash);
        Assert.Contains("forum setup", slash);
        Assert.Contains("server setup", slash);
        Assert.Contains("status set", slash);

        // Component and modal handlers are found by the ids the bot puts on its buttons and forms.
        var components = interactions.ComponentCommands.Select(c => c.Name).ToList();
        Assert.Contains("rules-accept", components);
        Assert.Contains("role-toggle:*", components);
        Assert.Contains("post-send:*:*:*:*:*", components);
        Assert.Contains("release-target:*:*", components);
        Assert.Contains("release-send:*:*:*:*", components);
        Assert.Contains("post-color:*:*:*", components);
        Assert.Contains("purge-all-keep:*", components);
        Assert.Contains("ticket-open", components);
        Assert.Contains("ticket-close", components);
        Assert.Contains("ticket-claim", components);
        Assert.Contains("ticket-reopen", components);
        Assert.Contains("ticket-delete", components);
        Assert.Contains("server-setup-apply", components);
        Assert.Contains("server-setup-cancel", components);
        var modals = interactions.ModalCommands.Select(c => c.Name).ToList();
        Assert.Contains("autoreply-save:*:*:*:*", modals);
        Assert.Contains("rules-save:*:*", modals);
        Assert.Contains("welcome-save:*", modals);
        Assert.Contains("ticket-create", modals);
        Assert.Contains("ticket-close-save", modals);
        Assert.Contains("tickets-panel-save:*", modals);
        Assert.Contains("tickets-message-save", modals);
    }

    [Fact]
    public async Task CommandNamesAndDescriptions_FitDiscordLimits()
    {
        await using var services = BuildServices();
        var interactions = services.GetRequiredService<InteractionService>();
        await interactions.AddModulesAsync(typeof(DiscordBotService).Assembly, services);

        foreach (var command in interactions.SlashCommands)
        {
            Assert.Matches("^[a-z0-9_-]{1,32}$", command.Name);
            Assert.InRange(command.Description.Length, 1, 100);
            foreach (var parameter in command.Parameters)
            {
                Assert.Matches("^[a-z0-9_-]{1,32}$", parameter.Name);
                Assert.InRange(parameter.Description.Length, 1, 100);
            }
        }

        foreach (var group in interactions.Modules.Where(m => m.IsSlashGroup))
        {
            Assert.InRange(group.Description.Length, 1, 100);
        }
    }

    #endregion

    #region Helpers

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Options.Create(new DiscordOptions { GuildId = 1 }));
        services.AddSingleton(Options.Create(new BotOptions { DataDirectory = Path.Combine(Path.GetTempPath(), "grepobot-discord-reg-" + Guid.NewGuid()) }));
        services.AddSingleton(new DiscordSocketClient());
        services.AddSingleton(provider => new InteractionService(provider.GetRequiredService<DiscordSocketClient>()));
        services.AddSingleton(new HttpClient());
        services.AddSingleton<StateStore>();
        services.AddSingleton<RepostService>();
        services.AddSingleton<PurgeService>();
        services.AddSingleton<RulesService>();
        services.AddSingleton<AutoReplyService>();
        services.AddSingleton<WelcomeService>();
        services.AddSingleton<ModLogService>();
        services.AddSingleton<TicketService>();
        services.AddSingleton<ForumService>();
        services.AddSingleton<AppStatusService>();
        services.AddSingleton<ServerSetupService>();
        services.AddSingleton<DirectMessageService>();
        return services.BuildServiceProvider();
    }

    #endregion
}
