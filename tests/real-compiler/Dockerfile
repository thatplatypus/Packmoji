# The machine the tests of the real compiler run on: Emojicode 1.0 beta 2 as its installer leaves
# it, a C and C++ toolchain, and the .NET SDK. Emojicode is released for x86-64 alone, so the image
# is linux/amd64 whatever builds it, and runs under emulation on anything else.
#   scripts/real-compiler.sh builds this image and runs the tests in it.
FROM --platform=linux/amd64 ubuntu:22.04

# libtinfo5 is the one library the released compiler needs that later Ubuntus no longer have, which
# is why this is 22.04. libicu70 is what the .NET SDK needs.
RUN apt-get update \
 && apt-get install -y --no-install-recommends build-essential ca-certificates curl libicu70 libtinfo5 \
 && rm -rf /var/lib/apt/lists/*

# The compiler, held to the digest its release had on 2026-10-10, and put where its own installer puts it.
ARG EMOJICODE=1.0-beta.2
ARG EMOJICODE_SHA256=c2e4a8ec156c198a80d844e747ce71c9d34efd54a2c5f3b6a116470d0cb8ef0f
RUN curl -fsSL -o /tmp/emojicode.tar.gz "https://github.com/emojicode/emojicode/releases/download/v${EMOJICODE}/Emojicode-${EMOJICODE}-Linux-x86_64.tar.gz" \
 && echo "${EMOJICODE_SHA256}  /tmp/emojicode.tar.gz" | sha256sum -c - \
 && tar -xzf /tmp/emojicode.tar.gz -C /opt \
 && cd "/opt/Emojicode-${EMOJICODE}-Linux-x86_64" \
 && install -m 755 emojicodec /usr/local/bin/emojicodec \
 && mkdir -p /usr/local/EmojicodePackages /usr/local/include/emojicode \
 && cp -r packages/. /usr/local/EmojicodePackages/ \
 && cp -r include/. /usr/local/include/emojicode/ \
 && rm -rf /tmp/emojicode.tar.gz "/opt/Emojicode-${EMOJICODE}-Linux-x86_64"

# The SDK that global.json asks for: the latest of 10.0, as CI's runner takes it.
RUN curl -fsSL -o /tmp/dotnet-install.sh https://dot.net/v1/dotnet-install.sh \
 && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
 && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet \
 && rm /tmp/dotnet-install.sh

ENV DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
WORKDIR /src
