# The manifest and the lockfile

A Packmoji project has two files beside its sources.

| File | Written by | Says |
|---|---|---|
| `packmoji.json` | You, and `pmj` | What the package is and what it needs |
| `packmoji.lock` | `pmj` only | The exact packages that answer those needs |

Both are strict JSON, and both belong in source control.

## packmoji.json

```json
{
  "package": {
    "name": "@thatplatypus/grapevine",
    "version": "0.3.0",
    "kind": "library",
    "emojicode": ">=1.0.0-beta.2",
    "description": "HTTP framework for Emojicode",
    "license": "MIT",
    "repository": "github.com/thatplatypus/grapevine"
  },
  "dependencies": {
    "@thatplatypus/crypto": "1.0",
    "@thatplatypus/deflate": "0.1"
  },
  "devDependencies": {
    "@thatplatypus/testkit": "0.1"
  },
  "build": {
    "entry": "src/lib.🍇",
    "sources": [
      "src/**/*.emojic",
      "src/**/*.🍇"
    ]
  },
  "native": {
    "sources": [
      "native/*.cpp"
    ],
    "includeDirs": [
      "native/include"
    ],
    "link": [
      "pthread"
    ]
  },
  "policy": {
    "requireAttestation": false
  }
}
```

Only `package` and its first four keys are required. This is a whole manifest:

```json
{
  "package": {
    "name": "@thatplatypus/crypto",
    "version": "1.0.0",
    "kind": "library",
    "emojicode": ">=1.0.0-beta.2"
  }
}
```

### Keys

