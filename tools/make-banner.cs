#:package SkiaSharp@3.119.1
#:package SkiaSharp.NativeAssets.Win32@3.119.1
#:package SkiaSharp.NativeAssets.macOS@3.119.1

using SkiaSharp;

// args: <background.png> <icon_transparent.png> <icon_bg.png> <outDir>
// The banner is a wordmark on the app's background, the avatar is the icon with its background.
var background = SKBitmap.Decode(args[0]);
var icon = SKBitmap.Decode(args[1]);
var iconBg = SKBitmap.Decode(args[2]);
var outDir = args[3];
Directory.CreateDirectory(outDir);

const int W = 1360;
const int H = 480;

using (var surface = SKSurface.Create(new SKImageInfo(W, H)))
{
    var c = surface.Canvas;

    // Background: cover-crop, biased towards the sunset in the middle.
    var scale = Math.Max((float)W / background.Width, (float)H / background.Height);
    var bw = background.Width * scale;
    var bh = background.Height * scale;
    var dest = SKRect.Create((W - bw) / 2, (H - bh) * 0.42f, bw, bh);
    using (var paint = new SKPaint { IsAntialias = true })
    {
        c.DrawImage(SKImage.FromBitmap(background), dest, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
    }

    // Navy wash so text reads, stronger at the edges, a little sunset left in the middle.
    using (var wash = new SKPaint())
    {
        wash.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(W, 0),
            [new SKColor(0x14, 0x1A, 0x3D, 0xF0), new SKColor(0x1E, 0x27, 0x5A, 0x80), new SKColor(0x14, 0x1A, 0x3D, 0xE8)],
            [0f, 0.5f, 1f], SKShaderTileMode.Clamp);
        c.DrawRect(0, 0, W, H, wash);
    }

    using (var vignette = new SKPaint())
    {
        vignette.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(0, H),
            [new SKColor(0, 0, 0, 0x30), new SKColor(0, 0, 0, 0x00), new SKColor(0x0A, 0x0E, 0x24, 0xC0)],
            [0f, 0.45f, 1f], SKShaderTileMode.Clamp);
        c.DrawRect(0, 0, W, H, vignette);
    }

    // Thin cyan line at the bottom, the visor color.
    using (var line = new SKPaint { IsAntialias = true })
    {
        line.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(W, 0),
            [new SKColor(0x3F, 0xD8, 0xF5, 0x00), new SKColor(0x3F, 0xD8, 0xF5, 0xFF), new SKColor(0x7B, 0x8C, 0xF5, 0xFF), new SKColor(0x7B, 0x8C, 0xF5, 0x00)],
            [0f, 0.3f, 0.7f, 1f], SKShaderTileMode.Clamp);
        c.DrawRect(0, H - 6, W, 6, line);
    }

    // Wordmark only, no robot: Discord draws the avatar (the robot) over the bottom-left of a profile
    // banner, a second robot next to it looked doubled. The text sits a little right of center: clear of
    // the avatar on the left and of the profile's "..." button in the top-right corner.
    // Fonts of grepobot.net: Space Grotesk for the wordmark, Inter for the subtitle.
    using var titleFont = new SKFont(LoadFont("SpaceGrotesk-Bold.ttf"), 136);
    using var subFont = new SKFont(LoadFont("Inter-SemiBold.ttf"), 28);
    const string subtitle = "AUTOMATION  &  HELPER  FOR  GREPOLIS";
    var titleW = titleFont.MeasureText("GrepoBot");
    var subW = subFont.MeasureText(subtitle) + (subtitle.Length - 1) * 2;
    var centerX = W * 0.585f;
    const float titleY = 235f;
    const float subY = 328f;

    // Soft cyan glow behind the wordmark, the visor color.
    using (var glow = new SKPaint { IsAntialias = true })
    {
        glow.Color = new SKColor(0x3F, 0xD8, 0xF5, 0x38);
        glow.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 70);
        c.DrawOval(centerX, 220, titleW * 0.55f, 90, glow);
    }

    using (var title = new SKPaint { IsAntialias = true })
    {
        title.ImageFilter = SKImageFilter.CreateDropShadow(0, 6, 12, 12, new SKColor(0, 0, 0, 0xA0));
        title.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, titleY - 110), new SKPoint(0, titleY + 10),
            [SKColors.White, new SKColor(0xD6, 0xE4, 0xFF)], SKShaderTileMode.Clamp);
        c.DrawText("GrepoBot", centerX - titleW / 2, titleY, titleFont, title);
    }

    // Short visor-colored rule between title and subtitle.
    using (var rule = new SKPaint { IsAntialias = true })
    {
        rule.Shader = SKShader.CreateLinearGradient(
            new SKPoint(centerX - subW / 2, 0), new SKPoint(centerX + subW / 2, 0),
            [new SKColor(0x3F, 0xD8, 0xF5, 0x00), new SKColor(0x3F, 0xD8, 0xF5, 0xE0), new SKColor(0x3F, 0xD8, 0xF5, 0x00)],
            [0f, 0.5f, 1f], SKShaderTileMode.Clamp);
        c.DrawRect(centerX - subW / 2, titleY + 42, subW, 2, rule);
    }

    using (var sub = new SKPaint { IsAntialias = true, Color = new SKColor(0x6F, 0xE6, 0xFA) })
    {
        sub.ImageFilter = SKImageFilter.CreateDropShadow(0, 3, 6, 6, new SKColor(0, 0, 0, 0xA0));
        // Slight letter spacing, like the website's small caps labels.
        var x = centerX - subW / 2;
        foreach (var ch in subtitle)
        {
            var text = ch.ToString();
            c.DrawText(text, x, subY, subFont, sub);
            x += subFont.MeasureText(text) + 2;
        }
    }

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(Path.Combine(outDir, "banner.png"), data.ToArray());
}

