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
  # Where a test run leaves its results depends on the SDK: beside each test project in one, and
  # in the working directory in another, where the two test projects write over each other's
  # report. So the place is given here, and the report is made by the tests of Core alone.
  results="$PWD/artifacts/coverage"
  rm -rf "$results"
  dotnet test --project tests/Packmoji.Cli.Tests -c Release --no-build
  dotnet test --project tests/Packmoji.Core.Tests -c Release --no-build \
    --results-directory "$results" \
    --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml
  report="$results/coverage.cobertura.xml"
  rate="$(sed -n 's/.*<package line-rate="\([0-9.]*\)"[^>]*name="Packmoji.Core".*/\1/p' "$report" | head -1)"
  echo "Packmoji.Core line coverage: $(awk -v r="${rate:-0}" 'BEGIN { printf "%.1f%%", r * 100 }')"
else
  dotnet test --solution Packmoji.sln -c Release --no-build
fi
