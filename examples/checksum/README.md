# File checksums

Compute checksums or digests for one or more files:

```console
gflat build examples/checksum/checksum.gfproj
examples/checksum/bin/Debug/checksum.exe first.bin second.bin
examples/checksum/bin/Debug/checksum.exe --sha256 first.bin second.bin
examples/checksum/bin/Debug/checksum.exe --sha512 first.bin second.bin
examples/checksum/bin/Debug/checksum.exe --crc32c first.bin second.bin
```

| Option | Output before each file path |
| --- | --- |
| Default | CRC32 and Adler32, eight hexadecimal digits each |
| `--crc32c` | Eight-digit Castagnoli CRC32C |
| `--sha256` | 64-digit SHA-256 digest |
| `--sha512` | 128-digit SHA-512 digest |

Choose at most one algorithm option before the first path. Use `--` before a
first filename that resembles an option. Later filenames are taken literally.
Hexadecimal output is lowercase.

CRC32, CRC32C, and Adler32 detect accidental corruption. For SHA-256 or SHA-512,
compare against an expected digest obtained from a trustworthy source.

Files are processed in 4096-byte chunks. A failed file produces a stderr
diagnostic and processing continues with the next file. An output failure stops
the command.

Exit codes are 0 when all files succeed, 1 for file or output errors, and 2 for
invalid arguments.
