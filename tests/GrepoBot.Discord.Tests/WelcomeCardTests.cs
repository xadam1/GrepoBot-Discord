using GrepoBot.Discord.Logic;
using GrepoBot.Discord.Services;
using SkiaSharp;

namespace GrepoBot.Discord.Tests;

/// <summary>
/// Draws the welcome card the way the bot does. The PNGs land in the temp folder
/// (grepobot-welcome-*.png) for a visual check.
/// </summary>
public class WelcomeCardTests
{
    #region Tests

    [Theory]
    [InlineData("short", "Bob")]
    [InlineData("accents", "Žluťoučký kůň Přemysl")]
    [InlineData("long", "AVeryLongDisplayNameThatGoesOnAndOnForever_1234567890")]
    [InlineData("mixed", "Ζεύς ⚡ Отец 🌩️")]
    public void Render_ProducesPng(string label, string name)
    {
        var png = WelcomeCard.Render(MakeAvatar(), name, "GrepoBot", 166, WelcomeQuips.All[0]);

        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(1100, bitmap.Width);
        Assert.Equal(420, bitmap.Height);
        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), $"grepobot-welcome-{label}.png"), png);
    }

    [Fact]
    public void Render_WithoutAvatar_Works()
    {
        Assert.NotEmpty(WelcomeCard.Render(null, "Bob", "GrepoBot", 1, WelcomeQuips.Pick()));
    }

    #endregion

    #region Helpers

    private static byte[] MakeAvatar()
    {
        using var surface = SKSurface.Create(new SKImageInfo(256, 256));
        surface.Canvas.Clear(new SKColor(0xE0, 0x7A, 0x3F));
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        surface.Canvas.DrawCircle(128, 100, 50, paint);
        surface.Canvas.DrawCircle(128, 250, 100, paint);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    #endregion
}
