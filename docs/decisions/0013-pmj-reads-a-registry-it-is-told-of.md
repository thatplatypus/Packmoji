# 0013: pmj reads a registry it is told of

- **Status:** accepted
- **Date:** 2026-10-11

## Context

Record 0012 made the registry files on a branch. Record 0007 says what finding packages on GitHub directly cannot do: a package in a shared repository has to be pointed to, `add` and `update` meet the limit of GitHub's API, the first use of a version is trusted by whoever makes it, and a version whose bytes changed is found out one lockfile at a time.

Blazemoji runs `pmj` for visitors, and relies on what it does today: an install with no network, the cache's layout, and the two files being all a command needs.

## Decision

- **`pmj` reads a registry when `PACKMOJI_REGISTRY` names one, and at no other time.** With nothing said it does what it did.
- **`--direct`, or `PACKMOJI_DIRECT=1`, asks GitHub itself whatever registry is named.**
- **What is said of a registry and cannot be used is never taken for no registry.** It stops a command that would ask the registry something, with `config.invalid`.
- **With a registry, versions are listed and chosen from the registry's files.** GitHub's API is asked nothing, and no archive is downloaded to find out what a version needs.
- **A registry holds no bytes.** What was chosen is downloaded from GitHub by the release's own address, and is kept only if its digest is the one the registry recorded and its manifest gives the package, the version, the repository and the dependencies that the registry gave.
- **A package the registry does not have is not looked for on GitHub.** The fix that is printed says how to ask GitHub itself.
- **A registry that cannot be read stops the command.** Nothing is passed over for direct mode.
- **`--repository` is refused with a registry.** The registry says where a package lives.
- **The lockfile is the same file.** One written with a registry is, for the same packages, the bytes one written without it is. It records that a version was verified by its checksum, which is what `pmj` did, whatever level a registry gives.
- **What the lockfile holds and the cache has is installed without asking anyone.** Statuses are held when the registry is read: by a command that chooses versions, by one that has to fetch, and by `pmj verify`, which asks about every locked package.
- **No token is sent to a registry.**
- **`PACKMOJI_SCOPES` holds as it does without a registry,** and the place that reads the registry asks for the file of no package outside the list, whatever asked.

## Why

- **Only when told, because nothing may change for anyone until they ask.** The address a `pmj` is built with comes when the registry has an address of its own.
- **No looking past the registry, because naming one is a limit someone set.** A package that is not in it may be one they meant to keep out.
- **Bytes held to the registry's record, because that is what a record is for.** A registry that is wrong, or that someone changed, can then name a digest and do no more: it cannot have a resolution choose by one set of dependencies and a build use another.
- **The same lockfile, so that a project can go from one way to the other,** and so that a lockfile says nothing of how its packages were found.
- **The cache answers for what is locked, because a lockfile says which bytes a version is.** Bytes of that digest are the version, whoever is or is not there to ask. An install with no network is what Blazemoji is built on.
- **A reader in `pmj` that is the indexer's own,** so that one rule decides what a file of the registry may be.

## Alternatives

- **Looking on GitHub when the registry does not have a package.** It is friendlier, and it makes the registry a hint: whoever controls what the registry lacks decides what is installed.
- **Asking the registry at every install.** It hears of a quarantine at once. An install with no network would then fail, or would have to guess that the network is why.
- **A registry in the lockfile.** A lockfile would then be tied to how it was made, and one made without a registry could not be installed with one.
- **Refusing `http://`.** A registry of one's own on the same machine is how the native binary is tested.

## Consequences

- **A quarantine reaches a machine whose cache is full only when something asks the registry.** `pmj verify` is the command that always does.
- **A registry read over `http://` is as trustworthy as the network it crosses.** The address is the user's to choose.
- **A lockfile written on GitHub itself may hold what a registry does not have.** With that registry named, the version is said to be not in the registry, and `--direct` installs it.
- **Two ways of finding packages are kept, and tested, side by side.**
