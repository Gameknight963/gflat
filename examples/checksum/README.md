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

Exit codes: 0 when every input succeeds, 1 if any file cannot be read, and 2
for missing arguments. A failed file does not prevent processing later files.