| Key | Type | Required | Rule |
|---|---|---|---|
| `package` | object | yes | |
| `package.name` | string | yes | A full package name: see [Package names](#package-names) |
| `package.version` | string | yes | A version: see [Versions](#versions) |
| `package.kind` | string | yes | `"library"`, which others depend on, or `"app"`, which is run |
| `package.emojicode` | string | yes | The oldest compiler the package builds with: see [The compiler requirement](#the-compiler-requirement) |
| `package.description` | string | no | One line of 1 to 200 characters, with no space at either end and no character that cannot be seen |
| `package.license` | string | no | An SPDX license expression: see [License](#license) |
| `package.repository` | string | no | Where the package's releases are: see [Repositories](#repositories). Default `github.com/<scope>/<name>` |
| `dependencies` | object | no | Each key is a full package name and each value a requirement: see [Requirements](#requirements) |
| `devDependencies` | object | no | The same, for packages needed only to develop this one |
| `build` | object | no | |
| `build.entry` | string | no | The package's main file, ending in `.emojic` or `.🍇`: see [The entry file](#the-entry-file) |
| `build.sources` | array of strings | no | Patterns for the package's source files. At least one, none repeated. Default `src/**/*.emojic` and `src/**/*.🍇` |
| `native` | object | no | |
| `native.sources` | array of strings | no | Patterns for the C and C++ files compiled into the package: see [Native code](#native-code) |
| `native.includeDirs` | array of strings | no | Directories of the package's own headers |
| `native.link` | array of strings | no | Libraries that a program which uses the package is linked with, each named as the linker's `-l` would name it |
| `policy` | object | no | |
| `policy.requireAttestation` | boolean | no | Whether every dependency must have a verified build attestation. Default `false`. `pmj` cannot verify one yet, so a project that sets it to `true` cannot install: see `attestation.unverifiable` |

An unknown key is an error at every level, and the error lists the keys that are allowed there. The order of keys does not matter when a manifest is read. When `pmj` writes one, the keys are in the order of this table and the dependencies are sorted by name.

### Rules across keys

- The owner in `package.repository` is the scope in `package.name`.
- A package does not depend on itself.
- A package is not in both `dependencies` and `devDependencies`.
- No two dependencies share a bare name, and none has the bare name of the package itself. Emojicode imports a package by its bare name, so `@one/crypto` and `@two/crypto` cannot be in one build.
- A name in `native.link` is letters, digits and `_ + . -`, beginning with a letter, a digit or `_`. It cannot be a flag or a path.

### The entry file

The compiler is given one main file for each package, and the package's other files join it through `📜`. That file is the entry.

When `build.entry` is absent, the entry is found by convention:

| `kind` | First choice | Second choice |
|---|---|---|
| `app` | `src/main.emojic` | `src/main.🍇` |
| `library` | `src/lib.emojic` | `src/lib.🍇` |

Exactly one of the two must exist. Neither is an error, and so is both. `pmj new` always writes `build.entry`, so a manifest it made never relies on the convention.

### Native code

A package may have C and C++ beside its Emojicode, for what Emojicode's own packages do not reach. `pmj build` compiles those files with the machine's own compilers and puts them in the package's archive.

- **`native.sources` selects the files to compile.** One that ends `.c` is C, and one that ends `.cpp`, `.cc` or `.cxx` is C++. A file that ends any other way stops a build, so a pattern must not also select the headers.
- **`native.includeDirs` names the directories of the package's own headers.** The Emojicode compiler's headers are found without being named.
- **`native.link` names the libraries a program needs because it uses the package.** They are linked into every program that depends on it.
- **There is no key for a compiler's flags.** C++ is compiled as C++17 and C as C11, both optimized, and a package cannot change that.

[How pmj builds](building.md#native-code) has the commands.

### Paths and patterns

A path is written with `/` between its parts and stays inside the package.

- No part is empty, so there is no leading `/`, no trailing `/` and no `//`.
- No part is `.` or `..`.
- No part begins with `-` or `@`, which a compiler would take for an option or for a file of options.
- No part begins or ends with a space, and none ends with a dot. Some platforms drop both.
- There is no `\`, no `:` and no control character, and none of `<`, `>`, `"` and `|`, which Windows does not allow in a name.
- There is no character that cannot be seen or that reorders text, such as a zero width space or a right-to-left override. The joiner and the tags that emoji are built with are the exception, so a file may be named with any emoji.
- A path is at most 255 characters, and no part of it is more than 255 bytes in UTF-8, which is the most a name can be on most disks.

A pattern is a path that may also hold these:

| In a pattern | Matches |
|---|---|
| `*` | Any run of characters within one part |
| `?` | One character within a part |
| `**` | Any number of whole parts, including none. It must be a whole part: `src/**/x`, never `src/**.x` |

Character classes, braces and a leading `!` are not supported. Matching is exact about case.

### License

`package.license` is an SPDX license expression of at most 100 characters: an identifier such as `MIT`, or identifiers joined by `AND`, `OR` and `WITH` in capitals with one space between, with parentheses where needed. `MIT OR Apache-2.0` and `GPL-2.0-only WITH Classpath-exception-2.0` are both accepted. Only the form is checked, not whether an identifier is on the SPDX list.

## Names, repositories and tags

### Package names

A full name is `@<scope>/<name>`, such as `@thatplatypus/crypto`.

| Part | May hold | Length |
|---|---|---|
| scope | Lowercase letters and digits, with single hyphens inside | 1 to 39 |
| name | Lowercase letters, digits and single underscores, beginning with a letter | 1 to 64 |

- **The scope is the GitHub user or organization that owns the package**, in lowercase. Only someone who can publish a release in that owner's repositories can publish under the scope.
- **The name is what Emojicode code imports.** `@thatplatypus/crypto` is imported with `📦 crypto 🏠`.
- **A name has no hyphen.** It is also a C++ identifier in a package's native code, where a hyphen cannot be. Use an underscore: `emoji_crypto`.
- **Six names are reserved**, because the compiler ships packages of those names and a second one would replace them: `s`, `runtime`, `files`, `sockets`, `json` and `testtube`.
- **Uppercase is an error.** Nothing is lowercased for you.

### Repositories

A repository is written `github.com/<owner>/<repo>`, in lowercase, with no scheme and nothing after the repository's name. The owner must be the package's scope.

A repository may hold several packages. A package that is not in a repository of its own name says where it is with `package.repository`.

### Release tags and assets

A version of a package is a GitHub release in its repository.

| Thing | Form | Example |
|---|---|---|
| The release's tag | `<name>-v<version>` | `crypto-v1.0.0` |
| The release's one asset | `<name>-<version>.pmj.tar.gz` | `crypto-1.0.0.pmj.tar.gz` |

The tag has this form in every repository, whether the repository holds one package or several.

## Versions

A version is [SemVer 2.0.0](https://semver.org): three numbers, and optionally a pre-release, as in `1.2.3` and `1.0.0-beta.1`.

- A version has no build metadata. `1.0.0+abc` is refused.
- Each number is at most 2147483647, and a version is at most 64 characters.
- Versions are ordered as SemVer orders them, so `1.0.0-beta.2` comes before `1.0.0`.

### Requirements

A dependency's requirement is a minimum version, written as two or three numbers.

| Written | Minimum | Also accepts |
|---|---|---|
| `"1.2"` | 1.2.0 | Anything later on the line 1.x |
| `"1.2.3"` | 1.2.3 | Anything later on the line 1.x |
| `"0.4.1"` | 0.4.1 | Anything later on the line 0.4.x |
| `"1.0.0-beta.1"` | 1.0.0-beta.1 | Anything later on the line 1.x |

There are no operators, ranges or wildcards: no `^1.2`, no `>=1.2`, no `1.*`, and no bare `1`. Resolution takes the highest minimum that anything in the build asks for and nothing newer, so an upper bound would have nothing to do.

### Compatibility lines

A requirement never selects across a compatibility line.

- From version 1 up, a line is a major version: `1.2.3` and `1.9.0` share one, and `2.0.0` does not.
- Below version 1, a line is a minor version: `0.4.1` and `0.4.9` share one, and `0.5.0` does not.

### The compiler requirement

`package.emojicode` is `>=` followed by a full version, with no spaces: `">=1.0.0-beta.2"`. It has no upper bound.

The one released compiler calls itself "1.0 beta 2", which is the version `1.0.0-beta.2`. That is earlier than `1.0.0`, so a package that should build today asks for `>=1.0.0-beta.2`.

`pmj build` holds every package, and the project, to this before it compiles anything. A compiler that is older than one of them asks for is `compiler.too-old`.

## packmoji.lock

```json
{
  "version": 1,
  "root": {
    "dependencies": [
      "@thatplatypus/grapevine@0.3"
    ],
    "devDependencies": []
  },
  "packages": [
    {
      "name": "@thatplatypus/crypto",
      "version": "1.0.0",
      "source": "github.com/thatplatypus/grapevine",
      "releaseTag": "crypto-v1.0.0",
      "asset": "crypto-1.0.0.pmj.tar.gz",
      "sha256": "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
      "verified": "attestation",
      "dependencies": []
    },
    {
      "name": "@thatplatypus/deflate",
      "version": "0.1.0",
      "source": "github.com/thatplatypus/grapevine",
      "releaseTag": "deflate-v0.1.0",
      "asset": "deflate-0.1.0.pmj.tar.gz",
      "sha256": "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
      "verified": "attestation",
      "dependencies": []
    },
    {
      "name": "@thatplatypus/grapevine",
      "version": "0.3.0",
      "source": "github.com/thatplatypus/grapevine",
      "releaseTag": "grapevine-v0.3.0",
      "asset": "grapevine-0.3.0.pmj.tar.gz",
      "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
      "verified": "checksum",
      "dependencies": [
        "@thatplatypus/crypto@1.0.0",
        "@thatplatypus/deflate@0.1.0"
      ]
    }
  ]
}
```

Do not edit a lockfile. If one is ever wrong, delete it and run `pmj install`, which writes it again.

### Keys

Every key is required.

| Key | Type | Holds |
|---|---|---|
| `version` | number | `1`, the version of this format |
| `root.dependencies` | array of strings | Each `<full name>@<requirement>`: what the manifest's `dependencies` asked for when the lockfile was written |
| `root.devDependencies` | array of strings | The same, for `devDependencies` |
| `packages` | array of objects | One entry for each locked package |
| `packages[].name` | string | The package's full name |
| `packages[].version` | string | The one version of it the build uses |
| `packages[].source` | string | The repository it is fetched from, owned by its scope |
| `packages[].releaseTag` | string | The release's tag, which is `<name>-v<version>` |
| `packages[].asset` | string | The release's asset, which is `<name>-<version>.pmj.tar.gz` |
| `packages[].sha256` | string | The SHA-256 of the asset: 64 hexadecimal digits in lowercase |
| `packages[].verified` | string | `"checksum"`, or `"attestation"` when the asset also has a verified build attestation |
| `packages[].dependencies` | array of strings | Each `<full name>@<version>`: the locked packages this one needs |

`root` is what lets `pmj` tell, without the network, whether the manifest has changed since the lockfile was written.

### What pmj checks

A lockfile must agree with itself:

- A package has one entry, and no two entries share a bare name.
- Every entry in a `dependencies` list is a package in `packages`, at exactly that version, and is not the package itself.
- No package is named twice in `root`, in one list or across the two.
- Every entry in `root` is answered by a package in `packages` whose version satisfies it.
- Every package in `packages` is led to from `root`.
- `releaseTag` and `asset` are the ones that belong to the entry's name and version.
- No package depends on itself through other packages. Emojicode cannot build such a circle, and `pmj` never writes one.

### The canonical form

`pmj` always writes a lockfile the same way: the keys in the order shown, the packages sorted by full name, and every list of names sorted. The same lockfile is the same bytes on every machine, so its diffs show only what changed.

## How the files are read and written

**Reading**

- The files are UTF-8. A byte order mark at the start is ignored.
- A manifest is at most 1 MiB (1,048,576 bytes) and a lockfile at most 4 MiB (4,194,304 bytes). A real manifest is a few hundred bytes, and 4 MiB is room for some nine thousand locked packages. A larger file is refused before any of it is read.
- They are strict JSON: no comments and no trailing commas.
- A key may appear once in an object.
- Either kind of line end is accepted.
- `pmj` reports the problems it finds in a file at once, each with its line and column, and not only the first. It lists up to a hundred and says how many more there are.
- A column counts characters, so an emoji is one column.

**Writing**

- Two spaces of indentation, and line ends of a line feed alone, with one at the end of the file.
- One key, or one entry of an array, to a line.
- A string is escaped only where JSON requires it, so `🍇` and `>=` appear as themselves.

## Diagnostics

Every problem `pmj` reports has a code, and says what failed, why, and what to do next. A code never changes its meaning, so a tool can rely on it.

A diagnostic often repeats text from the file, and the file may be someone else's. That text is made fit to print: a control character, or one that cannot be seen or that reorders text, is shown as its number, as in `\u{001B}`, and text of more than a thousand characters is cut.

Nearly every code is an error, which stops what `pmj` was doing. One is a warning, `resolve.yanked-locked`: it is reported, and the work goes on.

The codes that begin with `resolve.`, and `lock.mismatch`, are raised when `pmj` chooses versions or checks a lockfile against what is published. [resolution.md](resolution.md) says what to do about each.

The codes that begin with `compiler.`, `build.`, `built.`, `packages.` and `run.`, and `tool.not-found` and `native.unsupported`, are raised by `pmj build` and `pmj run`. [building.md](building.md#when-a-build-stops) says what to do about each. The one exception is `compiler.invalid`, which is a manifest's own.

| Code | Raised when |
|---|---|
| `file.too-large` | A manifest is over 1 MiB, or a lockfile is over 4 MiB |
| `json.syntax` | A file is not strict JSON, is not UTF-8, is empty, or holds merge conflict markers |
| `json.duplicate-key` | A key appears twice in one object |
| `json.wrong-type` | A value is of the wrong kind, such as a number where a string belongs |
| `key.unknown` | An object has a key the format does not define |
| `key.missing` | A required key is absent |
| `name.invalid` | A package name is not `@scope/name` in the form described above |
| `name.reserved` | A package name is one of the six reserved names |
| `version.invalid` | A version is not SemVer 2.0.0, or is over a limit |
| `version.build-metadata` | A version carries a `+` and build metadata |
| `requirement.invalid` | A requirement is not two or three numbers |
| `compiler.invalid` | `package.emojicode` is not `>=` and a version |
| `kind.invalid` | `package.kind` is neither `library` nor `app` |
| `description.invalid` | `package.description` is empty, too long, more than one line, has a space at an end, or holds a character that cannot be seen |
| `license.invalid` | `package.license` is not an SPDX license expression |
| `repository.invalid` | A repository is not `github.com/<owner>/<repo>` in lowercase |
| `repository.owner-mismatch` | A repository's owner is not the package's scope |
| `dependency.self` | A package depends on itself |
| `dependency.duplicate` | A package is in both `dependencies` and `devDependencies`, or is named twice in a lockfile's `root` |
| `dependency.name-collision` | Two packages in one manifest share a bare name |
| `path.invalid` | A path leaves the package or is not written as described above |
| `glob.invalid` | A pattern is not written as described above |
| `link.invalid` | An entry of `native.link` is not a library name |
| `list.empty` | `build.sources` is present and empty |
| `list.duplicate` | An array holds the same entry twice |
| `entry.suffix` | `build.entry` does not end in `.emojic` or `.🍇` |
| `entry.not-found` | No entry is named and neither conventional file exists, or `pmj pack` did not find the entry that is named |
| `entry.ambiguous` | No entry is named and both conventional files exist |
| `lock.unsupported-version` | A lockfile's `version` is not `1` |
| `lock.duplicate-package` | A lockfile has two entries for one package |
| `lock.name-collision` | Two entries of a lockfile share a bare name |
| `lock.tag-mismatch` | An entry's `releaseTag` is not its own |
| `lock.asset-mismatch` | An entry's `asset` is not its own |
| `lock.dangling-dependency` | An entry depends on a package or a version the lockfile does not hold |
| `lock.root-unsatisfied` | Nothing in the lockfile answers an entry of `root` |
| `lock.unreachable` | Nothing leads to an entry |
| `lock.mismatch` | A lockfile's digest, repository or dependencies for a version are not what is published |
| `pin.invalid` | A `<full name>@<version>` string in a lockfile has no `@` between its two parts |
| `sha256.invalid` | A digest is not 64 hexadecimal digits in lowercase |
| `verified.invalid` | `verified` is neither `checksum` nor `attestation` |
| `resolve.version-missing` | A version that a requirement names was never published |
| `resolve.yanked` | The version a build would use has been yanked, and the lockfile does not already hold it |
| `resolve.yanked-locked` | A version the lockfile holds has been yanked. This is the warning |
| `resolve.quarantined` | The version a build would use, or one the lockfile holds, is quarantined |
| `resolve.line-conflict` | One package is asked for on two compatibility lines |
| `resolve.name-collision` | Two packages of one build, or one of them and the project itself, share a bare name |
| `resolve.cycle` | Packages depend on one another in a circle, in a graph being resolved or in a lockfile being read |
| `resolve.graph-too-large` | A graph of dependencies has more than 10,000 versions in it, or would make a lockfile over its limit of 4 MiB |
| `project.not-found` | A command that works on a project was run where there is no `packmoji.json` |
| `project.exists` | `pmj new` or `pmj init` would write over a project that is there |
| `project.unreadable` | A project's file could not be read or written, or a directory that a build writes into is a symbolic link |
| `dependency.exists` | `pmj add` was given a package the manifest already has, and no requirement to change it to |
| `dependency.not-found` | `pmj remove` or `pmj update` named a package the manifest does not have |
| `package.not-found` | No release of a package is in any repository `pmj` looked in |
| `version.none-released` | Every released version of a package is a pre-release, and none was asked for by name |
| `release.invalid` | A release is there, and its archive is not that package at that version in that repository |
| `archive.invalid` | An archive is not the bytes `pmj pack` writes, is over a limit, or holds two files that the disk it is unpacked on keeps as one |
| `pack.nothing` | `pmj pack` would write an archive without the package's entry file, because no pattern of the manifest selects it |
| `pack.unportable` | A file to be packed has a name that another platform could not hold, or is a symbolic link |
| `lock.out-of-date` | The lockfile is missing or no longer answers the manifest, where a command needs one that does |
| `attestation.unverifiable` | A project requires attestation, and `pmj` cannot verify one |
| `github.unreachable` | GitHub could not be reached, refused a request for a reason other than its limit, or answered with something `pmj` did not expect |
| `github.rate-limited` | GitHub's API refused a request because of its limit on requests |
| `cache.unusable` | The cache could not be read or written |
| `cache.mismatch` | `pmj verify` found that what the cache holds of a package is not what `packmoji.lock` holds |
| `config.invalid` | Something `pmj` was told through its environment is not something it can use |
| `compiler.not-found` | A build needs the Emojicode compiler, and there is none: `EMOJICODEC` names nothing that can be run, or no `emojicodec` is on `PATH` |
| `compiler.unknown` | The compiler did not say which version it is: its `--help` has no banner that `pmj` can read |
| `compiler.too-old` | A package, or the project, asks for a newer compiler than the one that was found |
| `compiler.incomplete` | The compiler's own packages or headers are not where `pmj` looked, and the build needs them |
| `tool.not-found` | The C++ compiler, the C compiler or the archiver is needed and cannot be run |
| `native.unsupported` | A file that `native.sources` selects is neither C nor C++ by its name |
| `build.compile-failed` | The compiler refused a package's code, or the project's |
| `build.native-failed` | The C or C++ compiler refused a native file |
| `build.archive-failed` | The archiver could not make a package's archive |
| `build.link-failed` | The linker could not make the program |
| `built.unusable` | What `pmj` keeps of built packages could not be read or written |
| `packages.foreign` | A folder in the project's `packages/` has a locked package's name and was not put there by `pmj` |
| `run.not-an-app` | `pmj run` was run in a library, which has no program to run |
| `run.failed` | The program was built and could not be started |
| `scope.not-allowed` | A package that the project asks for, locks or comes to need is of a scope that `PACKMOJI_SCOPES` does not allow |
