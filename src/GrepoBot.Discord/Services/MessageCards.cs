using Discord;
using Discord.Net;
using Discord.WebSocket;
using GrepoBot.Discord.Logic;

namespace GrepoBot.Discord.Services;

/// <summary>
/// Helpers for the bot's Components v2 cards (rules, links, status, ticket panel, welcome): a container with
/// text, images and buttons inside instead of an embed. Cards are edited in place when they exist.
/// </summary>
public static class MessageCards
{
    #region Public Methods

    /// <summary>The bot's avatar, used as the logo thumbnail of the cards.</summary>
    public static string Logo(IGuild guild)
    {
        var self = ((SocketGuild)guild).CurrentUser;
        return self.GetDisplayAvatarUrl(ImageFormat.Png, 256);
    }

    /// <summary>A text block with the bot logo on the right.</summary>
    public static SectionBuilder WithLogo(string logoUrl, params string[] texts)
    {
        return new SectionBuilder(
            new ThumbnailBuilder(new UnfurledMediaItemProperties(logoUrl), "GrepoBot", false),
            texts.Select(t => new TextDisplayBuilder(t)));
    }

    /// <summary>A text block with a button on the right, e.g. a link button.</summary>
    public static SectionBuilder WithButton(ButtonBuilder button, params string[] texts)
    {
        return new SectionBuilder(button, texts.Select(t => new TextDisplayBuilder(t)));
    }

    /// <summary>The bundled banner as an image of the card (its file must be sent with the message).</summary>
    public static MediaGalleryBuilder Banner()
    {
        return new MediaGalleryBuilder().AddItem(BotBranding.BannerAttachmentUrl, "GrepoBot", false);
    }

    /// <summary>A link button.</summary>
    public static ButtonBuilder Link(string label, string url, string emoji)
    {
        return new ButtonBuilder(label, style: ButtonStyle.Link, url: url, emote: new Emoji(emoji));
    }

    /// <summary>
    /// Edits <paramref name="existing"/> into the card, or posts the card when there is no message (or it cannot
    /// be turned into a card, e.g. an old embed message Discord refuses to convert). <paramref name="files"/> are
    /// sent again with every edit, so <c>attachment://</c> images keep working.
    /// </summary>
    public static async Task<IUserMessage> UpsertAsync(ITextChannel channel, IUserMessage? existing, MessageComponent card, Func<IReadOnlyList<FileAttachment>>? files = null, AllowedMentions? mentions = null)
    {
        if (existing is not null)
        {
            var attachments = files?.Invoke() ?? [];
            try
            {
                await existing.ModifyAsync(p =>
                {
                    p.Content = string.Empty;
                    p.Embeds = Array.Empty<Embed>();
                    p.Components = card;
                    p.Flags = MessageFlags.ComponentsV2;
                    p.AllowedMentions = mentions ?? AllowedMentions.None;
                    p.Attachments = attachments.ToArray();
                });
                return existing;
            }
            catch (HttpException) when (!existing.Flags.GetValueOrDefault().HasFlag(MessageFlags.ComponentsV2))
            {
                // Discord could not turn the old embed message into a card: post a new one below.
            }
            finally
            {
                DisposeAll(attachments);
            }
        }

        if (files is null)
        {
            return await channel.SendMessageAsync(components: card, allowedMentions: mentions ?? AllowedMentions.None, flags: MessageFlags.ComponentsV2);
        }

        var newAttachments = files();
        try
        {
            return newAttachments.Count == 0
                ? await channel.SendMessageAsync(components: card, allowedMentions: mentions ?? AllowedMentions.None, flags: MessageFlags.ComponentsV2)
                : await channel.SendFilesAsync(newAttachments, components: card, allowedMentions: mentions ?? AllowedMentions.None, flags: MessageFlags.ComponentsV2);
        }
        finally
        {
            DisposeAll(newAttachments);
        }
    }

    /// <summary>Wraps one file for <see cref="UpsertAsync"/>.</summary>
    public static Func<IReadOnlyList<FileAttachment>> One(Func<FileAttachment> file)
    {
        return () => [file()];
    }

    public static void DisposeAll(IEnumerable<FileAttachment> files)
    {
        foreach (var file in files)
        {
            file.Dispose();
        }
    }

    /// <summary>All buttons of a message, also those inside containers and sections.</summary>
    public static IEnumerable<ButtonComponent> Buttons(IEnumerable<IMessageComponent> components)
    {
        foreach (var component in components)
        {
            switch (component)
            {
                case ButtonComponent button:
                    yield return button;
                    break;
                case ActionRowComponent row:
                    foreach (var inner in Buttons(row.Components))
                    {
                        yield return inner;
                    }

                    break;
                case ContainerComponent container:
                    foreach (var inner in Buttons(container.Components))
                    {
                        yield return inner;
                    }

                    break;
                case SectionComponent section:
                    foreach (var inner in Buttons(section.Components.Append(section.Accessory)))
                    {
                        yield return inner;
                    }

                    break;
            }
        }
    }

    /// <summary>All text of a message's text displays, also inside containers and sections.</summary>
    public static IEnumerable<string> Texts(IEnumerable<IMessageComponent> components)
    {
        foreach (var component in components)
        {
            switch (component)
            {
                case TextDisplayComponent text:
                    yield return text.Content;
                    break;
                case ContainerComponent container:
                    foreach (var inner in Texts(container.Components))
                    {
                        yield return inner;
                    }

                    break;
                case SectionComponent section:
                    foreach (var inner in Texts(section.Components))
                    {
                        yield return inner;
                    }

                    break;
            }
        }
    }

    #endregion
}
