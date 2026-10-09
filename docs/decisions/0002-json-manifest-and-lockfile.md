# 0002: The manifest and the lockfile are JSON

- **Status:** accepted
- **Date:** 2026-10-09

## Context

The build brief asked for a TOML manifest, `packmoji.toml`, after Cargo's, and a TOML lockfile. `pmj` is a native binary built with Native AOT, where a library that needs reflection does not work, so the brief also asked that a TOML library be shown to work under AOT before it was relied on, with a hand-written reader for a subset of TOML as the fallback.

Before that work began, the owner asked whether there was a strong reason to prefer TOML to JSON.

## Decision

Both files are strict JSON: `packmoji.json`, and `packmoji.lock`, which holds JSON.

- **Strict** means RFC 8259 and nothing more: no comments and no trailing commas.
- **A key of more than one word is in camelCase:** `devDependencies`, `includeDirs`, `releaseTag`, `requireAttestation`.

## Why

- **.NET reads JSON without reflection, and that reader is proven under Native AOT.** There is no third-party parser, nothing to prove, and no fallback to write. With this decision `Packmoji.Core` depends on nothing outside .NET.
- **Blazemoji's editor is Monaco, which validates and completes JSON from a schema.** For TOML it colours the text and no more.
- **A script can read the files with `jq`**, as a release workflow needs to.
- **Everything else Packmoji speaks is JSON already:** the registry's API, `--json` output, and GitHub's API.
- **camelCase follows from that.** A key with a hyphen needs brackets in `jq` and in JavaScript, which takes back part of what JSON was chosen for.

## Alternatives

- **TOML.** It allows comments, and a person can comment a dependency out. That is its one real advantage here, and it is a real one. A scoped name needs quotes in TOML as in JSON, so a dependency line is nearly the same in both.
- **JSON with comments.** It keeps the comments and loses the reason for JSON: `jq` and every strict parser refuse it.

## Consequences

- **A manifest cannot hold a comment.** `package.description` is the place for a sentence about the package.
- **`pmj` writes JSON with a small writer of its own.** System.Text.Json's writer escapes every emoji, whichever encoder it is given, and a manifest whose entry file is `lib.🍇` should say so readably. The writer is in `Packmoji.Core/Json`.
- **A lockfile has no "do not edit" line at its top.** The reference says it instead.
- **The config file that a later milestone adds is JSON too**, so that the tool has one syntax.
