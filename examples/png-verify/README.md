# Verify PNG image data

Check PNG chunk integrity and compressed image data for noninterlaced 8-bit RGB
and RGBA images:

```powershell
gflat build examples/png-verify/png-verify.gfproj --run -- image.png
```

Success prints `Verified RGB8 image data: <width> x <height>`, or `RGBA8` for
alpha. Verification finishes before output.

## Checks and limits

The tool checks chunk CRCs, IHDR and critical chunk ordering, optional PLTE,
consecutive IDAT chunks, and an empty IEND followed by EOF. It decompresses the
joined IDAT stream and checks the scanline length and each row's filter tag.
Unused bytes after the first complete zlib stream inside IDAT are tolerated.

Ancillary payloads remain opaque after CRC validation. Supported images use
8-bit RGB or RGBA without interlacing. Other color modes, APNG animation chunks,
unknown critical chunks, and reserved chunk-name versions are rejected.

Input is limited to 32 MiB, and filtered output to 64 MiB. File, compressed, and
decoded buffers can together hold roughly 128 MiB, plus allocator overhead.

## Errors

Exit codes are 0 for success, 1 for invalid or unsupported input, resource
limits, or IO failure, and 2 for invalid arguments. Errors go to stderr.

Format reference: [W3C PNG specification](https://www.w3.org/TR/png-3/).
