# Select CSV columns by name

Select and reorder columns while keeping their header:

```console
gflat build examples/csv-select/csv-select.gfproj --run -- input.csv output.csv quantity name
```

Syntax: `csv-select <input> <output> <column-name>...`.

Names match header bytes exactly and are case-sensitive. Each requested name must
appear exactly once in the header. Repeat a requested name to repeat its column
in the output. Every data record must have the same field count as the header.
Input may use LF or CRLF, quoted commas,
doubled quotes, and multiline fields. Output uses CRLF and quotes where needed.
Field bytes, including NUL and high bytes, are preserved.

The tool reads at most 64 MiB and validates the complete input before creating a
new output file. Existing output is preserved. A later IO or allocation failure
can leave a partial new file.

Argument bytes and header bytes must use the same encoding. A BOM is part of the
first header's bytes. Windows narrow arguments use the process's startup encoding.

Exit codes are 0 for success, 2 for usage, data, or initial size-limit errors, and
1 for IO or allocation errors. Diagnostics go to stderr.
