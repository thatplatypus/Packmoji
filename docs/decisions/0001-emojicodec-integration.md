# 0001: How Packmoji works with the Emojicode compiler

- **Status:** accepted
- **Date:** 2026-10-09

## Context

Packmoji has to build packages with `emojicodec`, and a package manager that guesses how its compiler behaves is wrong in ways that only show on someone else's machine. So before any design depended on it, the compiler's source was read and the released compiler was run.

- **The source read** is upstream `emojicode/emojicode` at commit `4dec45b4`, which `git describe` calls `v1.0-beta.2-21-g4dec45b4`. Paths below are in that repository.
- **The compiler run** is the official Emojicode 1.0 beta 2 for Linux x86-64, in a container, on 2026-10-09. "Seen in a run" below means that run.
- **Two programs that already build with it** were read as well: Grapevine's `build.sh` files and Blazemoji's `LocalToolchain.cs`, and Grapevine's list of the compiler's defects.

## What was found

| Question | Finding | Where it is from |
|---|---|---|
| How is a package compiled? | `emojicodec <main file> -p <name>` writes the archive `lib<name>.a` and the interface file `🏛` beside the main file, or where `-o` and `-i` say. `-c` stops after the object file. `-O` optimizes. `--json` prints diagnostics as JSON. The archive step runs `ar cr`, or `$AR`, over that one object. | `Compiler/CLI/Options.cpp:26-44`, `:106-129`, `:145-150`; `Compiler/CLI/main.cpp:40-73`; `Compiler/Compiler.cpp:99-106` |
| How many files is the compiler given? | One, the package's main file. The others join it through `📜 🔤path🔤`, relative to the file that includes them. A source file ends in `.emojic` or `.🍇`. | `Options.cpp:28`; `Compiler/Parsing/DocumentParser.cpp:139-151`; `Compiler/Package/Package.cpp:65-68` |
| How does `📦 name 🏠` find a package? | It takes the first directory called `name` in, in order: each `-S` path, `./packages` under the working directory, `$EMOJICODE_PACKAGES_PATH`, and the built-in `/usr/local/EmojicodePackages`. There it reads `🏛`, or `interface.emojii`, and links `lib<name>.a`. The help text says `-S` comes after `./packages`; the code puts it first. | `Options.cpp:88-100`; `Compiler.cpp:108-128`, `:157-164` |
| Can a package take the place of a stock one? | Yes. A package called `files` in a `-S` path was the one imported, not the compiler's own. | Seen in a run |
| Which names may a package have? | The name in an import is one `Variable` token: a run of characters that are neither whitespace nor emoji, not beginning with a digit, `-` or `+`. Names with hyphens, underscores, dots, digits and capitals, and one of 64 characters, all compiled, linked and ran. A leading digit and a leading hyphen were refused. | `Compiler/Lex/Lexer.cpp:182-193`, `:291-300`; `DocumentParser.cpp:115-120`; seen in a run |
| Where else does the name go? | Into symbol names unchanged, such as `emoji-crypto_class_info_1f95a`. And, through the runtime's macro `SET_INFO_FOR(type, package, emoji)`, into a C++ identifier by token pasting, which a hyphen or a dot breaks. | `Compiler/Generation/Mangler.cpp:97`, `:178`; `runtime/Runtime.h:115-117`; seen in a run |
| How is native code part of a package? | A method names a C symbol with `📻 🔤symbol🔤`. The C++ is compiled apart, with the runtime's headers on the include path, and its objects are put into the package's archive. Libraries to link are named with `🔗`, which the interface file carries to whatever imports the package. | The "Packages" page and the C++ guide at emojicode.org; `Compiler/Prettyprint/PrettyPrinter.cpp:54-59`; Grapevine's `packages/grapevine/build.sh` |
| Can the compiler link a program? | Not dependably in the release. Its link line names the imported archives in the reverse of the order they were first met, so a package that imports a package comes after the one it needs, and a linker that reads an archive once finds nothing to take. The result of the linker and of the archiver is not looked at, so the compiler ends with status 0 either way. Grapevine and Blazemoji both compile with `-c` and link by hand, with every archive in one linker group. | `Compiler.cpp:79-97`; Grapevine's `build.sh` and its defect 44; Blazemoji's `LocalToolchain.cs:283-321` |
| How is the compiler's version found? | There is no `--version`: it is answered with "Flag could not be matched". `--help` prints "Emojicode Compiler 1.0 beta 2." | `Options.cpp:27`; seen in a run |
| What does the exit status mean? | 0 for `--help`, and 0 as well for arguments it cannot use. 1 for a compile error. 70 for an internal crash. And 0 when LLVM's verifier refuses the code that was generated, with `Detected in:` in the output. | `Options.cpp:71-83`; `main.cpp:79-95`. The statuses 0 and 1 were seen in a run. The crash's status is read from the source, and the verifier's case is Grapevine's defect 09: neither was seen here |
| How do the compiler's own documents say to install a package? | With Yarn, told to install into `./packages`, the one folder the compiler searches with no flag. A package is published as source, with no object or interface file, and is compiled when it is installed, by a script of its own that leaves `🏛` and `lib<name>.a` beside its sources. After that `emojicodec main.emojic` finds it. | "Managing Emojicode Packages with Yarn", `src/guides/yarn.md` in `emojicode/emojicode.github.io`, read on 2026-10-09 |
| Where does the compiler run? | The released binaries are for x86-64. On Apple Silicon they run in a container, under emulation. | Grapevine's `Dockerfile`; Blazemoji's CI |

