# png-verify

Verify PNG chunk integrity and compressed image data for noninterlaced 8-bit RGB and
RGBA images.

```powershell
gflat build examples/png-verify/png-verify.gfproj --run -- image.png
```

Success prints `Verified RGB8 image data: <width> x <height>`, or `RGBA8` for alpha.

## Checks

- PNG signature and every chunk's CRC, length, and name
- IHDR, critical chunk ordering, optional suggested PLTE, and consecutive IDAT chunks
- Empty final IEND followed by physical EOF
- Zlib decoding of the joined IDAT contents
- Exact decoded scanline length and a valid filter byte for each row

Every 8-bit RGB/RGBA sample-byte pattern has a defined inverse under filters 0–4, so
checking filter tags is sufficient here. Pixels remain in filtered form.

Unused bytes after the first complete zlib stream in IDAT are ignored, including a
second compressed stream. Their enclosing chunk CRCs are still checked. PNG image data
must use zlib without a preset dictionary. The format rules follow the [W3C PNG
specification](https://www.w3.org/TR/png-3/), sections 5, 9–11, and 13.

## Supported input and limits

Ancillary payloads remain opaque after CRC validation. Detailed text, profile,
color-management, and other metadata semantics are outside this check.

Unsupported inputs:

- Grayscale, indexed, 16-bit, and interlaced images
- APNG animation chunks
- Unknown critical chunks and reserved chunk-name versions

| Limit | Maximum |
| --- | ---: |
| Input file | 32 MiB |
| Filtered output | 64 MiB |

Dimension arithmetic is checked before allocation. The file image, joined IDAT bytes,
and decoded buffer use roughly 128 MiB of payload storage at most, plus scratch and
allocator overhead. Arbitrarily split and empty IDAT chunks work.

## Errors

- 0 for verified image data and successful output
- 1 for invalid or unsupported input, resource limits, or I/O failures
- 2 for incorrect arguments

Verification finishes before output. Failures go to stderr.
