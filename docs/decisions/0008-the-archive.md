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
- **Reading accepts the bytes `pmj pack` writes, and no others.** What was read is written again, tar and gzip alike, and has to come out the same bytes.
- **Two limits:** an archive is at most 16 MiB, and holds at most 4,096 files. It is not compressed, so it unpacks to less than it is.
- **Two paths that are the same but for case are refused,** and so is a part of a path that Windows keeps for a device. Both when packing and when reading. Case is a letter and its one capital, as .NET's own tables have them.
- **Whether two names differ only by Unicode normalization is not checked,** nor by the fuller rules of case, under which a sharp s is two letters s.
- **When an archive is unpacked, no file is ever written over another.** Two names that only this disk holds to be one stop the install: nothing is unpacked, and nothing is locked.

## The bytes

Everything that is not named here is a zero byte.

**The gzip**

| Bytes | Hold |
|---|---|
| 10 | `1f 8b 08 00 00 00 00 00 00 ff`: gzip, deflate, no flags, no time, no extra flags, and an operating system of "unknown" |
| 5 for each block | `00`, or `01` for the last block. Then the block's length as two bytes, lowest first, and the same two bytes with every bit turned |
| Up to 65,535 for each block | The tar's own bytes, as they are. Every block but the last is full. An empty tar would be one block of nothing |
| 8 | The CRC-32 of the tar, and its length, each four bytes, lowest first |

**A file's header in the tar,** which is one block of 512 bytes

| At | Bytes | Hold |
|---|---|---|
| 0 | 100 | The path, when it is plain ASCII of at most 100 bytes. Otherwise `PaxPath` |
| 100 | 8 | `0000644` and a zero |
| 108 and 116 | 8 each | `0000000` and a zero: the owner and the group |
| 124 | 12 | The file's length as eleven octal digits, and a zero |
| 136 | 12 | `00000000000` and a zero: the time |
| 148 | 8 | The sum of the header's bytes, with these eight read as spaces: six octal digits, a zero and a space |
| 156 | 1 | `0`, a file |
| 257 | 8 | `ustar`, a zero, and `00` |

The file's bytes follow, filled out with zeros to a whole block.

**A path that is not in its header** is in an entry before the file's own. That entry's header is the same, but for the name `PaxHeader`, the type `x`, and the length of its one record, which is its content: the record's own length in decimal, a space, `path=`, the path in UTF-8, and a line feed.

**The end** is two blocks of zeros.

## Why

- **Stored, because no compressor promises its bytes.** The same input gives different output from different versions of zlib, from different libraries, and at different levels. A digest that holds only for one build of one library on one machine is not one to lock.
- **gzip all the same, because the name was already fixed.** A lockfile records the file's name, which ends `.pmj.tar.gz`, and every tool that opens a `.tar.gz` opens this one. Stored blocks are plain gzip.
- **One form of tar, because tar has many.** The owner's name, the time and the mode are a machine's own, and would make two people's archives differ. Fixing each leaves nothing of the machine in the file.
- **Reading by writing again, because it is one rule in place of many.** An archive that holds a link, a path that climbs out, a file marked to run, or an entry of a kind `pmj` never writes does not come out the same, and is refused without a rule for each.
- **One gzip for one tar, because a gzip can say the same thing in endless ways, and can carry more after its end.** A reader that took any of them would give one set of files many digests, and would let a release hold bytes that nobody who unpacks it ever sees. Nothing is inflated, so there is no archive that unpacks to fill a disk.
- **Limits, because the file is read into memory and unpacked onto a disk.** They can be raised later, and could not be lowered without refusing what was already published.
- **Case, because many disks keep one file for two names that differ by it.** A package packed on Linux would otherwise install on macOS as something else.
- **Normalization and the fuller rules of case are left, because `pmj` cannot check them.** It is built without culture data, where .NET's normalization does nothing and its casing knows single letters only. That is a known gap, and it is listed for the milestone that hardens things. Until then the disk is the judge, and it fails safe: a package with two such names installs where the disk keeps them apart, and is refused, whole, where it does not.

## Alternatives

- **Compress, and pin the compressor.** It gives smaller files. The pin would have to hold across .NET versions and across the native library each platform ships, which is not `pmj`'s to promise.
- **Compress with a compressor written here.** It would be reproducible, and it is a great deal of code to get exactly right for small text files.
- **A tar with no gzip around it.** It is the honest name for what this is. It would change the file's name, which the lockfile's format has fixed.
- **Zip.** Its entries carry times and attributes of their own, and it has as many forms as tar.
- **Read any tar, and check each entry.** It reads archives made by other tools, and the list of what to refuse is never finished.
- **Read any gzip, so long as the tar inside it is the one form.** It was the first design, and would let a later `pmj` compress what it writes and this one still read it. But .NET's reader joins gzips that follow one another and passes over what comes after the last, so one set of files had many archives, and an archive could carry megabytes that are never unpacked. A reader that is exact can be made looser later. One that is loose can never be made exact, once the digest of such a file is in someone's lockfile.

## Consequences

- **An archive is a little larger than its files, and not smaller.** Source text compresses to a fraction of its size, and here it does not. With the limit of 16 MiB that is small either way.
- **Only `pmj pack` writes an archive that `pmj` reads.** One made with another tool is refused unless it is the same bytes, which in practice it never is.
- **A package is at most 16 MiB of sources, less a little for the tar around them.** `pmj pack` says so before it reads a file, because the length of an archive follows from the names and the lengths of what goes into it.
- **A later `pmj` that compressed would write archives this one cannot read.** That would be another form, and would need a name of its own.
- **A package can be packed again from its tag and checked.** The digest of what comes out is the digest in every lockfile that uses that version.
- **The reader has not been fuzzed.** It is strict by construction, and fuzzing it is listed for the milestone that hardens things.
