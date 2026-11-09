using GrepoBot.Discord.Logic;
using GrepoBot.Discord.State;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrepoBot.Discord.Tests;

public class MessageSplitterTests
{
    #region Tests

    [Fact]
    public void ShortText_StaysOnePart()
    {
        Assert.Equal(["hello"], MessageSplitter.Split("hello"));
        Assert.Empty(MessageSplitter.Split(string.Empty));
    }

    [Fact]
    public void LongText_SplitsAtParagraphs_AndEveryPartFits()
    {
        var paragraph = new string('a', 900);
        var text = string.Join("\n\n", Enumerable.Repeat(paragraph, 4));

        var parts = MessageSplitter.Split(text);

        Assert.All(parts, p => Assert.True(p.Length <= MessageSplitter.MAX_LENGTH));
        Assert.Equal(2, parts.Count);
        Assert.Equal(text.Replace("\n", string.Empty), string.Concat(parts).Replace("\n", string.Empty));
    }

    [Fact]
    public void TextWithoutSeparators_IsCutHard()
    {
        var parts = MessageSplitter.Split(new string('x', 4500));

        Assert.Equal([2000, 2000, 500], parts.Select(p => p.Length));
    }

    #endregion
}

public class TextTemplateTests
{
    #region Tests

    [Fact]
    public void Render_ReplacesKnownPlaceholders_CaseInsensitive()
    {
        var result = TextTemplate.Render("Hi {User} on {server}, {unknown}", new Dictionary<string, string>
        {
            ["user"] = "@bob",
            ["server"] = "GrepoBot",
        });

        Assert.Equal("Hi @bob on GrepoBot, {unknown}", result);
    }

    #endregion
}

