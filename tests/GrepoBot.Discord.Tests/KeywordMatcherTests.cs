using GrepoBot.Discord.Logic;
using GrepoBot.Discord.State;

namespace GrepoBot.Discord.Tests;

public class KeywordMatcherTests
{
    #region Helpers

    private static AutoReplyRule Rule(MatchMode mode, params string[] keywords)
    {
        return new AutoReplyRule { Name = "test", Mode = mode, Keywords = keywords.ToList(), Response = "answer" };
    }

    #endregion

    #region Tests

    [Theory]
    [InlineData("How do I install it?", true)]
    [InlineData("INSTALL", true)]
    [InlineData("reinstall", false)]
    [InlineData("installation", false)]
    [InlineData("nothing here", false)]
    public void Any_MatchesWholeWordsOnly(string text, bool expected)
    {
        Assert.Equal(expected, KeywordMatcher.IsMatch(Rule(MatchMode.Any, "install"), text));
    }

    [Theory]
    [InlineData("installation failed", true)]
    [InlineData("installing", true)]
    [InlineData("reinstall", false)]
    public void TrailingStar_MatchesWordEndings(string text, bool expected)
    {
        Assert.Equal(expected, KeywordMatcher.IsMatch(Rule(MatchMode.Any, "instal*"), text));
    }

    [Theory]
    [InlineData("Nejde mi připojení", true)]
    [InlineData("nejde PRIPOJENI", true)]
    public void IgnoresCaseAndAccents(string text, bool expected)
    {
        Assert.Equal(expected, KeywordMatcher.IsMatch(Rule(MatchMode.Any, "pripojeni"), text));
        Assert.Equal(expected, KeywordMatcher.IsMatch(Rule(MatchMode.Any, "Připojení"), text));
    }

    [Fact]
    public void Phrase_MatchesAcrossExtraSpaces()
    {
        var rule = Rule(MatchMode.Any, "how to start");
        Assert.True(KeywordMatcher.IsMatch(rule, "hey, how   to\nstart the bot?"));
        Assert.False(KeywordMatcher.IsMatch(rule, "how do I start"));
    }

    [Fact]
    public void All_NeedsEveryKeyword()
    {
        var rule = Rule(MatchMode.All, "licence", "expired");
        Assert.True(KeywordMatcher.IsMatch(rule, "My licence expired yesterday"));
        Assert.False(KeywordMatcher.IsMatch(rule, "My licence is fine"));
    }

    [Fact]
    public void Regex_MatchesPattern()
    {
        var rule = Rule(MatchMode.Regex, @"\bv?\d+\.\d+\.\d+\b");
        Assert.True(KeywordMatcher.IsMatch(rule, "is v3.4.1 out?"));
        Assert.False(KeywordMatcher.IsMatch(rule, "is it out?"));
    }

    [Fact]
    public void Regex_InvalidPatternNeverMatchesAndIsReported()
    {
        var rule = Rule(MatchMode.Regex, "([unclosed");
        Assert.False(KeywordMatcher.IsMatch(rule, "anything"));
        Assert.NotNull(KeywordMatcher.ValidatePatterns(rule.Keywords));
        Assert.Null(KeywordMatcher.ValidatePatterns([@"\d+"]));
    }

    [Fact]
    public void Keywords_WithRegexCharactersAreLiteral()
    {
        var rule = Rule(MatchMode.Any, "c++", "what?");
        Assert.True(KeywordMatcher.IsMatch(rule, "do you use c++"));
        Assert.True(KeywordMatcher.IsMatch(rule, "what?"));
        Assert.False(KeywordMatcher.IsMatch(rule, "c#"));
    }

    [Fact]
    public void FindMatch_SkipsDisabledRules()
    {
        var disabled = Rule(MatchMode.Any, "download");
        disabled.Enabled = false;
        disabled.Name = "disabled";
        var enabled = Rule(MatchMode.Any, "download");
        enabled.Name = "enabled";

        Assert.Equal("enabled", KeywordMatcher.FindMatch([disabled, enabled], "where is the download")?.Name);
    }

    [Theory]
    [InlineData("a, b\nc ,  d", MatchMode.Any, new[] { "a", "b", "c", "d" })]
    [InlineData("A\na\n\n", MatchMode.Any, new[] { "A" })]
    [InlineData(@"\d{1,3}, x" + "\n" + "y", MatchMode.Regex, new[] { @"\d{1,3}, x", "y" })]
    public void ParseKeywords_SplitsByLineAndComma(string input, MatchMode mode, string[] expected)
    {
        Assert.Equal(expected, KeywordMatcher.ParseKeywords(input, mode));
    }

    #endregion
}
