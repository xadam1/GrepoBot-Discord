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
