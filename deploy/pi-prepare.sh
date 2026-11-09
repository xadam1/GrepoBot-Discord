#!/usr/bin/env bash
# One-time preparation of the Raspberry Pi for the bot (run on the Pi with sudo):
#   - network over the ethernet cable only: Wi-Fi (and Bluetooth) are switched off in the firmware,
#     so they stay off after every restart
#   - no desktop and no remote desktop (xrdp): the Pi boots to the console, which saves memory and CPU
#
# From the Mac:  PI=$(cat deploy/target.local); scp deploy/pi-prepare.sh "$PI:~" && ssh -t "$PI" 'sudo bash ~/pi-prepare.sh && sudo reboot'
#
# Undo: remove the two dtoverlay lines from /boot/firmware/config.txt, then
#   sudo systemctl set-default graphical.target && sudo systemctl enable xrdp xrdp-sesman && sudo reboot
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
    echo "Run with sudo." >&2
    exit 1
fi

CONFIG=/boot/firmware/config.txt
[ -f "$CONFIG" ] || CONFIG=/boot/config.txt

# Never cut the only connection: Wi-Fi goes off only while the cable works.
ETH_IP="$(ip -4 -o addr show eth0 2>/dev/null | awk '{print $4}' | head -n 1)"
if [ -z "$ETH_IP" ]; then
    echo "eth0 has no IPv4 address. Plug in the cable (and check the router) before switching Wi-Fi off." >&2
    exit 1
fi
echo "Ethernet works: eth0 $ETH_IP"

for overlay in disable-wifi disable-bt; do
    if ! grep -q "^dtoverlay=$overlay" "$CONFIG"; then
        printf '\n# GrepoBot: ethernet only\ndtoverlay=%s\n' "$overlay" >> "$CONFIG"
        echo "Added dtoverlay=$overlay to $CONFIG"
    fi
done

# Until the restart, also switch Wi-Fi off in NetworkManager and forget saved Wi-Fi networks' autoconnect.
if command -v nmcli >/dev/null 2>&1; then
    nmcli radio wifi off || true
    nmcli -t -f NAME,TYPE connection show | awk -F: '$2 == "802-11-wireless" {print $1}' | while read -r name; do
        nmcli connection modify "$name" connection.autoconnect no || true
    done
fi

# Console instead of the desktop, no remote desktop.
systemctl set-default multi-user.target
for service in xrdp xrdp-sesman; do
    if systemctl list-unit-files "$service.service" >/dev/null 2>&1; then
        systemctl disable --now "$service" 2>/dev/null || true
    fi
done
echo "Desktop and remote desktop are off (the packages stay installed)."

echo
echo "Done. Restart the Pi to apply everything: sudo reboot"
echo "Afterwards it is reachable over the cable only (same ssh login as before)."