public class StateStoreTests : IDisposable
{
    #region Private Fields

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "grepobot-discord-tests-" + Guid.NewGuid());

    #endregion

    #region Tests

    [Fact]
    public void Update_IsSavedAndLoadedAgain()
    {
        var path = Path.Combine(_directory, "state.json");
        var store = new StateStore(path, NullLogger<StateStore>.Instance);

        store.Update(state =>
        {
            state.UpdatesRoleId = 42;
            state.AutoReply.Rules.Add(new AutoReplyRule { Name = "install", Keywords = ["install"], Mode = MatchMode.All });
        });

        var reloaded = new StateStore(path, NullLogger<StateStore>.Instance);
        Assert.Equal(42UL, reloaded.Read(s => s.UpdatesRoleId));
        Assert.Equal(MatchMode.All, reloaded.Read(s => s.AutoReply.Rules.Single().Mode));
        Assert.Contains("\"All\"", File.ReadAllText(path));
    }

    [Fact]
    public void BrokenFile_IsKeptAsBackup_AndDefaultsAreUsed()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "state.json");
        File.WriteAllText(path, "{ not json");

        var store = new StateStore(path, NullLogger<StateStore>.Instance);

        Assert.True(store.Read(s => s.AutoPublish));
        Assert.Single(Directory.GetFiles(_directory, "state.json.broken-*"));
    }

    [Fact]
    public void Posts_AreTrimmedToTheNewest()
    {
        var store = new StateStore(Path.Combine(_directory, "state.json"), NullLogger<StateStore>.Instance);

        store.Update(state =>
        {
            for (var i = 0; i < 250; i++)
            {
                state.Posts.Add(new PostRecord { SourceMessageId = (ulong)i, PostedAt = DateTimeOffset.UnixEpoch.AddDays(i) });
            }
        });

        Assert.Equal(200, store.Read(s => s.Posts.Count));
        Assert.Equal(50UL, store.Read(s => s.Posts.Min(p => p.SourceMessageId)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    #endregion
}

public class TicketNamingTests
{
    #region Tests

    [Fact]
    public void ChannelNames_AreNumberedAndSlugged()
    {
        Assert.Equal("ticket-0007-jadam", TicketNaming.OpenChannelName(7, "jadam"));
        Assert.Equal("closed-0012-zluty-kun", TicketNaming.ClosedChannelName(12, "Žlutý_Kůň"));
        Assert.Equal("ticket-0001", TicketNaming.OpenChannelName(1, "🙂"));
    }


    [Fact]
    public void Topics_AreFoundByValue_UnknownIsSomethingElse()
    {
        Assert.Equal("Login", TicketTopics.Find("login").Label);
        Assert.Equal("Something else", TicketTopics.Find("nonsense").Label);
        Assert.Equal("Something else", TicketTopics.Find(null).Label);
    }

    #endregion
}

public class RepostLayoutTests
{
    #region Tests

    [Fact]
    public void Plain_PingStartsTheText()
    {
        Assert.Equal([new RepostPart("@everyone\nNew version", null)], RepostLayout.Build("New version", null, true, null));
        Assert.Equal([new RepostPart("<@&5>\nNew version", null)], RepostLayout.Build("New version", 5, false, null));
        Assert.Equal([new RepostPart("New version", null)], RepostLayout.Build("New version", null, false, null));
    }

    [Fact]
    public void Colored_TextGoesIntoTheEmbed_PingStaysOutside()
    {
        Assert.Equal([new RepostPart("@everyone", "New release")], RepostLayout.Build("New release", null, true, 0x3FD8F5));
        Assert.Equal([new RepostPart(null, "New release")], RepostLayout.Build("New release", null, false, 0x3FD8F5));
    }

    [Fact]
    public void Colored_LongTextUsesTheEmbedLimit_EmptyTextGivesOnePart()
    {
        var text = string.Join("\n\n", Enumerable.Repeat(new string('a', 1500), 2));

        Assert.Single(RepostLayout.Build(text, null, false, 1));
        Assert.Equal(2, RepostLayout.Build(text, null, false, null).Count);
        Assert.Equal([new RepostPart("@everyone", null)], RepostLayout.Build(null, null, true, 1));
        Assert.Equal([new RepostPart(null, null)], RepostLayout.Build("", null, false, null));
    }

    [Fact]
    public void Colors_UnknownIsCyan_NoneIsPlain()
    {
        Assert.Equal("cyan", RepostLayout.FindColor("nonsense").Value);
        Assert.Null(RepostLayout.FindColor(RepostLayout.NO_COLOR).Rgb);
        Assert.All(RepostLayout.Colors, c => Assert.True(c.Label.Length <= 100));
    }

    [Theory]
    [InlineData("Screenshot 2026-11-08 at 10.00.png", "Screenshot_2026-11-08_at_10.00.png")]
    [InlineData("update.png", "update.png")]
    [InlineData("čeština.jpg", "_e_tina.jpg")]
    public void SafeFileName_ReplacesWhatDiscordWouldChange(string name, string expected)
    {
        Assert.Equal(expected, RepostLayout.SafeFileName(name));
    }

    #endregion
}

public class FeedbackForumTests
{
    #region Tests

    [Fact]
    public void MergeTags_KeepsExistingIdsByName_AndDropsUnwanted()
    {
        var merged = FeedbackForum.MergeTags([(1UL, "feature"), (2UL, "Bug"), (3UL, "In progress"), (4UL, "Planned")]);

        Assert.Equal(7, merged.Count);
        Assert.Equal(1UL, merged.Single(t => t.Tag.Name == "Feature").ExistingId);
        Assert.Equal(4UL, merged.Single(t => t.Tag.Name == "Planned").ExistingId);
        Assert.Null(merged.Single(t => t.Tag.Name == "Duplicate").ExistingId);
        Assert.DoesNotContain(merged, t => t.Tag.Name == "In progress");
        Assert.All(merged.Where(t => FeedbackForum.StatusTags.Values.Contains(t.Tag)), t => Assert.True(t.Tag.Moderated));
    }

    [Fact]
    public void ApplyStatus_ReplacesTheStatus_KeepsMemberTags()
    {
        var status = new Dictionary<ForumStatus, ulong>
        {
            [ForumStatus.Planned] = 10,
            [ForumStatus.Done] = 11,
            [ForumStatus.Declined] = 12,
            [ForumStatus.Duplicate] = 13,
        };

        Assert.Equal([1UL, 11UL], FeedbackForum.ApplyStatus([1, 10], status, ForumStatus.Done));
        Assert.Equal([1UL], FeedbackForum.ApplyStatus([1, 12], status, null));
        Assert.Equal([1UL, 2UL, 3UL, 4UL, 13UL], FeedbackForum.ApplyStatus([1, 2, 3, 4, 5], status, ForumStatus.Duplicate));
    }

    [Fact]
    public void Statuses_CloseAndDelete_AsDocumented()
    {
        Assert.False(FeedbackForum.Closes(ForumStatus.Planned));
        Assert.True(FeedbackForum.Closes(ForumStatus.Done));
        Assert.False(FeedbackForum.IsDeletedLater(ForumStatus.Done));
        Assert.True(FeedbackForum.IsDeletedLater(ForumStatus.Declined));
        Assert.True(FeedbackForum.IsDeletedLater(ForumStatus.Duplicate));
    }

    [Theory]
    [InlineData("https://discord.com/channels/1/22", 22UL)]
    [InlineData("https://discord.com/channels/1/22/333", 22UL)]
    [InlineData("<#22>", 22UL)]
    [InlineData("22", 22UL)]
    [InlineData("not a link", null)]
    public void ParsePostId_ReadsLinksAndIds(string text, ulong? expected)
    {
        Assert.Equal(expected, FeedbackForum.ParsePostId(text));
    }

    [Fact]
    public void Texts_NeverMentionVersions()
    {
        var texts = new[]
        {
            FeedbackForum.GUIDELINES,
            TicketSettings.DEFAULT_MESSAGE,
            GrepoBot.Discord.Modules.TicketPanelModal.DEFAULT_TEXT,
            WelcomeSettings.DEFAULT_MESSAGE,
            GrepoBot.Discord.Services.DirectMessageService.BuildText("GrepoBot", 1, 2),
            ServerLayout.RULES_TEXT,
            ServerLayout.TEMPLATES_GUIDELINES,
            ReleaseNotes.THANKS_TEXT,
        };
        texts = texts
            .Concat(AppStatus.Looks.Values.Select(l => l.DefaultText))
            .Concat(ServerLayout.Categories.SelectMany(c => c.Channels).Select(c => c.Topic ?? string.Empty))
            .ToArray();

        Assert.All(texts, t => Assert.DoesNotContain("version", t, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DirectMessageText_LinksTicketsAndForum()
    {
        var text = GrepoBot.Discord.Services.DirectMessageService.BuildText("GrepoBot", 5, 6);

        Assert.Contains("<#5>", text);
        Assert.Contains("<#6>", text);
        Assert.Contains("the support channel", GrepoBot.Discord.Services.DirectMessageService.BuildText("GrepoBot", null, null));
    }

    #endregion
}

public class StateMigrationTests
{
    #region Tests

    [Fact]
    public void OldDefaultTexts_AreReplaced_CustomTextsKept()
    {
        var state = new BotState();
        state.Tickets.Message = "Hi {user}, thanks for reaching out! 👋\n\nA team member will be with you soon. Meanwhile, please describe your problem in as much detail as you can: your GrepoBot version, what you did and what happened. Screenshots help a lot.";
        state.Welcome.Message = "My own welcome";

        StateMigrations.Apply(state);

        Assert.Equal(TicketSettings.DEFAULT_MESSAGE, state.Tickets.Message);
        Assert.Equal("My own welcome", state.Welcome.Message);
    }

    #endregion
}

public class BotStatusesTests
{
    [Fact]
    public void Statuses_FitDiscordAndNeverMentionVersions()
    {
        foreach (var status in BotStatuses.All)
        {
            Assert.InRange(status.Length, 1, 128);
            Assert.DoesNotContain("version", status, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void At_WrapsAround()
    {
        Assert.Equal(BotStatuses.All[0], BotStatuses.At(0));
        Assert.Equal(BotStatuses.All[1], BotStatuses.At(BotStatuses.All.Count + 1));
    }
}

public class ServerLayoutTests
{
    [Theory]
    [InlineData("💬・general", "general")]
    [InlineData("╭──・🔥General🔥・──╮", "general")]
    [InlineData("╭──・🗑️Delete me🗑️・──╮", "delete me")]
    [InlineData("GrepoBot Developer", "grepobot developer")]
    [InlineData("🟢・bot-status", "bot-status")]
    public void Slug_StripsEmojiAndDecoration(string name, string expected)
    {
        Assert.Equal(expected, ServerLayout.Slug(name));
    }

    [Fact]
    public void Layout_NamesAreValidAndUnique()
    {
        var channels = ServerLayout.Categories.SelectMany(c => c.Channels).ToList();

        Assert.Equal(channels.Count, channels.Select(c => c.Key).Distinct().Count());
        Assert.All(channels, c => Assert.InRange(c.Name.Length, 1, 100));
        Assert.All(channels, c => Assert.True(c.Topic is null || c.Topic.Length <= 1024));
        Assert.All(channels, c => Assert.Contains(ServerLayout.Slug(c.Name), c.Aliases));
        Assert.All(ServerLayout.Categories, c => Assert.Contains(ServerLayout.Slug(c.Name), c.Aliases));
        Assert.All(ServerLayout.Roles, r => Assert.Contains(ServerLayout.Slug(r.Name), r.Aliases));
        Assert.All(ServerLayout.Roles, r => Assert.All(r.AddPermissions, p => Enum.Parse<global::Discord.GuildPermission>(p)));
    }

    [Fact]
    public void Layout_RenamesTheOldChannels()
    {
        Assert.True(ServerLayout.Matches("🪵・bot-farming", ServerLayout.Channel("help").Aliases));
        Assert.True(ServerLayout.Matches("🌐・website", ServerLayout.Channel("links").Aliases));
        Assert.True(ServerLayout.Matches("🔴・bot-status", ServerLayout.Channel("status").Aliases));
        Assert.True(ServerLayout.Matches("GrepoBot Developer", ServerLayout.Roles[0].Aliases));
        Assert.True(ServerLayout.Matches("Discord Moderator", ServerLayout.Roles[1].Aliases));
    }

    [Fact]
    public void Rules_FitAnEmbedAndLinkTheChannels()
    {
        var rules = ServerLayout.RenderRules(new Dictionary<string, ulong> { ["help"] = 1, ["feedback"] = 2, ["tickets"] = 3, ["templates"] = 4 });

        Assert.True(rules.Length <= 4096);
        Assert.Contains("<#1>", rules);
        Assert.Contains("<#4>", rules);
        Assert.DoesNotContain("{", rules);
    }

    [Fact]
    public void TemplateGuidelines_FitTheForumTopic()
    {
        Assert.True(ServerLayout.TEMPLATES_GUIDELINES.Length <= 4096);
        Assert.InRange(ServerLayout.TemplateTags.Count, 1, 20);
    }

    [Fact]
    public void StatusChannelName_KeepsTheSlug()
    {
        foreach (var look in AppStatus.Looks.Values)
        {
            Assert.Equal("bot-status", ServerLayout.Slug(ServerLayout.StatusChannelName(look.Emoji)));
        }
    }

    [Fact]
    public void AppStatus_HasCheckmarkCrossAndMaintenance()
    {
        Assert.Equal(["✅", "❌", "🔧"], Enum.GetValues<AppState>().Select(s => AppStatus.Look(s).Emoji));
    }

    [Fact]
    public void ObsoleteRoles_NeverMatchALayoutRole()
    {
        var layout = ServerLayout.Roles.SelectMany(r => r.Aliases.Append(ServerLayout.Slug(r.Name)));
        Assert.Empty(ServerLayout.ObsoleteRoles.Intersect(layout));
    }
}

public class ReleaseNotesTests
{
    #region Tests

    [Fact]
    public void Parse_ReadsVersionHeadlineIntroAndSections()
    {
        var draft = ReleaseNotes.Parse(
            "1.4.0 - Building is back!\nA big one.\n\nNew\n- Auto farming\n• Island view\nImproved:\n* Faster queue\n## Fixed\n- Login works",
            out var problem);

        Assert.Null(problem);
        Assert.NotNull(draft);
        Assert.Equal("1.4.0", draft.Version);
        Assert.Equal("Building is back!", draft.Headline);
        Assert.Equal("A big one.", draft.Intro);
        Assert.Equal(["✨ New features", "⚡ Improvements", "🪲 Bug fixes"], draft.Sections.Select(s => s.Title));
        Assert.Equal("- Auto farming\n- Island view", draft.Sections[0].Body.Replace("\r", string.Empty));
        Assert.Equal("- Faster queue", draft.Sections[1].Body.Trim());
    }

    [Theory]
    [InlineData("v2.0", "2.0", null)]
    [InlineData("GrepoBot 1.2.3.4: Hotfix", "1.2.3.4", "Hotfix")]
    [InlineData("# **1.5.1** — Small fixes", "1.5.1", "Small fixes")]
    public void Parse_AcceptsVersionLineStyles(string firstLine, string version, string? headline)
    {
        var draft = ReleaseNotes.Parse(firstLine + "\nFixed\n- x", out _);

        Assert.NotNull(draft);
        Assert.Equal(version, draft.Version);
        Assert.Equal(headline, draft.Headline);
    }

    [Theory]
    [InlineData("Hey everyone, new update!")]
    [InlineData("")]
    public void Parse_WithoutVersion_ExplainsTheProblem(string text)
    {
        Assert.Null(ReleaseNotes.Parse(text, out var problem));
        Assert.Contains("first line", problem);
    }

    [Theory]
    [InlineData("Bug fixes:", "🪲 Bug fixes")]
    [InlineData("**What's new**", "✨ New features")]
    [InlineData("### Known issues", "Known issues")]
    [InlineData("Fixed the login", null)]
    public void GetHeading_RecognizesHeadings(string line, string? expected)
    {
        Assert.Equal(expected, ReleaseNotes.GetHeading(line));
    }

    [Fact]
    public void Template_ParsesItself()
    {
        Assert.NotNull(ReleaseNotes.Parse(ReleaseNotes.TEMPLATE, out _));
    }

    [Fact]
    public void ReleaseBanner_Renders()
    {
        var png = GrepoBot.Discord.Services.WelcomeCard.RenderRelease("1.4.0", "Building is back!", DateTimeOffset.UtcNow);

        Assert.True(png.Length > 1000);
    }

    #endregion
}
