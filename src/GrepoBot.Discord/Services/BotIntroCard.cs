using Discord;
using GrepoBot.Discord.Logic;

namespace GrepoBot.Discord.Services;

/// <summary>
/// The "I'm only a bot" card, sent when a member messages the bot directly or tags it on the server:
/// where to get 1-on-1 help, where ideas and bugs go, with buttons to both.
/// </summary>
public static class BotIntroCard
{
    #region Public Methods

    public static MessageComponent Build(string logoUrl, string memberName, string serverName, ulong guildId,
        ulong? ticketChannelId, ulong? forumChannelId, bool isMention)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(BotBranding.Accent)
            .WithSection(MessageCards.WithLogo(logoUrl,
                isMention ? $"## Hi {memberName}, I'm just a bot! 🤖" : $"## Hi {memberName}, thank you for your message! 👋",
                BuildText(serverName, ticketChannelId, forumChannelId, isMention)));

        var buttons = new ActionRowBuilder();
        if (ticketChannelId is { } tickets)
        {
            buttons.WithButton(MessageCards.Link("Open a ticket", $"https://discord.com/channels/{guildId}/{tickets}", "🎫"));
        }

        if (forumChannelId is { } forum)
        {
            buttons.WithButton(MessageCards.Link("Ideas & bugs", $"https://discord.com/channels/{guildId}/{forum}", "💡"));
        }

        buttons.WithButton(MessageCards.Link("Website", "https://grepobot.net/", "🌐"));

        container.WithActionRow(buttons)
            .WithSeparator(SeparatorSpacingSize.Small, true)
            .WithTextDisplay(isMention
                ? "-# This is an automatic reply, the bot does not read tags."
                : "-# This is an automatic reply, messages sent here are not read.");

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    internal static string BuildText(string serverName, ulong? ticketChannelId, ulong? forumChannelId, bool isMention)
    {
        var tickets = ticketChannelId is { } t ? $"<#{t}>" : "the support channel";
        var forum = forumChannelId is { } f ? $"<#{f}>" : "the feedback forum";
        var intro = isMention
            ? $"I'm the helper bot of **{serverName}**, so I can't answer questions myself. But we'd love to help you:"
            : $"I'm the helper bot of the **{serverName}** Discord server, so I can't read or answer messages here. But we'd love to help you:";

        return
            intro + "\n\n" +
            $"🎫 **Need 1-on-1 help?** Open a ticket in {tickets}. Only you and the team can see it, and we'll answer you there as soon as we can.\n\n" +
            $"💡 **Have an idea or found a bug?** Create a post in {forum}, so others can vote on it and the team can track it.\n\n" +
            "💬 **Just a quick question?** Feel free to ask in the server chat, the community is happy to help.\n\n" +
            "Thanks for using GrepoBot! 🙂";
    }

    #endregion
}
