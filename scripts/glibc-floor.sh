#!/usr/bin/env bash
# Reads from a Linux program which C library it asks for, and fails if that is newer than the one
# a release promises to start on.
#   scripts/glibc-floor.sh <program> <version, such as 2.35>
#
# A native program is built against the C library of the machine that builds it, and asks for the
# newest thing of that library it happened to use. On a machine with an older library it does not
# start at all. So a release is built on the oldest Linux it is to start on, and this is what
# says so if it ever is not. It needs objdump, which every Linux toolchain has.
set -euo pipefail

fail() {
  echo "error: $1" >&2
  exit 1
}

[ $# -eq 2 ] || fail "usage: scripts/glibc-floor.sh <program> <version, such as 2.35>"
program="$1"
promised="$2"
[ -f "$program" ] || fail "there is no program \"$program\""

# Each thing a program takes from the C library is marked with the version it came in, as
# (GLIBC_2.34). Only those are counted: GLIBCXX is the C++ library's, and GLIBC_PRIVATE no version.
needed="$(objdump -T "$program" | sed -n 's/.*(GLIBC_\([0-9][0-9.]*\)).*/\1/p' | sort -u -V | tail -1)"
if [ -z "$needed" ]; then
  echo "$(basename "$program") asks for no version of glibc."
  exit 0
fi

newest="$(printf '%s\n%s\n' "$promised" "$needed" | sort -V | tail -1)"
if [ "$newest" != "$promised" ]; then
  fail "$(basename "$program") asks for glibc $needed, and a release promises to start on $promised: it was built on a Linux that is too new"
fi

echo "$(basename "$program") asks for glibc $needed at the newest, and $promised is promised."
