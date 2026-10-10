# 0010: How a build is run

- **Status:** accepted
- **Date:** 2026-10-10

## Context

Record 0001 says what the compiler does and what `pmj` asks of it. Building it raised the choices that record left open: what a build does about versions, where the tools are found, what native code is compiled with, and how a build ends. The brief answers some of them, and two of its answers could not be kept.

What follows was checked against Emojicode 1.0 beta 2 on 2026-10-10, in a container. The sample projects in `samples/hello-pkg` are built by it on every run of `scripts/real-compiler.sh`.

## Decision

- **A build chooses no version.** It reads the lockfile, and one that is missing or that no longer answers the manifest is `lock.out-of-date`. A project that asks for no package needs no lockfile.
- **A build fetches what is locked and not yet in the cache,** exactly as `pmj install` does. With the cache filled it asks nothing of GitHub.
- **The compiler is `EMOJICODEC`, or the first `emojicodec` on `PATH`.** Its own packages are where `EMOJICODE_PACKAGES_PATH` says, or in `/usr/local/EmojicodePackages`. Its headers are where `EMOJICODE_INCLUDE` says, or in `/usr/local/include/emojicode`. The two places without a variable are where the compiler's installer puts them.
- **The compiler's own packages are in the first of those two places that holds `s`.** The compiler reads `EMOJICODE_PACKAGES_PATH` as one more place to look for any package, so it may name a directory of someone's own.
- **Native code is compiled with `$CXX` or `c++` as C++17, and with `$CC` or `cc` as C11 with the system's declarations (`gnu11`),** both with `-O2`. A file is C or C++ by the end of its name. A manifest can give no flag.
- **A tool is one program with no arguments,** started with a list of arguments and never through a shell.
- **No tool's status is believed alone.** Each has failed when it says so, and also when it says all is well and has not written what it was run to write. For the compiler there is a third way, from record 0001: `Detected in:` in what it printed.
- **`pmj` links,** with every package's archive and every archive of the compiler's own packages in one group, then `-l` for each `native.link`, then `-lm -lpthread`. On macOS the group flags are left out.
- **A build that a tool stopped ends with status 1.** `pmj run` ends with the status of the program.
- **What a tool printed goes to standard error,** each line behind the name of what was being built, and made fit to print as a diagnostic's text is. It goes there with `--json` too.
- **A build holds a lock on the project,** the file `target/.pmj-lock`, and a second build of the same project waits for it. It waits only for a lock that is held: a project that cannot be written is a problem to report.
- **Nothing is cleared or written through a symbolic link.** A build stops, with `project.unreadable` and before it writes anything, when `packages`, `target`, `target/obj`, `target/debug` or `target/release`, or the lock, is one.
- **What is missing is found before anything is compiled:** the compiler, the archiver, the compilers of every native file that is to be compiled, the project's own among them, the headers and the compiler's own packages.
- **Packages are built one at a time,** each after all it depends on, and by name among those that are ready.

## Where this leaves the brief

- **The brief says to end with the compiler's own status.** The compiler ends with 0 for three kinds of failure, and its 70 is what `pmj` ends with for a fault of its own. So a build that was stopped ends with 1, as every command that reports a problem does.
- **The brief says to skip a package when its digest, the compiler's version and the native flags are unchanged.** The compiler's version cannot tell two compilers apart, so it is the compiler's digest. Record 0009 has the whole key.
- **The brief has a build stamp in each project's `target/`.** The stamp lies with what was built, which record 0009 moved.

## Why

- **A build that chose versions would change the lockfile under someone who asked only to compile.** Refusing is the stricter of the two, and can be loosened later without breaking anything.
- **C++17 is what the compiler and its headers are built as** (`CMakeLists.txt:4` in the compiler's source). `gnu11` and not `c11`, because native code is there to reach the system, and plain C11 hides the system's own declarations.
- **A flag from a package would be someone else's option given to the compiler of whoever builds it.** `-fplugin` and `-include` are flags.
- **`m` and `pthread` are the link hints of the compiler's own package `s`,** the only one of the six that has any (seen in a run, in the installed `🏛` files). `pmj` does not read hints out of interface files: a package's manifest says what to link.
- **The linker of macOS refuses `--start-group`,** and links archives that need each other in either order without it. That was seen with Apple clang 17 on archives of C. No Emojicode compiler runs on the machine it was seen on, so a link of a real program on macOS has not been seen.
- **A project's directory may have come from someone else, links and all.** A build clears what is at the places it writes to and then writes there. With `target/debug` a link to a directory, and a package named for a folder in that directory, a build would delete the folder. On a disk that does not tell case apart, a package called `documents` is enough for a home directory's `Documents`.
- **What a tool prints may repeat a line of a package's code,** and the code is someone else's. A character that could drive a terminal is shown as its number.

## Alternatives

- **Resolve when the lockfile is out of date, as `cargo build` does.** One command would then do everything. It is the looser choice, and it stays open.
- **Let a manifest give flags, or a C++ standard.** Grapevine's one native file needs neither.
- **Read each package's link hints from its interface file,** as the compiler's own link does. It would need a reader for a file whose form is the compiler's to change.
- **Give the compiler one search path that holds every built package, made of links.** Several `-S` are simpler, and were seen to work.
- **Compile a package in a directory of its own, from a fresh copy of its archive.** The compiler's messages would then name files that are gone when they are read. Compiling the cache's own files, held to the archive first, names files that are there.

## Consequences

- **A project whose lockfile is out of date takes two commands to build:** `pmj install`, then `pmj build`.
- **A package whose native code needs a flag cannot be built.** None is known.
- **Linking on macOS is untried with a real program.** The compiler is released for macOS on x86-64, and nobody has run this there.
- **Nothing limits how long a tool may take.** A tool that drives `pmj` sets its own limit, and a `pmj` that is stopped stops the tool it started and waits for it to be gone.
- **A `target` or a `packages` that someone linked to another disk on purpose is refused,** and there is no way round it yet. Letting one through later, by a setting of the person's own, would break nothing.
- **A program that ends with 1, 2, 70 or 130 cannot be told from `pmj run` itself ending so.** That is what passing a program's status on means.
