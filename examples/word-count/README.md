# Word count

```console
gflat build examples/word-count/word-count.gfproj
examples/word-count/bin/Debug/word-count.exe input.txt
```

Counts case-insensitive ASCII words in a file. Punctuation, digits, and non-ASCII
bytes separate words. Output order is unspecified because the counts live in a
hash map. This example is intentionally not Unicode word segmentation.

Input is streamed in 4096-byte chunks; words can span chunks. One reusable String
builds the current word. Lookups borrow it; only previously unseen words allocate
map keys. The map owns every key, and file and buffer resources are released on
ordinary return and exceptions. No managed allocations are needed.
