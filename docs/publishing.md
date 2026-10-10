# Publishing a package

A version of a package is a GitHub release. To publish one you pack the package with `pmj pack`, and release the file it writes under the tag that names the version. There is nothing to sign up for, and no server to upload to.

This page is how to do that by hand. `pmj publish`, and a workflow that releases for you when you push a tag, come with a later milestone.

## What a release has to be

`pmj` takes a release for version `X.Y.Z` of `@scope/name` when all of these hold:

| It has | Which must be |
|---|---|
| A repository | Owned by `scope`, and the one the package's manifest names |
| A tag | Exactly `<name>-v<X.Y.Z>`, such as `crypto-v1.2.0` |
| One file | Named `<name>-<X.Y.Z>.pmj.tar.gz`, written by `pmj pack` |
| A state | Published. A draft is not seen |

Inside that file, `packmoji.json` has to give the same name and the same version, and the repository the release was found in. `pmj` reads it before it takes the release.

## The manifest

A package in a repository of its own name needs nothing more than `pmj new` writes:

```json
{
  "package": {
    "name": "@thatplatypus/greeter",
    "version": "0.1.0",
    "kind": "library",
    "emojicode": ">=1.0.0-beta.2"
  },
  "build": {
    "entry": "src/lib.🍇"
  }
}
```

That package lives in `github.com/thatplatypus/greeter`. [The manifest reference](manifest.md) has every key.

### Several packages in one repository

Give each package a directory of its own, with its own `packmoji.json`. A package that is not in a repository of its own name says where it lives:

```json
{
  "package": {
    "name": "@thatplatypus/crypto",
    "version": "0.1.0",
    "kind": "library",
    "emojicode": ">=1.0.0-beta.2",
    "repository": "github.com/thatplatypus/grapevine"
  },
  "build": {
    "entry": "crypto.🍇",
    "sources": [
      "*.🍇"
    ]
  }
}
```

- **A repository is written in lowercase,** whatever its spelling on GitHub, and its owner has to be the package's scope.
- **A package that depends on its neighbour asks for it like any other,** under `dependencies`. `pmj` finds packages that share a repository beside each other.
- **A package is its own directory and nothing above it.** A file that two packages share from a directory beside them cannot be packed into either. Give each its own copy, or make what is shared a package.
- **`build.entry` and `build.sources` say where the sources are** when they are not under `src`.

## Pack it

In the package's directory:

```
$ pmj pack
Packed @thatplatypus/crypto 0.1.0: 3 files, 5143 bytes.
  target/crypto-0.1.0.pmj.tar.gz
  sha256 9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08
To publish it, release that file under the tag crypto-v0.1.0 in github.com/thatplatypus/grapevine.
```

- **The same files give the same bytes,** whoever packs them and wherever. The digest is the one that everyone who installs this version will lock.
- **Only what the manifest selects is packed.** [The command's own section](cli.md#pmj-pack) says what that is. Tests, notes and build output beside the sources stay out unless a pattern names them.
- **Keep `target/` out of git.** `pmj new` writes a `.gitignore` that does.

## Release it

Commit the manifest at the version you packed. Then tag that commit, push the tag, and make the release with the file attached. With GitHub's own command line:

```sh
git tag crypto-v0.1.0
git push origin crypto-v0.1.0
gh release create crypto-v0.1.0 target/crypto-0.1.0.pmj.tar.gz --verify-tag --title "crypto 0.1.0" --notes "The first release."
```

- **`--verify-tag` stops `gh` making a tag of its own** if yours did not reach GitHub.
- **A pre-release goes by its version.** `1.0.0-beta.1` is a pre-release to `pmj` whatever the release is marked as. Passing `--prerelease` makes GitHub show it as one too.
- **GitHub marks one release of a repository as its latest.** In a repository of several packages that says little, and `--latest=false` keeps the mark off a release.
- **The release can be made on the website instead.** What matters is the tag, the file's name, and that the release is published.

Publish what a package depends on before the package itself, so that nobody can add a version whose dependencies are not there yet.

## Check it

In any other directory:

```sh
pmj new @you/try
cd try
pmj add @thatplatypus/crypto --repository github.com/thatplatypus/grapevine
```

`--repository` is needed here only because `crypto` shares its repository and this project depends on nothing else of that owner. [How a package is found](cli.md#how-a-package-is-found) says why.

Look in `packmoji.lock` for the digest `pmj pack` printed. It is the same.

## A release never changes

- **Publish a new version. Never replace the file of an old one.** Every project that uses a version has its digest in a lockfile, and a file with other bytes stops each of them with `lock.mismatch`, which is what a tampered release looks like.
- **Do not delete a release that someone may depend on.** `pmj install` then fails for them with `resolve.version-missing`.
- **Withdrawing a version comes with the registry.** Until then, publish a version that fixes what was wrong.
