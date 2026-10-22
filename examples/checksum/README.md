# File checksums

```console
gflat build examples/checksum/checksum.gfproj
examples/checksum/bin/Debug/checksum.exe first.bin second.bin
```

Prints an eight-digit hexadecimal CRC32, an eight-digit Adler32, and the file
path for each input. Files are read in binary mode, in 4096-byte chunks. Neither
the file contents nor the formatted checksums require a String or managed
allocation. Streams close before output, including on read failure.

CRC32 is the IEEE variant used by gzip; Adler32 is the zlib checksum. These can
detect accidental corruption. They are not cryptographic hashes and cannot
establish that a file is trustworthy.

Checksum rows go to stdout; usage and failure diagnostics go to stderr. Output
is explicitly flushed before success, including when the host buffers writes.

Exit codes: 0 when every input and output succeeds, 1 if any file cannot be read
or checksum output fails, and 2 for missing arguments. A failed input does not
prevent processing later files; an output failure stops processing and is
reported separately from input errors.
