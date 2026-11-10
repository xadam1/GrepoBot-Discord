using System.Text.RegularExpressions;
using Discord;
using Discord.Interactions;
using GrepoBot.Discord.Logic;
using GrepoBot.Discord.Services;
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
[RequireStaff]
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
        var title = rule.Title ?? string.Empty;
        var buttons = AutoReplyButtons.Format(rule.Buttons);
        var image = rule.ImageUrl ?? string.Empty;
        await RespondWithModalAsync<AutoReplyModal>(
            $"{SAVE_ID}:edit:{rule.Name}:{mode ?? rule.Mode}:{cooldown ?? rule.CooldownSeconds}",
            modifyModal: builder =>
            {
                builder.UpdateTextInput(AutoReplyModal.KEYWORDS_ID, input => input.Value = keywords);
                builder.UpdateTextInput(AutoReplyModal.RESPONSE_ID, input => input.Value = response);
                builder.UpdateTextInput(AutoReplyModal.TITLE_ID, input => input.Value = title);
                builder.UpdateTextInput(AutoReplyModal.BUTTONS_ID, input => input.Value = buttons);
                builder.UpdateTextInput(AutoReplyModal.IMAGE_ID, input => input.Value = image);
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

        var (buttons, buttonError) = AutoReplyButtons.Parse(modal.Buttons);
        if (buttonError is not null)
        {
            await RespondAsync($"❌ Buttons: {buttonError}", ephemeral: true);
            return;
        }

        var imageUrl = string.IsNullOrWhiteSpace(modal.ImageUrl) ? null : modal.ImageUrl.Trim();
        if (imageUrl is not null && !AutoReplyButtons.IsWebAddress(imageUrl))
        {
            await RespondAsync("❌ The image must be a web address starting with `https://`.", ephemeral: true);
            return;
        }

        var rule = new AutoReplyRule
        {
            Name = name,
            Keywords = keywords,
            Mode = mode,
            Title = string.IsNullOrWhiteSpace(modal.Title) ? null : modal.Title.Trim(),
            Response = modal.Response.Trim(),
            Buttons = buttons,
            ImageUrl = imageUrl,
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
            existing.Title = rule.Title;
            existing.Response = rule.Response;
            existing.Buttons = rule.Buttons;
            existing.ImageUrl = rule.ImageUrl;
            existing.CooldownSeconds = rule.CooldownSeconds;
        });

        var verb = kind == "new" ? "Added" : "Updated";
        await RespondAsync(
            $"✅ {verb} `{name}` ({mode}, cooldown {rule.CooldownSeconds}s). Keywords: {FormatKeywords(keywords)}\n-# This is how it looks, only you can see it:",
            ephemeral: true);
        await FollowupAsync(components: BuildPreview(rule), ephemeral: true, flags: MessageFlags.ComponentsV2);
    }

    [SlashCommand("defaults", "Add the basic answers (download, help, updates, features, …); existing names are kept")]
    public async Task DefaultsAsync()
    {
        var defaults = AutoReplyDefaults.Create();
        var added = _store.Update(state =>
        {
            var names = state.AutoReply.Rules.Select(r => r.Name).ToHashSet();
            var missing = defaults.Where(rule => !names.Contains(rule.Name)).ToList();
            state.AutoReply.Rules.AddRange(missing);
            return missing.Select(rule => rule.Name).ToList();
        });

        var skipped = defaults.Select(rule => rule.Name).Except(added).ToList();
        var text = added.Count == 0
            ? "Nothing to add, all basic answers already exist."
            : $"✅ Added {added.Count} answers: {FormatKeywords(added)}";
        if (skipped.Count > 0)
        {
            text += $"\n-# Kept your existing {FormatKeywords(skipped)}. Delete one with `/autoreply remove` and run this again to get the default.";
        }

        text += "\n-# See each card with `/autoreply show`, change it with `/autoreply edit`.";
        await RespondAsync(text, ephemeral: true);
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

    [SlashCommand("show", "Show the answer card as members see it (only you can see it)")]
    public async Task ShowAsync([Summary(description: "The answer to show"), Autocomplete(typeof(AutoReplyNameAutocomplete))] string name)
    {
        var rule = FindRule(name);
        if (rule is null)
        {
            await RespondAsync($"❌ There is no answer called `{name}`.", ephemeral: true);
            return;
        }

        await RespondAsync(
            $"**{rule.Name}** · {rule.Mode} · cooldown {rule.CooldownSeconds}s · used {rule.TimesUsed}×\nKeywords: {Shorten(FormatKeywords(rule.Keywords), 1500)}",
            ephemeral: true);
        await FollowupAsync(components: BuildPreview(rule), ephemeral: true, flags: MessageFlags.ComponentsV2);
    }

    [SlashCommand("test", "Check which answer a message would get")]
    public async Task TestAsync([Summary(description: "An example question")] string text)
    {
        var rules = _store.Read(state => state.AutoReply.Rules.ToList());
        var match = KeywordMatcher.FindMatch(rules, text);
        if (match is null)
        {
            await RespondAsync("No answer matches this text.", ephemeral: true);
            return;
        }

        await RespondAsync($"✅ `{match.Name}` would answer:", ephemeral: true);
        await FollowupAsync(components: BuildPreview(match), ephemeral: true, flags: MessageFlags.ComponentsV2);
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

    /// <summary>The answer card exactly as a member would get it, with this member's name filled in.</summary>
    private MessageComponent BuildPreview(AutoReplyRule rule)
    {
        var footer = _store.Read(state => state.AutoReply.Footer);
        return AutoReplyCard.Build(Context.Guild, rule, footer, new Dictionary<string, string>
        {
            ["user"] = Context.User.Mention,
            ["username"] = Context.User.GlobalName ?? Context.User.Username,
            ["server"] = Context.Guild.Name,
        });
    }

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

/// <summary>The form for keywords and the answer card.</summary>
public sealed class AutoReplyModal : IModal
{
    #region Properties

    public const string KEYWORDS_ID = "keywords";
    public const string TITLE_ID = "title";
    public const string RESPONSE_ID = "response";
    public const string BUTTONS_ID = "buttons";
    public const string IMAGE_ID = "image";

    string IModal.Title => "Automatic answer";

    [InputLabel("Keywords", "One per line or comma separated. word* matches word endings. Case and accents are ignored.")]
    [ModalTextInput(KEYWORDS_ID, TextInputStyle.Paragraph, "install*, download, setup\nhow to start", maxLength: 1000)]
    public string Keywords { get; set; } = string.Empty;

    [InputLabel("Headline", "Optional big title of the card, e.g. ⬇️ Download GrepoBot")]
    [ModalTextInput(TITLE_ID, TextInputStyle.Short, "⬇️ Download GrepoBot", maxLength: 100)]
    [RequiredInput(false)]
    public string? Title { get; set; }

    [InputLabel("Answer", "Markdown. {user} {username} {server}, channels: {#tickets} {#status} {#rules}")]
    [ModalTextInput(RESPONSE_ID, TextInputStyle.Paragraph, "Download GrepoBot from https://grepobot.net and …", maxLength: 3500)]
    public string Response { get; set; } = string.Empty;

    [InputLabel("Buttons", "Optional, one per line: Label | https://… or Label | {#tickets} (max 5)")]
    [ModalTextInput(BUTTONS_ID, TextInputStyle.Paragraph, "⬇️ Download | https://grepobot.net/download/\n🎫 Get help | {#tickets}", maxLength: 1000)]
    [RequiredInput(false)]
    public string? Buttons { get; set; }

    [InputLabel("Image", "Optional https address of an image shown in the card")]
    [ModalTextInput(IMAGE_ID, TextInputStyle.Short, "https://grepobot.net/…/screenshot.png", maxLength: 500)]
    [RequiredInput(false)]
    public string? ImageUrl { get; set; }

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
