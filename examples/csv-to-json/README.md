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

Every field becomes a JSON string. There is no header detection, numeric parsing,
column naming, or schema inference. Records may have different field counts.
Empty input becomes `[]`; a blank CSV record becomes `[""]` within the outer
array. Quoted commas, quotes, and embedded line endings retain their decoded
values. Embedded NUL is counted data and is escaped in JSON.

The complete file is loaded into an owning `String`. `CsvReader` borrows its
bytes and uses `CsvReaderText::CopyValue` to decode fields into one reusable
`String`, retaining capacity between fields. A first pass validates the entire CSV
and every decoded field's UTF-8
before any stdout output. A second pass writes row arrays through `JsonWriter`.
This avoids a table of allocated field strings while keeping malformed CSV and
invalid UTF-8 from producing partial JSON. It does not stream an unbounded file;
memory includes the full input and the largest decoded field.

No BOM is stripped, no encoding conversion is performed, and whitespace inside
fields is preserved. Filenames use the host's narrow-character file APIs. The
[CSV reader profile](../../docs/CSV_READER.md) defines accepted delimiters and
line endings; the output JSON uses valid UTF-8 strings.

Exit codes: 0 after output is flushed successfully, 2 for usage, malformed CSV,
or invalid UTF-8, and 1 for allocation or IO failures. Diagnostics go to stderr.
Output errors during the second pass may leave partial JSON on stdout; there is
no output rollback. The trailing output newline follows the host console's
standard-stream text mode.
