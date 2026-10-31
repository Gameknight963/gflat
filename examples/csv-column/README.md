# Extract one CSV column

Write one selected field from every input record into a one-column CSV file:

```console
gflat build examples/csv-column/csv-column.gfproj
examples/csv-column/bin/Debug/csv-column.exe input.csv 1 output.csv
```

Syntax: `csv-column <input> <zero-based-column-index> <output>`. The index is an
unsigned decimal integer, optionally prefixed with `+`. Headers are treated as
records, and every record must contain the requested column.

The [CSV reader](../../docs/CSV_READER.md) accepts LF and CRLF endings, quoted
commas, doubled quotes, and quoted newlines. Output uses CRLF endings and adds
quotes where needed. Field bytes, including embedded NUL and invalid UTF-8, are
preserved. Empty input produces an empty file.

The complete input is loaded and validated before the output file is opened.
Invalid CSV or a missing column leaves an existing destination unchanged.
Input and output can name the same file. A later write failure can leave the
destination truncated or partially written.

Exit codes are 0 for success, 2 for invalid arguments or CSV, and 1 for IO or
allocation failure. Errors go to stderr.
