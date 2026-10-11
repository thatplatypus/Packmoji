# The registry

This branch is the registry of Packmoji. Under `v1/packages` there is a file for each package,
with every version of it that has been indexed. Under `v1/refused` there is a file for each
repository with a release that claims to be a package and was refused, and why.

A workflow writes this branch, and `pmj` reads it. Nothing here is written by hand.
`docs/registry.md` on the branch `main` says what each file holds, how a repository comes to be
indexed, and what may change.
