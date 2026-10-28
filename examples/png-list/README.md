# PNG chunk and CRC listing

```console
gflat build examples/png-list/png-list.gfproj --run -- input.png
```

Inspects PNG chunk framing and CRCs without decoding an image. Rows contain the
zero-based chunk-header offset (16 lowercase hexadecimal digits), four ASCII type
letters, and decimal payload byte length, separated by spaces. For example:

```text
0000000000000008 IHDR 13
0000000000000021 IDAT 4
0000000000000031 IEND 0
```

The example rows illustrate offsets, not a complete image-conformance verdict.
The tool checks signature, length/type framing, per-chunk type+payload CRCs,
initial IHDR/13 and final IEND/0. It rejects duplicate IHDR and trailing bytes.

This is not a full PNG conformance checker: IHDR fields, required IDAT data,
PLTE/IDAT/ancillary ordering, unknown critical semantics, APNG metadata, zlib,
filters and pixels are not validated. A CRC-correct IHDR/IEND-only stream is
accepted by this inspector even though it is not a complete conforming PNG image.
Other chunks, including unknown critical types, are listed as opaque data.

No seeking or file-size snapshot is used. Payloads stream through a 4096-byte
stack buffer into existing CRC32. A separate 4096-byte BufferedWriter scratch
batches rows; fixed row/offset scratch bounds formatting. No gflat String, GC or
application allocations are needed on success; libc may allocate its buffers.

Exit codes are 0 for a successful listing, 1 for framing/CRC/read/close/output
failure, and 2 for wrong argument count. Usage and failures go to stderr; errors include the path and the caught IO
message when available, distinguishing CRC errors, truncation and open failure. A single
argument is always a literal filename, including an empty or option-like name;
there is no stdin mode or option parser. Input is never modified.

A row is enqueued only after the chunk and stored CRC have been read and matched;
IEND also requires immediate EOF. Output is streaming, not transactional. On a
later failure, already drained bytes may remain, while pending verified rows in
BufferedWriter scratch are discarded. The visible prefix may end partway through
a row; there is no rollback or guaranteed prefix length. Success explicitly
closes input, drains the BufferedWriter and flushes stdout. Unwinding closes the
input; the buffer's destructor performs no output.

The wire rules follow the [W3C PNG Third Edition](https://www.w3.org/TR/png/),
sections 5.2?5.5 (structure/type/CRC), 7.1 (byte order), and 11.2.1/11.2.4
(IHDR/IEND). The additional exact-file-EOF requirement is this tool's policy.
