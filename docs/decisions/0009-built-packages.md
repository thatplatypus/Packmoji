# 0009: A package is built once, and placed in each project

- **Status:** accepted
- **Date:** 2026-10-10

## Context

A package is published as source, so it is compiled where it is used. Record 0001 has each dependency built into the project's own `./packages/<name>/`, which is the one directory the compiler searches without a flag.

Two things were learned since:

- **Compiling is slow.** Grapevine's three packages took 115 seconds to build under emulation on 2026-10-10, and the compiler runs on x86-64 alone, so emulation is common.
- **The first tool that drives `pmj` makes a directory for every set of packages it meets.** Blazemoji checks a project half a second after each pause in typing, and builds a project's packages apart from the project. It asked that a package built once for a digest and a compiler never be compiled again for another directory with the same lockfile.

Built into each project alone, every one of those directories would compile Grapevine again.

## Decision

- **A package is built once for the whole machine,** into `built/<scope>/<name>/<version>/<key>/<name>/` under `PACKMOJI_HOME`, beside the cache of what was downloaded.
- **A project is given a copy,** in its own `packages/<name>/`. The rules of record 0001 for that directory stand: `pmj` changes only what it put there, and takes away what the lockfile no longer names.
- **The key is the SHA-256 of everything a build is made from:** the digest of the compiler's own file, whether it optimizes, the digest of the package's archive, the key of each package it depends on, and for a package with native code what the C or C++ compiler prints for `--version`. A number in front of them goes up whenever `pmj` itself builds in another way.
- **A stamp, `pmj-build.json`, lies beside what was built** and says what it was built from. In a project it is also the mark that `pmj` put a folder there.
- **A project's copy is held to the one `pmj` keeps, byte for byte, at every build,** and is replaced when it differs. Its stamp is not taken as proof. A project's directory may have come from someone else, and a folder in it can have the right stamp beside an archive that nobody built from what is locked.
- **An entry is there whole or not at all.** It is made in a directory beside its place and renamed into it, as a file of the cache is, so nothing here needs a lock. Of two builds of one package at once, the second finds it done and throws its own away.
- **What a package is compiled from is its unpacked files in the cache, held to its archive first.** Files that are not the archive's are `cache.mismatch`, and are never compiled.

## Why each part of the key

| Part | What would go wrong without it |
|---|---|
| The compiler's own digest, and not its version | The owner's fork of the compiler prints the banner of the release, "1.0 beta 2". By version, the two would share what they built |
| The key of each dependency | One version of a package is compiled against whichever versions of its dependencies a project locks. Two projects can lock the same `grapevine` with two versions of `crypto`, and each needs its own build of `grapevine` |
| The native compiler, only for a package with native code | A package of Emojicode alone would be built again whenever the C++ compiler was updated, and could not be built at all on a machine without one |
| Whether the compiler optimizes | `--release` would be given what was built without it |

## Alternatives

- **Build into each project alone,** as record 0001 had it. It is what the brief describes, and it compiles every package again for every project.
- **Keep built packages under `PACKMOJI_HOME` only, and give the compiler a search path for each.** Nothing is copied. But a compiler run by hand in a project finds no package, which is why the owner chose `./packages` in record 0001. And a tool would be handed paths inside `pmj`'s own directory, whose layout could then never change.
- **Links in `packages/` in place of copies.** The compiler follows them, as a run on 2026-10-10 showed. But a link breaks when `built/` is cleared, and making one takes a right that Windows does not give everyone. The copies are small: Grapevine's three built packages are 1.5 MB.
- **Build as part of `pmj install`,** which is what the compiler's own guide does with Yarn. A machine that installs need not have a compiler, and Blazemoji's host has none.
- **Put digests of what was built into the stamp, and hold an entry to them when it is used.** What is kept is `pmj`'s own, in its own directory, and can be deleted at any time. A stamp that gives the wrong key, or none, already has an entry built over.

## Consequences

- **`built/` grows.** Every compiler, every version, and every set of dependency versions a package was built against has an entry, and nothing removes one. `pmj cache clean` is where that belongs, and it is not built yet. Deleting the directory does the same.
- **The compiler's own packages and headers are not in the key.** They are taken to come with the compiler. Someone who changes them and keeps the compiler has to delete `built/`.
- **Every build reads each file of a project's `packages/` and the file it was copied from.** Grapevine's three built packages are 1.5 MB.
- **A project's `packages/` holds whichever build was made last.** A `--release` build after a plain one replaces the copies, with no compile if both were built before.
- **A machine with no C++ compiler cannot tell whether a package with native code has been built,** since the compiler is in its key. It is told that the compiler is missing.
- **Taking the compiler's digest reads the whole file,** 56 MB for the release. That took 0.8 seconds under emulation, and is done once in a build.
