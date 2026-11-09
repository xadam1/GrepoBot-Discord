#!/usr/bin/env bash
# Installs or updates the GrepoBot Discord bot on a Raspberry Pi (Raspberry Pi OS 64-bit).
# deploy.ps1 uploads this folder and runs: sudo bash ~/grepobot-discord-deploy/install.sh
set -euo pipefail

APP_DIR=/opt/grepobot-discord
ETC_DIR=/etc/grepobot-discord
ENV_FILE="$ETC_DIR/grepobot-discord.env"
SERVICE=grepobot-discord
SRC_DIR="$(cd "$(dirname "$0")" && pwd)"

if [ "$(id -u)" -ne 0 ]; then
    echo "Run with sudo." >&2
    exit 1
fi

if [ "$(uname -m)" != "aarch64" ]; then
    echo "This build is for 64-bit Raspberry Pi OS (aarch64), this system is $(uname -m)." >&2
    exit 1
fi

if ! id grepobot >/dev/null 2>&1; then
    useradd --system --no-create-home --shell /usr/sbin/nologin grepobot
    echo "Created system user 'grepobot'."
fi

# .NET needs ICU (cultures, accent-insensitive auto-replies); the welcome card needs fontconfig and a font.
# grep without -q: -q stops reading early, ldconfig then gets SIGPIPE and pipefail treats that as "missing".
MISSING=""
if ! ldconfig -p | grep 'libicuuc\.so' >/dev/null; then
    MISSING="$MISSING $(apt-cache search --names-only '^libicu[0-9]+$' | awk '{print $1}' | sort -V | tail -n 1)"
fi
if ! ldconfig -p | grep 'libfontconfig\.so\.1' >/dev/null; then
    MISSING="$MISSING libfontconfig1"
fi
if [ ! -d /usr/share/fonts/truetype/dejavu ]; then
    MISSING="$MISSING fonts-dejavu-core"
fi
if [ -n "$MISSING" ]; then
    echo "Installing$MISSING..."
    apt-get update -qq
    # shellcheck disable=SC2086
    apt-get install -y -qq $MISSING
fi

mkdir -p "$APP_DIR" "$ETC_DIR"

if [ -d "$SRC_DIR/app" ]; then
    systemctl stop "$SERVICE" 2>/dev/null || true
    rm -rf "${APP_DIR:?}"/*
    cp -a "$SRC_DIR/app/." "$APP_DIR/"
    chmod +x "$APP_DIR/GrepoBot.Discord"
    chown -R root:root "$APP_DIR"
    echo "Installed the bot to $APP_DIR."
fi

FIRST_INSTALL=0
if [ ! -f "$ENV_FILE" ]; then
    cp "$SRC_DIR/grepobot-discord.env.example" "$ENV_FILE"
    chown root:root "$ENV_FILE"
    chmod 600 "$ENV_FILE"
    FIRST_INSTALL=1
fi

cp "$SRC_DIR/grepobot-discord.service" "/etc/systemd/system/$SERVICE.service"
systemctl daemon-reload
systemctl enable "$SERVICE" >/dev/null

if [ "$FIRST_INSTALL" -eq 1 ] || ! grep -q '^Discord__Token=.\+' "$ENV_FILE"; then
    echo
    echo "Almost done: put the bot token and server ID into $ENV_FILE:"
    echo "    sudo nano $ENV_FILE"
    echo "then start the bot:"
    echo "    sudo systemctl restart $SERVICE"
    exit 0
fi

systemctl restart "$SERVICE"
sleep 3
systemctl --no-pager --lines=15 status "$SERVICE" || true
echo
echo "Logs: journalctl -u $SERVICE -f"
