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
- `Services/*` – one service per feature (repost, purge, rules, auto-reply, welcome, mod log). Discord logic lives here, modules stay thin.
- `Modules/*` – slash commands, context menu commands, buttons and modals. Context menu commands cannot be inside a `[Group]` module; component/modal handlers inside a group use `ignoreGroupNames: true`.
- `Logic/*` – pure, unit-tested helpers (`KeywordMatcher`, `MessageSplitter`, `TextTemplate`, `RoleSafety`, `BotBranding`).
- `State/StateStore` – everything set through commands, one JSON file, written atomically after every `Update`. Read through `Read(...)` and return copies, never live lists.
- `deploy/` – `deploy.ps1` (Windows → Pi), `install.sh`, systemd unit, env file template.

## Conventions

- Same style as the GrepoBot app: Allman braces, no one-line `if`/`foreach`, `#region` blocks (`Private Fields`, `Properties`, `Constructor`, `Public Methods`, `Private Methods`, `Helpers`), `_camelCase` private fields.
- Admin commands: `[DefaultMemberPermissions]` (hides them) **and** `[RequireUserPermission]` (enforces it, also for buttons). Member-facing buttons live in `PublicModule`.
- Custom IDs are `name:arg:arg`, args are IDs or slug names only (no `:`), max 100 characters.
- Every role the bot hands out goes through `RoleSafety.GetProblem`.
- Modal labels max 45 characters, descriptions and placeholders max 100; `ModalTests` builds every modal and `CommandRegistrationTests` builds every module, so keep them updated when adding one.
- Never commit tokens: `appsettings.Local.json`, `data/` and `*.env` are gitignored.

## Build & validation

- Always run `dotnet build` and `dotnet test` after changes.
- Check the Pi build with `dotnet publish src/GrepoBot.Discord -c Release -r linux-arm64 --self-contained true`.
