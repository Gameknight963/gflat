# ZIP central-directory listing

List the central directory of a single-disk ZIP32 archive, including VSIX packages:

```console
gflat build examples/zip-list/zip-list.gfproj --run -- package.vsix
```

The tool accepts files up to 128 MiB and validates the complete directory before
printing compressed size, uncompressed size, numeric compression method, and quoted
filename bytes in directory order. Sizes come from archive metadata.

## Names and output

Names are counted bytes. Printable ASCII appears directly except quotes and backslashes.
Other bytes use uppercase `\xHH` escapes. The four output columns contain sizes, method,
and name. Archive comments up to 65,535 bytes are supported.

## Format checks

The reader requires an exact EOCD-to-EOF comment extent, matching single-disk entry
counts, and a directory consisting of exactly that many bounded central entries, ending
immediately before the EOCD. Signature-like bytes in comments are only candidates. An
invalid candidate is skipped while the search continues backward. A comment containing
another valid directory/EOCD interpretation can select that later interpretation.
Repeated candidate-directory walks can increase work for adversarial comments.

ZIP64 sentinel counts, sizes and offsets, nonzero disk numbers, spanning,
central-directory digital signatures, self-extracting offset adjustments,
encrypted/compressed central directories, and trailing bytes after the declared archive
comment are unsupported. Extra fields are skipped as opaque bytes. Encrypted entries'
visible directory metadata can be listed. Use [zip-verify](../zip-verify/README.md) to
check supported entry payloads and CRCs.

## Storage and errors

Input is completely materialized before inspection. One owning byte vector holds the
image. Entry names borrow its unchanged storage. Entries are validated once and parsed
again for printing, without an allocated entry collection. All metadata is checked
before stdout output begins. Output failures can still leave partial output. Exit codes
are 0 for a completed listing, 1 for read/metadata/output failure, and 2 for incorrect
arguments. Error reporting is best effort on stderr.

Format reference: [PKWARE
APPNOTE](https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT), sections 4.3.12
and 4.3.16.

## Filter raw entry names

```console
gflat build examples/zip-list/zip-list.gfproj --run -- package.vsix --match "*.dll"
```

The optional `--match` flag follows the input filename and takes exactly one pattern.
Quote the pattern so the shell passes it unchanged. With no flag, all entries are listed
as before. Unknown options, missing patterns and extra operands print usage and exit
with code 2 before opening the input.

Matching covers the whole counted filename using `Standard::Text::MatchesWildcard`: `*`
matches zero or more bytes, `?` matches one byte, and backslash escapes the next literal
byte. A dangling backslash matches no names. Comparison is ordinal and case-sensitive.
Separators are ordinary bytes. A UTF-8 character can require multiple `?` bytes.
Embedded zero bytes in archive names remain part of matching. the command-line pattern
is a C string and cannot contain a literal zero.

Every central entry is validated before any output, including entries excluded by the
filter. Matching rows retain central-directory order and the same four columns and
quoted byte escaping. The header is printed even when no name matches. The wildcard
matcher uses constant storage and can take O(N*M) work for a filename of N bytes and a
pattern of M bytes.
