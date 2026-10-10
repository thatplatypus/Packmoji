# hello-pkg

Three small projects that the tests build, and that show what a package looks like.

| Directory | Package | What it is |
|---|---|---|
| `words/` | `@thatplatypus/hello_words` | A library of Emojicode alone |
| `greeter/` | `@thatplatypus/hello_greeter` | A library that depends on `hello_words`, with one function written in C++ |
| `app/` | `@thatplatypus/hello` | An application that depends on `hello_greeter` |

Built and run, the application prints:

```
Hello, Packmoji!
42
```

The greeting comes through both libraries, and the number from the C++.

## These packages are not released

A package is a release on GitHub, and neither library has one. The tests pack each with `pmj pack`, serve the two files from a GitHub that is made up, and install, build and run the application against that.

`app/packmoji.lock` holds the digests that `pmj pack` gives the two libraries as they stand. A test fails when it falls behind them, and says what the lockfile should hold.

To see them built, run the tests:

```
scripts/check.sh              with a compiler that is made up
scripts/real-compiler.sh      with Emojicode 1.0 beta 2, in a container
```
