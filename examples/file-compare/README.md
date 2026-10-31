# Compare binary files

Report the first difference between two files:

```console
gflat build examples/file-compare/file-compare.gfproj --run -- left.bin right.bin
```

Equal files produce no output. A difference produces a row containing its
zero-based hexadecimal offset and the left and right byte values:

```text
byte 000000000000000f 00 ff
length 0000000000001000 EOF 80
```

`byte` means the bytes differ. `length` means one file ended after a matching
prefix, with `EOF` marking that side. Filenames beginning with `-` are accepted
literally.

Comparison streams through fixed buffers, so memory use stays constant as file
size grows. Files are compared as they are read. Concurrent writes can affect
the result.

Exit codes are 0 for equal, 1 for different, and 2 for invalid arguments or IO
failure. Errors go to stderr.
