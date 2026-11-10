# CLAUDE.md

Guidance for Claude Code in this repository.

GrepoBot.Discord is the community bot of the GrepoBot Discord server (the desktop app lives in `xadam1/GrepoBot`). It runs as a systemd service on a Raspberry Pi 3B+ with 64-bit Raspberry Pi OS. Bot texts are English.

## Tech stack

- .NET 10, Generic Host (`Microsoft.Extensions.Hosting` + `.Systemd`)
- Discord.Net 3.20 (`DiscordSocketClient` + `InteractionService`)
- xUnit tests in `tests/GrepoBot.Discord.Tests`

## Layout

- `Program.cs` – host, configuration (`appsettings.json` < `appsettings.Local.json` < environment `Discord__Token`, `Discord__GuildId`, `Bot__DataDirectory`), DI.
- `Services/DiscordBotService` – login, registers commands to the one configured server (`RegisterCommandsToGuildAsync`, instant), routes interactions and gateway events. Gateway handlers run through `RunSafe` (off the gateway thread, exceptions logged).
- `Services/*` – one service per feature (repost, purge, rules, auto-reply, welcome + `WelcomeCard` (SkiaSharp), mod log, tickets, feedback forum + hourly `ForumCleanupService`, direct-message auto-reply and `MentionReplyService` for tags of the bot, both sending `BotIntroCard`; auto-replies are cards built by `AutoReplyCard`, buttons parsed by `Logic/AutoReplyButtons`). Discord logic lives here, modules stay thin.
- `Modules/*` – slash commands, context menu commands, buttons and modals. Context menu commands cannot be inside a `[Group]` module; component/modal handlers inside a group use `ignoreGroupNames: true`.
- `Logic/*` – pure, unit-tested helpers (`KeywordMatcher`, `MessageSplitter`, `TextTemplate`, `RoleSafety`, `BotBranding`, `TicketTopics`, `FeedbackForum`, `WelcomeQuips`, `BotStatuses`, `ServerLayout` (roles, channels, rules of `/server setup`), `AppStatus`, `ReleaseNotes` (parses release drafts for "Post as release")).
- `State/StateMigrations` – replaces saved texts that are still an old default with the new default on load.
- `State/StateStore` – everything set through commands, one JSON file, written atomically after every `Update`. Read through `Read(...)` and return copies, never live lists.
- `Services/ReleaseCard` + `WelcomeCard.RenderRelease` – the release card and its version banner.
- `Services/MessageCards` – helpers for the Components v2 cards (rules, links, status, ticket panel, welcome); `UpsertAsync` edits a card in place.
- `tools/` – file-based C# scripts: `make-banner`, `set-profile` (bot banner).
- `deploy/` – `deploy.ps1` (Windows → Pi), `deploy.sh` (macOS → Pi), `pi-prepare.sh` (one-time: ethernet only, no desktop/xrdp), `install.sh`, systemd unit, env file template.

## Conventions

- Same style as the GrepoBot app: Allman braces, no one-line `if`/`foreach`, `#region` blocks (`Private Fields`, `Properties`, `Constructor`, `Public Methods`, `Private Methods`, `Helpers`), `_camelCase` private fields.
- Admin commands: `[DefaultMemberPermissions]` (hides them) **and** `[RequireUserPermission]` (enforces it, also for buttons). Member-facing buttons live in `PublicModule` (ticket buttons in `TicketButtonsModule`, which checks owner/staff itself).
- Custom IDs are `name:arg:arg`, args are IDs or slug names only (no `:`), max 100 characters.
- Every role the bot hands out goes through `RoleSafety.GetProblem`.
- Cards are Components v2 (`ComponentBuilderV2`, `MessageFlags.ComponentsV2`): no content or embeds, max 4000 characters of text per message. Set role colors with `RoleProperties.Colors`, `Color` alone is ignored by Discord.
- Modal labels max 45 characters, descriptions and placeholders max 100; `ModalTests` builds every modal and `CommandRegistrationTests` builds every module, so keep them updated when adding one.
- Globalization is ICU (not invariant): Discord.Net creates the server locale culture and accent-insensitive matching needs normalization; `install.sh` installs libicu on the Pi.
- `initValue` on modal attributes is not sent, prefill modals with `modifyModal`. Dropdowns in modals use `[ModalSelectMenu]` + `[ModalSelectMenuOption]`.
- GrepoBot only supports its newest version: no bot text asks for or mentions a version (`Texts_NeverMentionVersions` checks the defaults).
- When changing a default text that may already be saved, add the old text to `StateMigrations`.
- Never commit tokens, server IDs or the Pi's address: `appsettings.Local.json`, `data/`, `*.env` and `deploy/target.local` (the Pi's ssh login for the deploy scripts) are gitignored.

## Build & validation

- Always run `dotnet build` and `dotnet test` after changes.
- Check the Pi build with `dotnet publish src/GrepoBot.Discord -c Release -r linux-arm64 --self-contained true`.
