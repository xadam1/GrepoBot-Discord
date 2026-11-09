using SkiaSharp;

namespace GrepoBot.Discord.Services;

/// <summary>
/// Draws the personal welcome card: the member's avatar in a glowing ring, "WELCOME TO …", their name,
/// a playful line and the member number, on the GrepoBot harbour background.
/// It uses the fonts of grepobot.net (Space Grotesk and Inter, bundled in Assets/Fonts). Names in other scripts
/// fall back to any installed font that has the characters (DejaVu on the Pi).
/// </summary>
public static class WelcomeCard
{
    #region Private Fields

    public const string FILE_NAME = "welcome.png";

    private const int WIDTH = 1100;
    private const int HEIGHT = 420;
    private const float AVATAR_SIZE = 250;
    private const float AVATAR_X = 85;
    private const float TEXT_X = 395;
    private const float TEXT_MAX_WIDTH = WIDTH - TEXT_X - 50;

    private static readonly SKColor _accent = new(0x3F, 0xD8, 0xF5);
    private static readonly SKColor _soft = new(0xD6, 0xE4, 0xFF);
    private static readonly SKColor _navy = new(0x2A, 0x35, 0x6E);

    private static readonly string[] _boldFamilies = ["Avenir Next", "Helvetica Neue", "Segoe UI", "DejaVu Sans", "Noto Sans", "Liberation Sans", "Arial"];

    #endregion

    #region Properties

    public static string BackgroundPath => Path.Combine(AppContext.BaseDirectory, "Assets", "welcome-background.png");

    #endregion

    #region Public Methods