## Decision

1. **The compiler's version is read from the `--help` banner.** "1.0 beta 2" is the version `1.0.0-beta.2`. A compiler whose banner cannot be read is refused, with a diagnostic, and never guessed at.
2. **The compiler is never asked to link or to archive.** `pmj` compiles each package and each program to an object with `-c`, makes each archive itself, and links a program itself with every package archive in one group, as Grapevine and Blazemoji do.
3. **A compile has succeeded only when three things are true:** the status is 0, the output does not hold `Detected in:`, and the object file exists.
4. **Each dependency is built into `./packages/<name>/` in the project's own directory,** holding `🏛` and `lib<name>.a`. Record 0009 changes how it comes to be there: it is built once for the whole machine, and a copy is placed in the project. Everything else in this item stands. That is the one folder the compiler searches with no flag, and the one its own Yarn guide installs into, so a compile run by hand, and any editor that runs the compiler from the project's directory, finds the packages too. Three rules come with it. `pmj` overwrites and removes only a folder that it put there and marked as its own, and refuses to touch one of the same name that it did not make. It removes what the lockfile no longer names, so that nothing stale is found. And `pmj new` writes `packages/` into the project's `.gitignore`. What the project itself is built into stays `target/`.
5. **Native sources are compiled with `$CXX`, or `c++`,** with the compiler's own `include` directory on the include path, and their objects go into the package's archive.
6. **Every path given to the compiler or to the C++ compiler is absolute,** so that no file's name can be taken for an option. The manifest refuses a path that begins with `-` or `@`, but the name of a file that a pattern matches is checked nowhere else.
7. **Everything in 1 to 6 sits behind one interface, `ICompilerDriver`,** which the build milestone defines.

Four rules of the file formats follow from the findings, and are in force now:

- A package name has no hyphen, because of `SET_INFO_FOR`. Record 0004 has the reasoning.
- The names of the six stock packages are reserved, because a package can take a stock one's place.
- Every package has an entry file, a library as much as an app, because the compiler is given one file.
- Examples ask for `>=1.0.0-beta.2`, because `>=1.0.0` refuses the only compiler there is.

## Alternatives

- **Let the compiler link.** It is less code, and it fails for any package that imports a package, silently.
- **Require the owner's fork, which fixes the link order and the exit status.** A registry is for everyone who has the released compiler. The fork is a second implementation of `ICompilerDriver`, or a check for what a compiler can do, not a requirement.
- **Trust the exit status alone.** Three of the compiler's ways of failing end with status 0.
- **Use Yarn itself, as the compiler's documents suggest.** It brings a second manifest and names without scopes, it runs a script from each package at install, which is someone else's code run with the user's rights, and it does not build a package that imports a package in the order that needs. What is taken from it is the folder, in decision 4.
- **Build dependencies into `target/packages` and give that to the compiler with `-S`,** as the brief first had it. Everything `pmj` makes would then be in one folder that is wholly its own. But `emojicodec main.emojic`, which is what every tutorial and the compiler's own guide say to run, would not find a single package. The owner chose `./packages` on 2026-10-09.

## Consequences

- Tests against the real compiler need Linux on x86-64, so they run in a container, and they are a category of their own that a default test run leaves out.
- When a compiler appears that can link, or that has `--version`, one driver changes and nothing else does.
- The stock packages may grow with a later compiler. A name that becomes reserved then cannot be taken from a package that already has it, so the list is looked at again whenever the compiler adds a package.
