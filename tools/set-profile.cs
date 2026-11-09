#:package Discord.Net.Rest@3.20.2

using System.Text.Json;
using Discord;
using Discord.Rest;

// Uploads the bot's profile banner (src/GrepoBot.Discord/Assets/banner.png), and the avatar when a path is given.
//
//   dotnet run tools/set-profile.cs -- [avatar.png]
//
// The token comes from DISCORD_TOKEN or src/GrepoBot.Discord/appsettings.Local.json.

var token = Environment.GetEnvironmentVariable("DISCORD_TOKEN") ?? ReadLocalToken();
var bannerPath = Path.Combine("src", "GrepoBot.Discord", "Assets", "banner.png");
var avatarPath = args.FirstOrDefault();

await using var client = new DiscordRestClient();
await client.LoginAsync(TokenType.Bot, token);

using var banner = new Image(bannerPath);
using var avatar = avatarPath is null ? (Image?)null : new Image(avatarPath);
await client.CurrentUser.ModifyAsync(p =>
{
    p.Banner = banner;
    if (avatar is { } image)
    {
        p.Avatar = image;
    }
});

Console.WriteLine($"Updated the profile of {client.CurrentUser.Username}.");

static string ReadLocalToken()
{
    var path = Path.Combine("src", "GrepoBot.Discord", "appsettings.Local.json");
    using var json = JsonDocument.Parse(File.ReadAllText(path));
    return json.RootElement.GetProperty("Discord").GetProperty("Token").GetString()
        ?? throw new InvalidOperationException("No token in appsettings.Local.json.");
}
