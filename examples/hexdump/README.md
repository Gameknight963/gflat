# Hex dump

```console
gflat build examples/hexdump/hexdump.gfproj
examples/hexdump/bin/Debug/hexdump.exe input.bin
```

Prints 16 bytes per row, with a 64-bit hexadecimal byte offset and printable ASCII
on the right. Other bytes display as dots. The final partial row is padded; an
empty file produces no rows. On Linux, omit the executable's `.exe` suffix.

The tool reads in 4096-byte chunks. It uses `Hex`, `IntegerFormatting`, `Ascii`,
and a stack-backed `BufferWriter` to format each complete row before writing it
to the console. There are no String or managed allocations. The C file runtime
can allocate its own buffering independently.

Exit codes are 0 for success, 1 for an I/O failure, and 2 for incorrect arguments.
Diagnostics go to stderr. If reading or writing fails, already printed rows remain
visible and the input file still closes during unwinding.
