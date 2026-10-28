# Streaming binary file comparison

```console
gflat build examples/file-compare/file-compare.gfproj --run -- left.bin right.bin
```

Compares exactly two literal filenames, including names beginning with `-`.
There are no options or stdin mode. Exit codes are 0 for equal, 1 for different,
and 2 for usage, open/read/close, or output errors. Equal files print nothing.
Usage and IO diagnostics go to stderr; inputs are never modified. If reporting a
diagnostic itself fails, status 2 is retained even when its text cannot be written.

The first difference produces one ASCII row. Offsets are zero-based, with 16
lowercase hexadecimal digits. The two final tokens describe left then right:
two lowercase hexadecimal digits for a byte, or `EOF` when that file has ended.

```text
byte 000000000000000f 00 ff
length 0000000000001000 EOF 80
```

`byte` means the bytes differ; `length` means the bytes matched up to the shorter
file's end. The tool does not scan the longer tail or report both total lengths.
Earlier byte differences take precedence over later length differences.

Two 4096-byte stack buffers are viewed through borrowed spans and compared with
`CommonPrefixLength<byte>`. Reads use File's binary mode: NUL, high bytes, CRLF,
and Ctrl+Z are ordinary data. `File.Read` reports actual IO failures by throwing;
a short read without an error is EOF. Comparison uses only the returned bytes.
Both chunks are read before comparison, so an error while filling either chunk
can be reported even if earlier bytes in that chunk differ.

No seeking, whole-file loading, String/Vector/GC storage or gflat application
allocation is needed on normal equal/different paths; libc may allocate its
stream buffers. Fixed scratch also formats the one result row. Output is checked
and explicitly flushed before returning a difference. An output error can leave
a partial row and returns 2; no rollback is promised.

Files are compared as observed, without an atomic snapshot or a concurrent-change
guarantee. File metadata, encoding, permissions, directories and named pipes are
not interpreted; special streams may block according to their ordinary IO rules.
