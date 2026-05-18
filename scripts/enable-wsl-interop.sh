#!/bin/sh
set -eu

WSL_CONF="/etc/wsl.conf"
TMP_FILE="$(mktemp)"

cleanup() {
  rm -f "$TMP_FILE"
}
trap cleanup EXIT

if [ "$(id -u)" -ne 0 ]; then
  echo "Please run with sudo: sudo $0" >&2
  exit 1
fi

if [ -f "$WSL_CONF" ]; then
  awk '
    BEGIN { skip = 0 }
    /^\[interop\][[:space:]]*$/ { skip = 1; next }
    /^\[[^]]+\][[:space:]]*$/ { skip = 0 }
    skip == 0 { print }
  ' "$WSL_CONF" > "$TMP_FILE"
else
  : > "$TMP_FILE"
fi

{
  cat "$TMP_FILE"
  printf '\n[interop]\n'
  printf 'enabled=true\n'
  printf 'appendWindowsPath=true\n'
} > "$WSL_CONF"

echo "Updated $WSL_CONF"
echo "Run this from Windows PowerShell, then reopen WSL:"
echo "  wsl --shutdown"
