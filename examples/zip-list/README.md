# List ZIP entries

Print compressed size, uncompressed size, compression method, and entry name
from a ZIP central directory:

```console
gflat build examples/zip-list/zip-list.gfproj --run -- package.vsix
```

Sizes come from archive metadata. Names keep directory order and are displayed
as bytes, with quotes, backslashes, and nonprintable bytes escaped as `\xHH`.

## Filtering

Add `--match` after the input filename to filter entry names:

```console
gflat build examples/zip-list/zip-list.gfproj --run -- package.vsix --match "*.dll"
```

Matching is case-sensitive and covers the full name. `*` matches any number of
bytes, `?` matches one byte, and backslash escapes the next byte. Separators
are ordinary bytes. Quote the pattern to preserve it through the shell.

## Supported archives

The tool accepts single-disk ZIP32 archives up to 128 MiB, including VSIX
packages. The complete archive is loaded and its directory checked before
output. Comments may contain at most 65,535 bytes and must end at EOF.

ZIP64, split archives, self-extracting offset adjustments, directory encryption
or compression, and central-directory signatures are unsupported. If an archive
comment contains another valid directory and end record, that later
interpretation can be selected. Repeated candidates can increase parsing work.

Use [zip-verify](../zip-verify/README.md) to check supported payloads and CRCs.

Exit codes are 0 for success, 1 for malformed input or IO failure, and 2 for
invalid arguments. Errors go to stderr.
