#!/usr/bin/env bash
# Publishes pmj with Native AOT for one runtime and runs what was published, exactly as CI does.
#   scripts/aot-smoke.sh osx-arm64
#   scripts/aot-smoke.sh linux-x64
#   scripts/aot-smoke.sh win-x64
# A runtime can only be published on its own operating system and architecture. On Windows this is
# run by the bash that comes with Git.
#
# The native pmj is then put through what a person does with it: it makes a library and packs it,
# and makes an application that adds the library, installs it on a machine with an empty cache, and
# verifies it. GitHub is stood in for by the packed file served from this machine, so nothing here
# reaches the network. It needs Python 3, for that server alone.
#
# On the way it is run once on a machine that limits the scopes it may depend on, where the
# library is refused and GitHub is asked nothing, and once as a tool runs it, for one JSON object.
#
# The server answers a download as GitHub does, by sending pmj on to another address, and it keeps
# which requests came with a token. So this is also where pmj is held, over real HTTP, to following
# a download where it is sent and to giving its token to the API alone.
#
# Last, the native pmj builds the application and runs it. The compiler, the C++ compiler and the
# archiver are stood in for by small scripts, since no Emojicode compiler runs on every machine
# this runs on. What that shows is the native binary starting programs, reading what they print,
# running what was built with its own streams, and ending as the program ended. The real compiler
# is scripts/real-compiler.sh's to run.
#
# On Windows that last part is left out, and pmj is held to saying that there is no compiler: none
# runs there, and the tools that stand in for one are scripts for a shell, which Windows does not
# start as programs.
set -euo pipefail
cd "$(dirname "$0")/.."

rid="${1:?usage: scripts/aot-smoke.sh <runtime identifier, such as osx-arm64, linux-x64 or win-x64>}"
dotnet publish src/Packmoji.Cli -c Release -r "$rid"

case "$rid" in
  win-*) windows="yes"; program="pmj.exe" ;;
  *) windows=""; program="pmj" ;;
esac
pmj="$PWD/src/Packmoji.Cli/bin/Release/net10.0/$rid/publish/$program"
expected="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)"

fail() {
  echo "error: $1" >&2
  exit 1
}

# pmj ends a line as its machine does, and on Windows that is with a carriage return before the
# line feed. It is no part of a line that is looked for here.
plain() {
  tr -d '\r'
}

version="$("$pmj" --version)"
echo "pmj --version: $version"
case "$version" in
  "$expected"*) ;;
  *) fail "expected a version beginning with $expected" ;;
esac

# Captured first and searched afterwards: a pipe into grep -q would end pmj early, and with
# pipefail that is a failure.
help="$("$pmj" --help)"
case "$help" in
  *"Packmoji, the package manager for Emojicode."*) ;;
  *) fail "--help did not print pmj's description" ;;
esac

status=0
"$pmj" frobnicate > /dev/null 2>&1 || status=$?
[ "$status" -eq 2 ] || fail "an unknown command ended with status $status, and not with 2"

work="$(mktemp -d)"
# Written as Windows writes a path, which pmj and Python need there and this bash reads as well.
[ -z "$windows" ] || work="$(cygpath -m "$work")"
server=""
cleanup() {
  if [ -n "$server" ]; then
    kill "$server" 2> /dev/null || true
    wait "$server" 2> /dev/null || true
  fi
  chmod -R u+w "$work" 2> /dev/null || true
  rm -rf "$work"
}
trap cleanup EXIT

# A library, made and packed by the native pmj. What pmj says of it is read as a tool reads it,
# through a pipe, and it names a file whose name is not ASCII: that has to arrive as UTF-8 on every
# machine, whatever the machine writes to a console of its own.
cd "$work"
made="$("$pmj" new @smoke/greeter --lib | plain)"
case "$made" in
  *"  src/lib.🍇"*) ;;
  *) echo "$made" >&2; fail "pmj new did not name the library's source file, src/lib.🍇, in UTF-8" ;;
