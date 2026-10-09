# Packmoji

The package manager and package registry for [Emojicode](https://www.emojicode.org). The command is `pmj`.

## Status

Packmoji is being built one milestone at a time, and what exists today is its foundation:

- the rules for package names, repositories, release tags and versions;
- strict readers and deterministic writers for the two files a project has, `packmoji.json` and `packmoji.lock`;
- a `pmj` that publishes as a single native binary and, so far, answers `--version` and `--help`.

Resolving, installing, building and publishing packages come in the milestones that follow.

## The ideas it is built on

| Idea | Borrowed from |
|---|---|
| A package version is a GitHub release, and a published version never changes | Cargo |
| A package is named `@scope/name`, and the scope is the GitHub owner who may publish it | npm |
| A dependency states a minimum version, and a build takes the highest minimum asked for and nothing newer | Go modules |
| There is one manifest and one lockfile, in one format | The lesson of NuGet |

## Build and test

You need the .NET 10 SDK. The exact version is pinned in `global.json`.

```sh
scripts/check.sh               # build everything and run every test
scripts/check.sh --coverage    # the same, and print Packmoji.Core's line coverage
scripts/aot-smoke.sh osx-arm64 # publish pmj as a native binary and run it (or linux-x64, on Linux)
```

CI runs exactly these scripts.

## Layout

| Path | Holds |
|---|---|
| `src/Packmoji.Core` | Names, versions, the manifest and the lockfile. It depends on nothing outside .NET |
| `src/Packmoji.Cli` | `pmj` |
| `tests/` | The tests of each |
| `docs/manifest.md` | The reference for `packmoji.json` and `packmoji.lock` |
| `docs/decisions/` | Why things are the way they are, one short record for each decision |
| `scripts/` | What CI runs, to be run by hand as well |

## License

MIT. See [LICENSE](LICENSE).
