# Sum a CSV column

```console
gflat build examples/csv-sum/csv-sum.gfproj --run -- inventory.csv 1
```

For this headerless file, column 1 sums to `20`:

```csv
"bolts, large",12
"m4 ""nut""",8
```

## Input

The column index is zero-based. Every record must contain that column, which must decode
to a strict signed 64-bit decimal integer. Quoted numbers work. Whitespace remains part
of the field and is rejected by the numeric parser. The decoded numeric field has a
64-byte limit, including any sign or leading zeroes. Other fields have no tool-specific
length limit and can contain quoted commas, doubled quotes, or newlines. Headers are
treated as records. Records may have different widths if they all contain the selected
column. Empty input sums to zero.

## Storage

The tool loads the complete file into one owning String. CsvReader borrows those counted
bytes and reuses a stack buffer for numeric fields, without allocating individual field
strings. A signed 128-bit accumulator keeps the sum exact: the number of records is
bounded by the pointer-sized input length, and each selected value fits in 64 bits. It
prints only after all input is validated.

The reader accepts LF and CRLF record endings and preserves quoted embedded endings.
Input is opaque bytes, including a BOM. Filenames use the host's narrow-character file
APIs. See [CSV reader](../../docs/CSV_READER.md) for its exact profile and borrowing
rules.

## Errors

Exit codes: 0 after successful output flush, 2 for usage or invalid data, and 1 for
allocation or IO failures. Diagnostics go to stderr. Output failure may leave a partial
number on stdout.
