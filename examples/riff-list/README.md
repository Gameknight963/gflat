# List RIFF chunks

List a little-endian RIFF root and its immediate child chunks:

```console
gflat build examples/riff-list/riff-list.gfproj
examples/riff-list/bin/Debug/riff-list.exe input.wav
```

```text
0000000000000000 "RIFF" 36 "WAVE"
000000000000000c "fmt " 16
0000000000000024 "data" 0
```

Rows contain the zero-based hexadecimal header offset, quoted FourCC, and
decimal payload length. The root row also shows its form type. Identifier
bytes outside printable ASCII, plus quotes and backslashes, use `\xhh` escapes.

The tool checks headers, child bounds, required odd-length padding, and EOF
after the declared root. Nested LIST payloads remain opaque. RF64, big-endian
RIFX, and concatenated roots are unsupported.

Payloads are read sequentially through fixed buffers. Memory use stays constant
as file size grows.

Exit codes are 0 for success, 1 for malformed input or IO errors, and 2 for
invalid arguments. Errors go to stderr with the path. Earlier rows may already
have been written when a later chunk fails.

Format reference: [Microsoft RIFF overview](https://learn.microsoft.com/en-us/windows/win32/xaudio2/resource-interchange-file-format--riff-).
