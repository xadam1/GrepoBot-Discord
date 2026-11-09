#!/usr/bin/env bash
# Tests, builds and installs the GrepoBot Discord bot on the Raspberry Pi (macOS / Linux version of deploy.ps1).
#
#   ./deploy/deploy.sh                      # the Pi from deploy/target.local
#   ./deploy/deploy.sh pi@grepobot-pi.local
#   SKIP_TESTS=1 ./deploy/deploy.sh         # not recommended
#
# The Pi's login (user@host) is private, so it lives in deploy/target.local (gitignored, one line), e.g.:
#   echo "pi@grepobot-pi.local" > deploy/target.local
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TARGET="${1:-${GREPOBOT_PI:-}}"
if [ -z "$TARGET" ] && [ -f "$ROOT/deploy/target.local" ]; then
    TARGET="$(head -n 1 "$ROOT/deploy/target.local" | tr -d '[:space:]')"
fi
if [ -z "$TARGET" ]; then
    echo "Which Pi? Pass user@host, set GREPOBOT_PI, or put user@host into deploy/target.local." >&2
    exit 1
fi

OUT="$(mktemp -d)/grepobot-discord-deploy"
trap 'rm -rf "$(dirname "$OUT")"' EXIT

if [ "${SKIP_TESTS:-0}" != "1" ]; then
    echo "Running tests..."
    dotnet test "$ROOT" --nologo
fi

echo "Publishing for linux-arm64..."
dotnet publish "$ROOT/src/GrepoBot.Discord" -c Release -r linux-arm64 --self-contained true -o "$OUT/app" --nologo

# Never ship local secrets.
rm -f "$OUT/app/appsettings.Local.json"
cp "$ROOT/deploy/install.sh" "$ROOT/deploy/grepobot-discord.service" "$ROOT/deploy/grepobot-discord.env.example" "$OUT/"

# rsync only sends what changed since the last deploy (usually a few MB instead of ~90 MB). --checksum because
# every publish gives all files new timestamps.
echo "Uploading to $TARGET..."
if command -v rsync >/dev/null 2>&1 && ssh "$TARGET" "command -v rsync" >/dev/null 2>&1; then
    rsync -rlz --checksum --delete "$OUT/" "$TARGET:grepobot-discord-deploy/"
else
    ssh "$TARGET" "rm -rf ~/grepobot-discord-deploy"
    scp -r -q "$OUT" "$TARGET:~/grepobot-discord-deploy"
fi

echo "Installing..."
ssh -t "$TARGET" "sudo bash ~/grepobot-discord-deploy/install.sh"
echo "Done."
