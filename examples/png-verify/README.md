# png-verify

Checks PNG chunk integrity and the image-data path for noninterlaced 8-bit RGB and RGBA images. It does not render, unfilter pixels, extract files, or modify its input.

```powershell
gflat build examples/png-verify/png-verify.gfproj --run -- image.png
```

A successful RGB input prints `Verified RGB8 image data: <width> x <height>` (RGBA8 for alpha). Exit codes are 0 for verified supported image data, 1 for invalid/unsupported input, resource limits, or I/O failures, and 2 for incorrect arguments. Nothing is printed to standard output before complete verification; failures go to standard error.

The command checks the signature, every physical chunk CRC, bounded lengths/names, IHDR and critical chunk ordering, optional suggested PLTE, consecutive IDAT chunks, empty final IEND, and exact physical EOF. IDAT contents are joined without their chunk headers/CRCs and decoded as zlib; the decoded length must exactly match the expected scanlines, including one valid filter byte per row. No sample unfiltering is needed for this check because every 8-bit RGB/RGBA sample-byte pattern has a defined inverse under filter types 0-4.

Unused bytes following the first complete zlib stream within the joined IDAT data are ignored, including a second compressed stream. Their enclosing PNG chunk CRCs are still checked. This follows PNG decoder tolerance; it does not permit bytes after IEND or nonconsecutive IDAT chunks. No preset dictionary is accepted for PNG image data. These format rules come from the [W3C PNG specification](https://www.w3.org/TR/png-3/), especially sections 5, 9-11 and 13.

The supported scope is image data and core structure, not complete ancillary metadata conformance. Ordinary ancillary payloads are opaque: their CRC is checked, but text, embedded profiles, color-management fields, their detailed ordering, and other metadata semantics are not certified. Legal grayscale, indexed, 16-bit, and interlaced modes are unsupported. APNG animation chunks, unknown critical chunks, and future reserved chunk-name versions are also unsupported. This example is not a general PNG decoder or conformance checker.

Input is limited to 32 MiB and filtered output to 64 MiB. Dimension arithmetic is checked before allocation. The owned file image, joined IDAT bytes, and decoded buffer use at most roughly 128 MiB of payload storage plus fixed scratch/allocator overhead. The first pass checks the container; the second joins the already-validated IDAT run (and rechecks those chunks). One bounded zlib decode and a row-tag scan follow. Arbitrarily split and zero-length IDAT chunks are supported.
