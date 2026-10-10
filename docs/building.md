# How pmj builds

`pmj build` compiles the packages a project depends on, and then the project. `pmj run` does the same and runs the program. This page says what a build needs on the machine, what it does and in what order, where it puts things, and what to do when it stops.

[The command line](cli.md) has the two commands and their options.

## What a build needs

`pmj` does not bring a compiler. It uses what the machine has:

| Needed | For | Looked for |
|---|---|---|
| The Emojicode compiler | Every build | Where `EMOJICODEC` says, or the first `emojicodec` on `PATH` |
| The compiler's own packages | Linking an application | Where `EMOJICODE_PACKAGES_PATH` says, or in `/usr/local/EmojicodePackages` |
| The compiler's headers | A package with native code | Where `EMOJICODE_INCLUDE` says, or in `/usr/local/include/emojicode` |
| A C++ compiler | C++ in a package, and linking an application | Where `CXX` says, or `c++` on `PATH` |
| A C compiler | C in a package | Where `CC` says, or `cc` on `PATH` |
| An archiver | Every package that is compiled | Where `AR` says, or `ar` on `PATH` |

- **The places without a variable are where Emojicode's installer puts things** when it is told nothing.
- **A build asks only for what it uses.** A package of Emojicode alone needs no C++ compiler, and a library needs no linker.
- **What is missing is said before anything is compiled,** so a build that could not end does not begin.
- **Emojicode 1.0 beta 2 is the one compiler there is,** and it is released for Linux and macOS on x86-64. On any other machine it runs in a container or under emulation, and `pmj` has to run there with it.

## What a build does

1. **It reads `packmoji.json` and `packmoji.lock`.** A build chooses no version. A lockfile that is missing, or that no longer answers the manifest, is `lock.out-of-date`: run `pmj install`. A project that asks for no package needs no lockfile.
2. **It waits for any other build of the same project.** Two builds do not write into one project at once.
3. **It holds each locked package to what is published, and fetches what the cache does not hold.** This is what `pmj install` does. With the cache filled, nothing is asked of GitHub.
4. **It finds the compiler and asks which it is.** The version comes from the first line of the compiler's help, which is the only place the compiler says it.
5. **It holds every package, and the project, to the compiler they ask for.** A manifest's `emojicode` is the oldest compiler the package builds with.
6. **It compiles each package that has not been built before,** each after everything it depends on.
7. **It puts every package in the project's `packages/` directory.**
8. **It compiles the project,** and links an application or archives a library. With `--dependencies-only` it stops before this.

A build that is stopped by anything leaves nothing half built: what was being compiled is thrown away, and what was finished before it is kept.

## Where things go

**`packages/`, in the project.** One folder for each locked package, named as the package is imported:

```
packages/crypto/🏛
packages/crypto/libcrypto.a
packages/crypto/documentation.json
packages/crypto/pmj-build.json
```

- **`🏛` is the package's interface** and `lib<name>.a` its archive. They are what the compiler and the linker need of it.
- **`documentation.json` is the compiler's own report** of what the package declares, for an editor to read.
- **`pmj-build.json` says what the folder was built from,** and marks the folder as one that `pmj` put there.
- **The compiler looks in `./packages` without being told to.** So `emojicodec src/main.🍇`, run by hand in the project, finds the same packages a build does.

Someone may keep packages of their own in that directory, so `pmj` changes only what is its own:

| What is at `packages/<name>` | What a build does |
|---|---|
| Nothing | Puts the package there |
| The build of the package that is wanted | Nothing |
| Another build of it, or one that has lost a file | Replaces it |
| Something `pmj` did not put there, with the name of a locked package | Stops, with `packages.foreign` |
| Something `pmj` put there for a package that is no longer locked | Takes it away |
| Anything else | Leaves it alone |

**`target/`, in the project.**

```
target/debug/<name>         an application: the program
target/debug/<name>/        a library: its 🏛, its lib<name>.a and its documentation.json
target/release/             the same, built with --release
target/obj/                 what is made on the way
```

