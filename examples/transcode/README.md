# UTF-8 / UTF-16LE conversion

```console
gflat build examples/transcode/transcode.gfproj
examples/transcode/bin/Debug/transcode.exe utf8-to-utf16le input.txt output.txt
examples/transcode/bin/Debug/transcode.exe utf16le-to-utf8 output.txt roundtrip.txt
```

On Linux, omit `.exe`. Conversion is strict: malformed UTF-8, isolated UTF-16
surrogates, and odd UTF-16 byte counts are errors. Embedded nulls and Unicode
noncharacters are preserved. No byte-order mark is added or removed: a BOM in
the input remains the same U+FEFF scalar in the output.

The program uses `FileBytes`, owning vectors, caller-buffer `UnicodeTranscoding`,
and explicit little-endian `BinaryPrimitives` operations. It reads the complete
input and builds the converted bytes before opening the output. Invalid input
therefore leaves an existing output file untouched, and input/output may name
the same file. File-writing failures are not an atomic replacement guarantee.

This is a whole-file example, intended for files that fit comfortably in memory.
Exit codes: 0 for success, 1 for conversion or I/O failure, and 2 for invalid
arguments. Diagnostics go to stderr.
