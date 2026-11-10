using GrepoBot.Discord.State;

namespace GrepoBot.Discord.Logic;

/// <summary>
/// The basic automatic answers added by <c>/autoreply defaults</c>: help when something does not work,
/// download, macOS first start, updates, changelog, features, ideas and bugs, templates, links and rules.
/// </summary>
/// <remarks>
/// The first matching answer wins, so the specific ones ("not working", "damaged") come before the general
/// ones ("download"). Keywords are English and Czech; case and accents are ignored.
/// </remarks>
public static class AutoReplyDefaults
{
    #region Private Fields

    private const string DOWNLOAD_URL = "https://grepobot.net/download/";
    private const string FEATURES_URL = "https://grepobot.net/features/";
    private const string CHANGELOG_URL = "https://grepobot.net/changelog/";
    private const string WEBSITE_URL = "https://grepobot.net/";

    #endregion

    #region Public Methods

    /// <summary>New instances of the default answers, in the order they are matched.</summary>
    public static List<AutoReplyRule> Create()
    {
        return
        [
            Rule("not-working",
                "not working, doesnt work, does not work, dont work, stopped working, broken, crash*, wont start, " +
                "nefunguje, nejde, spadl*, pada, nespusti*",
                "🛠️ Something not working?",
                "Sorry to hear that, {user}! Let's get it running again:\n\n" +
                "**1.** Check {#status}, maybe we already know about it.\n" +
                "**2.** Restart GrepoBot, it installs the newest update on start.\n" +
                "**3.** Still broken? Open a ticket in {#tickets} and add a screenshot, it helps us a lot.",
                600,
                Button("📡 Bot status", "{#status}"),
                Button("🎫 Open a ticket", "{#tickets}")),

            Rule("macos-open",
                "damaged, cannot be opened, cant be opened, can't be opened, unidentified developer, gatekeeper, " +
                "apple could not verify, poskozen*, nelze otevrit",
                "🍎 Opening GrepoBot on macOS",
                "macOS stops apps downloaded in a browser that are not from the App Store. You only need to allow it once:\n\n" +
                "**1.** Try to open GrepoBot, then go to **System Settings → Privacy & Security**.\n" +
                "**2.** Scroll down and click **Open Anyway** next to GrepoBot.\n\n" +
                "If macOS says the app **is damaged**, run this in Terminal (use your path if you moved the app):\n" +
                "```\nxattr -cr /Applications/GrepoBot.app\n```\n" +
                "Updates are installed by GrepoBot itself and never ask again.",
                600,
                Button("⬇️ Download", DOWNLOAD_URL),
                Button("🎫 Still stuck?", "{#tickets}")),

            Rule("download",
                "download*, install*, installer, where get, where to get, where can i get, setup file, " +
                "stahn*, stahnout, instal*, nainstal*, kde stahnu, kde stahnout",
                "⬇️ Download GrepoBot",
                "GrepoBot runs on **Windows**, **macOS** and **Linux**. Always download it from the official website, " +
                "it is the only safe place.\n\n" +
                "You only download it once, updates install automatically. 🔄",
                600,
                Button("⬇️ Download", DOWNLOAD_URL),
                Button("✨ Features", FEATURES_URL)),

            Rule("update",
                "how to update, how do i update, update the bot, update grepobot, new update, latest update, " +
                "aktualiz*, nova verze, novou verzi",
                "🔄 GrepoBot updates itself",
                "No need to download anything again. GrepoBot checks for an update when it starts and once a day while " +
                "it runs, installs it and restarts, so you are always up to date.\n\n" +
                "What changed is posted in {#updates}.",
                600,
                Button("📜 Changelog", CHANGELOG_URL),
                Button("🔔 Updates", "{#updates}")),

            Rule("changelog",
                "changelog, whats new, what's new, patch notes, release notes, what changed, " +
                "co je noveho, novinky, zmeny, co se zmenilo",
                "📜 What's new",
                "Every change of every release is in the changelog on the website.\n" +
                "New releases are also announced in {#updates}, grab the updates role to get pinged.",
                900,
                Button("📜 Changelog", CHANGELOG_URL),
                Button("🔔 Updates", "{#updates}")),

            Rule("features",
                "features, what can it do, what does it do, can it, is it able, does it support, " +
                "co umi, umi to, funkce",
                "✨ What GrepoBot can do",
                "🌾 **Farming** of your villages, in every town\n" +
                "🏗️ **Automatic Builder** with building templates\n" +
                "🎭 **Culture**: festivals, Olympic games, processions and theater\n" +
                "🕳️ **Cave** that stores overflowing silver\n" +
                "🏘️ **Village upgrades** and **user scripts**, and more\n\n" +
                "Missing something? Suggest it in {#feedback}.",
                900,
                Button("✨ All features", FEATURES_URL),
                Button("💡 Suggest a feature", "{#feedback}")),

            Rule("ideas-bugs",
                "idea, suggestion, feature request, bug report, report a bug, found a bug, " +
                "napad, navrh, nahlasit chybu, nasel jsem chybu",
                "💡 Ideas & bugs",
                "Thank you, {user}! Create a post in {#feedback} and pick 🆕 **Feature**, 🪲 **Bug** or 💡 **Idea**.\n" +
                "Others can vote on it, and you get notified when the team picks it up.",
                900,
                Button("💡 Create a post", "{#feedback}")),

            Rule("templates",
                "building template*, share template*, gbtemplate, template for, sablon*",
                "🏗️ Building templates",
                "Templates are shared in {#templates}. Export yours in GrepoBot as a **.gbtemplate** file, post it there, " +
                "and import the ones you like with one click.",
                900,
                Button("🏗️ Templates", "{#templates}")),

            Rule("links",
                "website, official site, invite link, discord link, web, odkaz*, pozvank*",
                "🔗 GrepoBot links",
                "Everything in one place: the website, the download, the changelog and the invite for your friends " +
                "are in {#links}.",
                1800,
                Button("🌐 Website", WEBSITE_URL),
                Button("🔗 All links", "{#links}")),

            Rule("rules",
                "rules, server rules, pravidla, pravidel",
                "📑 Server rules",
                "The rules apply everywhere on this server, you can find them in {#rules}.",
                1800,
                Button("📑 Rules", "{#rules}")),
        ];
    }

    #endregion

    #region Helpers

    private static AutoReplyRule Rule(string name, string keywords, string title, string response, int cooldown,
        params AutoReplyButtonSpec[] buttons)
    {
        return new AutoReplyRule
        {
            Name = name,
            Keywords = KeywordMatcher.ParseKeywords(keywords, MatchMode.Any),
            Mode = MatchMode.Any,
            Title = title,
            Response = response,
            Buttons = buttons.ToList(),
            CooldownSeconds = cooldown,
        };
    }

    private static AutoReplyButtonSpec Button(string label, string target)
    {
        return new AutoReplyButtonSpec(label, target);
    }

    #endregion
}
