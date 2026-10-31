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

## Output

Each row gives the chunk header's zero-based byte offset (16 hexadecimal digits), quoted
FourCC, and declared payload byte count (decimal, excluding its header and pad). The
root row also shows the form type. Identifier bytes outside printable ASCII, plus quote
and backslash, appear as lowercase `\xhh` escapes.

## Format checks

The tool checks the root marker and form field, top-level header completeness, child
bounds including the required odd-length pad byte, physical truncation, and EOF
immediately after the declared root. Pad bytes are consumed without checking their
value. Unknown chunks and LIST payloads are skipped as opaque bytes. The listing covers
top-level container structure. WAVE/audio data, chunk order, nested structure, and
format-specific required chunks are outside that check. RF64, RIFX/big-endian input, and
concatenated roots are unsupported.

Payloads and pads are read sequentially through `File.SkipExactly`. Fixed stack buffers
bound memory independently of file and chunk sizes. Libc supplies its own stream
buffering. Large payloads still require reading their bytes.

## Errors

Exit codes: 0 success, 1 malformed/truncated input or file/output error, 2 wrong
argument count. Errors go to stderr with the filename. Listing streams to stdout. The
root and earlier complete chunks can already be printed when a later failure occurs. A
child row is emitted only after its payload and pad have been consumed. The input closes
on success and during unwinding. Success flushes stdout. On Linux, prefix the executable
path with `./`. The default project output uses `.exe`.

Format references: Microsoft's [RIFF
overview](https://learn.microsoft.com/en-us/windows/win32/xaudio2/resource-interchange-file-format--riff-),
[RIFF
services](https://learn.microsoft.com/en-us/windows/win32/multimedia/resource-interchange-file-format-services),
and [AVI RIFF
layout](https://learn.microsoft.com/en-us/windows/win32/directshow/avi-riff-file-reference).
