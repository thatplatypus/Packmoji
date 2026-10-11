#!/usr/bin/env bash
# Holds the tag that starts a release to the version the programs will say they are.
#   scripts/release-tag.sh <tag, such as v0.1.0>
#
# A release names what it is: the tag is v and the version in Directory.Build.props, and nothing
# else. It is asked before anything is built, so that a mistyped tag costs a moment and no more.
set -euo pipefail

fail() {
  echo "error: $1" >&2
  exit 1
}

[ $# -eq 1 ] || fail "usage: scripts/release-tag.sh <tag, such as v0.1.0>"
root="$(cd "$(dirname "$0")/.." && pwd)"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props")"
[ -n "$version" ] || fail "Directory.Build.props does not say which version this is"

if [ "$1" != "v$version" ]; then
  fail "the tag is \"$1\", and this is version $version, whose tag is v$version: raise the version in Directory.Build.props, or tag v$version"
fi

echo "The tag $1 is the tag of version $version."
