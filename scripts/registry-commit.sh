#!/usr/bin/env bash
# Commits a registry to its branch and pushes it: the v1 of a directory becomes the v1 of the
# branch, and everything else the branch holds is left as it is.
#   scripts/registry-commit.sh <directory> <file of the commit's words> <file of the commit's id>
# The words are what "Packmoji.Indexer check" wrote, and a file of no words is nothing to commit.
# The id is what scripts/registry-tree.sh wrote: the commit the registry was taken from, or
# nothing when there was no branch. The new commit follows that one and no other, so a branch
# that has moved since refuses the push, and nothing on it is written over.
# REGISTRY_BRANCH names the branch, which is "registry" when nothing is said, and REGISTRY_REMOTE
# the remote it is on, which is "origin".
set -euo pipefail

fail() { echo "registry-commit: $*" >&2; exit 1; }

if [ $# -ne 3 ]; then
  echo "usage: scripts/registry-commit.sh <directory> <file of the commit's words> <file of the commit's id>" >&2
  exit 2
fi
from="$1"
words="$2"
told="$3"
branch="${REGISTRY_BRANCH:-registry}"
remote="${REGISTRY_REMOTE:-origin}"

git check-ref-format --branch "$branch" > /dev/null 2>&1 || fail "\"$branch\" cannot be the name of a branch"
[ -d "$from" ] || fail "\"$from\" is not a directory"
[ -f "$words" ] || fail "\"$words\", which should hold the commit's words, is not there"
[ -f "$told" ] || fail "\"$told\", which should hold the id of the commit this one follows, is not there"

if [ ! -s "$words" ]; then
  echo "registry-commit: there are no words for a commit, so nothing has changed and nothing is committed"
  exit 0
fi

parent="$(tr -d '[:space:]' < "$told")"
case "$parent" in
  "") ;;
  *[!0-9a-f]*) fail "\"$told\" does not hold the id of a commit" ;;
  *) git cat-file -e "$parent^{commit}" 2> /dev/null || fail "the commit $parent, which this one should follow, is not here: scripts/registry-tree.sh fetches it" ;;
esac

git_dir="$(git rev-parse --absolute-git-dir)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# The commit is made in an index of its own, from the branch's own tree: nothing of this
# repository's working files, and nothing staged in it, goes near the registry.
export GIT_INDEX_FILE="$work/index"
if [ -n "$parent" ]; then git read-tree "$parent"; else git read-tree --empty; fi
git rm -r -q --cached --ignore-unmatch -- v1 > /dev/null
if [ -d "$from/v1" ]; then
  (cd "$from" && git --git-dir="$git_dir" --work-tree=. add --force -- v1)
fi

# A registry holds plain files. Anything else that got this far is not committed.
odd="$(git ls-files --stage -- v1 | awk '$1 != "100644" { print $4 }' | head -1)"
[ -z "$odd" ] || fail "\"$odd\" is not a plain file, and a registry holds nothing else"

# The branch says what it is to whoever comes upon it. An apostrophe in a here-document inside
# $( ) stops the bash that macOS has from reading the script, so the text goes by way of a file.
if [ -z "$(git ls-files -- README.md)" ]; then
  cat > "$work/README.md" <<'README'
# The registry

This branch is the registry of Packmoji. Under `v1/packages` there is a file for each package,
with every version of it that has been indexed. Under `v1/refused` there is a file for each
repository with a release that claims to be a package and was refused, and why.

A workflow writes this branch, and `pmj` reads it. Nothing here is written by hand.
`docs/registry.md` on the branch `main` says what each file holds, how a repository comes to be
indexed, and what may change.
README
  readme="$(git hash-object -w "$work/README.md")"
  git update-index --add --cacheinfo "100644,$readme,README.md"
fi

tree="$(git write-tree)"
if [ -n "$parent" ] && [ "$tree" = "$(git rev-parse "$parent^{tree}")" ]; then
  echo "registry-commit: the branch holds this already, so nothing is committed"
  exit 0
fi

name="${REGISTRY_AUTHOR_NAME:-github-actions[bot]}"
email="${REGISTRY_AUTHOR_EMAIL:-41898282+github-actions[bot]@users.noreply.github.com}"
if [ -n "$parent" ]; then
  commit="$(GIT_AUTHOR_NAME="$name" GIT_AUTHOR_EMAIL="$email" GIT_COMMITTER_NAME="$name" GIT_COMMITTER_EMAIL="$email" git commit-tree "$tree" -p "$parent" -F "$words")"
else
  commit="$(GIT_AUTHOR_NAME="$name" GIT_AUTHOR_EMAIL="$email" GIT_COMMITTER_NAME="$name" GIT_COMMITTER_EMAIL="$email" git commit-tree "$tree" -F "$words")"
fi

# Never forced. A branch that is no longer at the commit this one follows refuses it.
git push --quiet "$remote" "$commit:refs/heads/$branch" \
  || fail "\"$remote\" refused the commit: the branch \"$branch\" is no longer where it was when the registry was taken from it. Nothing was written, and the next run begins again"
echo "registry-commit: \"$branch\" is at $commit"
