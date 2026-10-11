# 0011: pmj as a released program

- **Status:** accepted
- **Date:** 2026-10-10

## Context

Until now `pmj` could be had only by building it. Blazemoji is the first tool that runs it, and it is packed for three machines: Linux and Windows on x86-64, and macOS on Apple silicon. It keeps, in a file of its own, the version of `pmj` it uses and the SHA-256 of the program for each machine, and fetches the program and holds it to that.

`pmj` is one native program, and .NET makes a native program only on the system it is for. Nothing of `pmj` had been built or run on Windows.

## Decision

- **A release is made by a workflow, when a tag `v<version>` is pushed,** and by nothing else. The tag has to be the version in `Directory.Build.props`, or the workflow stops before it builds.
- **The same workflow, run by hand, builds everything and releases nothing.** What it built is kept with the run for a week, to be looked at before a tag is pushed. It is a rehearsal: a tag builds the programs again, and what a release carries is what the run that made it built and ran.
- **A release is made once.** If one of that tag is there, the workflow fails and changes nothing.
- **A release carries four files:** `pmj-<version>-linux-x64`, `pmj-<version>-osx-arm64` and `pmj-<version>-win-x64.exe`, each the program itself, and `pmj-<version>.sha256`, with a line for each as `sha256sum` writes and checks them. The notes of the release hold the same digests.
- **Each program is built on its own system and run there before it is kept,** by the script that runs the native program in CI.
- **The Linux program is built on Ubuntu 22.04,** the oldest that GitHub runs, and the workflow reads from the program which C library it asks for and fails if that is newer than glibc 2.35. CI builds it the same way.
- **Only the job that makes the release may write to the repository,** and it runs only for a tag. Every action a workflow uses is named by its commit.
- **No program is signed,** and there is no archive and no installer.
- **On Windows `pmj` manages packages and does not build.** `build` and `run` end there with `compiler.not-found`, as on any machine with no compiler.
- **CI runs the tests and the native program on Windows** for every pull request, as it does on Linux and macOS.
- **What `pmj` writes through a pipe or into a file is UTF-8 on every machine.** What it writes to a console is left to .NET.

## What was seen

On 2026-10-10 the workflow was run on a branch, for no tag, and what it kept was fetched and tried:

- **The digests check,** with `sha256sum` on Linux and with `shasum` on macOS. A program with one byte added is refused by both.
- **The Linux program asks for glibc 2.34 at the newest.** It starts on Ubuntu 22.04, which has 2.35, and in `mcr.microsoft.com/dotnet/aspnet:10.0`, the image Blazemoji runs it in. On Ubuntu 20.04, which has 2.31, it does not start: ``version `GLIBC_2.34' not found``.
- **The macOS program starts on another Mac** once it is marked as a program. The linker signed it ad hoc, with no identity, which is all an arm64 program needs to start. It says of itself that it needs macOS 12 or newer.
- **On Windows Server 2025 every test ran and passed but those that need a shell, a symbolic link, or the compiler.** Seven failed at first, each for how the test itself read a path or deleted a file there: nothing in `pmj` had to change for them.
- **One thing in `pmj` was wrong there.** Started from a console whose code page is not UTF-8, which is every console of Windows that nobody has changed, it wrote `src/lib.??` through a pipe for `src/lib.🍇`. .NET writes in the console's code page whatever it writes to. The same was had on a Mac by naming Latin-1 in the environment. Both are tested now: the script that runs the native program runs it as on such a machine.

## Why

- **A tag, because a tag is in the repository's history and names one commit.** A release made by hand in a browser can carry any file. Pushing the tag stays the owner's act.
- **Run by hand first, so that the first tag is not also the first try.** A release that came out wrong cannot be mended, only followed.
- **Once, because a pin holds a digest.** A version whose program changed would break every tool that had pinned it, and would look like an attack to each of them.
- **The program itself, because the pin is of the program.** An archive would have a digest of its own, and how it is made, the order, the times and the compression, would have to be agreed and then kept the same for ever. What an archive would have kept is the mark that a file is a program, which costs whoever fetches one a `chmod`.
- **An old Ubuntu, because a native program asks for the C library of the machine that built it** and does not start on an older one. Ubuntu 22.04 is also the last that has a library the released Emojicode compiler needs, which is why this repository runs that compiler there: so `pmj` starts where the compiler does.
- **The check of the C library is in CI too,** so that what would stop a release stops the pull request that brought it, and not the release.
- **Not signed, because nothing that was asked for needs it.** A tool that fetches `pmj` holds it to a digest it keeps with its own code. Signing for macOS and for Windows takes certificates, and secrets in CI, which today has none.
- **No build on Windows, because no Emojicode compiler runs there.** Emojicode 1.0 beta 2 is released for Linux and macOS on x86-64 alone.
- **UTF-8 through a pipe, because a tool reads a pipe.** A machine's own characters are for a person at its console. The name of every Emojicode source file ends in a character that few of them hold.

## Alternatives

- **Archives as assets,** `.tar.gz` and `.zip`. Set aside for the reason above.
- **Signing and notarizing, and an attestation of where each program was built.** Worth having, and not asked for.
- **Building for Windows by another road,** a cross-compiler or an emulator. .NET makes a native program only on the system it is for.
- **Setting the console's code page to UTF-8** when `pmj` starts, which mends a console too. It changes the console for whatever runs after `pmj`, and it cannot be tested where CI runs, which has no console to look at.
- **A pull request that stays open and red until Windows passes,** in place of a branch of its own with a trigger that is taken out again. Both show Windows before anything is merged.

## Consequences

- **A release cannot be mended.** One that is wrong is followed by another version.
- **What a run by hand kept is not what a release carries.** A native program built twice is not the same bytes twice: the same commit, published twice in the same place on 2026-10-10, gave two programs that differ in 47 bytes. So the digests of a release are the ones the release carries, and a tool that pins one takes it from there. Releasing the very bytes that were looked at would take a workflow that promotes what an earlier run kept, which was not asked for.
- **The first release is the owner's to make:** the workflow by hand, a look at what it kept, then the tag.
- **Raising the version is all a release needs** beside the tag. The guide names the assets by `<version>`, and its one example names 0.1.0.
- **A program that a browser fetched is held back by macOS** until the mark the browser left on it is taken away. A script's is not.
- **At a console of Windows whose code page is not UTF-8, a character outside that code page is still shown as a question mark.** Only a pipe and a file were mended.
- **There is no program for Linux on ARM, macOS on Intel or Windows on ARM.**
- **When GitHub stops running Ubuntu 22.04, the workflows fail and say so,** and the promise has to be made again for whatever is then the oldest.
- **Thirteen tests of symbolic links are skipped on Windows,** as they were before any test ran there: what `pack` and `verify` do with a link on Windows is not tested.
