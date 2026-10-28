# RIFF chunk listing

```console
gflat build examples/riff-list/riff-list.gfproj
examples/riff-list/bin/Debug/riff-list.exe input.wav
```

Lists a single little-endian RIFF root and its immediate child chunks. For example:

```text
0000000000000000 "RIFF" 36 "WAVE"
000000000000000c "fmt " 16
0000000000000024 "data" 0
```

Each row gives the chunk header's zero-based byte offset (16 hexadecimal digits),
quoted FourCC, and declared payload byte count (decimal, excluding its header and
pad). The root row also shows the form type. Identifier bytes outside printable
ASCII, plus quote and backslash, appear as lowercase `\xhh` escapes.

The tool checks the root marker and form field, top-level header completeness,
child bounds including the required odd-length pad byte, physical truncation,
and EOF immediately after the declared root. Pad bytes are consumed without
checking their value. Unknown chunks and LIST payloads are skipped as opaque
bytes. This lists container structure; it does not validate WAVE/audio data,
chunk order, nested chunk structure, or format-specific required chunks. RF64,
RIFX/big-endian input and concatenated roots are unsupported.

Payloads and pads are discarded sequentially through `File.SkipExactly`; there
is no seeking. Fixed stack storage bounds memory independently of file/chunk
sizes, without gflat String or managed allocations on success. libc may allocate
its stream buffers. Large payloads still require reading their bytes.

Exit codes: 0 success, 1 malformed/truncated input or file/output error, 2 wrong
argument count. Errors go to stderr with the filename. Listing streams to stdout;
the root and earlier complete chunks can already be printed when a later failure
occurs. A child row is emitted only after its payload and pad have been consumed.
The input closes on success and during unwinding; success flushes stdout. Input
bytes are never modified. On Linux omit the executable's `.exe` suffix.

Format references: Microsoft's [RIFF overview](https://learn.microsoft.com/en-us/windows/win32/xaudio2/resource-interchange-file-format--riff-),
[RIFF services](https://learn.microsoft.com/en-us/windows/win32/multimedia/resource-interchange-file-format-services),
and [AVI RIFF layout](https://learn.microsoft.com/en-us/windows/win32/directshow/avi-riff-file-reference).