Both directories are for `pmj` to write and for git to ignore, which `pmj new` sees to.

## Once for the whole machine

A package is compiled once, and kept in `pmj`'s own directory beside the cache:

```
~/.packmoji/built/<scope>/<name>/<version>/<key>/<name>/
```

Every project that locks the same package is given a copy of it, and compiles nothing. What decides whether two builds are the same is the key, which is the SHA-256 of all that a build is made from:

| In the key | Because |
|---|---|
| The SHA-256 of the compiler's own file | Another compiler builds something else. Its version is not enough: a fork can give the same one |
| Whether the compiler optimizes | `--release` builds something else |
| The SHA-256 of the package's archive | It holds the package's sources and its manifest |
| The key of each package it depends on | The same version of a package is compiled against whichever versions of them a project locks |
| For a package with native code, what the C or C++ compiler prints for `--version` | Another native compiler builds something else |

- **What a key does not hold is the compiler's own packages and headers.** They are taken to come with the compiler. If you change them and keep the compiler, delete `built/`.
- **`built/` can be deleted at any time.** The next build compiles what it needs again.
- **Nothing in a project points into `built/`.** A project holds copies, so it goes on working when `built/` is gone.

## What each tool is asked

Every tool is started as a program with its arguments, never through a shell, and every file it is given is named in full.

**The compiler** is asked only to compile. It is given one main file, which is the manifest's `build.entry`:

```
emojicodec <main file> -p <name> -c -o <object> -i <interface> -r -S <directory>...     a package or a library
emojicodec <main file> -c -o <object> -S <directory>                                      an application
```

- **`-S` names where to look for packages.** A package is shown everything it depends on, directly or through another package, because the interface of a package names what that package imports.
- **`-r` has the compiler write its report.** It costs no second compile.
- **`-O` is added with `--release`.**
- **It is run in a directory that holds no `packages`,** so that it looks only where it is told.

**The compiler has succeeded when three things are true:** it ended with status 0, it did not print `Detected in:`, and what it was to write is there. Its status alone is not believed, because it ends with 0 for some failures.

**Native files** are compiled one at a time:

```
c++ -std=c++17 -O2 -c <file> -I <the compiler's headers> -I <the package's own>... -o <object>
cc -std=gnu11 -O2 -c <file> -I <the compiler's headers> -I <the package's own>... -o <object>
```

**The archive** of a package is always made new:

```
ar rcs lib<name>.a <object>...
```

**The link** of an application is `pmj`'s to do, and not the compiler's:

```
c++ <object>... -Wl,--start-group <every package's archive> <the compiler's own archives> -Wl,--end-group -l<library>... -lm -lpthread -o <program>
```

- **Every archive is in one group,** where their order does not matter and the linker takes only what the program uses. The compiler's own link names them in an order that fails as soon as one package imports another.
- **On macOS the two group flags are left out.** Its linker reads archives that way without being asked, and refuses the flags.
- **The libraries are those that the project's manifest and its packages' manifests name** under `native.link`, each once.
- **`m` and `pthread` are what the compiler's own package `s` needs.**

`pmj` asks the compiler which it is with `--help`, and a C or C++ compiler with `--version`.

## Native code

A package may have C and C++ beside its Emojicode. Its manifest says which files, where their headers are, and what a program that uses the package is linked with:

```json
{
  "package": {
    "name": "@thatplatypus/hello_greeter",
    "version": "0.1.0",
    "kind": "library",
    "emojicode": ">=1.0.0-beta.2"
  },
  "native": {
    "sources": [
      "native/*.cpp"
    ],
    "includeDirs": [
      "native/include"
    ],
    "link": [
      "m"
    ]
  }
}
```

