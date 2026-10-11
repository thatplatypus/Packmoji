#!/usr/bin/env bash
# Takes the registry as it stands into a directory: the v1 of the registry's branch, or nothing
# when the branch is not there yet, which is how a registry begins.
#   scripts/registry-tree.sh <a directory to make, or an empty one> [<a file for the commit's id>]
# The file is given the id of the commit the registry was taken from, and is left empty when
# there is no branch. scripts/registry-commit.sh is told it, so that what is committed follows
# the registry that was looked at and no other.
# REGISTRY_BRANCH names the branch, which is "registry" when nothing is said, and REGISTRY_REMOTE
# the remote it is on, which is "origin".
set -euo pipefail

fail() { echo "registry-tree: $*" >&2; exit 1; }

if [ $# -lt 1 ] || [ $# -gt 2 ] || [ -z "$1" ]; then
  echo "usage: scripts/registry-tree.sh <directory> [<file for the commit's id>]" >&2
  exit 2
fi
into="$1"
told="${2:-}"
branch="${REGISTRY_BRANCH:-registry}"
remote="${REGISTRY_REMOTE:-origin}"

git check-ref-format --branch "$branch" > /dev/null 2>&1 || fail "\"$branch\" cannot be the name of a branch"
if [ -e "$into" ] && { [ ! -d "$into" ] || [ -n "$(ls -A "$into")" ]; }; then
  fail "\"$into\" holds something already: the registry is taken into a directory of its own"
fi
mkdir -p "$into"
if [ -n "$told" ]; then : > "$told"; fi

# git ls-remote ends with 2 when the remote has no such branch, and with something else when it
# could not be asked. A remote that could not be asked is not a registry that has not begun.
asked=0
git ls-remote --exit-code --heads "$remote" "refs/heads/$branch" > /dev/null 2>&1 || asked=$?
case "$asked" in
  0) ;;
  2) echo "registry-tree: \"$remote\" has no branch \"$branch\" yet, so the registry holds nothing"; exit 0 ;;
  *) fail "\"$remote\" could not be asked whether it has the branch \"$branch\"" ;;
esac

git fetch --quiet --no-tags --depth 1 "$remote" "refs/heads/$branch" || fail "the branch \"$branch\" could not be fetched from \"$remote\""
commit="$(git rev-parse --verify --quiet 'FETCH_HEAD^{commit}')" || fail "what was fetched from \"$remote\" is not a commit"

# A branch that holds no v1 is a registry that holds nothing, as one that has just been made is.
if git cat-file -e "$commit:v1" 2> /dev/null; then
  git archive --format=tar "$commit" v1 | tar -x -f - -C "$into"
fi
if [ -n "$told" ]; then echo "$commit" > "$told"; fi
echo "registry-tree: \"$branch\" is at $commit"
