# File checksums

```console
gflat build examples/checksum/checksum.gfproj
examples/checksum/bin/Debug/checksum.exe first.bin second.bin
examples/checksum/bin/Debug/checksum.exe --sha256 first.bin second.bin
examples/checksum/bin/Debug/checksum.exe --sha512 first.bin second.bin
examples/checksum/bin/Debug/checksum.exe --crc32c first.bin second.bin
```

Syntax: `checksum [--sha256 | --sha512 | --crc32c] [--] <file> [more files...]`.
Options are recognized only before the first path. `--` ends option parsing,
allowing a first filename such as `--sha256`, `--sha512`, or `--crc32c`; later paths are always literal.
The algorithm options are mutually exclusive. Unknown, repeated, or mixed leading
options and missing paths are usage errors.

By default, prints an eight-digit hexadecimal CRC32, an eight-digit Adler32,
two spaces, and the file path for each input. `--sha256` instead prints a
64-digit lowercase SHA-256 digest, two spaces, and the path, using `FileHash`.
`--sha512` prints a 128-digit lowercase SHA-512 digest in the same row format.
`--crc32c` instead prints one eight-digit lowercase CRC32C, two spaces, and the path.
Files are read in binary mode, in 4096-byte chunks. Neither
the file contents nor the formatted checksums require a String or managed
allocation. Streams close before output, including on read failure.

CRC32 is the IEEE variant used by gzip; CRC32C uses the Castagnoli polynomial
and produces different checksums. Adler32 is the zlib checksum. These can
detect accidental corruption. They are not cryptographic hashes and cannot
establish that a file is trustworthy.

SHA-256 and SHA-512 are cryptographic digests. A matching digest is useful only when the
expected digest itself comes from a trustworthy source; this tool does not
authenticate files or signatures.

Checksum rows go to stdout; usage and failure diagnostics go to stderr. Output
is explicitly flushed before success, including when the host buffers writes.

Exit codes: 0 when every input and output succeeds, 1 if any file cannot be read
or checksum output fails, and 2 for invalid options or missing paths. A failed input does not
prevent processing later files; an output failure stops processing and is
reported separately from input errors.
