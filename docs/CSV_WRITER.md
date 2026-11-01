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

## Lifetimes and failures

Keep the destination alive and at the same address. Field input must be separate
from storage the destination may change, such as the same String's buffer.

A destination exception may leave partial CSV and permanently fails this writer.
Later field and record writes throw. Handle the incomplete output before starting
another writer.

Copies share the destination but duplicate comma-position and failure state.
Use one writer per logical CSV stream.

Source: `core/Csv/CsvWriter.gf`
