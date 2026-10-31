# Count ASCII words

Print occurrence counts for case-insensitive ASCII words in a file:

```console
gflat build examples/word-count/word-count.gfproj
examples/word-count/bin/Debug/word-count.exe input.txt
```

Punctuation, digits, and non-ASCII bytes separate words. Output order is
unspecified because counts are stored in a hash map.

The file is read in chunks, and words can span chunk boundaries. Memory use
grows with the number and length of distinct words. The example combines
[String](../../docs/STRINGS.md), [ASCII conversion](../../docs/ASCII.md), and
[HashMap](../../docs/HASH_COLLECTIONS.md).

Exit codes are 0 for success, 1 for IO failure, and 2 for invalid arguments.
Results go to stdout and errors to stderr.
