# 0005: The shape of the manifest and the lockfile

- **Status:** accepted
- **Date:** 2026-10-09

## Context

Record 0002 chose JSON. This one records what is in the two files where that differs from the brief's sketch, and how strictly they are read.

## Decision

**The manifest**

- **Every package has an entry file.** `build.entry` names it. When it is absent, an app's is `src/main` and a library's is `src/lib`, with `.emojic` tried before `.🍇`, and exactly one of the two must exist. `pmj new` always writes the key.
- **The default sources are `src/**/*.emojic` and `src/**/*.🍇`.**
- **`policy.requireAttestation` is in the format now,** though nothing enforces it until attestations are verified.
- **A path stays inside the package,** and a pattern has `*`, `?` and `**` and nothing else.
- **An entry of `native.link` is a library's name** and cannot begin with a hyphen.

**The lockfile**

- **`root` records what the manifest asked for when the lockfile was written.**
- **`releaseTag` and `asset` are written, and are checked against the name and the version** when the file is read. The model does not store them.
- **The reader checks that a lockfile agrees with itself:** no package twice, no two of one bare name, no dependency on something the file does not hold, nothing in `root` unanswered, and nothing that is not led to.
- **Whatever is wrong with a lockfile, the fix given is the same:** delete it and have it written again.

**Both**

- **An unknown key is an error.**
- **A reader reports every problem it finds, not the first,** each with its line and column.
- **`pmj` writes each file in one canonical form.**

## Why

- **An entry file for a library too, because the compiler is given exactly one file for a package** (record 0001). The brief had an entry for apps alone.
- **Both suffixes, because the compiler accepts both** and Grapevine is written in `.🍇` throughout.
- **`root`, because the brief says the resolver runs only when the manifest has changed,** and that needs something to compare the manifest with. The requirements themselves, written out, can be read in a diff and merged. A hash of them could do neither, and conflicts on every merge.
- **A library's name and nothing else in `link`, because a manifest is someone else's file,** and it must not be able to hand the linker a flag.
- **Unknown keys refused, because a format that refuses what it does not know never has to guess** what an old file meant by a key that has since been given a meaning.
- **Every problem at once, because a person fixing a manifest should not be made to run a command for each mistake.**

## Alternatives

- **An entry that is always written,** with no convention. It is one rule fewer, and the owner chose the convention, with `pmj new` writing the key so that a made manifest does not lean on it.
- **Working out whether the manifest changed from the lockfile's packages alone.** A requirement that was lowered would then go unnoticed, and the lockfile would keep a version newer than anything asks for.
- **Leaving `releaseTag` and `asset` out, since they follow from the name and the version.** The brief has them, and with them a tool can fetch a package from the lockfile alone, knowing none of Packmoji's rules.

## Consequences

- A manifest written for a later `pmj`, with a key this one does not know, is refused by this one. That is the price of strictness, and it is paid once for each key added.
- A lockfile that was merged by hand and no longer agrees with itself is caught when it is read, before anything is fetched because of it.
- Editing a manifest in place, keeping the order its author gave its keys, is a later milestone's work. What exists now writes a whole manifest from a model.
