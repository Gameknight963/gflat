# Hex dump

Print a file's bytes in hexadecimal and ASCII:

```console
gflat build examples/hexdump/hexdump.gfproj
examples/hexdump/bin/Debug/hexdump.exe input.bin
examples/hexdump/bin/Debug/hexdump.exe --skip 4097 input.bin
```

Rows contain a 64-bit hexadecimal offset, 16 bytes, and printable ASCII.
Nonprintable bytes appear as dots. The final partial row is padded.

`--skip <count> <file>` reads and discards an initial byte count. The first row
starts at that offset, including offsets between row boundaries. Skipping past
EOF is an error. The count is an unsigned decimal integer, optionally prefixed
with `+`.

The tool processes 4096-byte chunks and formats rows with [Hex](../../docs/BINARY_CODECS.md)
and [BufferWriter](../../docs/BUFFER_WRITER.md).

Exit codes are 0 for success, 1 for IO failure, and 2 for invalid arguments.
Errors go to stderr. An IO failure can leave earlier rows on stdout.