- **A file that ends `.c` is C.** One that ends `.cpp`, `.cc` or `.cxx` is C++. Any other file that `native.sources` selects stops the build, with `native.unsupported`: headers are named by `native.includeDirs`, and not by `native.sources`.
- **C++ is compiled as C++17, and C as C11 with the system's own declarations.** Both with `-O2`.
- **A manifest can give no flag.** A flag from a package would be someone else's option handed to your compiler.
- **The objects go into the package's archive,** beside the object of its Emojicode.

[The manifest reference](manifest.md#native-code) has the keys.

## When a build stops

| Code | What happened | What to do |
|---|---|---|
| `lock.out-of-date` | There is no lockfile, or the manifest has moved on from it | Run `pmj install` |
| `cache.mismatch` | The unpacked files of a package in the cache are not the files of its archive, so they are not compiled | Delete the directory the problem names, and run `pmj install` |
| `entry.not-found` | The project has no main file, or a package was published without its own | For the project, create the file or name it under `build.entry`. For a package, tell its author |
| `compiler.not-found` | No compiler was found, or the one that was found could not be run | Install Emojicode, or set `EMOJICODEC` to where the compiler is |
| `compiler.unknown` | The compiler's help has no line that `pmj` can read a version from | Check that it is the Emojicode compiler. A compiler newer than this `pmj` knows of needs a newer `pmj` |
| `compiler.too-old` | A package, or the project, asks for a newer compiler | Use a newer compiler, or a version of the package that this one builds |
| `compiler.incomplete` | The compiler's own packages, or its headers, are not where `pmj` looked | Set `EMOJICODE_PACKAGES_PATH` or `EMOJICODE_INCLUDE` to where they are |
| `tool.not-found` | The C++ compiler, the C compiler or the archiver was not found, could not be run, or would not say which it is | Install a C and C++ toolchain, or set `CXX`, `CC` or `AR` |
| `native.unsupported` | `native.sources` selects a file that is neither C nor C++ | Narrow the pattern to the files to compile |
| `build.compile-failed` | The compiler refused the code | Read what it printed, which is above the problem. In your own code, mend it. In a package's, tell its author |
| `build.native-failed` | The C or C++ compiler refused a native file | The same |
| `build.archive-failed` | The archiver failed | Read what it printed. `AR` has to name an archiver that takes `rcs` |
| `build.link-failed` | The linker failed | Read what it printed. A library it cannot find is one that a package names under `native.link`, and has to be on the machine |
| `built.unusable` | `pmj` could not read or write what it keeps of built packages | Check that `~/.packmoji/built` is yours to use, or set `PACKMOJI_HOME` |
| `packages.foreign` | A folder in `packages/` has a locked package's name and is not `pmj`'s | Move it away, or delete it |
| `run.not-an-app` | `pmj run` was run in a library | Run `pmj build`, or run an application that depends on it |
| `run.failed` | The program was built, and the machine would not start it | Check that the compiler and the C++ compiler both build for this machine |

Whatever a tool printed is on standard error above the problem, each line behind the name of what was being built. The problem's own reason is the first thing the tool said.

## For a tool that compiles the project itself

A tool such as an editor may compile a project its own way and still take its packages from `pmj`:

1. Put `packmoji.json` and `packmoji.lock` in a directory. Nothing else is needed there.
2. Run `pmj build --dependencies-only --json` in it.
3. Give the compiler `packagesDirectory` to search, with `-S`.
4. Link as `pmj` does: every package's `lib<bareName>.a` from its `directory`, and the compiler's own archives, in one group. Then `-l` for each library in each package's `link`, and `-lm -lpthread`.

With the archives of the locked packages already in the cache, none of this asks anything of GitHub, and it writes only into that directory and into `PACKMOJI_HOME`.

## Why it works this way

The decisions behind this page, and what each costs:

- [decisions/0001-emojicodec-integration.md](decisions/0001-emojicodec-integration.md): what the compiler was read and seen to do.
- [decisions/0009-built-packages.md](decisions/0009-built-packages.md): why a package is built once and copied into each project.
- [decisions/0010-how-a-build-is-run.md](decisions/0010-how-a-build-is-run.md): the tools, their flags, and how a build ends.
