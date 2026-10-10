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
- **Building.** `pmj build` compiles what a project depends on with the Emojicode compiler, C and C++ included, each package once for the whole machine, and then the project. `pmj run` builds an application and runs it.
- **One native binary.** `pmj` publishes with Native AOT and needs no .NET where it runs. A release carries it for Linux and Windows on x86-64 and for macOS on Apple silicon, each with its digest: [Getting pmj](docs/cli.md#getting-pmj). On Windows it manages packages and does not build.

What does not work yet: `pmj test`, and the registry, `publish` and attestations, which follow.

```sh
pmj new @you/site
cd site
pmj add @thatplatypus/grapevine
pmj tree
pmj run
```

Building needs the Emojicode compiler, which `pmj` does not bring with it. Emojicode 1.0 beta 2 is the one release there is, for Linux and macOS on x86-64: see [docs/building.md](docs/building.md).

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
scripts/aot-smoke.sh osx-arm64 # publish pmj as a native binary and run it (or linux-x64 on Linux, or win-x64 on Windows)
scripts/real-compiler.sh       # build the sample projects with the released compiler, in a container (needs Docker)
```

CI runs exactly these scripts, on Linux, and the first and the third on Windows too, with the bash that comes with Git. Every test but those of the last has a compiler that is made up, so the first three need no Emojicode.

## Releasing

A release is made by the workflow `.github/workflows/release.yml`, and by nothing else.

1. Set `<Version>` in `Directory.Build.props`, and merge that.
2. Run the workflow **Release** by hand, from the Actions tab or with `gh workflow run release.yml`. It builds `pmj` for the three machines, runs each, and keeps the four files of a release with the run. It releases nothing. Look at what it kept.
3. Push the tag, which is `v` and the version: `git tag v0.1.0 && git push origin v0.1.0`. The same workflow then makes the release.

A tag that is not the version stops the workflow before it builds anything, and a release that is there already is never changed. [Record 0011](docs/decisions/0011-released-programs.md) says why, and what the three scripts it runs are for: `scripts/release-tag.sh`, `scripts/glibc-floor.sh` and `scripts/release-assets.sh`.

## Layout

| Path | Holds |
|---|---|
| `src/Packmoji.Core` | Names, versions, the manifest, the lockfile, the resolver, the archive, how a package is found with no registry, and the order and the key of a build. It depends on nothing outside .NET |
| `src/Packmoji.GitHub` | How `pmj` speaks to GitHub: downloading a release's file, and listing a repository's releases |
| `src/Packmoji.Cli` | `pmj`: the commands, the cache, and the build |
| `tests/` | The tests of each, and the container the real compiler runs in |
| `samples/hello-pkg` | Two small libraries, one with C++, and an application that uses them. The tests pack, install, build and run them |
| `docs/cli.md` | The commands, what they print, how they end, and where they find things |
| `docs/publishing.md` | How to publish a package |
| `docs/manifest.md` | The reference for `packmoji.json` and `packmoji.lock`, and every diagnostic code |
| `docs/resolution.md` | How `pmj` chooses versions, what stops it, and what to do then |
| `docs/building.md` | How `pmj` builds: what it needs, what each tool is asked, where things go, and what to do when a build stops |
| `docs/decisions/` | Why things are the way they are, one short record for each decision |
| `art/` | The box with eyes at the top of this page, drawn once as an SVG |
| `scripts/` | What CI runs, to be run by hand as well |

## License

MIT. See [LICENSE](LICENSE).
