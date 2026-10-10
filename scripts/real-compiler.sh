#!/usr/bin/env bash
# Runs the tests that need the real Emojicode compiler, in a container that has it, exactly as CI does.
#   scripts/real-compiler.sh
#
# Every other test has a compiler that is made up. These build the sample projects with Emojicode
# 1.0 beta 2 as it is released, C++ included, link the program and run it. It needs Docker.
#
# Emojicode is released for x86-64 alone, so the container is linux/amd64, and on any other machine
# it runs under emulation. There a .NET process is now and then frozen for good or aborted by the
# emulator, as it starts other processes while it is still compiling its own code. So a run that
# ended that way is tried again, up to three times. A test that failed is never tried again.
set -euo pipefail
cd "$(dirname "$0")/.."

image=packmoji-real-compiler
docker build -q --platform linux/amd64 -t "$image" tests/real-compiler > /dev/null

# The checkout is copied into the container and thrown away with it, so that nothing is written
# into it, and so that names keep their case, which a directory shared from a Mac does not.
docker run --rm --platform linux/amd64 \
  -v "$PWD":/host:ro \
  -v packmoji-real-compiler-nuget:/root/.nuget/packages \
  "$image" bash -c '
    set -euo pipefail
    cd /host
    tar -cf - --exclude=./.git --exclude=bin --exclude=obj --exclude=./artifacts --exclude=./TestResults --exclude=./docs/superpowers --exclude=target . | tar -xf - -C /src
    cd /src
    attempt=1
    while :; do
      status=0
      # A test that is skipped here has not been run where it was meant to be, and that is a failure.
      PACKMOJI_REAL_COMPILER=1 timeout -k 10 900 dotnet test --project tests/Packmoji.Cli.Tests -c Release --filter-trait Category=RealCompiler --fail-skips on || status=$?
      case "$status" in
        124|133|137)
          if [ "$attempt" -ge 3 ]; then
            exit "$status"
          fi
          attempt=$((attempt + 1))
          echo "The test process was frozen or aborted by the emulator (status $status). Trying again." >&2
          ;;
        *)
          exit "$status"
          ;;
      esac
    done
  '
