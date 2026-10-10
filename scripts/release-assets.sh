#!/usr/bin/env bash
# Names the programs of a release and writes their digests, exactly as the release workflow does.
#   scripts/release-assets.sh <directory of what was built> <directory to put the assets in>
#
# The first directory holds what each machine built, a directory for each runtime:
#   linux-x64/pmj   osx-arm64/pmj   win-x64/pmj.exe
# The second is given what a release carries: each program under the name it is fetched by,
# pmj-<version>-<runtime>, and pmj-<version>.sha256, with a line for each as sha256sum writes
# and checks. The version is the one in Directory.Build.props.
#
# An asset is the program itself and no archive of it: whoever fetches one holds it to its digest,
# and an archive would put a digest of its own in the way.
set -euo pipefail

fail() {
  echo "error: $1" >&2
  exit 1
}

[ $# -eq 2 ] || fail "usage: scripts/release-assets.sh <directory of what was built> <directory to put the assets in>"
[ -d "$1" ] || fail "there is no directory \"$1\" of what was built"
built="$(cd "$1" && pwd)"
assets="$2"
root="$(cd "$(dirname "$0")/.." && pwd)"

version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props")"
[ -n "$version" ] || fail "Directory.Build.props does not say which version this is"

# A release is made once. What an earlier run left is someone's to look at, and is not written over.
if [ -e "$assets" ] && [ -n "$(ls -A "$assets")" ]; then
  fail "\"$assets\" is not empty, and what is there is not written over"
fi

# Every program is looked for before any is copied, so that a run that stops leaves nothing.
runtimes="linux-x64 osx-arm64 win-x64"
for runtime in $runtimes; do
  case "$runtime" in win-*) program="pmj.exe" ;; *) program="pmj" ;; esac
  [ -f "$built/$runtime/$program" ] || fail "\"$built/$runtime/$program\" was not built: a release carries the program for each of $runtimes"
done

mkdir -p "$assets"
for runtime in $runtimes; do
  case "$runtime" in win-*) program="pmj.exe"; ending=".exe" ;; *) program="pmj"; ending="" ;; esac
  cp "$built/$runtime/$program" "$assets/pmj-$version-$runtime$ending"
done

# The names alone, so that the file checks wherever the assets are put. macOS has no sha256sum of
# old, and its shasum writes the same lines.
digest="sha256sum"
command -v sha256sum > /dev/null 2>&1 || digest="shasum -a 256"
(
  cd "$assets"
  LC_ALL=C $digest "pmj-$version-linux-x64" "pmj-$version-osx-arm64" "pmj-$version-win-x64.exe" > "pmj-$version.sha256"
  $digest -c "pmj-$version.sha256" > /dev/null || fail "the digests that were written do not check"
  cat "pmj-$version.sha256"
)
