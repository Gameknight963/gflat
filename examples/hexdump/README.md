# Hex dump

```console
gflat build examples/hexdump/hexdump.gfproj
examples/hexdump/bin/Debug/hexdump.exe input.bin
examples/hexdump/bin/Debug/hexdump.exe --skip 4097 input.bin
```

Prints 16 bytes per row, with a 64-bit hexadecimal byte offset and printable ASCII on
the right. Other bytes display as dots. The final partial row is padded. An empty file
produces no rows. On Linux, prefix the executable path with `./`. The default project
output uses `.exe`.

## Skipping bytes

`--skip <count> <file>` sequentially reads and discards the requested bytes before
printing. The first row's offset is the skipped count, including when it is not a
multiple of 16. Skipping exactly the file length succeeds without rows. Skipping past
EOF reports an I/O failure.

The count is a nonnegative decimal integer fitting the native unsigned pointer width.
Leading zeros and an optional `+` are accepted. Minus signs, whitespace, radix prefixes,
and overflow produce usage error 2 before opening the file. A single argument is always
a filename, so `hexdump --skip` opens that literal path.

## Processing

The tool reads in 4096-byte chunks. It uses `Hex`, `IntegerFormatting`, `Ascii`, and a
stack-backed `BufferWriter` to format each complete row before writing it to the
console. The C file runtime supplies its own stream buffering.

## Errors

Exit codes are 0 for success, 1 for an I/O failure, and 2 for incorrect arguments.
Diagnostics go to stderr. If reading or writing fails, already printed rows remain
visible and the input file still closes during unwinding.