esac
cd greeter
packed="$("$pmj" pack | plain)"
echo "$packed"
digest="$(echo "$packed" | sed -n 's/^  sha256 //p')"
[ "${#digest}" -eq 64 ] || fail "pmj pack did not print a digest"

# The release that would carry it, laid out as GitHub's addresses are, and served from here.
release="$work/site/smoke/greeter/releases/download/greeter-v0.1.0"
mkdir -p "$release" "$work/site/repos/smoke/greeter"
cp target/greeter-0.1.0.pmj.tar.gz "$release/"
echo '[{"tag_name":"greeter-v0.1.0","draft":false,"prerelease":false,"assets":[{"name":"greeter-0.1.0.pmj.tar.gz"}]}]' > "$work/site/repos/smoke/greeter/releases"

cat > "$work/github.py" <<'PYTHON'
import http.server
import socketserver
import sys
import threading

site, asked = sys.argv[1], sys.argv[2]


class Server(http.server.ThreadingHTTPServer):
    # http.server looks up the name of the address it binds, and nothing here uses the name. On
    # CI's macOS that lookup of 127.0.0.1 took 35 seconds (seen on 2026-10-10), so it is not made.
    def server_bind(self):
        socketserver.TCPServer.server_bind(self)
        self.server_name, self.server_port = self.server_address[:2]


class Files(http.server.SimpleHTTPRequestHandler):
    kind = "file"

    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=site, **kwargs)

    # Whether a token came is kept, and never the token.
    def note(self, kind):
        token = "yes" if self.headers.get("Authorization") else "no"
        # A line ends as it does here on every machine: whole lines of this file are looked for.
        with open(asked, "a", newline="\n") as log:
            log.write(f"{kind} {self.path.split('?')[0]} token={token}\n")

    def do_GET(self):
        self.note(self.kind)
        super().do_GET()

    def log_message(self, *args):
        pass


class GitHub(Files):
    kind = "api"

    def do_GET(self):
        if "/releases/download/" not in self.path:
            super().do_GET()
            return
        self.note("download")
        self.send_response(302)
        self.send_header("Location", f"http://127.0.0.1:{files.server_port}{self.path}")
        self.send_header("Content-Length", "0")
        self.end_headers()


files = Server(("127.0.0.1", 0), Files)
github = Server(("127.0.0.1", 0), GitHub)
threading.Thread(target=files.serve_forever, daemon=True).start()
print(f"Serving on port {github.server_port} .", flush=True)
github.serve_forever()
PYTHON

# Python by the name python3 or, as on Windows, python. A program of that name that does not run
# is passed over: Windows keeps one that only says where Python can be had.
python=""
for named in python3 python; do
  if "$named" -c "" > /dev/null 2>&1; then
    python="$named"
    break
  fi
done
[ -n "$python" ] || fail "Python 3 was not found, and the server that stands in for GitHub is written in it"
"$python" -u "$work/github.py" "$work/site" "$work/asked.log" > "$work/server.log" 2>&1 &
server=$!

# Waited for by what it does and not by the clock, for how long a process takes to start on a
# machine that is shared is not known: it has started when it names its port, and it has failed
# when it is gone.
port=""
waited=$SECONDS
while [ -z "$port" ] && kill -0 "$server" 2> /dev/null && [ $((SECONDS - waited)) -lt 60 ]; do
  sleep 0.1
  port="$(sed -n 's/.* port \([0-9][0-9]*\) .*/\1/p' "$work/server.log" | head -1)"
done
if [ -z "$port" ]; then
  echo "The server that stands in for GitHub, run by $(command -v "$python"), said this in $((SECONDS - waited)) seconds:" >&2
  cat "$work/server.log" >&2
  fail "the server that stands in for GitHub did not start"
fi