    /// <summary>Renders the card as PNG. <paramref name="avatarPng"/> may be null, a navy circle is drawn then.</summary>
    public static byte[] Render(byte[]? avatarPng, string displayName, string serverName, int memberNumber, string quip)
    {
        using var surface = SKSurface.Create(new SKImageInfo(WIDTH, HEIGHT, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        DrawBackground(canvas);
        DrawAvatar(canvas, avatarPng);

        using var heading = LoadTypeface("SpaceGrotesk-Bold.ttf", SKFontStyle.Bold);
        using var bold = LoadTypeface("Inter-Bold.ttf", SKFontStyle.Bold);
        using var semiBold = LoadTypeface("Inter-SemiBold.ttf", SKFontStyle.Bold);
        using var medium = LoadTypeface("Inter-Medium.ttf", SKFontStyle.Normal);

        var y = 125f;
        DrawText(canvas, $"WELCOME TO {serverName.ToUpperInvariant()}", TEXT_X, y, bold, 28, _accent, TEXT_MAX_WIDTH, letterSpacing: 3);

        // The name is the hero: as big as it fits, shrinking for long names.
        var nameSize = FitSize(displayName, heading, 86, 44, TEXT_MAX_WIDTH);
        y += 18 + nameSize;
        DrawText(canvas, displayName, TEXT_X, y, heading, nameSize, SKColors.White, TEXT_MAX_WIDTH, shadow: true);

        y += 62;
        DrawText(canvas, quip, TEXT_X, y, medium, 31, _soft, TEXT_MAX_WIDTH);

        y += 64;
        DrawText(canvas, $"Member #{memberNumber:N0}", TEXT_X, y, semiBold, 26, _accent.WithAlpha(0xD0), TEXT_MAX_WIDTH, letterSpacing: 1);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Renders the release banner as PNG: the robot in the glowing ring, "NEW UPDATE", "GrepoBot 1.4.0" with the
    /// version in cyan, the headline and the date, on the same background as the welcome card.
    /// </summary>
    public static byte[] RenderRelease(string version, string? headline, DateTimeOffset date)
    {
        using var surface = SKSurface.Create(new SKImageInfo(WIDTH, HEIGHT, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        DrawBackground(canvas);
        var avatarPath = Path.Combine(AppContext.BaseDirectory, "Assets", "avatar.png");
        DrawAvatar(canvas, File.Exists(avatarPath) ? File.ReadAllBytes(avatarPath) : null);

        using var heading = LoadTypeface("SpaceGrotesk-Bold.ttf", SKFontStyle.Bold);
        using var bold = LoadTypeface("Inter-Bold.ttf", SKFontStyle.Bold);
        using var semiBold = LoadTypeface("Inter-SemiBold.ttf", SKFontStyle.Bold);
        using var medium = LoadTypeface("Inter-Medium.ttf", SKFontStyle.Normal);

        var y = 125f;
        DrawText(canvas, "NEW UPDATE", TEXT_X, y, bold, 28, _accent, TEXT_MAX_WIDTH, letterSpacing: 4);

        // "GrepoBot" in white and the version in cyan, as big as both fit.
        var name = "GrepoBot ";
        var size = FitSize(name + version, heading, 86, 44, TEXT_MAX_WIDTH);
        y += 18 + size;
        DrawText(canvas, name, TEXT_X, y, heading, size, SKColors.White, TEXT_MAX_WIDTH, shadow: true);
        var nameWidth = MeasureRuns(name, heading, size);
        DrawText(canvas, version, TEXT_X + nameWidth, y, heading, size, _accent, TEXT_MAX_WIDTH - nameWidth, shadow: true);

        y += 62;
        DrawText(canvas, string.IsNullOrWhiteSpace(headline) ? "Fresh features and fixes are here!" : headline.Trim(), TEXT_X, y, medium, 31, _soft, TEXT_MAX_WIDTH);

        y += 64;
        DrawText(canvas, date.ToLocalTime().ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture), TEXT_X, y, semiBold, 26, _accent.WithAlpha(0xD0), TEXT_MAX_WIDTH, letterSpacing: 1);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    #endregion

    #region Private Methods

    private static void DrawBackground(SKCanvas canvas)
    {
        if (File.Exists(BackgroundPath))
        {
            using var background = SKBitmap.Decode(BackgroundPath);
            if (background is not null)
            {
                using var paint = new SKPaint { IsAntialias = true };
                canvas.DrawImage(SKImage.FromBitmap(background), SKRect.Create(0, 0, WIDTH, HEIGHT), new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
                return;
            }
        }

        using var gradient = new SKPaint();
        gradient.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(WIDTH, HEIGHT),
            [new SKColor(0x1E, 0x27, 0x5A), new SKColor(0x10, 0x15, 0x33)],
            SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, WIDTH, HEIGHT, gradient);
    }

    private static void DrawAvatar(SKCanvas canvas, byte[]? avatarPng)
    {
        var cx = AVATAR_X + AVATAR_SIZE / 2;
        var cy = HEIGHT / 2f;
        var radius = AVATAR_SIZE / 2;

        using (var glow = new SKPaint { IsAntialias = true, Color = _accent.WithAlpha(0x70) })
        {
            glow.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 28);
            canvas.DrawCircle(cx, cy, radius + 10, glow);
        }

        using (var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 8 })
        {
            ring.Shader = SKShader.CreateSweepGradient(
                new SKPoint(cx, cy),
                [_accent, new SKColor(0x7B, 0x8C, 0xF5), _accent],
                [0f, 0.5f, 1f]);
            canvas.DrawCircle(cx, cy, radius + 6, ring);
        }

        using var avatar = avatarPng is null ? null : SKBitmap.Decode(avatarPng);
        canvas.Save();
        using (var clip = new SKPath())
        {
            clip.AddCircle(cx, cy, radius);
            canvas.ClipPath(clip, antialias: true);
        }

        if (avatar is not null)
        {
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawImage(SKImage.FromBitmap(avatar), SKRect.Create(cx - radius, cy - radius, AVATAR_SIZE, AVATAR_SIZE), new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        }
        else
        {
            using var fill = new SKPaint { Color = _navy };
            canvas.DrawCircle(cx, cy, radius, fill);
        }

        canvas.Restore();
    }

    /// <summary>
    /// Draws text in runs: characters the main font lacks (other scripts, symbols) use a font that has them,
    /// characters no font has are skipped. Text that is too wide ends with "…".
    /// </summary>
    private static void DrawText(SKCanvas canvas, string text, float x, float y, SKTypeface typeface, float size, SKColor color, float maxWidth, float letterSpacing = 0, bool shadow = false)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = color };
        if (shadow)
        {
            paint.ImageFilter = SKImageFilter.CreateDropShadow(0, 4, 8, 8, new SKColor(0, 0, 0, 0xA0));
        }

        var runs = SplitRuns(text, typeface);
        var cursor = x;
        foreach (var (runText, runFace) in runs)
        {
            using var font = new SKFont(runFace, size) { Edging = SKFontEdging.SubpixelAntialias };
            foreach (var element in EnumerateElements(runText))
            {
                var width = font.MeasureText(element) + letterSpacing;
                if (cursor + width > x + maxWidth)
                {
                    using var ellipsisFont = new SKFont(typeface, size);
                    canvas.DrawText("…", cursor, y, ellipsisFont, paint);
                    return;
                }

                canvas.DrawText(element, cursor, y, font, paint);
                cursor += width;
            }
        }
    }

    /// <summary>The biggest size between max and min at which the text fits, min otherwise (it is then cut).</summary>
    private static float FitSize(string text, SKTypeface typeface, float max, float min, float maxWidth)
    {
        for (var size = max; size > min; size -= 2)
        {
            if (MeasureRuns(text, typeface, size) <= maxWidth)
            {
                return size;
            }
        }

        return min;
    }

    private static float MeasureRuns(string text, SKTypeface typeface, float size)
    {
        var total = 0f;
        foreach (var (runText, runFace) in SplitRuns(text, typeface))
        {
            using var font = new SKFont(runFace, size);
            total += font.MeasureText(runText);
        }

        return total;
    }

    private static List<(string Text, SKTypeface Typeface)> SplitRuns(string text, SKTypeface primary)
    {
        var runs = new List<(string, SKTypeface)>();
        var current = new System.Text.StringBuilder();
        SKTypeface? currentFace = null;

        foreach (var rune in text.EnumerateRunes())
        {
            var face = primary.ContainsGlyph(rune.Value)
                ? primary
                : SKFontManager.Default.MatchCharacter(rune.Value);

            if (face is null)
            {
                continue;
            }

            if (currentFace is not null && face.FamilyName != currentFace.FamilyName)
            {
                runs.Add((current.ToString(), currentFace));
                current.Clear();
            }

            currentFace = face;
            current.Append(rune.ToString());
        }

        if (currentFace is not null && current.Length > 0)
        {
            runs.Add((current.ToString(), currentFace));
        }

        return runs;
    }

    /// <summary>Text elements (letters with their accents, emoji sequences) so letter spacing never splits one.</summary>
    private static IEnumerable<string> EnumerateElements(string text)
    {
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            yield return enumerator.GetTextElement();
        }
    }

    /// <summary>A bundled font from Assets/Fonts, a matching installed font when the file is missing.</summary>
    private static SKTypeface LoadTypeface(string fileName, SKFontStyle fallbackStyle)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", fileName);
        if (File.Exists(path) && SKTypeface.FromFile(path) is { } typeface)
        {
            return typeface;
        }

        return MatchTypeface(fallbackStyle);
    }

    private static SKTypeface MatchTypeface(SKFontStyle style)
    {
        foreach (var family in _boldFamilies)
        {
            var face = SKFontManager.Default.MatchFamily(family, style);
            if (face is not null)
            {
                return face;
            }
        }

        return SKTypeface.CreateDefault();
    }

    #endregion
}
