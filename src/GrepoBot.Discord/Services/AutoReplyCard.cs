using Discord;
using Discord.WebSocket;
using GrepoBot.Discord.Logic;
using GrepoBot.Discord.State;

namespace GrepoBot.Discord.Services;

/// <summary>
/// Builds an automatic answer as a Components v2 card: the bot logo next to the headline and the text,
/// an optional image, link buttons (web addresses or channels) and the footer.
/// </summary>
public static class AutoReplyCard
{
    #region Public Methods

    /// <param name="values">Values of <c>{user}</c>, <c>{username}</c> and <c>{server}</c>.</param>
    public static MessageComponent Build(SocketGuild guild, AutoReplyRule rule, string footer, IReadOnlyDictionary<string, string> values)
    {
        var channels = guild.Channels.Select(c => (c.Id, c.Name)).ToList();

        string Render(string text)
        {
            return ServerLayout.RenderChannelMentions(TextTemplate.Render(text, values), channels);
        }

        var texts = new List<string>();
        if (!string.IsNullOrWhiteSpace(rule.Title))
        {
            texts.Add("## " + Render(rule.Title.Trim()));
        }

        texts.Add(Render(rule.Response));

        var container = new ContainerBuilder()
            .WithAccentColor(BotBranding.Accent)
            .WithSection(MessageCards.WithLogo(MessageCards.Logo(guild), texts.ToArray()));

        if (AutoReplyButtons.IsWebAddress(rule.ImageUrl))
        {
            container.WithMediaGallery(new MediaGalleryBuilder().AddItem(rule.ImageUrl!.Trim(), rule.Title, false));
        }

        var buttons = rule.Buttons
            .Select(button => (button.Label, Url: AutoReplyButtons.ResolveUrl(button, guild.Id, channels)))
            .Where(button => button.Url is not null)
            .Take(AutoReplyButtons.MAX_BUTTONS)
            .ToList();

        if (buttons.Count > 0)
        {
            var row = new ActionRowBuilder();
            foreach (var (label, url) in buttons)
            {
                row.WithButton(new ButtonBuilder(label, style: ButtonStyle.Link, url: url));
            }

            container.WithActionRow(row);
        }

        if (!string.IsNullOrWhiteSpace(footer))
        {
            container.WithSeparator(SeparatorSpacingSize.Small, true)
                .WithTextDisplay("-# " + footer.Trim());
        }

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    #endregion
}
