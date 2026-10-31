# Convert CSV to JSON

Convert CSV records into arrays of JSON strings:

```console
gflat build examples/csv-to-json/csv-to-json.gfproj --run -- inventory.csv
```

For this input:

```csv
"bolts, large",12
"m4 ""nut""",8
```

the tool writes:

```json
[["bolts, large","12"],["m4 \"nut\"","8"]]
```

Every field becomes a string, including headers and numeric text. Rows may
have different field counts. Empty input produces `[]`, and a blank record
produces `[""]` within the outer array. Quoted commas, quotes, embedded
newlines, and NUL bytes retain their values.

The complete file is loaded and checked for valid CSV and UTF-8 before output.
Memory use includes the file and one reusable decoded-field buffer. See the
[CSV reader profile](../../docs/CSV_READER.md) for quoting and line endings.

Exit codes are 0 for success, 2 for invalid arguments, CSV, or UTF-8, and 1 for
IO or allocation failure. Errors go to stderr. An output failure can leave
partial JSON on stdout.
