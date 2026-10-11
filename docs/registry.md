# The registry

The registry says which versions of which packages there are, and which bytes each one is. It is files, and nothing else: there is no server to ask, and nothing to keep running. A workflow looks at the releases of the repositories on a list, and writes what it finds to a branch of this repository.

`pmj` does not read the registry yet. That comes next. Today `pmj` finds packages on GitHub itself: see [How a package is found](cli.md#how-a-package-is-found).

## What it is for

Finding packages on GitHub directly works, and it has limits that only a record kept in one place can lift.

| Without a registry | With one |
|---|---|
| A package that shares a repository has to be pointed to, with `--repository` | A package is found by its name |
| The first person to fetch a version decides, for their own lockfile, what its bytes are | The registry records a version's bytes once, when it first sees them, for everyone |
| `pmj add` and `pmj update` ask GitHub's API, which answers 60 times an hour to someone it does not know | They read a file |
| Choosing versions downloads the archive of every version that has to be asked about | It reads one small file for each package, and downloads only what was chosen |
| A version whose file was replaced is found out one lockfile at a time | It is quarantined, for everyone |

The registry never holds a package's bytes. Those stay where they were published, in the release, and a version's record says where that is and what its SHA-256 has to be.

## Where it is

On the branch `registry` of `github.com/thatplatypus/packmoji`. A file is read by its own address:

```
https://raw.githubusercontent.com/thatplatypus/Packmoji/registry/v1/packages/thatplatypus/grapevine.json
```

- **What GitHub serves of a branch may be five minutes old.**
- **The branch's history is the registry's record.** Every change is a commit that says what was added, refused or quarantined, and nobody can change it unseen.

## The files

The branch holds a directory `v1`, and a `README.md` that says what the branch is.

| Path | Holds |
|---|---|
| `v1/packages/<scope>/<name>.json` | One package: every version of it that has been indexed |
| `v1/refused/<owner>/<repository>.json` | The releases of one repository that claim to be a package and were refused |

Both are strict JSON in a canonical form, as a lockfile is: the keys in the order shown here, every list in order, and the same registry the same bytes whoever writes it.

### A package

```json
{
  "version": 1,
  "name": "@thatplatypus/grapevine",
  "versions": [
    {
      "version": "0.1.0",
      "status": "active",
      "source": "github.com/thatplatypus/grapevine",
      "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
      "size": 48213,
      "verified": "checksum",
      "immutable": false,
      "published": "2026-10-11T15:02:11Z",
      "indexed": "2026-10-11T15:17:40Z",
      "dependencies": {
        "@thatplatypus/crypto": "0.1",
        "@thatplatypus/deflate": "0.1"
      }
    },
    {
      "version": "0.2.0",
      "status": "quarantined",
      "source": "github.com/thatplatypus/grapevine",
      "sha256": "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
      "size": 51077,
      "verified": "checksum",
      "immutable": false,
      "published": "2026-10-20T09:30:00Z",
      "indexed": "2026-10-20T10:17:05Z",
      "dependencies": {
        "@thatplatypus/crypto": "0.1"
      },
      "quarantined": {
        "seen": "2026-11-02T09:17:12Z",
        "sha256": "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824"
      }
    }
  ]
}
```

| Key | Type | Holds |
|---|---|---|
| `version` | number | `1`, the version of this format |
| `name` | string | The package's full name, which is also the file's path |
| `versions` | array | One entry for each indexed version, lowest first |
| `versions[].version` | string | The version |
| `versions[].status` | string | `"active"`, `"yanked"` or `"quarantined"`: see [Statuses](#statuses) |
| `versions[].source` | string | The repository whose release holds it, owned by the package's scope |
| `versions[].sha256` | string | The SHA-256 of the release's file as it was when the version was first seen |
| `versions[].size` | number | How many bytes that file is |
| `versions[].verified` | string | `"checksum"`. `"attestation"` is kept for a version whose build attestation has been verified, which nothing does yet |
| `versions[].immutable` | boolean | Whether GitHub said the release could not be changed, when the version was first seen |
| `versions[].published` | string | When GitHub says the release was published |
| `versions[].indexed` | string | When the version was first seen |
| `versions[].dependencies` | object | The `dependencies` of the version's manifest: each package with its requirement |
| `versions[].quarantined` | object | Only on a quarantined version |
| `versions[].quarantined.seen` | string | When its bytes were found to have changed |
| `versions[].quarantined.sha256` | string | The SHA-256 of what the release then held |

The tag and the file of a release are not written. They follow from the name and the version: see [Release tags and assets](manifest.md#release-tags-and-assets).

### The releases that were refused

```json
{
  "version": 1,
  "repository": "github.com/thatplatypus/grapevine",
  "releases": [
    {
      "tag": "crypto-v0.2.0",
      "sha256": "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
      "seen": "2026-10-12T08:17:03Z",
      "code": "release.invalid",
      "why": "the manifest in its archive gives the version 0.1.0"
    }
  ]
}
```

| Key | Type | Holds |
|---|---|---|
| `version` | number | `1` |
| `repository` | string | The repository, which is also the file's path |
| `releases` | array | One entry for each refused release, in order of tag |
| `releases[].tag` | string | The release's tag |
| `releases[].sha256` | string | GitHub's own digest of the file that was refused |
| `releases[].seen` | string | When it was refused |
| `releases[].code` | string | The code of the problem, as a diagnostic of `pmj` has one |
| `releases[].why` | string | Why, in one line: see [Why a release is refused](#why-a-release-is-refused) |

- **A release is in this file only while it holds the file that was refused.** Replace the file, or delete the release, and the entry goes at the next run.
- **A repository with nothing refused has no file.**

### Rules

- **A time is in UTC, to the second,** as `2026-10-11T15:02:11Z`.
- **A file is at most 4 MiB** (4,194,304 bytes), which is room for some ten thousand versions.
- **A reader passes over a key it does not know.** The registry is read by every `pmj` that was ever released, and gains keys as it grows. A key that is known has to be there and well formed, or the file is refused, with `registry.invalid` or the code of what is wrong.
- **A change that an older `pmj` could not read gets a directory of its own,** `v2`, beside this one. The three statuses and the two levels of verification are all that `v1` will ever hold.

### Statuses

| Status | Says |
|---|---|
| `active` | The version can be used |
| `yanked` | Its author has withdrawn it. What already locks it goes on using it, and nothing new may start to. Nothing sets this yet |
| `quarantined` | Its bytes changed after it was indexed. It must not be used at all |

## What is indexed

A release in a listed repository is a claim to be a version of a package when all of these hold:

- its tag is `<name>-v<version>`, as `pmj` reads one;
- it carries a file named `<name>-<version>.pmj.tar.gz`, which GitHub says is uploaded and gives a digest for;
- GitHub says when it was published.

A release that is no such claim is passed over, and nothing is said of it. A repository may release other things.

- **The package a release claims to be is `@<owner>/<name>`,** where the owner is the repository's. So only someone who can release in an owner's repositories can put a package under that scope, whatever a manifest says.
- **A claim is taken by the rule `pmj` takes a release by.** The file has to be an archive that `pmj pack` wrote, of at most 16 MiB. The manifest in it has to give that name and that version, and has to say the package lives in the repository the release is in.
- **What a version needs does not have to be indexed first.** A project that asks for a version whose dependency is not there is told which one is missing.
- **GitHub's mark of a pre-release is not read.** The version says whether it is one.

### Why a release is refused

| Code | Why | What to do |
|---|---|---|
| `archive.invalid` | its file is not an archive as pmj pack writes one | Make the file with `pmj pack`, and nothing else |
| `archive.invalid` | its file is larger than 16777216 bytes | A package's archive holds its sources and no more |
| `release.invalid` | the packmoji.json in its archive cannot be read as a manifest | Run `pmj pack` in the package, which says what is wrong with it |
| `release.invalid` | the manifest in its archive is that of `<another package>` | The tag has to name the package the archive holds |
| `release.invalid` | the manifest in its archive gives the version `<another version>` | The tag has to name the version the manifest gives |
| `release.invalid` | the manifest in its archive says that the package lives in `<another repository>` | Give `package.repository` in the manifest, or release where it says |
| `release.invalid` | this version is already in the registry, from `<a repository>` | A version is one thing for ever. Release the next version |
| `release.invalid` | what GitHub serves for it does not have the digest that GitHub lists for it | Nothing: it is looked at again when GitHub's list changes |
| `name.reserved` | the name `<name>` is one of the compiler's own packages | Name the package something else |

A reason repeats nothing from inside an archive but a name, a version or a repository that was read and found well formed. To see everything that is wrong with a release, ask `pmj` itself: `pmj add @owner/name@version --repository github.com/owner/repo`.

## What a run does

For each listed repository, and each claim in it:

| The registry has | The run does |
|---|---|
| The version, from this repository, with the digest GitHub lists | Nothing |
| The version, from this repository, and GitHub lists another digest | Downloads the file. If its bytes are not the ones recorded, the version is quarantined |
| The version, from another repository | Refuses the claim |
| The claim among the refused, with the digest GitHub lists | Nothing |
| Neither | Downloads the file, takes it or refuses it, and records which |

- **Every version is held at every run,** by the digest GitHub lists for each file of a release. Nothing is downloaded for a version that has not changed.
- **A version is never quarantined on GitHub's word alone.** The file is downloaded and its digest taken first. A file that is too large to be an archive is the one exception: it cannot be the bytes that were recorded.
- **A quarantine is for good.** A version that was quarantined stays so when its first bytes come back.
- **An indexed version whose release has been deleted stays as it is.** The run says that the release is gone.
- **A repository that cannot be reached is left as it was,** and the others are still done.
- **A repository that is taken off the list keeps its versions.** They are not looked at again.
- **A run downloads at most 64 files and 256 MiB.** What is over is left for the next run.
- **A run that finds nothing new writes nothing.**

## What never changes

- **What is recorded of a version never changes, but for its status.** No version is removed, and no package's file is.
- **A run changes a status only to quarantined,** and a new version begins active.

The run holds itself to this. It is checked again, by other code, before anything is committed: see [How it is kept](#how-it-is-kept).

## How a repository comes to be listed

Open a pull request that adds a line to [`registry-repositories.txt`](../registry-repositories.txt), on the branch `main`:

```
github.com/thatplatypus/grapevine
```

- **One repository to a line,** written `github.com/<owner>/<repo>` in lowercase, in order, and none twice.
- **The repository has to be public.**
- **Once the pull request is merged, the registry is kept again at once,** and every release the repository already has is looked at.
- **After that, a new release is in the registry within the hour.** There is nothing to tell the registry, and nothing to sign up for.

## How it is kept

The workflow [`Registry`](../.github/workflows/registry.yml) runs every hour, when the list changes, and when it is started by hand. It is two jobs.

| Job | May | Does |
|---|---|---|
| `read` | Read this repository | Takes the registry from its branch, asks GitHub for the releases, downloads what is new, and makes the registry's next state |
| `write` | Write to this repository | Takes the registry from its branch again. Holds what the first job made to the rules of a registry, and to the registry as it stands. Commits it |

- **What reads strangers' archives may write nothing.** The token that commits the registry is a token for the repository `pmj` itself is released from.
- **What may write runs nothing of what it was handed.** It reads it as data, with the registry's own reader, and refuses a file that is not one of the two kinds, a link, a version or a package that is gone, a record that changed, and a status that went anywhere but to quarantined.
- **So a run that was misled can add a version and can refuse one.** It cannot change which bytes a version is, and it cannot write outside the registry.
- **A commit follows the commit the registry was taken from, and is never forced.** A branch that moved in between refuses it, and the next run begins again.
- **GitHub may run a schedule late, and may drop a run.** It turns a schedule off in a repository where nothing has happened for 60 days, and it is turned on again by hand.

A person can commit to the branch too, and that is not held by the workflow's check. A run keeps a status that it finds. A quarantine that was lifted by hand comes back at the next run if the bytes still differ.

## A registry of your own

The registry is a directory of files, and the program that keeps it runs anywhere .NET does.

```sh
dotnet run --project src/Packmoji.Indexer -c Release -- run --registry <directory> --repositories <file>
```

- **`<file>` is a list as `registry-repositories.txt` is one.**
- **`<directory>` is brought to its next state in place,** and is made when it is not there.
- **`GITHUB_TOKEN` or `GH_TOKEN`, when one is set, is sent to GitHub's API,** which raises its limit from 60 requests an hour to 5,000. A run makes one request for every hundred releases of each listed repository.
- **`check --before <directory> --after <directory>` holds one state to another,** as the workflow's second job does.

## What the registry does not do yet

| Not yet | Comes with |
|---|---|
| `pmj` reading it | The next change to `pmj` |
| Search, and a list of every package | `pmj search` and `pmj info` |
| Withdrawing a version | A decision on how an author says so |
| Verifying a build attestation | Publishing |
| An address of its own | The day `pmj` reads the registry with nothing said |
