# Streaming binary file comparison

```console
gflat build examples/file-compare/file-compare.gfproj --run -- left.bin right.bin
```

Compares exactly two literal filenames, including names beginning with `-`. Exit codes
are 0 for equal, 1 for different, and 2 for usage, open/read/close, or output errors.
Equal files print nothing. Usage and IO diagnostics go to stderr. If reporting a
diagnostic itself fails, status 2 is retained even when its text cannot be written.

## Output

The first difference produces one ASCII row. Offsets are zero-based, with 16 lowercase
hexadecimal digits. The two final tokens describe left then right: two lowercase
hexadecimal digits for a byte, or `EOF` when that file has ended.

```text
byte 000000000000000f 00 ff
length 0000000000001000 EOF 80
```

`byte` means the bytes differ. `length` means the bytes matched up to the shorter file's
end. Comparison stops at the first difference or EOF. Earlier byte differences take
precedence over later length differences.

## Reading and storage

Two 4096-byte stack buffers are viewed through borrowed spans and compared with
`CommonPrefixLength<byte>`. Reads use File's binary mode: NUL, high bytes, CRLF, and
Ctrl+Z are ordinary data. `File.Read` throws on IO failure. A short read without an
error is EOF. Comparison uses only the returned bytes. Both chunks are read before
comparison, so an error while filling either chunk can be reported even if earlier bytes
in that chunk differ.

Comparison and result formatting use fixed stack buffers. Libc may allocate its stream
buffers. Output is checked and explicitly flushed before returning a difference. An
output error can leave a partial row and returns 2.

Files are compared as they are read. Concurrent writes can affect the result. Special
streams may block according to their ordinary IO rules.
