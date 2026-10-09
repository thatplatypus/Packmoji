#!/usr/bin/env bash
# Builds everything and runs every test, exactly as CI does.
#   scripts/check.sh              build and test
#   scripts/check.sh --coverage   also measure coverage and print Core's line coverage
set -euo pipefail
cd "$(dirname "$0")/.."

case "${1:-}" in
  ""|--coverage) ;;
  *) echo "usage: scripts/check.sh [--coverage]" >&2; exit 2 ;;
esac

dotnet build Packmoji.sln -c Release

if [[ "${1:-}" == "--coverage" ]]; then
  dotnet test --solution Packmoji.sln -c Release --no-build \
    --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml
  report=tests/Packmoji.Core.Tests/bin/Release/net10.0/TestResults/coverage.cobertura.xml
  rate="$(sed -n 's/.*<package line-rate="\([0-9.]*\)"[^>]*name="Packmoji.Core".*/\1/p' "$report" | head -1)"
  echo "Packmoji.Core line coverage: $(awk -v r="${rate:-0}" 'BEGIN { printf "%.1f%%", r * 100 }')"
else
  dotnet test --solution Packmoji.sln -c Release --no-build
fi
