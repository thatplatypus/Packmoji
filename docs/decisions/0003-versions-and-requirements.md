# 0003: Versions and requirements

- **Status:** accepted
- **Date:** 2026-10-09

## Context

Packmoji resolves dependencies by minimal version selection: every requirement is a minimum, and a build takes the highest minimum that anything asks for and nothing newer. That needs versions that can be ordered without doubt, and a requirement that is always a minimum. The brief asked for SemVer 2.0.0, followed strictly, from a library or from code of our own.

## Decision

- **A version is SemVer 2.0.0, parsed and ordered by `Packmoji.Core`'s own code.** It is about two hundred lines, held to the specification's own examples of what is valid, what is not, and what comes before what.
- **A package version has no build metadata.** `1.0.0+abc` is refused, under a code of its own.
- **A requirement is two or three numbers**, such as `1.2` or `0.4.1`, and the form with three may carry a pre-release. There are no operators, ranges or wildcards, and no bare `1`.
- **A compatibility line is a major version from 1 up, and a minor version below 1.** A requirement never selects across a line.
- **`package.emojicode` is required, and is `>=` followed by a full version.** It has no line and no upper bound.
- **Two limits:** each of a version's three numbers is at most 2147483647, and a version is at most 64 characters.

## Why

- **Our own code, because the specification is small and Core then depends on nothing.** The `Semver` package is from 2024, says nothing of Native AOT, and brings a dependency of its own. The requirement grammar would be ours to write either way.
- **No build metadata, because two versions that differ only there have equal precedence.** A registry of versions that never change has no way to say which of the two a requirement selects.
- **No operators, because minimal version selection leaves an upper bound with nothing to do.** The line already says where compatibility ends.
- **No bare `1`, because it can be allowed later without breaking a manifest**, and a rule that has been loose cannot be made strict.
- **`emojicode` required, because a package published without it can never have it added.** A published version does not change.

## Alternatives

- **Cargo's rule for versions below 0.1**, where `0.0.3` and `0.0.4` are on lines of their own. The brief says a line below 1 is a minor version, which puts them on one line, and one rule for everything below 1 is simpler. This is a difference from Cargo that a reader who knows Cargo should be told of, and now has been.
- **Keeping build metadata and ignoring it when ordering**, as Go does with its own marker. It leaves two tags that mean one version.

## Consequences

- A pre-release is selected only when a requirement names one, or when it is the highest minimum asked for.
- The one compiler that exists is `1.0.0-beta.2`, which is before `1.0.0`. Every example asks for `>=1.0.0-beta.2`.
- A version with a number above 2147483647 is valid SemVer and is refused here. No one has needed one.