export PACKMOJI_HOME="$work/home"
export PACKMOJI_GITHUB="http://127.0.0.1:$port"
export PACKMOJI_GITHUB_API="http://127.0.0.1:$port"
export GITHUB_TOKEN="smoke-token-that-is-no-ones"

# An application that depends on it.
cd "$work"
"$pmj" new @smoke/app > /dev/null
cd app
"$pmj" add @smoke/greeter 2>&1 | tee "$work/said.log"
grep -q "\"sha256\": \"$digest\"" packmoji.lock || fail "the lockfile does not hold the digest that pmj pack printed"

# The token went to the API. It went with no download, and not on to where a download was sent.
download="/smoke/greeter/releases/download/greeter-v0.1.0/greeter-0.1.0.pmj.tar.gz"
grep -qx "api /repos/smoke/greeter/releases token=yes" "$work/asked.log" || fail "the list of releases was not asked for with the token"
grep -qx "download $download token=no" "$work/asked.log" || fail "the release was not downloaded, or was asked for with the token"
grep -qx "file $download token=no" "$work/asked.log" || fail "the download was not followed to where it was sent on, or the token went with it"
if grep -Eq "^(download|file) .* token=yes$" "$work/asked.log"; then fail "the token went with a download"; fi
if grep -q "smoke-token" "$work/said.log" packmoji.lock packmoji.json; then fail "the token is in what pmj printed or wrote"; fi

# On a machine that has fetched nothing, the lockfile alone is enough, and may not change.
chmod -R u+w "$PACKMOJI_HOME" && rm -rf "$PACKMOJI_HOME"

# First on a machine that limits the scopes pmj may depend on, to one the package is not of: it is
# refused, and though nothing is in the cache, nothing is asked of GitHub about it.
asked="$(wc -l < "$work/asked.log")"
status=0
PACKMOJI_SCOPES=elsewhere "$pmj" install --locked > /dev/null 2> "$work/scope.said" || status=$?
plain < "$work/scope.said" > "$work/scope.err"
[ "$status" -eq 1 ] || { cat "$work/scope.err" >&2; fail "pmj install ended with status $status under a limit that does not allow the package"; }
grep -qx 'error\[scope.not-allowed\]: "@smoke/greeter" is outside the scopes pmj is limited to here.' "$work/scope.err" || fail "the package was not refused for its scope"
[ "$(wc -l < "$work/asked.log")" = "$asked" ] || fail "GitHub was asked something about a package that is not allowed"
[ ! -e "$PACKMOJI_HOME/cache" ] || fail "a package that is not allowed was fetched"

"$pmj" install --locked
unpacked="$PACKMOJI_HOME/cache/smoke/greeter/0.1.0/$digest/src/lib.🍇"
[ -f "$unpacked" ] || fail "the library's source was not unpacked into the cache at $unpacked"

"$pmj" tree
"$pmj" verify

# A tool that restores a project is answered with one object, which says what is locked.
answer="$("$pmj" install --locked --json 2> "$work/json.err")"
case "$answer" in
  '{'*'"ok": true'*'"written": false'*'"changes": []'*'"name": "@smoke/greeter"'*"\"sha256\": \"$digest\""*) ;;
  *) echo "$answer" >&2; fail "pmj install --json did not answer with what is locked" ;;
esac
[ ! -s "$work/json.err" ] || { cat "$work/json.err" >&2; fail "pmj install --json wrote to standard error"; }

if [ -n "$windows" ]; then
  status=0
  "$pmj" build > /dev/null 2> "$work/build.said" || status=$?
  plain < "$work/build.said" > "$work/build.err"
  [ "$status" -eq 1 ] || { cat "$work/build.err" >&2; fail "pmj build ended with status $status on a machine with no compiler"; }
  grep -qx 'error\[compiler.not-found\]: The Emojicode compiler was not found.' "$work/build.err" || { cat "$work/build.err" >&2; fail "pmj build did not say that there is no compiler"; }
  echo "pmj runs as a native binary for $rid ($(wc -c < "$pmj" | tr -d ' ') bytes), and builds nothing there"
  exit 0
