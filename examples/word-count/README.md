# Word count

```console
gflat build examples/word-count/word-count.gfproj
examples/word-count/bin/Debug/word-count.exe input.txt
```

Counts case-insensitive ASCII words in a file. Punctuation, digits, and non-ASCII bytes
separate words. Output order is unspecified because the counts live in a hash map.

## Storage

Input is streamed in 4096-byte chunks. Words can span chunks. One reusable String builds
the current word. Lookups borrow it. Only previously unseen words allocate map keys. The
map owns every key, and file and buffer resources are released on ordinary return and
exceptions.

Output writes borrowed key bytes and formats each count directly to the console.

## Output and errors

Count rows go to stdout, and usage and IO diagnostics go to stderr. The tool flushes
stdout before reporting success. Exit codes are 0 on success, 1 on input or output IO
failure, and 2 for invalid arguments. Output failures are reported separately from input
failures.
