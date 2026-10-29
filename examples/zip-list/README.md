# ZIP central-directory listing

From the repository root, build and run against a package as data:

```console
gflat build examples/zip-list/zip-list.gfproj --run -- package.vsix
```

A read-only metadata inspector for ordinary single-disk ZIP32 files, including
VSIX packages. It accepts input files up to 128 MiB, validates the complete central-directory
walk, then prints compressed size, uncompressed size, numeric compression method,
and quoted filename bytes in central-directory order. Both sizes are the values
declared by the archive, not independently measured payload sizes.

Names are counted bytes, not decoded Unicode. Printable ASCII is shown directly
except quotes and backslashes; all other bytes use uppercase `\xHH` escapes.
Embedded zero, newline, terminal escapes and high bytes cannot become raw terminal
control characters. Extra fields, entry comments and the archive comment are not
printed. Archive comments up to 65,535 bytes are supported.

The reader requires an exact EOCD-to-EOF comment extent, matching single-disk entry
counts, and a directory consisting of exactly that many bounded central entries,
ending immediately before the EOCD. Signature-like bytes in comments are only
candidates: an invalid candidate is skipped while the search continues backward.
A crafted comment containing another fully valid directory/EOCD interpretation
can select that later interpretation. There is no general linear-time guarantee
for adversarial comments that cause repeated candidate-directory walks.

ZIP64 sentinel counts, sizes and offsets, nonzero disk numbers, spanning,
central-directory digital signatures, encrypted/compressed central directories,
and trailing bytes after the declared archive comment are unsupported. Extra
fields are skipped without interpretation. There is no SFX offset adjustment,
local-header cross-check, decompression, CRC validation, signature verification,
extraction, or execution of archived content. An encrypted entry's visible central
metadata may be listed; that does not validate or decrypt its contents. Successful
listing means the supported central metadata is structurally readable, not that
the archive or its payload is valid or safe to extract.

Input is completely materialized before inspection. One owning byte vector holds
the image; entry names borrow its unchanged storage. Entries are validated once
and parsed again for printing, without an allocated entry collection. All metadata
is checked before stdout output begins; output failures can still leave partial
output. Exit codes are 0 for a completed listing, 1 for read/metadata/output failure,
and 2 for incorrect arguments. Error reporting is best effort on stderr.

Format reference: [PKWARE APPNOTE](https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT),
sections 4.3.12 and 4.3.16. This example adds no public standard-library API.

## Filter raw entry names

```console
gflat build examples/zip-list/zip-list.gfproj --run -- package.vsix --match "*.dll"
```

The optional `--match` flag follows the input filename and takes exactly one
pattern. Quote the pattern so the shell passes it unchanged. With no flag, all
entries are listed as before. Unknown options, missing patterns and extra operands
print usage and exit with code 2 before opening the input.

Matching covers the whole counted filename using `std::Text::MatchesWildcard`:
`*` matches zero or more bytes, `?` matches one byte, and backslash escapes the
next literal byte. A dangling backslash matches no names. Comparison is ordinal
and case-sensitive. Separators are ordinary bytes, with no path rules or special
`**` behavior. This is not Unicode matching: one UTF-8 character can require
multiple `?` bytes. Embedded zero bytes in archive names remain part of matching;
the command-line pattern is a C string and cannot contain a literal zero.

Every central entry is validated before any output, including entries excluded by
the filter. Filtering does not make malformed metadata acceptable. Matching rows
retain central-directory order and the same four columns and quoted byte escaping.
The header is printed even when no name matches. No entry list or normalized name
copy is built. The wildcard matcher uses constant storage and can take O(N*M)
work for a filename of N bytes and a pattern of M bytes.
