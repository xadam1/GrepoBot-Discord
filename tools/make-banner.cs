#:package SkiaSharp@3.119.1
#:package SkiaSharp.NativeAssets.Win32@3.119.1

using SkiaSharp;

// args: <background.png> <icon_transparent.png> <icon_bg.png> <outDir>
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

    // Icon with a cyan glow. Content sits right of center: Discord draws the avatar over the bottom-left of a profile banner.
    using var titleFont = new SKFont(SKTypeface.FromFile(@"C:\Windows\Fonts\georgiab.ttf"), 116);
    using var subFont = new SKFont(SKTypeface.FromFile(@"C:\Windows\Fonts\bahnschrift.ttf"), 29);
    const string subtitle = "AUTOMATION  &  HELPER  FOR  GREPOLIS";
    var titleW = titleFont.MeasureText("GrepoBot");
    var subW = subFont.MeasureText(subtitle);
    var iconH = 230f;
    var iconW = icon.Width * iconH / icon.Height;
    const float gap = 40f;
    var groupW = iconW + gap + Math.Max(titleW, subW + 6);
    var iconX = W * 0.58f - groupW / 2;
    var iconY = (H - iconH) / 2 - 6;
    using (var glow = new SKPaint { IsAntialias = true })
    {
        glow.Color = new SKColor(0x3F, 0xD8, 0xF5, 0x70);
        glow.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 40);
        c.DrawCircle(iconX + iconW / 2, iconY + iconH / 2, iconH * 0.42f, glow);
    }

    using (var shadow = new SKPaint { IsAntialias = true })
    {
        shadow.ImageFilter = SKImageFilter.CreateDropShadow(0, 10, 18, 18, new SKColor(0, 0, 0, 0x90));
        c.DrawImage(SKImage.FromBitmap(icon), SKRect.Create(iconX, iconY, iconW, iconH), new SKSamplingOptions(SKCubicResampler.Mitchell), shadow);
    }

    // Wordmark.
    var textX = iconX + iconW + gap;
    using (var title = new SKPaint { IsAntialias = true })
    {
        title.ImageFilter = SKImageFilter.CreateDropShadow(0, 6, 12, 12, new SKColor(0, 0, 0, 0xA0));
        title.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 160), new SKPoint(0, 280),
            [SKColors.White, new SKColor(0xD6, 0xE4, 0xFF)], SKShaderTileMode.Clamp);
        c.DrawText("GrepoBot", textX, 262, titleFont, title);
    }

    using (var sub = new SKPaint { IsAntialias = true, Color = new SKColor(0x6F, 0xE6, 0xFA) })
    {
        sub.ImageFilter = SKImageFilter.CreateDropShadow(0, 3, 6, 6, new SKColor(0, 0, 0, 0xA0));
        c.DrawText(subtitle, textX + 6, 314, subFont, sub);
    }

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(Path.Combine(outDir, "banner.png"), data.ToArray());
}

// Avatar: the icon with its background at 512 px (Discord shows it at most at 128 px, 512 keeps it sharp).
using (var avatar = iconBg.Resize(new SKImageInfo(512, 512), new SKSamplingOptions(SKCubicResampler.Mitchell)))
using (var image = SKImage.FromBitmap(avatar))
using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
{
    File.WriteAllBytes(Path.Combine(outDir, "avatar.png"), data.ToArray());
}

Console.WriteLine("done");
