using Discord;

namespace GrepoBot.Discord.Logic;

/// <summary>
/// Colors and bundled images of the GrepoBot brand, taken from the app icon.
/// </summary>
public static class BotBranding
{
    #region Properties

    /// <summary>Cyan of the robot's visor.</summary>
    public static readonly Color Accent = new(0x3F, 0xD8, 0xF5);

    /// <summary>Navy of the icon background.</summary>
    public static readonly Color Navy = new(0x2A, 0x35, 0x6E);

    public static readonly Color Success = new(0x43, 0xB5, 0x81);

    public static readonly Color Warning = new(0xF0, 0xB2, 0x32);

    public static readonly Color Danger = new(0xE5, 0x4B, 0x4B);

    public const string BANNER_FILE_NAME = "banner.png";

    public static string AvatarPath => Path.Combine(AppContext.BaseDirectory, "Assets", "avatar.png");

    public static string BannerPath => Path.Combine(AppContext.BaseDirectory, "Assets", BANNER_FILE_NAME);

    /// <summary>Embed image URL for the banner sent as an attachment with the same message.</summary>
    public static string BannerAttachmentUrl => "attachment://" + BANNER_FILE_NAME;

    #endregion

    #region Public Methods

    public static bool HasBanner => File.Exists(BannerPath);

    /// <summary>The banner as a file to attach. Check <see cref="HasBanner"/> first and dispose the result.</summary>
    public static FileAttachment OpenBanner()
    {
        return new FileAttachment(BannerPath, BANNER_FILE_NAME);
    }

    #endregion
}
