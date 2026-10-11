# 0012: A registry of files

- **Status:** accepted
- **Date:** 2026-10-11

## Context

The brief asks for a registry: a service that is always running, with a database, an address that `pmj` asks, a queue that indexes a release when it is told of one, and a job that checks bytes again. It gives "deployed as a container on a server" as the owner's assumption.

When the registry came to be built, the owner said otherwise. He would rather have nothing that has to be kept up: files that something writes now and then, which `pmj` reads. Asked where they should be kept, he chose this repository, for now.

Record 0007 says what finding packages on GitHub directly cannot do. A package in a shared repository has to be pointed to. The first use of a version is trusted by whoever makes it, and by nobody else. `add` and `update` meet the limit of GitHub's API. A version cannot be withdrawn, and one whose bytes changed is found out one lockfile at a time.

## Decision

- **The registry is files, and there is no server.** A program that runs and stops writes them.
- **They are kept in this repository, on a branch of their own, `registry`.** What is read is `v1/packages/<scope>/<name>.json`, one file for each package, with every indexed version: its status, where it lives, its digest and size, how far it was verified, when it was published and when it was first seen, and what it needs.
- **What is indexed is the releases of the repositories on a list,** `registry-repositories.txt` on `main`. A pull request changes the list.
- **A release is taken by the rule `pmj` takes one by,** in the same code: `ReleaseRule`. The scope it claims is the owner of the repository it is in.
- **What is recorded of a version never changes, but for its status.** No version is removed. A status goes to quarantined, and by a run it goes nowhere else.
- **A release that claims to be a package and is refused is recorded too,** in `v1/refused/<owner>/<repository>.json`, with a reason that repeats nothing from inside the archive but a name, a version or a repository that was read and found well formed.
- **Every version is held at every run,** by the digest GitHub lists for each file of a release. A difference is confirmed by downloading the file before a version is quarantined.
- **The workflow is two jobs.** One asks GitHub, reads archives, and has no right to write. One has the right to write, reads what the first made as data, holds it to the rules and to the registry as it stood, and commits it.
- **A commit follows the commit the registry was taken from, and is never forced.**
- **It runs every hour, when the list changes, and when it is started by hand.**
- **A reader passes over a key it does not know.** A change that an older `pmj` could not read gets a directory of its own, `v2`.
- **The program that keeps the registry is not part of `pmj`.** It is `src/Packmoji.Indexer`, with two commands: `run` and `check`.

## What became of the brief's server

| The brief's server | Here |
|---|---|
| An API under `/v1` that `pmj` asks | Files under `v1`, read by their address |
| A queue that indexes a release when asked, and a webhook | A run that looks at every release of every listed repository |
| SQLite and migrations | None. The files are the data |
| Audit events | The commits of the branch, each of which says what it changed |
| A job that checks a sample of bytes again | GitHub's digest of every file, compared at every run |
| An allowlist for the early days | The list |
| Search | Not yet. A file to search in comes with `pmj search` |
| A Docker image, health checks, a limit on requests, OpenAPI | None |
| Routes for an admin, which refuse everything | A person's own commit to the branch |
| `IReleaseVerifier` and `IPackageStatusService` | One function each, until there is a second way to do what each does |

## What was checked against GitHub

These were seen on 2026-10-11, and are what the design stands on.

| Asked or read | Answered |
|---|---|
| `https://api.github.com/repos/<owner>/<repo>/releases` | For each release `immutable` and `published_at`. For each file `name`, `size`, `state` and `digest`, which is `sha256:` and the digest GitHub took when the file was uploaded |
| `https://raw.githubusercontent.com/<owner>/<repo>/<branch>/<path>` | The bytes, with `cache-control: max-age=300` and an `etag`. Asked again with the tag, 304. For a file or a branch that is not there, 404 |
| GitHub's page on what starts a workflow | "The shortest interval you can run scheduled workflows is once every 5 minutes." A schedule "can be delayed during periods of high loads", and "some queued jobs may be dropped". It runs "on the latest commit on the default branch". "In a public repository, scheduled workflows are automatically disabled when no repository activity has occurred in 60 days." |
| GitHub's page on the token of a workflow | "The token's permissions are limited to the repository that contains your workflow." What is pushed with it starts no workflow |
| GitHub's page on the limits of its API | "The rate limit for `GITHUB_TOKEN` is 1,000 requests per hour per repository." Requests are to be made one after another |