fi

# The tools of a build, stood in for. Each writes what it is asked to write, and the program the
# linker makes says what it was given and ends with a status of its own.
tools="$work/tools"
mkdir -p "$tools" "$work/stock/s" "$work/stock/runtime"
touch "$work/stock/s/libs.a" "$work/stock/runtime/libruntime.a"
cat > "$tools/emojicodec" <<'TOOL'
#!/bin/sh
out=""; interface=""; report=""
while [ $# -gt 0 ]; do
  case "$1" in
    --help) echo "  emojicodec file {OPTIONS}"; echo; echo "    Emojicode Compiler 1.0 beta 2. Visit https://www.emojicode.org for help."; exit 0 ;;
    -o) out="$2"; shift ;;
    -i) interface="$2"; shift ;;
    -r) report="yes" ;;
    -p|-S) shift ;;
  esac
  shift
done
echo "a stand-in object" > "$out"
[ -z "$interface" ] || echo "💭 a stand-in interface" > "$interface"
[ -z "$report" ] || echo '{ "types": [] }' > "$(dirname "$out")/documentation.json"
echo "the stand-in compiler has a word to say" >&2
TOOL
cat > "$tools/c++" <<'TOOL'
#!/bin/sh
out=""
for argument in "$@"; do
  [ "$previous" = "-o" ] && out="$argument"
  previous="$argument"
done
printf '#!/bin/sh\necho "the program was given: $*"\nexit 3\n' > "$out"
chmod +x "$out"
TOOL
cat > "$tools/ar" <<'TOOL'
#!/bin/sh
shift
out="$1"; shift
cat "$@" > "$out"
TOOL
chmod +x "$tools/emojicodec" "$tools/c++" "$tools/ar"
export EMOJICODEC="$tools/emojicodec" CXX="$tools/c++" AR="$tools/ar" EMOJICODE_PACKAGES_PATH="$work/stock"

# The native pmj starts each of them, reads what it printed, and puts what was built where it belongs.
status=0
"$pmj" build > "$work/built.log" 2> "$work/built.err" || status=$?
[ "$status" -eq 0 ] || { cat "$work/built.err" >&2; fail "pmj build ended with status $status"; }
cat "$work/built.log"
grep -qx "Building @smoke/greeter 0.1.0" "$work/built.log" || fail "the library was not built"
grep -qx "Built the application target/debug/app." "$work/built.log" || fail "the application was not built"
grep -qx "\[greeter\] the stand-in compiler has a word to say" "$work/built.err" || fail "what the compiler printed was not passed on behind the package's name"
for made in "packages/greeter/🏛" packages/greeter/libgreeter.a packages/greeter/documentation.json packages/greeter/pmj-build.json target/debug/app; do
  [ -f "$made" ] || fail "the build did not leave $made"
done

# A tool is answered with one object, and what was built is not built again.
answer="$("$pmj" build --dependencies-only --json 2> /dev/null)"
case "$answer" in
  '{'*'"ok": true'*'"bareName": "greeter"'*'"built": false'*) ;;
  *) echo "$answer" >&2; fail "pmj build --json did not answer with the package, built before" ;;
esac

# The program is run with pmj's own streams, is given what follows the two dashes, and pmj ends as it ended.
status=0
said="$("$pmj" run -- one "two words" 2> "$work/run.err")" || status=$?
[ "$said" = "the program was given: one two words" ] || fail "pmj run printed \"$said\", and not what the program says"
[ "$status" -eq 3 ] || fail "pmj run ended with status $status, and not with the program's 3"
grep -qx "Built the application target/debug/app." "$work/run.err" || fail "what pmj run says of the build is not on standard error"

echo "pmj runs as a native binary for $rid ($(wc -c < "$pmj" | tr -d ' ') bytes)"
