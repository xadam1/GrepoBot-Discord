using System.Text.RegularExpressions;
using Discord;
using Discord.Interactions;
using GrepoBot.Discord.Logic;
using GrepoBot.Discord.State;
using Microsoft.Extensions.DependencyInjection;

namespace GrepoBot.Discord.Modules;

/// <summary>
/// <c>/autoreply …</c>: manage the keyword answers.
/// </summary>
[Group("autoreply", "Automatic answers to common questions")]
[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
[RequireUserPermission(GuildPermission.ManageGuild)]
public sealed partial class AutoReplyModule : InteractionModuleBase<SocketInteractionContext>
{
    #region Private Fields

    private const string SAVE_ID = "autoreply-save";

    private readonly StateStore _store;

    #endregion

    #region Constructor

    public AutoReplyModule(StateStore store)
    {
        _store = store;
    }

    #endregion

    #region Public Methods

    [SlashCommand("add", "Add an automatic answer (a form for keywords and the answer opens)")]
    public async Task AddAsync(
        [Summary(description: "Short name, e.g. install-help (a-z, 0-9, - and _)")] string name,
        [Summary(description: "Any keyword, all keywords, or regular expressions")] MatchMode mode = MatchMode.Any,
        [Summary(description: "Seconds before the same answer is sent again in a channel"), MinValue(0), MaxValue(86400)] int cooldown = 300)
    {
        name = name.Trim().ToLowerInvariant();
        if (!NamePattern().IsMatch(name))
        {
            await RespondAsync("❌ Use 1-32 characters: a-z, 0-9, `-` and `_`.", ephemeral: true);
            return;
        }

        if (FindRule(name) is not null)
        {
            await RespondAsync($"❌ `{name}` already exists. Use `/autoreply edit`.", ephemeral: true);
            return;
        }

        await RespondWithModalAsync<AutoReplyModal>($"{SAVE_ID}:new:{name}:{mode}:{cooldown}");
    }

    [SlashCommand("edit", "Edit an automatic answer")]
    public async Task EditAsync(
        [Summary(description: "The answer to edit"), Autocomplete(typeof(AutoReplyNameAutocomplete))] string name,
        [Summary(description: "Change how keywords match")] MatchMode? mode = null,
        [Summary(description: "Change the cooldown in seconds"), MinValue(0), MaxValue(86400)] int? cooldown = null)
    {
        var rule = FindRule(name);
        if (rule is null)
        {
            await RespondAsync($"❌ There is no answer called `{name}`.", ephemeral: true);
            return;
        }

        var keywords = string.Join("\n", rule.Keywords);
        var response = rule.Response;
        await RespondWithModalAsync<AutoReplyModal>(
            $"{SAVE_ID}:edit:{rule.Name}:{mode ?? rule.Mode}:{cooldown ?? rule.CooldownSeconds}",
            modifyModal: builder =>
            {
                builder.UpdateTextInput(AutoReplyModal.KEYWORDS_ID, input => input.Value = keywords);
                builder.UpdateTextInput(AutoReplyModal.RESPONSE_ID, input => input.Value = response);
            });
    }

    [ModalInteraction(SAVE_ID + ":*:*:*:*", ignoreGroupNames: true)]
    public async Task OnSaveAsync(string kind, string name, string modeText, string cooldownText, AutoReplyModal modal)
    {
        var mode = Enum.Parse<MatchMode>(modeText);
        var keywords = KeywordMatcher.ParseKeywords(modal.Keywords, mode);
        if (keywords.Count == 0)
        {
            await RespondAsync("❌ Enter at least one keyword.", ephemeral: true);
            return;
        }

        if (mode == MatchMode.Regex && KeywordMatcher.ValidatePatterns(keywords) is { } error)
        {
            await RespondAsync($"❌ Invalid regular expression {error}", ephemeral: true);
            return;
        }

        var rule = new AutoReplyRule
        {
            Name = name,
            Keywords = keywords,
            Mode = mode,
            Response = modal.Response.Trim(),
            CooldownSeconds = int.Parse(cooldownText),
        };

        _store.Update(state =>
        {
            var existing = state.AutoReply.Rules.FirstOrDefault(r => r.Name == name);
            if (existing is null)
            {
                state.AutoReply.Rules.Add(rule);
                return;
            }

            existing.Keywords = rule.Keywords;
            existing.Mode = rule.Mode;
            existing.Response = rule.Response;
            existing.CooldownSeconds = rule.CooldownSeconds;
        });

        var verb = kind == "new" ? "Added" : "Updated";
        await RespondAsync(
            $"✅ {verb} `{name}` ({mode}, cooldown {rule.CooldownSeconds}s). Keywords: {FormatKeywords(keywords)}\n-# Try it with `/autoreply test`.",
            ephemeral: true);
    }

    [SlashCommand("remove", "Delete an automatic answer")]
    public async Task RemoveAsync([Summary(description: "The answer to delete"), Autocomplete(typeof(AutoReplyNameAutocomplete))] string name)
    {
        var removed = _store.Update(state => state.AutoReply.Rules.RemoveAll(r => r.Name == name) > 0);
        await RespondAsync(removed ? $"🗑️ Deleted `{name}`." : $"❌ There is no answer called `{name}`.", ephemeral: true);
    }

    [SlashCommand("toggle", "Turn an automatic answer on or off")]
    public async Task ToggleAsync([Summary(description: "The answer to turn on or off"), Autocomplete(typeof(AutoReplyNameAutocomplete))] string name)
    {
        var enabled = _store.Update(state =>
        {
            var rule = state.AutoReply.Rules.FirstOrDefault(r => r.Name == name);
            if (rule is null)
            {
                return (bool?)null;
            }

            rule.Enabled = !rule.Enabled;
            return rule.Enabled;
        });

        var text = enabled switch
        {
            null => $"❌ There is no answer called `{name}`.",
            true => $"✅ `{name}` is on.",
            false => $"⏸️ `{name}` is off.",
        };

        await RespondAsync(text, ephemeral: true);
    }

    [SlashCommand("list", "Show all automatic answers")]
    public async Task ListAsync()
    {
        var settings = _store.Read(state => new
        {
            Rules = state.AutoReply.Rules.Select(r => new { r.Name, r.Enabled, r.Mode, Keywords = r.Keywords.ToList(), r.CooldownSeconds, r.TimesUsed }).ToList(),
            Ignored = state.AutoReply.IgnoredChannelIds.ToList(),
            state.AutoReply.IgnoreStaff,
        });

        var embed = new EmbedBuilder()
            .WithColor(BotBranding.Accent)
            .WithTitle($"🤖 Automatic answers ({settings.Rules.Count})");

        var description = settings.Rules.Count == 0
            ? "No answers yet. Add one with `/autoreply add`."
            : $"Ignored channels: {(settings.Ignored.Count == 0 ? "none" : string.Join(", ", settings.Ignored.Select(id => $"<#{id}>")))}\n" +
              $"Staff ignored: {(settings.IgnoreStaff ? "yes" : "no")}";
        embed.WithDescription(description);

        foreach (var rule in settings.Rules.Take(25))
        {
            var status = rule.Enabled ? "🟢" : "⚪";
            embed.AddField(
                $"{status} {rule.Name}",
                Shorten($"{rule.Mode} · {rule.CooldownSeconds}s · used {rule.TimesUsed}×\n{FormatKeywords(rule.Keywords)}", 1024));
        }

        if (settings.Rules.Count > 25)
        {
            embed.WithFooter($"…and {settings.Rules.Count - 25} more");
        }

        await RespondAsync(embed: embed.Build(), ephemeral: true);
    }

    [SlashCommand("show", "Show the full answer text")]
    public async Task ShowAsync([Summary(description: "The answer to show"), Autocomplete(typeof(AutoReplyNameAutocomplete))] string name)
    {
        var rule = FindRule(name);
        if (rule is null)
        {
            await RespondAsync($"❌ There is no answer called `{name}`.", ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithColor(BotBranding.Accent)
            .WithTitle(rule.Name)
            .WithDescription(rule.Response)
            .AddField("Keywords", Shorten(FormatKeywords(rule.Keywords), 1024))
            .WithFooter($"{rule.Mode} · cooldown {rule.CooldownSeconds}s · used {rule.TimesUsed}×");

        await RespondAsync(embed: embed.Build(), ephemeral: true);
    }

    [SlashCommand("test", "Check which answer a message would get")]
    public async Task TestAsync([Summary(description: "An example question")] string text)
    {
        var rules = _store.Read(state => state.AutoReply.Rules.ToList());
        var match = KeywordMatcher.FindMatch(rules, text);
        await RespondAsync(
            match is null ? "No answer matches this text." : $"✅ `{match.Name}` would answer:\n>>> {match.Response}",
            ephemeral: true);
    }

    [SlashCommand("ignore", "Never auto-reply in a channel or category (run again to undo)")]
    public async Task IgnoreAsync(
        [Summary(description: "Channel or category, e.g. the ticket category"), ChannelTypes(ChannelType.Text, ChannelType.News, ChannelType.Category, ChannelType.Forum)] IGuildChannel channel)
    {
        var ignored = _store.Update(state =>
        {
            if (state.AutoReply.IgnoredChannelIds.Remove(channel.Id))
            {
                return false;
            }

            state.AutoReply.IgnoredChannelIds.Add(channel.Id);
            return true;
        });

        await RespondAsync(ignored ? $"🔇 No more automatic answers in <#{channel.Id}>." : $"🔊 Automatic answers are back in <#{channel.Id}>.", ephemeral: true);
    }

    [SlashCommand("settings", "Who gets automatic answers and the footer under them")]
    public async Task SettingsAsync(
        [Summary("ignore-staff", "Do not answer members who can manage messages")] bool? ignoreStaff = null,
        [Summary(description: "Small text under every answer"), MaxLength(200)] string? footer = null)
    {
        var result = _store.Update(state =>
        {
            if (ignoreStaff is not null)
            {
                state.AutoReply.IgnoreStaff = ignoreStaff.Value;
            }

            if (footer is not null)
            {
                state.AutoReply.Footer = footer;
            }

            return (state.AutoReply.IgnoreStaff, state.AutoReply.Footer);
        });

        await RespondAsync($"⚙️ Staff ignored: **{(result.IgnoreStaff ? "yes" : "no")}**\nFooter: {result.Footer}", ephemeral: true);
    }

    #endregion

    #region Private Methods

    private AutoReplyRule? FindRule(string name)
    {
        return _store.Read(state => state.AutoReply.Rules.FirstOrDefault(r => r.Name == name));
    }

    private static string FormatKeywords(IEnumerable<string> keywords)
    {
        return string.Join(", ", keywords.Select(k => $"`{k}`"));
    }

    private static string Shorten(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";
    }

    [GeneratedRegex("^[a-z0-9_-]{1,32}$")]
    private static partial Regex NamePattern();

    #endregion
}

/// <summary>The form for keywords and the answer.</summary>
public sealed class AutoReplyModal : IModal
{
    #region Properties

    public const string KEYWORDS_ID = "keywords";
    public const string RESPONSE_ID = "response";

    public string Title => "Automatic answer";

    [InputLabel("Keywords", "One per line or comma separated. word* matches word endings. Case and accents are ignored.")]
    [ModalTextInput(KEYWORDS_ID, TextInputStyle.Paragraph, "install*, download, setup\nhow to start", maxLength: 1000)]
    public string Keywords { get; set; } = string.Empty;

    [InputLabel("Answer", "Markdown. Placeholders: {user} {username} {server}")]
    [ModalTextInput(RESPONSE_ID, TextInputStyle.Paragraph, "Download GrepoBot from https://grepobot.net and …", maxLength: 4000)]
    public string Response { get; set; } = string.Empty;

    #endregion
}

/// <summary>Suggests existing answer names while typing.</summary>
public sealed class AutoReplyNameAutocomplete : AutocompleteHandler
{
    #region Public Methods

    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;
        var names = services.GetRequiredService<StateStore>()
            .Read(state => state.AutoReply.Rules.Select(r => r.Name).ToList());

        var suggestions = names
            .Where(n => n.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(n => new AutocompleteResult(n, n));

        return Task.FromResult(AutocompletionResult.FromSuccess(suggestions));
    }

    #endregion
}
