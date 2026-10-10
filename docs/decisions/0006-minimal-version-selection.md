# 0006: Minimal version selection

- **Status:** accepted
- **Date:** 2026-10-09

## Context

The brief asks for minimal version selection as Go has it: every requirement is a minimum, and a build uses the highest minimum anyone asked for and nothing newer. That one sentence leaves open what to do when the version it selects cannot be used, which requirements count, and what a resolver should do with a graph that Emojicode could never build. This record settles those, and says what each answer costs.

## Decision

- **A selected version that is yanked, or that was never published, is an error.** The resolver says which minimum to raise. It never moves to a later version by itself.
- **A yanked version that the lockfile already holds goes on being used,** with a warning.
- **Every requirement that can be reached counts,** including those of a version that ends up passed over for a higher one.
- **A reachable version that was never published is an error even when it is passed over.**
- **A package that only a passed-over version asks for is selected for and is left out of the build.** What is built is what the project asks for and what the selected versions lead to.
- **A circle among the versions a build would use is an error that shows the circle,** and so is one of them depending on the project itself.
- **A graph of more than 10,000 versions is refused.**
- **Errors are listed before warnings,** each kind in a fixed order.

## Why

- **No moving past a yank, so that "nothing newer than was asked for" has no exception.** The resolver then only ever asks for exact versions, which a repository's releases can answer as readily as a registry can. A resolver that stepped past a yanked version would need a list of versions, and would give two people two different builds from one manifest, depending on when each resolved.
- **A locked yank keeps working, because a yank must not break a build that worked yesterday.** That is what yanking is for, as against deleting.
- **Every requirement counts, because which versions are passed over depends on the same graph.** Counting only the requirements of selected versions means selecting, following, and selecting again until nothing changes, and the answer can then depend on where that started. Counting everything that can be reached gives one answer, found in one pass. It is also what Go does.
- **A missing version is an error wherever it is, because otherwise publishing it later would change a build that no one touched.** Its requirements would start to count on the day it appeared.
- **A package nothing built depends on is left out, because a lockfile lists what is fetched and built.** The lockfile reader already refuses an entry that nothing leads to.
- **A circle is an error here because the compiler refuses circular imports.** Saying so before anything is fetched is kinder than a compiler error after everything is.
- **A limit on the graph, because the resolver believes its source.** A source that answers wrongly could otherwise keep it going for ever.
- **Errors first, because only a hundred problems are listed.** A long run of warnings must not push the one error out of sight.

## Alternatives

- **Step past a yanked version to the next one on the line.** Cargo and npm do something like it. It needs a list of versions and makes a resolution depend on its date.
- **Count only the requirements of selected versions.** It gives smaller graphs, and it is what a person first expects. It was passed over for the reasons above.
- **Let a missing version pass when something higher is asked for.** It forgives a publisher's slip, and it lets a later publication change builds.
- **Keep every selected package in the lockfile, used or not,** as Go keeps every module in its build list. Packmoji would then fetch and build packages that nothing imports.

## Consequences

- **A yank deep in a graph stops fresh resolutions downstream** until someone raises a minimum. The project can always do that itself, in its own manifest, on the same line.
- **A missing version cannot be fixed by asking for more,** since the missing version is still reached. The project can change only the requirement at the head of the chain that leads to it: when a later version of that package no longer leads there, asking for it is the fix. Otherwise the package whose manifest names the missing version has to publish one that names another, and each package between it and the project has to move to that in turn.
- **A conflict of lines is reported even when one side comes from a passed-over version.** The fix is to raise the minimum that brings the older version into the graph.
- **A package can be asked about and not built.** It still has to be published, and its requirements still count.
- **Raising a minimum can lower another package's version.** The passed-over version's requirements stop counting once nothing reaches it. Only adding a requirement is sure never to lower anything.
- **The limit of 10,000 is on versions, not on requirements.** A version with a very long list of dependencies costs time in proportion, which the sources that read real manifests bound by the size of a manifest.
