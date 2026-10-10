<p align="center">
  <img src="art/box.svg" width="180" alt="Packmoji: a cardboard box with eyes">
</p>

# Packmoji

The package manager and package registry for [Emojicode](https://www.emojicode.org). The command is `pmj`.

## Status

Packmoji is being built one milestone at a time. What works today:

- **Projects.** `pmj new` and `pmj init` make one that compiles and packs as it stands.
- **Dependencies, with no registry.** `pmj add`, `remove`, `install` and `update` find packages on GitHub directly, as releases of their repositories, and lock the exact bytes. `pmj tree` shows what is locked, and `pmj verify` checks it again.
- **Packing.** `pmj pack` writes the one file a release carries, the same bytes from the same files on every machine.
- **One native binary.** `pmj` publishes with Native AOT and needs no .NET where it runs.

What does not work yet: `pmj` does not build. `build`, `run` and `test` are the next milestone, and the registry, `publish` and attestations follow. Until `pmj` builds, the sources of what you install are in its cache, unpacked.

```sh
pmj new @you/site
cd site
pmj add @thatplatypus/grapevine
pmj tree
```

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
| `src/Packmoji.Core` | Names, versions, the manifest, the lockfile, the resolver, the archive, and how a package is found with no registry. It depends on nothing outside .NET |
| `src/Packmoji.GitHub` | How `pmj` speaks to GitHub: downloading a release's file, and listing a repository's releases |
| `src/Packmoji.Cli` | `pmj`: the commands, and the cache |
| `tests/` | The tests of each |
| `docs/cli.md` | The commands, what they print, how they end, and where they find things |
| `docs/publishing.md` | How to publish a package |
| `docs/manifest.md` | The reference for `packmoji.json` and `packmoji.lock`, and every diagnostic code |
| `docs/resolution.md` | How `pmj` chooses versions, what stops it, and what to do then |
| `docs/decisions/` | Why things are the way they are, one short record for each decision |
| `art/` | The box with eyes at the top of this page, drawn once as an SVG |
| `scripts/` | What CI runs, to be run by hand as well |

## License

MIT. See [LICENSE](LICENSE).
