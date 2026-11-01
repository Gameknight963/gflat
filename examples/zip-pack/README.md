# Create a ZIP archive

Pack files under explicit entry names:

```console
gflat build examples/zip-pack/zip-pack.gfproj --run -- package.zip readme.txt README.md data/items.bin items.bin
```

Arguments after the output path are entry-name/input-file pairs. The tool creates
a ZIP32 archive with stored entries and prints `Packed N entries`. Existing output
files are refused.

Entry names contain 1 to 65,535 UTF-8 bytes and are copied exactly as supplied.
Input and output file paths use the host's narrow-character encoding. Each input
is read and released before the next, while the writer retains entry names and
directory metadata.

Each entry is limited to 16 MiB. The complete archive, including headers and entry
names, is limited to 128 MiB, with at most 65,534 entries. Files are stored without
compression.

Exit codes are 0 for success, 1 for IO, format, or limit errors, and 2 for invalid
arguments. Errors go to stderr. Failure after output creation can leave a partial
archive at the new path.
