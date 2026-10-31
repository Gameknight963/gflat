# PNG chunk and CRC listing

```console
gflat build examples/png-list/png-list.gfproj --run -- input.png
```

Lists PNG chunks and checks their framing and CRCs. Rows contain the zero-based
chunk-header offset (16 lowercase hexadecimal digits), four ASCII type letters, and
decimal payload byte length, separated by spaces. For example:

```text
0000000000000008 IHDR 13
0000000000000021 IDAT 4
0000000000000031 IEND 0
```

## Checks

- PNG signature, chunk lengths, and type names
- CRC of each chunk's type and payload
- Initial IHDR with a 13-byte payload
- Final empty IEND followed by EOF
- Rejection of duplicate IHDR chunks

Other payloads, including unknown critical chunks, are listed as opaque bytes. IHDR
fields, image data, and chunk ordering beyond IHDR/IEND are outside this check. Use
[png-verify](../png-verify/README.md) to check supported image data.

## Storage

Payloads stream through a 4096-byte stack buffer into CRC32. A separate 4096-byte
BufferedWriter scratch buffer batches rows. Libc supplies its own stream buffering.

## Errors

Exit codes are 0 for a successful listing, 1 for framing/CRC/read/close/output failure,
and 2 for wrong argument count. Usage and failures go to stderr. Errors include the path
and caught IO message when available. A single argument is always a literal filename,
including an empty or option-like name.

A row is enqueued only after the chunk and stored CRC have been read and matched. IEND
also requires immediate EOF. On a later failure, already drained bytes may remain, while
pending verified rows in BufferedWriter scratch are discarded. The visible prefix may
end partway through a row. Success explicitly closes input, drains the BufferedWriter
and flushes stdout. Unwinding closes the input. Buffered output is discarded during
unwinding.

The wire rules follow the [W3C PNG Third Edition](https://www.w3.org/TR/png/), sections
5.2–5.5 (structure/type/CRC), 7.1 (byte order), and 11.2.1/11.2.4 (IHDR/IEND). The
additional exact-file-EOF requirement is this tool's policy.
