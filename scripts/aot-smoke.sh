#!/usr/bin/env bash
# Publishes pmj with Native AOT for one runtime and runs what was published, exactly as CI does.
#   scripts/aot-smoke.sh osx-arm64
#   scripts/aot-smoke.sh linux-x64
# A runtime can only be published on its own operating system and architecture.
#
# The native pmj is then put through what a person does with it: it makes a library and packs it,
# and makes an application that adds the library, installs it on a machine with an empty cache, and
# verifies it. GitHub is stood in for by the packed file served from this machine, so nothing here
# reaches the network. It needs python3, for that server alone.
#
# The server answers a download as GitHub does, by sending pmj on to another address, and it keeps
# which requests came with a token. So this is also where pmj is held, over real HTTP, to following
# a download where it is sent and to giving its token to the API alone.
set -euo pipefail
cd "$(dirname "$0")/.."

rid="${1:?usage: scripts/aot-smoke.sh <runtime identifier, such as osx-arm64 or linux-x64>}"
dotnet publish src/Packmoji.Cli -c Release -r "$rid"

pmj="$PWD/src/Packmoji.Cli/bin/Release/net10.0/$rid/publish/pmj"
expected="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)"

fail() {
  echo "error: $1" >&2
  exit 1
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

# A library, made and packed by the native pmj.
cd "$work"
"$pmj" new @smoke/greeter --lib > /dev/null
cd greeter
packed="$("$pmj" pack)"
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
import sys
import threading

site, asked = sys.argv[1], sys.argv[2]


class Files(http.server.SimpleHTTPRequestHandler):
    kind = "file"

    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=site, **kwargs)

    # Whether a token came is kept, and never the token.
    def note(self, kind):
        token = "yes" if self.headers.get("Authorization") else "no"
        with open(asked, "a") as log:
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


files = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Files)
github = http.server.ThreadingHTTPServer(("127.0.0.1", 0), GitHub)
threading.Thread(target=files.serve_forever, daemon=True).start()
print(f"Serving on port {github.server_port} .", flush=True)
github.serve_forever()
PYTHON
python3 -u "$work/github.py" "$work/site" "$work/asked.log" > "$work/server.log" 2>&1 &
server=$!
port=""
for _ in $(seq 1 50); do
  port="$(sed -n 's/.* port \([0-9][0-9]*\) .*/\1/p' "$work/server.log" | head -1)"
  [ -n "$port" ] && break
  sleep 0.1
done
[ -n "$port" ] || fail "the server that stands in for GitHub did not start"

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
"$pmj" install --locked
unpacked="$PACKMOJI_HOME/cache/smoke/greeter/0.1.0/$digest/src/lib.🍇"
[ -f "$unpacked" ] || fail "the library's source was not unpacked into the cache at $unpacked"

"$pmj" tree
"$pmj" verify

echo "pmj runs as a native binary for $rid ($(wc -c < "$pmj" | tr -d ' ') bytes)"