// Welcome card background: the same scene without text, the bot draws the member's avatar and name on it.
const int WW = 1100;
const int WH = 420;
using (var surface = SKSurface.Create(new SKImageInfo(WW, WH)))
{
    var c = surface.Canvas;
    var scale = Math.Max((float)WW / background.Width, (float)WH / background.Height);
    var bw = background.Width * scale;
    var bh = background.Height * scale;
    using (var paint = new SKPaint { IsAntialias = true })
    {
        c.DrawImage(SKImage.FromBitmap(background), SKRect.Create((WW - bw) / 2, (WH - bh) * 0.42f, bw, bh), new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
    }

    // Darker on the right where the text goes, the scene stays visible behind the avatar.
    using (var wash = new SKPaint())
    {
        wash.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(WW, 0),
            [new SKColor(0x14, 0x1A, 0x3D, 0xB0), new SKColor(0x14, 0x1A, 0x3D, 0xD8), new SKColor(0x10, 0x15, 0x33, 0xF0)],
            [0f, 0.35f, 1f], SKShaderTileMode.Clamp);
        c.DrawRect(0, 0, WW, WH, wash);
    }

    using (var line = new SKPaint { IsAntialias = true })
    {
        line.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(WW, 0),
            [new SKColor(0x3F, 0xD8, 0xF5, 0x00), new SKColor(0x3F, 0xD8, 0xF5, 0xFF), new SKColor(0x7B, 0x8C, 0xF5, 0xFF), new SKColor(0x7B, 0x8C, 0xF5, 0x00)],
            [0f, 0.3f, 0.7f, 1f], SKShaderTileMode.Clamp);
        c.DrawRect(0, WH - 6, WW, 6, line);
    }

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(Path.Combine(outDir, "welcome-background.png"), data.ToArray());
}

// Avatar: the icon with its background at 512 px (Discord shows it at most at 128 px, 512 keeps it sharp).
using (var avatar = iconBg.Resize(new SKImageInfo(512, 512), new SKSamplingOptions(SKCubicResampler.Mitchell)))
using (var image = SKImage.FromBitmap(avatar))
using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
{
    File.WriteAllBytes(Path.Combine(outDir, "avatar.png"), data.ToArray());
}

Console.WriteLine("done");

// The fonts of grepobot.net, bundled with the bot (Assets/Fonts, SIL Open Font License).
static SKTypeface LoadFont(string file)
{
    var path = Path.Combine(AppContext.GetData("EntryPointFilePath") is string script ? Path.GetDirectoryName(script)! : ".",
        "..", "src", "GrepoBot.Discord", "Assets", "Fonts", file);
    if (!File.Exists(path))
    {
        path = Path.Combine("src", "GrepoBot.Discord", "Assets", "Fonts", file);
    }

    return SKTypeface.FromFile(path) ?? throw new FileNotFoundException($"Font {file} not found.", path);
}
