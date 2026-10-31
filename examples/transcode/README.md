# Convert UTF-8 and UTF-16LE files

Convert a file between UTF-8 and little-endian UTF-16:

```console
gflat build examples/transcode/transcode.gfproj
examples/transcode/bin/Debug/transcode.exe utf8-to-utf16le input.txt output.txt
examples/transcode/bin/Debug/transcode.exe utf16le-to-utf8 output.txt roundtrip.txt
```

Malformed UTF-8, unpaired UTF-16 surrogates, and odd UTF-16 byte counts are
errors. Embedded NULs, noncharacters, and a BOM retain their scalar values.

The complete input is validated and converted before opening the output file.
Invalid input leaves an existing destination unchanged. Input and output can
name the same file. A later write failure can leave the destination truncated
or partially written. Memory use includes both complete files.

Exit codes are 0 for success, 1 for conversion or IO failure, and 2 for invalid
arguments. Errors go to stderr.
