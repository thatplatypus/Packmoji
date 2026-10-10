# The pmj command line

`pmj` makes projects, fetches the packages they depend on, and packs a package for release. This page says what each command does, what it prints, how it ends, and where it finds things.

Packages are found on GitHub directly, as releases of their repositories. There is no registry yet.

`pmj` does not build yet. That is the next milestone, and until then the sources of what you install are in [the cache](#the-cache), unpacked and ready for the compiler.

## A first project

```
$ pmj new @you/site
Made the application @you/site in site/:
  packmoji.json
  src/main.🍇
  .gitignore
  README.md

$ cd site
$ pmj add @thatplatypus/grapevine
Added @thatplatypus/grapevine 0.3.0 to dependencies.
packmoji.lock now holds 3 packages:
  + @thatplatypus/crypto 1.0.0
  + @thatplatypus/deflate 0.1.0
  + @thatplatypus/grapevine 0.3.0

$ pmj tree
@you/site 0.1.0
└── @thatplatypus/grapevine 0.3.0
    ├── @thatplatypus/crypto 1.0.0
    └── @thatplatypus/deflate 0.1.0
```

Commit both `packmoji.json` and `packmoji.lock`. On another machine, or in CI, `pmj install --locked` then fetches exactly the same bytes.

## The commands

Every command works in the current directory. A command that reads a project needs a `packmoji.json` there, and does not look in the directories above.

| Command | Does |
|---|---|
| `pmj new <@scope/name>` | Makes a project in a new directory |
| `pmj init <@scope/name>` | Makes a project in this directory |
| `pmj add <@scope/name>` | Depends on a package: writes it into `packmoji.json`, locks it and fetches it |
| `pmj remove <@scope/name>` | Stops depending on a package, and locks what is left |
| `pmj install` | Fetches exactly what `packmoji.lock` holds |
| `pmj update` | Raises what `packmoji.json` asks for to the latest version on each requirement's line |
| `pmj tree` | Shows the packages `packmoji.lock` holds, and what each depends on |
| `pmj pack` | Writes the file that a release of this package carries |
| `pmj verify` | Downloads every locked package again, and holds it and the cache's copy to `packmoji.lock` |

`pmj --help` lists them, `pmj <command> --help` says what one takes, and `pmj --version` prints the version.

### pmj new and pmj init

```
pmj new <@scope/name> [--lib | --app]
pmj init <@scope/name> [--lib | --app]
```

- **The scope is the GitHub owner who will publish the package.** That is what lets someone else depend on it later.
- **`pmj new` makes a directory of the bare name.** It fills one that is there and empty, and refuses one that holds something.
- **`pmj init` works in the directory it is run in.** A file that is already there is left as it is, and a `packmoji.json` that is already there is an error.
- **`--app` is what is made when neither is given.** `--lib` makes a library.

What is written is a manifest, an entry file under `src`, a `.gitignore` for `target/` and `packages/`, and a README. It compiles and packs as it stands.

### pmj add

```
pmj add <@scope/name>[@<requirement>] [--dev] [--repository <github.com/owner/repo>]
```

- **With no requirement, `add` asks for the latest version, written in full.** The latest is the highest version that is not a pre-release.
- **With a requirement, it is written as you gave it.** `pmj add @thatplatypus/crypto@1.2` asks for 1.2.0 or anything later on the line `1.x`. See [How pmj chooses versions](resolution.md).
- **A pre-release is added only by naming it,** as in `pmj add @thatplatypus/crypto@2.0.0-rc.1`.
- **`--dev` puts it under `devDependencies`:** needed to develop this project, and not by what depends on it.
- **`--repository` says where the package lives.** It is needed once, and only in the one case described under [How a package is found](#how-a-package-is-found).

A package the project already depends on:

- **With no requirement, it is an error.** Nothing would change.
- **With a requirement, its requirement is changed.** It stays in the table it is in.
- **With `--dev`, it is moved to `devDependencies`.** To move one the other way, remove it and add it again.

`add` resolves and fetches what the resolution chose before it writes anything. So when a package cannot be found, or the resolution is stopped, neither `packmoji.json` nor `packmoji.lock` is changed.

### pmj remove

```
pmj remove <@scope/name>
```

Takes the package out of whichever table has it, and resolves what is left. The lockfile then holds only what something still needs.

### pmj install

```
pmj install [--locked]
```

- **When `packmoji.lock` still answers `packmoji.json`, nothing is chosen.** Each locked package is fetched and held to the digest the lockfile records. A package whose locked bytes are already in the cache is not asked of GitHub at all, so `install` then works with no network.
- **When the manifest asks for something else than the lockfile recorded, `install` resolves** and writes the lockfile. Reordering a table, or writing `1.2.0` where it said `1.2`, is not a change.
- **`--locked` makes that second case an error,** and so does a missing lockfile. Use it in CI, where nobody is there to see the lockfile change.

### pmj update

```
pmj update [<@scope/name>...] [--dry-run]
```

A build never moves to a newer version by itself, so this is how you move it.

- **Each requirement is raised to the latest version on its own line,** and written in full. With `1.0` asked for and 1.2.0, 1.10.0 and 2.0.0 released, the requirement becomes `1.10.0`.
- **It never crosses to another line,** and never to a pre-release. Going from `1.x` to `2.x` is a decision, and you make it with `pmj add @scope/name@2.0`.
- **With packages named, only those are raised.**
- **`--dry-run` says what would change and writes nothing to the project.** What it downloads to find out stays in the cache.

```
$ pmj update --dry-run
Would raise 1 requirement in packmoji.json:
  @thatplatypus/crypto from 1.0 to 1.10.0
packmoji.lock would hold 1 package:
  ~ @thatplatypus/crypto 1.0.0 to 1.10.0
Nothing was written.
```

What a lockfile gains is marked `+`, what it loses `-`, and a package that moves to another version `~`.

### pmj tree

```
pmj tree [--json]
```

Draws the project and, under each package, what it depends on. It reads the project's two files and asks nothing of anyone.

- **`(dev)`** marks what is needed only to develop the project.
- **`(*)`** marks a package that was already drawn with what it depends on, higher up.
- **`(...)`** marks a package whose dependencies lie deeper than the 64 levels the drawing goes.

`tree` needs a lockfile that answers the manifest. Run `pmj install` first.

### pmj pack

```
pmj pack
```

Writes `target/<name>-<version>.pmj.tar.gz`, and prints its SHA-256.

- **The same files give the same bytes,** on every machine and every time. The digest printed is the digest that everyone who installs the package will lock.
- **What goes in** is `packmoji.json`, any file at the top whose name begins `README` or `LICENSE`, and what the manifest's own patterns select: `build.sources`, `native.sources`, and everything under `native.includeDirs`.
- **Nothing else goes in.** Not `target/`, not `packages/`, not `.git/`, and not a file whose name begins with a dot unless a pattern writes the dot.
- **A symbolic link among the files is an error,** and so are two files whose names differ only by case, and a name that Windows keeps for a device.

[Publishing a package](publishing.md) says what to do with the file.

### pmj verify

```
pmj verify [--json]
```

Downloads every locked package again, whatever the cache holds, and checks three things for each:

| What | Held to |
|---|---|
| The file its release carries today | The digest and the repository in `packmoji.lock` |
| The archive in the cache | The same digest |
| The files unpacked beside that archive | The archive |

`verify` changes nothing. It does not repair the cache and does not write what it downloads: what it finds is for you to act on. It ends with status 0 only if every package is exactly what the lockfile holds.

## What pmj prints

- **What a command did goes to standard output,** in plain sentences. Problems go to standard error.
- **A problem is its code, what failed, why, and what to do next,** with the place in a file when it has one:

```
error[package.not-found]: No release of "@thatplatypus/crypto" was found.
  why: pmj looked in github.com/thatplatypus/crypto for a release tagged crypto-v and a version, and there is none
  fix: if the package shares a repository with others, say which, once: pmj add @thatplatypus/crypto --repository github.com/thatplatypus/<repository>
```

- **Every code is listed** in [the manifest reference](manifest.md#diagnostics), and those of a resolution are explained in [How pmj chooses versions](resolution.md).
- **There is no color and no prompt.** `pmj` never waits for an answer, so it behaves the same in a terminal and in CI.

### For a tool: --json

`pmj tree --json` and `pmj verify --json` put one JSON object on standard output, and nothing else there.

- **`ok`** is false when any problem is an error.
- **`diagnostics`** holds the problems. With `--json` they are not also printed to standard error.
- **`omittedDiagnostics`** counts the problems beyond the hundred that are listed.
- **A command line `pmj` could not read, or a fault in `pmj`, is not JSON.** Tell them apart by the exit status.

A problem in `diagnostics`:

| Key | Holds |
|---|---|
| `severity` | `error` or `warning` |
| `code` | The problem's code, such as `lock.out-of-date` |
| `message` | What failed |
| `reason` | Why |
| `fix` | What to do next |
| `location` | Only when the problem has a place: `file`, `line` and `column`, each counted from 1 |

`pmj tree --json`, for the project above:

```json
{
  "ok": true,
  "diagnostics": [],
  "omittedDiagnostics": 0,
  "project": {
    "name": "@you/site",
    "version": "0.1.0"
  },
  "dependencies": [
    {
      "name": "@thatplatypus/grapevine",
      "requirement": "0.3.0",
      "version": "0.3.0"
    }
  ],
  "devDependencies": [],
  "packages": [
    {
      "name": "@thatplatypus/crypto",
      "version": "1.0.0",
      "source": "github.com/thatplatypus/grapevine",
      "sha256": "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
      "verified": "checksum",
      "dependencies": []
    },
    {
      "name": "@thatplatypus/deflate",
      "version": "0.1.0",
      "source": "github.com/thatplatypus/grapevine",
      "sha256": "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
      "verified": "checksum",
      "dependencies": []
    },
    {
      "name": "@thatplatypus/grapevine",
      "version": "0.3.0",
      "source": "github.com/thatplatypus/grapevine",
      "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
      "verified": "checksum",
      "dependencies": [
        {
          "name": "@thatplatypus/crypto",
          "version": "1.0.0"
        },
        {
          "name": "@thatplatypus/deflate",
          "version": "0.1.0"
        }
      ]
    }
  ]
}
```

- **`dependencies` and `devDependencies`** are what the project asks for, each with the requirement as it is written and the version that answers it.
- **`packages`** is every locked package once, in order of name. A build holds one version of a package, so a name is enough to find one.

`pmj verify --json` gives `ok`, `diagnostics`, `omittedDiagnostics`, and `packages`: every package that was held to the lockfile, each with its `name`, `version`, `source`, `sha256` and `verified`. A package that is not what the lockfile holds is named in a problem's text.

These shapes may still change, until a tool depends on them.

## Exit statuses

| Status | Means |
|---|---|
| `0` | What was asked for was done. A warning does not change this |
| `1` | A problem was found and reported. Nothing was left half done |
| `2` | The command line could not be read: a command or an option `pmj` does not have, or an argument that is missing |
| `70` | `pmj` itself failed. That is a fault in `pmj`, and worth reporting |
| `130` | `pmj` was stopped before it had finished, as by Ctrl+C |

Each file `pmj` writes is written whole beside its place and then put there in one step. So a `pmj` that is stopped leaves an old file or a new one, and never half of one.

## Environment

| Variable | Use |
|---|---|
| `PACKMOJI_HOME` | The directory `pmj` keeps its own files in. The default is `.packmoji` in your home directory |
| `GITHUB_TOKEN`, `GH_TOKEN` | A token for GitHub's API, which raises its limit. The first that is set is used |
| `PACKMOJI_GITHUB` | Another address for `https://github.com`, where releases are downloaded from |
| `PACKMOJI_GITHUB_API` | Another address for `https://api.github.com`, where versions are listed |
| `PACKMOJI_DIRECT` | Accepted, as `--direct` is. Finding packages on GitHub directly is the only way there is yet |

- **A token is sent to the API's address and to nothing else.** It is never sent with a download, and it is in nothing `pmj` prints or writes.
- **The two addresses are for a GitHub of your own, and for tests.** A token goes to whatever address you give for the API.

## How a package is found

A version of `@scope/name` is the release tagged `<name>-v<version>` that carries the file `<name>-<version>.pmj.tar.gz`, in a repository that the scope owns. Without a registry, `pmj` has to work out which repository. It looks in these, in this order, and takes the first that has the release:

1. The repository `packmoji.lock` records for the package.
2. The repository given with `--repository`, for the package being added.
3. `github.com/<scope>/<name>`: a repository of the package's own name.
4. The other repositories of the same owner that it knows by now: the project's own, those of packages the lockfile holds, and those of packages it has just found.

- **A release counts only if it says it is what was looked for.** The manifest inside its archive has to give that name and that version, and has to say that the package lives in the repository it was found in.
- **What is found is remembered in `packmoji.lock`,** so it is worked out once.
- **Packages that share a repository are found beside each other.** `@thatplatypus/grapevine` is in `github.com/thatplatypus/grapevine`, and so are the two packages it depends on. Adding `grapevine` finds all three.

One case cannot be worked out: a project that depends on a package in a shared repository, and on nothing else of that owner. Say where it is, once:

```
$ pmj add @thatplatypus/crypto --repository github.com/thatplatypus/grapevine
Added @thatplatypus/crypto 1.0.0 to dependencies.
packmoji.lock now holds 1 package:
  + @thatplatypus/crypto 1.0.0
```

### What is asked of GitHub

| Command | Asks |
|---|---|
| `add` with no requirement, and `update` | GitHub's API, for a repository's list of releases, once for each repository |
| `add`, `install`, `update` and `remove` | A release's file by its own address, for each package that is not locked yet, or whose locked bytes the cache does not hold |
| `verify` | A release's file by its own address, for every locked package |
| `tree`, `pack`, `new` and `init` | Nothing |

- **GitHub's API answers 60 requests an hour to someone it does not know,** and 5,000 with a token. Set `GITHUB_TOKEN` if you meet the limit.
- **Downloading a release's file needs no token** and does not count against that limit.

## The cache

Everything `pmj` downloads is kept, so that it is downloaded once:

```
~/.packmoji/cache/<scope>/<name>/<version>/<sha256>.pmj.tar.gz
~/.packmoji/cache/<scope>/<name>/<version>/<sha256>/
```

- **The file is the archive as it was downloaded,** named by its own SHA-256.
- **The directory beside it holds the archive's files,** unpacked. The digest is the one `packmoji.lock` records for the package.
- **Both are marked as not to be written.** They are shared by every project on the machine.
- **An archive is held to its name each time it is read.** One that is no longer the bytes its name says is not used, and is fetched again.
- **The cache can be deleted at any time.** `pmj install` fills it again from the lockfile.

## What pmj does not do yet

| Not yet | Comes with |
|---|---|
| `build`, `run` and `test` | The next milestone |
| A registry, and `search` and `info` | The registry |
| `publish`, attestations, and a release workflow | Publishing |
| Installing for a project that requires attestation | Publishing. Until then such a project is refused, with `attestation.unverifiable`: a requirement that cannot be checked is not met |
| A configuration file | The registry |
| Downloading several packages at once | Later |
| `pmj cache clean` | Later. Deleting the cache's directory does the same |
