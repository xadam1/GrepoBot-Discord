using System.Reflection;
using Discord;
using Discord.Interactions;
using GrepoBot.Discord.Modules;

namespace GrepoBot.Discord.Tests;

/// <summary>
/// Builds every form through Discord.Net like the bot does, so labels, placeholders and lengths
/// that Discord would reject (label 45, description and placeholder 100 characters) fail here.
/// </summary>
public class ModalTests
{
    #region Tests

    [Fact]
    public async Task AutoReplyModal_Builds()
    {
        var modal = await BuildAsync<AutoReplyModal>("autoreply-save:new:install:Any:300", builder =>
        {
            builder.UpdateTextInput(AutoReplyModal.KEYWORDS_ID, input => input.Value = "install");
            builder.UpdateTextInput(AutoReplyModal.RESPONSE_ID, input => input.Value = "answer");
        });

        Assert.Equal("Automatic answer", modal.Title);
    }

    [Fact]
    public async Task RulesModal_Builds()
    {
        var modal = await BuildAsync<RulesModal>("rules-save:1:2", builder =>
        {
            builder.UpdateTextInput(RulesModal.TITLE_ID, input => input.Value = State.RulesSettings.DEFAULT_TITLE);
            builder.UpdateTextInput(RulesModal.TEXT_ID, input => input.Value = State.RulesSettings.DEFAULT_TEXT);
        });

        Assert.Equal("Server rules", modal.Title);
    }

    [Fact]
    public async Task WelcomeModal_Builds()
    {
        var modal = await BuildAsync<WelcomeModal>("welcome-save:1", builder =>
            builder.UpdateTextInput(WelcomeModal.MESSAGE_ID, input => input.Value = State.WelcomeSettings.DEFAULT_MESSAGE));

        Assert.Equal("Welcome message", modal.Title);
    }

    #endregion

    #region Helpers

    private static async Task<Modal> BuildAsync<T>(string customId, Action<ModalBuilder> modify)
        where T : class, IModal
    {
        // Discord.Net caches modal infos statically, another test may have registered the modal already.
        using var client = new global::Discord.WebSocket.DiscordSocketClient();
        try
        {
            new InteractionService(client).AddModalInfo<T>();
        }
        catch (InvalidOperationException)
        {
        }

        var interaction = DispatchProxy.Create<IDiscordInteraction, CapturingInteraction>();
        await interaction.RespondWithModalAsync<T>(customId, modifyModal: modify);

        var modal = ((CapturingInteraction)(object)interaction).Modal;
        Assert.NotNull(modal);
        Assert.Equal(customId, modal.CustomId);
        return modal;
    }

    /// <summary>Stands in for a Discord interaction and keeps the modal the bot responds with.</summary>
    public class CapturingInteraction : DispatchProxy
    {
        public Modal? Modal { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IDiscordInteraction.RespondWithModalAsync) && args?[0] is Modal modal)
            {
                Modal = modal;
                return Task.CompletedTask;
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    #endregion
}
