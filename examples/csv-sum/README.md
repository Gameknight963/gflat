# Sum a CSV column

Add the signed 64-bit decimal values in one CSV column:

```console
gflat build examples/csv-sum/csv-sum.gfproj --run -- inventory.csv 1
```

Column 1 in this file sums to `20`:

```csv
"bolts, large",12
"m4 ""nut""",8
```

The column index is zero-based. Every record, including a header if present,
must contain a valid integer in that column. Quoted numbers work. Whitespace
inside a field is retained and causes numeric parsing to fail. A decoded number
can contain at most 64 bytes, including its sign and leading zeroes.

The tool loads the complete file. It validates every record before printing an
exact 128-bit sum. Empty input sums to zero. See the [CSV reader profile](../../docs/CSV_READER.md)
for accepted quoting and line endings.

Exit codes are 0 for success, 2 for invalid arguments or data, and 1 for IO or
allocation failure. Errors go to stderr.
