#!/usr/bin/env bash
# Publishes pmj with Native AOT for one runtime and runs what was published, exactly as CI does.
#   scripts/aot-smoke.sh osx-arm64
#   scripts/aot-smoke.sh linux-x64
# A runtime can only be published on its own operating system and architecture.
set -euo pipefail
cd "$(dirname "$0")/.."

rid="${1:?usage: scripts/aot-smoke.sh <runtime identifier, such as osx-arm64 or linux-x64>}"
dotnet publish src/Packmoji.Cli -c Release -r "$rid"

pmj="src/Packmoji.Cli/bin/Release/net10.0/$rid/publish/pmj"
expected="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)"

version="$("$pmj" --version)"
echo "pmj --version: $version"
case "$version" in
  "$expected"*) ;;
  *) echo "error: expected a version beginning with $expected" >&2; exit 1 ;;
esac

# Captured first and searched afterwards: a pipe into grep -q would end pmj early, and with
# pipefail that is a failure.
help="$("$pmj" --help)"
case "$help" in
  *"Packmoji, the package manager for Emojicode."*) ;;
  *) echo "error: --help did not print pmj's description" >&2; exit 1 ;;
esac

if "$pmj" frobnicate > /dev/null 2>&1; then
  echo "error: an unknown command ended with status 0" >&2
  exit 1
fi

echo "pmj runs as a native binary for $rid ($(wc -c < "$pmj" | tr -d ' ') bytes)"
