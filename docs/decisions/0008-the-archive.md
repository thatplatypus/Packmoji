# 0008: The archive

- **Status:** accepted
- **Date:** 2026-10-09

## Context

A version of a package is one file, `<name>-<version>.pmj.tar.gz`, and its SHA-256 is what a lockfile holds. The brief asks that packing the same tree twice give the same bytes. More than that follows from locking a digest: the same tree has to give the same bytes on another machine, on another operating system, and from a later `pmj`. Otherwise a package cannot be packed again and checked against what was published.

The file is also unpacked on the machine of everyone who installs it, and it comes from someone else.

## Decision

- **The archive is a tar in one canonical form, inside a gzip whose data is stored and not compressed.**
- **The tar holds regular files only,** in order of path by UTF-8 bytes. No entry for a directory, no link, no device.
- **Every header says the same:** mode 0644, owner and group 0 with no names, time 0.
- **A path of plain ASCII up to 100 bytes is in the header's name field.** Any other is in a pax record before its entry, which holds `path` and nothing else.
- **The tar ends with two blocks of zeros,** and nothing after them.
- **The gzip header has no name, no time and no flags.** The data is in stored blocks of at most 65,535 bytes.
- **Reading accepts any valid gzip of one member, and inside it only the canonical tar.** What was read is written again, and has to come out the same bytes.
- **Three limits:** an archive is at most 16 MiB, holds at most 4,096 files, and unpacks to at most 64 MiB.
- **Two paths that are the same but for case are refused,** and so is a part of a path that Windows keeps for a device. Both when packing and when reading.
- **Whether two names differ only by Unicode normalization is not checked.**

## Why

- **Stored, because no compressor promises its bytes.** The same input gives different output from different versions of zlib, from different libraries, and at different levels. A digest that holds only for one build of one library on one machine is not one to lock.
- **gzip all the same, because the name was already fixed.** A lockfile records the file's name, which ends `.pmj.tar.gz`, and every tool that opens a `.tar.gz` opens this one. Stored blocks are plain gzip.
- **One form of tar, because tar has many.** The owner's name, the time and the mode are a machine's own, and would make two people's archives differ. Fixing each leaves nothing of the machine in the file.
- **Reading by writing again, because it is one rule in place of many.** An archive that holds a link, a path that climbs out, a file marked to run, or an entry of a kind `pmj` never writes does not come out the same, and is refused without a rule for each.
- **Any gzip when reading, because the form of the tar is what matters.** It leaves room for a later `pmj` to compress what it writes, and for this one to read it.
- **Limits, because the file is read into memory and unpacked onto a disk.** They can be raised later, and could not be lowered without refusing what was already published.
- **Case, because many disks keep one file for two names that differ by it.** A package packed on Linux would otherwise install on macOS as something else.
- **Normalization is left, because `pmj` cannot check it.** It is built without culture data, where .NET's normalization does nothing. That is a known gap, and it is listed for the milestone that hardens things.

## Alternatives

- **Compress, and pin the compressor.** It gives smaller files. The pin would have to hold across .NET versions and across the native library each platform ships, which is not `pmj`'s to promise.
- **Compress with a compressor written here.** It would be reproducible, and it is a great deal of code to get exactly right for small text files.
- **A tar with no gzip around it.** It is the honest name for what this is. It would change the file's name, which the lockfile's format has fixed.
- **Zip.** Its entries carry times and attributes of their own, and it has as many forms as tar.
- **Read any tar, and check each entry.** It reads archives made by other tools, and the list of what to refuse is never finished.

## Consequences

- **An archive is a little larger than its files, and not smaller.** Source text compresses to a fraction of its size, and here it does not. With the limit of 16 MiB that is small either way.
- **Only `pmj pack` writes an archive that `pmj` reads.** One made with another tool is refused unless it is the same bytes, which in practice it never is.
- **The limit that binds a package is 16 MiB of sources.** The limit of 64 MiB unpacked matters only for an archive that was compressed by something else.
- **A package can be packed again from its tag and checked.** The digest of what comes out is the digest in every lockfile that uses that version.
- **The reader has not been fuzzed.** It is strict by construction, and fuzzing it is listed for the milestone that hardens things.
