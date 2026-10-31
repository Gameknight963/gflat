# Convert CSV to JSON

```console
gflat build examples/csv-to-json/csv-to-json.gfproj --run -- inventory.csv
```

For this CSV input:

```csv
"bolts, large",12
"m4 ""nut""",8
```

the tool writes:

```json
[["bolts, large","12"],["m4 \"nut\"","8"]]
```

## Input and output

Every field becomes a JSON string, including headers and numeric text. Records may have
different field counts. Empty input becomes `[]`. A blank CSV record becomes `[""]`
within the outer array. Quoted commas, quotes, and embedded line endings retain their
decoded values. Embedded NUL is counted data and is escaped in JSON.

## Processing

The complete file is loaded into an owning `String`. `CsvReader` borrows its bytes and
uses `CsvReaderText::CopyValue` to decode fields into one reusable `String`, retaining
capacity between fields. A first pass validates the entire CSV and every decoded field's
UTF-8 before any stdout output. A second pass writes row arrays through `JsonWriter`.
Memory includes the full input and the largest decoded field. Malformed CSV or invalid
UTF-8 produces no output.

A BOM and whitespace inside fields are preserved. Filenames use the host's
narrow-character file APIs. The [CSV reader profile](../../docs/CSV_READER.md) defines
accepted delimiters and line endings. The output JSON uses valid UTF-8 strings.

## Errors

Exit codes: 0 after output is flushed successfully, 2 for usage, malformed CSV, or
invalid UTF-8, and 1 for allocation or IO failures. Diagnostics go to stderr. Output
errors during the second pass may leave partial JSON on stdout. The trailing output
newline follows the host console's standard-stream text mode.
