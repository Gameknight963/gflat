# CSV writer

`Standard::Csv::CsvWriter` sends byte fields to an `ITextWriter`:

```csharp
using Standard;
using Standard::Csv;
using Standard::IO;

int main()
{
    FileStream output = File::OpenWrite("inventory.csv");
    CsvWriter writer = new(&output);
    writer.WriteField("bolts, large", 12);
    writer.WriteField("12", 2);
    writer.WriteEndRecord();
    writer.WriteField("m4 \"nut\"", 8);
    writer.WriteField("8", 1);
    writer.WriteEndRecord();
    output.Close();
    return 0;
}
```

The file contains CRLF-terminated records:

```text
"bolts, large",12
"m4 ""nut""",8
```

## Fields and record boundaries

`WriteField(data, count)` inserts commas between fields. Fields containing comma,
quote, CR, or LF are enclosed in quotes, with internal quotes doubled. Spaces,
NULs, and other byte values are preserved.

`WriteEndRecord()` emits CRLF and starts another record. Call it explicitly,
including for the last record when a final line ending is wanted.

| Operations on a fresh writer | Result |
| --- | --- |
| None | Empty document |
| `WriteEndRecord()` | One record with an empty field |
| Two empty fields, then `WriteEndRecord()` | Comma followed by CRLF |
| One empty field without a record ending | Empty bytes, indistinguishable from zero records |

Use a binary FileStream or String destination when exact bytes matter. A Console stream
can translate line endings according to its text mode.

## Write a row of Strings

`WriteRecord(row)` borrows a `Span<immutable(String)>` and writes one
CRLF-terminated record:

```csharp
using Standard::Collections;
using Standard::Csv;
using Standard::Text;

int main()
{
    Vector<String> row = new();
    row.Add(s"bolts, large");
    row.Add(s"12");
    char[64] bytes;
    BufferWriter output = new(bytes, 64);
    CsvWriter writer = new(&output);
    writer.WriteRecord(row.AsImmutable());
    if (!writer.IsAtRecordBoundary || output.Length != 19) return 1;
    return 0;
}
```

An empty span emits CRLF, representing one empty field. Counted String contents
preserve NULs and use the same quoting rules as `WriteField`.

### Row storage

Keep the row span and String owners stable during the call, separate from
storage the destination modifies. Fields go directly to the destination.

### Record state

`IsAtRecordBoundary` is true on a fresh writer and after a successful record
ending. `WriteRecord` requires that state and rejects partial or failed records
before writing. After a manual `WriteField`, finish that record with
`WriteEndRecord()` before calling `WriteRecord`.

## Lifetimes and failures

Keep the destination alive and at the same address. Field input must be separate
from storage the destination may change, such as the same String's buffer.

A destination exception may leave partial CSV and permanently fails this writer.
Later field and record writes throw. Handle the incomplete output before starting
another writer.

Copies share the destination but duplicate comma-position and failure state.
Use one writer per logical CSV stream.

Sources: `core/Csv/CsvWriter.gf`, `core/Csv/csv_writer_records.gf`