## Why

- **Files, because nothing then has to be kept up.** A server has to be hosted, watched, backed up and defended, and when it is down nothing resolves. What is read here is as available as GitHub is, which every package depends on already.
- **This repository, on the owner's word.** `pmj` knows only an address, so moving the files later is copying them.
- **A branch of its own, so that the tool's history stays the tool's.** Every indexed release would otherwise be a commit among the code's. A branch also moves to a repository of its own with one push, history and all.
- **A list, because a way in for strangers has to be defended.** A pull request is a way in that is there already, and the owner reads it.
- **One file for each package, so that a command reads what it asks about and nothing else,** however many packages there come to be.
- **GitHub's own digests, because they hold every version for the price of the list** that is asked for anyway. A sample would hold a few. The bytes are still what decides: nothing is quarantined until they have been seen.
- **Two jobs, because the token that commits the registry is a token for the repository `pmj` is released from.** What reads a stranger's archive must not hold it. And what holds it checks the work again, in other code than did it: a run that was misled, by a mistake or by an archive made to mislead it, can add a version and can refuse one, and cannot change which bytes a version is.
- **Never forced, so that a person's commit to the branch is never written over.**
- **A reader that passes over what it does not know, because every `pmj` ever released reads the registry.** The manifest's rule, that an unknown key is an error, would stop each of them the day a key was added.
- **Reasons from a fixed set, because a reason is shown to people and printed by tools,** and what an archive holds is a stranger's text.

## Alternatives

- **The brief's server.** It answers at once when a release is made, and it can search. Its cost is above.
- **Blob storage.** The same files. The job that writes needs an identity there, and blobs keep no history, so the record of changes would have to be designed. A branch gives both.
- **A repository of its own.** It keeps the token that writes the registry away from the tool altogether, and the hourly runs out of the tool's list of runs. It is where the registry goes if this one comes to be too small for both.
- **A folder on `main`.** The two histories would be one, and hard to part again.
- **One file for the whole registry.** It is the least to build. Every command would download every package's history, and the day that is too much is a new format.
- **Letting anyone ask for a repository to be indexed,** by a topic on the repository, an issue, or a small endpoint. Each has to be defended against whoever asks for a thousand.
- **A log of events as a file.** The commits are that log, and nobody can edit them unseen.
- **The indexer as a command of `pmj`.** It would put the registry's keeper in every copy of the tool.
- **Holding each added version to GitHub's own list in the job that commits.** A misled run could then add only what GitHub lists. The job that may write would read from the network, which it does not: all it reads is what the first job made.

## Consequences

- **A release is in the registry within the hour, and not at once.** GitHub may run a schedule late or drop a run, and what it serves of a branch may be five minutes old. A run can be started by hand.
- **The registry cannot be asked why it refused a release.** The file of what was refused says, at the next run.
- **Each run is two jobs in this repository's list of runs,** 24 times a day.
- **GitHub turns the schedule off after 60 days in which nothing happens in the repository.** It is turned on again by hand.
- **`pmj` reads the registry only when it is told where it is:** record 0013. Until it reads one with nothing said, the address is the branch's own.
- **Withdrawing a version is not decided.** A version whose release was deleted stays as it is, and a run says that the release is gone.
- **A misled run can take a version's number.** What it adds is held to the rules and not to GitHub's list. A version it adds before any release carries it is quarantined when the real release comes, and a person's commit to the branch is what puts it right.
- **The repositories take turns at what a run may download.** Whoever is listed could otherwise keep everyone after them waiting, by releasing enough.
- **A run asks GitHub once for every hundred releases of each listed repository,** and the workflow's token may ask 1,000 times an hour. That is room for some hundreds of repositories, and not for thousands.
- **Search needs a file that lists every package,** which nothing writes yet.
