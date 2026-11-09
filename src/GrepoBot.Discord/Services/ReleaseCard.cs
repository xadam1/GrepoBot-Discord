using Discord;
using Discord.WebSocket;
using GrepoBot.Discord.Logic;

namespace GrepoBot.Discord.Services;

/// <summary>
/// The release card: the version banner, "GrepoBot 1.4.0 is here!", the draft's intro and sections, its images,
/// the thanks and the download, changelog and feedback buttons. Files are sent with the message: the banner as
/// <see cref="BANNER_FILE_NAME"/>, then the draft's attachments.
/// </summary>
public static class ReleaseCard
{
    #region Properties

    public const string BANNER_FILE_NAME = "release.png";

    #endregion

    #region Public Methods

    /// <summary>
    /// Builds the card. <paramref name="fileNames"/> are the draft's attachments as sent (images go into a gallery,
    /// other files are shown as files). <paramref name="mention"/> (e.g. @everyone) is shown above the card.
    /// </summary>
    public static MessageComponent Build(IGuild guild, ReleaseDraft draft, IReadOnlyList<(string Name, bool IsImage)> fileNames, string? mention)
    {
        var builder = new ComponentBuilderV2();
        if (mention is not null)
        {
            builder.WithTextDisplay(mention);
        }

        var container = new ContainerBuilder()
            .WithAccentColor(BotBranding.Accent)
            .WithMediaGallery(new MediaGalleryBuilder().AddItem("attachment://" + BANNER_FILE_NAME, $"GrepoBot {draft.Version}", false))
            .WithTextDisplay($"# 🚀 GrepoBot {draft.Version} is here!" + (draft.Intro is null ? string.Empty : "\n" + draft.Intro));

        foreach (var section in draft.Sections)
        {
            container
                .WithSeparator(SeparatorSpacingSize.Small, true)
                .WithTextDisplay($"### {section.Title}\n{section.Body}");
        }

        var images = fileNames.Where(f => f.IsImage).Take(10).ToList();
        if (images.Count > 0)
        {
            var gallery = new MediaGalleryBuilder();
            foreach (var image in images)
            {
                gallery.AddItem("attachment://" + image.Name, null, false);
            }

            container.WithMediaGallery(gallery);
        }

        foreach (var file in fileNames.Where(f => !f.IsImage))
        {
            container.WithFile(new UnfurledMediaItemProperties("attachment://" + file.Name), false);
        }

        container
            .WithSeparator(SeparatorSpacingSize.Large, true)
            .WithSection(MessageCards.WithButton(MessageCards.Link("Download", "https://grepobot.net/download/", "⬇️"),
                ReleaseNotes.THANKS_TEXT,
                "-# Get the update from the official website."));

        var buttons = new ActionRowBuilder()
            .WithButton(MessageCards.Link("Changelog", "https://grepobot.net/changelog/", "📜"))
            .WithButton(MessageCards.Link("Features", "https://grepobot.net/features/", "✨"));
        var feedback = (guild as SocketGuild)?.Channels.FirstOrDefault(c => c is IForumChannel && ServerLayout.Matches(c.Name, ServerLayout.Channel("feedback").Aliases));
        if (feedback is not null)
        {
            buttons.WithButton(MessageCards.Link("Ideas & bugs", $"https://discord.com/channels/{guild.Id}/{feedback.Id}", "💡"));
        }

        container.WithActionRow(buttons);
        return builder.WithContainer(container).Build();
    }

    #endregion
}
