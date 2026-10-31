# List PNG chunks

Print chunk offsets, type names, and payload lengths:

```console
gflat build examples/png-list/png-list.gfproj --run -- input.png
```

```text
0000000000000008 IHDR 13
0000000000000021 IDAT 4
0000000000000031 IEND 0
```

Offsets are zero-based hexadecimal byte positions. Lengths are decimal.
The tool checks the PNG signature, chunk names and lengths, and each chunk's
CRC. It requires an initial 13-byte IHDR and a final empty IEND followed by EOF.
Duplicate IHDR chunks are rejected.

Chunk payloads are otherwise opaque. Use [png-verify](../png-verify/README.md)
to check supported compressed image data. This listing streams through fixed
buffers, so memory use stays constant as the file grows.

Exit codes are 0 for success, 1 for malformed input, CRC, or IO errors, and 2 for
invalid arguments. Errors go to stderr with the path. A later failure can leave
an incomplete listing on stdout.

Format reference: [W3C PNG specification](https://www.w3.org/TR/png/).
