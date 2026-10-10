# 0007: Finding packages on GitHub directly

- **Status:** accepted
- **Date:** 2026-10-09

## Context

The brief asks for a `pmj` that is useful before there is a registry: with `--direct` it speaks to GitHub itself. Record 0004 let a repository hold several packages, and left open how `pmj` learns where a package lives when no registry tells it. A name gives the owner. It does not always give the repository.

The first real user is Grapevine: three packages in one repository, of which two are not in a repository of their own name. An application should be able to add `@thatplatypus/grapevine` and have all three found.

## Decision

- **Direct mode is the only mode there is, and it is on.** `--direct` and `PACKMOJI_DIRECT` are accepted and change nothing yet.
- **Nothing is added to the manifest.** A package's own manifest says where the package lives. A project that depends on it never names a repository.
- **A package is looked for in a fixed order:** where the project's lockfile says it lives, where `--repository` says, in a repository of its own name, and then in the other repositories of its owner that `pmj` has come to know. Those are the project's own, and those of packages the lockfile holds or that have just been found.
- **A release is taken only if it says it is what was looked for.** The manifest in its archive has to give the name and the version, and has to say the package lives in the repository it was found in.
- **What is found is written into the lockfile,** and read from there the next time.
- **Every command that resolves takes `--repository`,** as a place to look in as well for any package of that owner. The lockfile is the only record of where a package was found, and a lockfile can be lost.
- **A resolution that could not find a version is run again when a repository has been learned of since.** It ends when nothing new is learned.
- **A file is downloaded by the release's own address,** with no API call and no token. Only the list of a repository's releases comes from the API, for `add` with no requirement and for `update`.
- **A token is sent to the API and to nothing else.**
- **Every version is active, and verified by its checksum on first use.** Yanking, quarantine and attestations need a registry or a verifier, and come with them.
- **A project that requires attestation is refused.** Nothing is locked or fetched for it until an attestation can be verified.
- **The cache takes no lock.** An archive is kept under its own digest, and is written beside its place and put there in one step.
- **`pmj verify` changes nothing,** not even the cache.

## Why

- **No new key in the manifest, on the owner's word.** The format already says where a package lives, in the one place that knows: the package. A table of repositories in every project that uses Grapevine would say it again, in a place that can be wrong.
- **The owner's other repositories, because packages that are published together are used together.** Whatever depends on `crypto` in Grapevine's repository very likely came to it through `grapevine`. That one rule finds all three with nothing said.
- **The release has to say where it lives, because the search is wide.** Looking in every repository of an owner that `pmj` knows would otherwise let a tag in one repository stand for a package that belongs in another. The owner is the same either way, so this is against mistakes more than against attack: the scope is still the whole of the ownership check.
- **Run again, so that order does not matter.** A resolution asks for packages in order of name, and `crypto` comes before the `grapevine` that shows where it is. The source keeps what it has found, so a second run asks only for what was missing.
- **Downloads without the API, because the API answers 60 times an hour** to someone it does not know. An install of forty packages would use most of that. A release's own address is not counted.
- **Refused, because a requirement that cannot be checked is not met.** Record 0005 put `requireAttestation` in the format before anything could enforce it. Now that `pmj` installs, going on without it would turn a project's own rule off in silence.
- **No lock on the cache, because a file named by its content cannot be spoiled by a second writer.** Two runs that want the same archive write the same bytes. What is read is held to its name every time, so a file that was damaged where it lay is not used.
- **A check that repairs is a check that hides.** `verify` is run to find out whether something is wrong, and a cache that was quietly put right would answer that nothing is.

## What was checked against GitHub

These were seen on 2026-10-09, with no token, and are what the client and its tests are written to.

| Asked | Answered |
|---|---|
| `https://github.com/<owner>/<repo>/releases/download/<tag>/<file>` | 302 to a host of GitHub's for release files, then 200 with the bytes |
| The same, with the owner or the repository in another case | The same file |
| The same, for a file, a tag or a repository that is not there | 404 |
| `https://api.github.com/repos/<owner>/<repo>/releases?per_page=100&page=N` | A list with `tag_name`, `draft`, `prerelease`, `immutable`, and for each file its `name`, `size` and `digest` |
| The same, for a repository with no release | An empty list |
| The same, for a repository that is not there | 404 |
| The same, with no `User-Agent` | 403 |
| Any call to the API | The headers `x-ratelimit-limit`, `x-ratelimit-remaining` and `x-ratelimit-reset`. The limit was 60 an hour |

## Alternatives

- **A table of repositories in the project's manifest.** It always works and needs no search. It was the first design, and the owner turned it down: the format already says this once.
- **Ask GitHub's API which repositories an owner has, and look in each.** It finds anything, at the cost of many calls against a limit of 60, and of looking in repositories that have nothing to do with the package.
- **Download through the API.** It needs a token for anything more than a few packages, and gives nothing the release's own address does not.
- **Trust the tag, and not read the manifest first.** It saves reading an archive that is about to be read anyway, and takes a release for a package that may not be in it.
- **Repair the cache during `verify`.** It is kinder, and it makes the command's answer mean less.

## Consequences

- **A package in a shared repository costs one request that finds nothing,** the first time: a repository of its own name is tried before its neighbours. The lockfile remembers, so it is once.
- **Some cases need to be told.** A project that depends on a package in a shared repository, and on nothing else of that owner, has nothing to find it beside. Neither has a package that needs one that is not released beside it. And a project whose lockfile is gone has lost what it was told. Each is told with `--repository`, on whichever command is resolving.
- **Only public repositories are read.** A release's file is downloaded with no token. GitHub's API lists a private repository's releases to a token that may see them, and the file is then not found by its own address.
- **A package that moves to another repository has to be pointed to again.** `pmj update` lists a package's versions from the first place that has any, which is the repository the lockfile records, so versions released somewhere else are not seen. `pmj add` with the version and `--repository` moves a project to the new place. A version that is published again there is another file, and `pmj install` reports it as `lock.mismatch`.
- **The first use of a version is trusted.** Nothing but the lockfile records what a version's bytes were. Two people who first fetch a version at different times are protected from each other only once one of their lockfiles is shared.
- **`add` and `update` can meet the API's limit,** and say so, with how to raise it. `install` cannot.
- **Packages are downloaded one after another.** A resolution has to read each manifest to know what to ask for next. Fetching what a lockfile holds could be done several at a time, and is not yet.
