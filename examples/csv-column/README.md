# Extract one CSV column

```console
gflat build examples/csv-column/csv-column.gfproj
examples/csv-column/bin/Debug/csv-column.exe input.csv 1 output.csv
```

Syntax: `csv-column <input> <zero-based-column-index> <output>`. Column zero is
the first field. The index uses the standard unsigned decimal parser: digits
with an optional leading `+`, without whitespace, separators, signs `-`, radix
prefixes, or trailing bytes. Values exceeding the platform's `nuint` are rejected.
On Linux, prefix the executable path with `./`; the default project output still
uses `.exe`.

Each input record contributes one output record containing the selected field.
Headers are ordinary records. Ragged records are accepted when every record has
the requested column. A short record is an error. An empty document produces an
empty output for any valid index; an empty record contains one empty field.

Input and output use binary Files. The current `CsvReader` accepts LF or CRLF
record endings and quoted commas, doubled quotes, and embedded CR/LF. Bare CR
outside a quoted field is invalid. Empty trailing fields and a final record
without a line ending are supported. Spaces are field data, and a quote must
start a quoted field or be doubled inside it. No headers, comments, delimiters,
or schemas are guessed.

The selected fields are decoded and re-encoded as valid one-column CSV. The
writer uses CRLF record endings and quotes fields containing comma, quote, CR,
or LF, doubling embedded quotes. Counted field bytes are preserved, including
NUL and invalid UTF-8; the tool does not decode text, trim, or remove a BOM.
Original quoting and input record endings are not preserved.

The entire input is read and closed before processing. A first pass validates
all CSV syntax, checks the requested column in every record, and copies selected
fields into a reusable String. Only after that succeeds is the output opened.
Malformed data, missing columns, invalid indices, and input errors therefore
leave an existing output file unchanged. Memory use includes the whole input
and the largest selected field's buffer capacity; there is no GC requirement.

The second pass writes through `CsvWriter` into the binary output File. It
closes explicitly to report delayed write errors. Output failures can leave a
partial or truncated destination; this is not an atomic file replacement. Input
and output may name the same file because reading and validation finish first.
Diagnostics go to stderr, with no records written to stdout. Paths follow the
host libc's narrow-character filename encoding.

Exit codes: 0 for success, 2 for usage/index/CSV/missing-column errors, and 1 for
IO, allocation, or output failures. Output-open/write/close failures are reported
separately from input and validation failures.
