# How pmj chooses versions

`pmj` turns what your `packmoji.json` asks for into the exact packages your build uses, and records them in `packmoji.lock`. This page says how it chooses, what stops it, and what to do then.

The rule is in `Packmoji.Core` today. The `pmj` commands that use it come with the next milestone.

## The rule

- **Every requirement is a minimum.** `"@thatplatypus/crypto": "1.2"` asks for version 1.2.0, or anything later on the same compatibility line.
- **The version used is the highest minimum anyone asked for, and nothing newer.** If your project asks for `1.0` and a package you depend on asks for `1.2`, the build uses 1.2.0, even when 1.9.0 has been published.
- **So a build does not change because something was published.** It changes when a manifest does.

This is minimal version selection, the rule Go modules use.

### An example

Your project asks for `@thatplatypus/grapevine` at `0.3` and `@thatplatypus/crypto` at `1.0`. Version 0.3.0 of grapevine asks for crypto at `1.2` and `@thatplatypus/deflate` at `0.1`. Crypto has published 1.0.0, 1.2.0 and 1.9.0.

| Package | Used | Because |
|---|---|---|
| `@thatplatypus/crypto` | `1.2.0` | The highest minimum asked for is `1.2`. Nothing asks for 1.9.0 |
| `@thatplatypus/deflate` | `0.1.0` | Grapevine asks for `0.1` |
| `@thatplatypus/grapevine` | `0.3.0` | Your project asks for `0.3` |

### What follows from the rule

- **To get a newer version, ask for it.** Raise the minimum in your own `packmoji.json`. Your project may always ask for more than its dependencies do, on the same line.
- **`pmj` never moves to a later version by itself,** not even past a version that has been withdrawn. It tells you which minimum to raise.
- **A pre-release is used only when it is the highest minimum asked for.** Nothing hides one that was asked for, and none is used that was not.
- **What your project needs only to develop it is resolved too.** That is its `devDependencies`. What a dependency needs only to develop it is not.
- **Every requirement that can be reached counts,** whichever version made it. Say your project and another package ask for two versions of crypto. The higher is used, and what the lower one asked for of its own dependencies still counts. That keeps the answer from depending on the order anything is looked at in.
- **A package that only a passed-over version asks for is not in the build.** It still counts as above: its requirements raise minimums, and a version of it that was never published is still an error.

## Compatibility lines

A build holds one version of a package, and a requirement only ever selects within its own line. From 1.0.0 up, a line is a major version: `1.x`, `2.x`. Below 1.0.0, each minor version is a line of its own: `0.3.x`, `0.4.x`.

So one package asked for on two lines has no answer, and `pmj` stops and shows who asks for each. The fix is to raise the minimums that lead to the older line.

## What stops a resolution

Each of these is an error, and `pmj` reports every one it finds, not only the first.

| Code | What happened | What to do |
|---|---|---|
| `dependency.duplicate` | Your manifest names one package twice | Keep one of the two requirements |
| `resolve.version-missing` | A requirement names a version that was never published. This counts wherever it is asked for, because publishing that version later could otherwise change your build | Ask for a published version. If the requirement is in a package you depend on, use a later version of that package |
| `resolve.yanked` | The version your build would use has been yanked, and your lockfile does not already hold it | Ask for a later version on the same line, in your own manifest |
| `resolve.quarantined` | The version your build would use is quarantined | Ask for another version, and tell the package's author |
| `resolve.line-conflict` | One package is asked for on two compatibility lines | Raise the minimums that lead to the older line |
| `resolve.name-collision` | Two packages share a bare name, or one shares yours. Emojicode imports by bare name, so they cannot be in one build | Depend on only one of them |
| `resolve.cycle` | Packages depend on one another in a circle, or one depends on your project. Emojicode cannot build that | Use versions that do not need one another |
| `resolve.graph-too-large` | What your project depends on is more than 10,000 versions | Look at what brings so much in, and at where `pmj` gets its package information |
| `repository.owner-mismatch` | A version is said to live in a repository that its scope does not own | Do not build with it. What told `pmj` where it lives is wrong |
| `lock.mismatch` | Your lockfile records one digest or repository for a version, and what is published has another | Find out which is right before going on. See below |

One code is a warning, and does not stop anything:

| Code | What happened | What to do |
|---|---|---|
| `resolve.yanked-locked` | A version your lockfile already holds has been yanked | Move to a later version when you can |

Errors are listed before warnings, and at most a hundred problems are listed, with the rest counted.

### Chains

A problem with a package says how `pmj` came to it, as a chain of requirements:

```
@thatplatypus/app → @thatplatypus/grapevine@0.3.0 → @thatplatypus/crypto@1.0
```

The first step is your project. Each step after it is a version that asked. The last is the package asked for, with the requirement as it was written. This one says that your project asks for grapevine, and that grapevine 0.3.0 asks for crypto at `1.0`.

- **The chain shown is the shortest,** and of two equally short the one that sorts first, so the same graph always shows the same chain.
- **A long chain shows its two ends.** The steps between are counted, as in `(12 more)`.
- **A conflict shows a chain for each line, a shared name a chain for each package, and a circle shows the circle.**

## Yanked and quarantined versions

**Yanked** means the author withdrew the version. It is still there, so that what already uses it keeps working.

- If your lockfile already holds that exact version, your build goes on using it, and `pmj` warns you.
- If not, it is an error. Nothing may start to use a yanked version.
- Either way the fix is yours to make: ask for a later version in your own manifest.

**Quarantined** means the version's bytes changed after it was published. That is what a tampered release looks like, so it is an error whether or not your lockfile holds it.

**A lockfile that disagrees with what is published** (`lock.mismatch`) is the same kind of signal, and deleting the lockfile is the wrong first move. If the lockfile is as it was committed, the published version has been replaced and must not be used. If the lockfile was edited or badly merged, restore it.

## When nothing is resolved

If your manifest still asks for exactly what the lockfile recorded, `pmj` chooses nothing. Reordering a table, or writing `1.2.0` where it said `1.2`, is not a change.

It does still ask about each locked package, because what was locked may have been yanked or quarantined since:

| What is published now | Outcome |
|---|---|
| The same version, with the same digest and repository | Nothing |
| It has been yanked | The warning `resolve.yanked-locked` |
| It is quarantined | The error `resolve.quarantined` |
| It is not published | The error `resolve.version-missing` |
| Another digest, or another repository | The error `lock.mismatch` |

## The same answer every time

Nothing in a resolution depends on the order of a manifest's tables or of a package's dependencies.

- The packages are in order of full name.
- What stops a resolution is listed in the order of the table above, and within one row by name.
- `pmj` asks only for the exact versions that requirements name, each once, and never for a list of what exists.

## Why it works this way

The decisions behind this page, and what each costs, are in [decisions/0006-minimal-version-selection.md](decisions/0006-minimal-version-selection.md).
