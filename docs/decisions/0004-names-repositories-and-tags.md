# 0004: Names, repositories and release tags

- **Status:** accepted
- **Date:** 2026-10-09

## Context

A package's name is the one thing about it that can never change, and three things had to be settled about it before any code depended on them.

- The brief allowed a hyphen in a name. Record 0001 found that the compiler accepts one, and that the runtime's `SET_INFO_FOR` macro, which native code uses to mirror an Emojicode class, pastes the package's name into a C++ identifier, where a hyphen cannot be.
- The brief had one package to a repository, with the tag `v1.2.3`. The first real package, Grapevine, is a repository that holds three.
- The brief's examples were under the scope of an organization. These are the owner's own projects, published from the owner's own account, so their scope is `@thatplatypus`.

## Decision

- **A name is lowercase letters, digits and single underscores, beginning with a letter, of 1 to 64 characters.** It has no hyphen. It is the name Emojicode imports, unchanged.
- **A scope is the GitHub owner, in lowercase,** by GitHub's own rule for an owner: letters and digits with single hyphens inside, 1 to 39 characters.
- **Six names are reserved:** `s`, `runtime`, `files`, `sockets`, `json` and `testtube`, the compiler's own packages.
- **A repository may hold several packages.** Its owner must be the scope of each. A package that is not in a repository of its own name says where it is in its manifest.
- **A release's tag is always `<name>-v<version>`,** in a repository of one package as in a repository of several.
- **Uppercase is refused, never lowercased.** The diagnostic names the lowercase spelling.

## Why

- **No hyphen, because the rule can be loosened later and not tightened.** Allowing hyphens one day breaks no published name. Forbidding them one day would.
- **One form of tag, because two forms have to be told apart forever.** A name has no hyphen, so the first hyphen in a tag ends the name, even when the version has hyphens of its own, as `crypto-v1.0.0-beta.1` has.
- **The scope is the owner, because that is the whole of the ownership check.** Only someone who can publish a release in an owner's repositories can publish under the owner's scope, and Packmoji needs no accounts.
- **Reserved names, because a package of a stock package's name replaces it** for every package in a build, which is a way to deceive as well as a way to break things.

## Alternatives

- **Hyphens in the name, turned into underscores for the import,** as Cargo does. It gives friendlier names at the cost of a rule that every tool and every author has to know.
- **Hyphens passed straight through.** It works for a package of pure Emojicode, and its author finds out about the macro after the name is published.
- **One package to a repository, tagged `v1.2.3`.** It is the simplest, and the name then always says where the package is. It would have Grapevine split into three repositories, and adding shared repositories later would mean a second form of tag.

## Consequences

- **Grapevine's packages are `@thatplatypus/crypto`, `@thatplatypus/deflate` and `@thatplatypus/grapevine`,** with the tags `crypto-v…`, `deflate-v…` and `grapevine-v…` in one repository.
- **A name no longer always says where a package is.** The registry learns that from the manifest when it indexes a release. Without the registry, `pmj` can find a package in a repository of its own name, or from a lockfile, and for any other it needs to be told. How it is told is for the milestone that builds that mode.
- **Each package's archive holds its own directory and nothing above it.** Files that Grapevine's packages share from a directory beside them need a home in each package, or a package of their own.
