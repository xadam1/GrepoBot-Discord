<p align="center">
  <img src="src/GrepoBot.Discord/Assets/banner.png" alt="GrepoBot" width="680">
</p>

# GrepoBot Discord bot

The community bot of the GrepoBot Discord server. It runs on a Raspberry Pi 3B+ (or any 64-bit Linux / Windows machine) and is written in C# (.NET 10, [Discord.Net](https://github.com/discord-net/Discord.Net)).

## Features

| Feature | How |
|---|---|
| **Post release updates as GrepoBot** | Write the update yourself in a hidden channel (text, images, files, emojis, server stickers). Right click it → **Apps → Post as GrepoBot** → pick a channel → **Post** or **Post & ping @Updates**. Posts in announcement channels are published to following servers. Fixed a typo in the draft? Right click the draft → **Apps → Update GrepoBot post**. |
| **Purge** | `/purge messages count:50 [user]`, `/purge all` (delete everything, or recreate the channel instantly), right click → **Apps → Purge from here**. |
| **Automatic answers** | `/autoreply add name:install-help` opens a form for keywords and the answer. When a member's message contains a keyword, the bot replies with the answer. Whole words, case and accents ignored, `instal*` matches word endings, modes *Any* / *All* / *Regex*, cooldown per channel. `/autoreply list · show · edit · toggle · remove · test · ignore · settings`. |
| **Rules & member role** | `/rules post channel role` posts the rules with an *I have read and accept the rules* button that gives the member role. `/rules edit` changes them in place. |
| **Welcome messages** | `/welcome setup channel` with placeholders `{user} {username} {server} {rules} {membercount}`, `/welcome test`, `/welcome disable`. |
| **Self roles** | `/roles panel` posts buttons that give or remove up to 5 roles, e.g. *Updates* (pinged on releases) or languages. Roles with moderator permissions are refused. |
| **Moderation log** | `/setup log-channel` logs deleted and edited messages, joins (with a warning for accounts younger than 7 days), leaves, bans and unbans. |
| **Settings & look** | `/setup show · updates-role · auto-publish · branding` (sets the bot's avatar and profile banner). `/about` for everyone. |

Ticket handling stays with the existing ticket bot. All admin commands are hidden from members who lack *Manage Server* (purge: *Manage Messages*, roles: *Manage Roles*); you can fine-tune who sees which command in **Server Settings → Integrations → GrepoBot**.

## 1. Create the Discord application

1. Open the [Developer Portal](https://discord.com/developers/applications) → **New Application** → name it `GrepoBot`.
2. **General Information**: App Icon = `src/GrepoBot.Discord/Assets/avatar.png`, add a short description.
3. **Bot**:
   - Icon = `avatar.png`, Banner = `banner.png` (both in `src/GrepoBot.Discord/Assets`). You can also do this later with `/setup branding`.
   - Turn **Public Bot** off, so only you can add it to servers.
   - Under **Privileged Gateway Intents** turn on **Server Members Intent** and **Message Content Intent** (needed for welcome messages and automatic answers).
   - **Reset Token** and keep the token for step 3. Never share or commit it.
4. **Installation**: Install Link = *None*. Invite the bot with this link (replace `APP_ID` with the *Application ID* from General Information):
   ```
   https://discord.com/oauth2/authorize?client_id=APP_ID&scope=bot+applications.commands&permissions=8
   ```
   `permissions=8` is Administrator, the bot acts as a server admin.
5. **Server Settings → Roles**: drag the **GrepoBot** role above the member role and the updates role, otherwise it cannot give them.

## 2. Prepare the Raspberry Pi

1. Flash **Raspberry Pi OS Lite (64-bit)** with [Raspberry Pi Imager](https://www.raspberrypi.com/software/). In the Imager's settings set the hostname `grepobot-pi`, the user `pi` with a password, your Wi-Fi (skip it when using a cable), and enable **SSH**.
2. Boot the Pi and log in once from Windows to check it is reachable:
   ```powershell
   ssh pi@grepobot-pi.local
   ```
   If `.local` does not resolve, use the Pi's IP address from your router.
3. Set up key login so deploys do not ask for a password (run on Windows, once):
   ```powershell
   ssh-keygen -t ed25519   # only if you have no key yet, accept the defaults
   type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh pi@grepobot-pi.local "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys"
   ```
4. Optional but recommended: `sudo apt update && sudo apt full-upgrade -y`.

No .NET install is needed on the Pi, the bot is published self-contained (~90 MB).

## 3. Deploy

From the repository on Windows:

```powershell
./deploy/deploy.ps1                       # or: ./deploy/deploy.ps1 -Target pi@192.168.1.50
```

It runs the tests, publishes for `linux-arm64`, uploads the result and runs `deploy/install.sh` on the Pi, which installs the bot to `/opt/grepobot-discord` as a systemd service running under its own `grepobot` user.

On the first install, put the token and the server ID into the settings file on the Pi:

```bash
sudo nano /etc/grepobot-discord/grepobot-discord.env
sudo systemctl restart grepobot-discord
journalctl -u grepobot-discord -f          # watch the log, Ctrl+C to leave
```

The service starts with the Pi, waits for the network and restarts automatically after a crash. Discord.Net reconnects by itself when Wi-Fi drops. Settings changed through commands are saved in `/var/lib/grepobot-discord/state.json`; back it up if you reinstall the Pi.

Later updates: just run `./deploy/deploy.ps1` again.

## 4. First setup on the server

1. `/setup updates-role role:@Updates` – the role pinged by *Post & ping*.
2. `/roles panel channel:#roles title:Notifications role1:@Updates` – members opt in to release pings.
3. `/rules post channel:#rules role:@Member` – edit the text in the form and submit. Then lock the server: deny *View Channel* for `@everyone` on your categories, allow it for `@Member`, and keep `#rules` and `#welcome` visible to `@everyone`.
4. `/welcome setup channel:#welcome` and `/welcome test`.
5. `/setup log-channel channel:#mod-log` (a staff-only channel).
6. `/autoreply ignore channel:<ticket category>` so the bot stays out of tickets, then add answers, e.g. `/autoreply add name:download` with keywords `download, install*, where to get` and the answer `You can download GrepoBot at https://grepobot.net`.

## Running locally

Copy `src/GrepoBot.Discord/appsettings.Local.example.json` to `appsettings.Local.json` in the same folder (gitignored), fill in the token and server ID, then:

```powershell
dotnet run --project src/GrepoBot.Discord
```

Stop the bot on the Pi meanwhile (`sudo systemctl stop grepobot-discord`), two instances would both answer.

## Good to know

- Attachments are re-uploaded, so the post does not depend on the draft. The bot can upload at most the server's upload limit per message (10 MB without boosts).
- Custom emojis from any server the bot is in work; stickers only if they belong to this server. Voice messages cannot be reposted.
- Drafts longer than 2000 characters (Nitro allows 4000) are posted as several messages.
- Discord only bulk-deletes messages younger than 14 days. Older ones are deleted one by one, about one per second; *Recreate channel* is instant but gives the channel a new ID.
- Deleted-message logs only show the content of messages sent while the bot was running.
- `tools/make-banner.cs` regenerates `banner.png` and `avatar.png` from the GrepoBot app's artwork (`dotnet run tools/make-banner.cs -- <login_background.png> <grepobot_icon_large.png> <grepobot_icon_bg.png> <out dir>`).
