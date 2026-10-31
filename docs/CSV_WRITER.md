# CSV writer

`Standard::Csv::CsvWriter` writes counted byte fields to a borrowed `ITextWriter*`.
It does not build a table or allocate temporary strings. The destination can
allocate or perform IO. The writer itself needs no heap storage.

```csharp
using Standard; using Standard::Csv; using Standard::IO;

int main()
{
    File output = File::OpenWrite("inventory.csv");
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

This writes the following two records to a binary file, with CRLF line endings:

```text
"bolts, large",12
"m4 ""nut""",8
```

## Fields and record boundaries

- `WriteField(data, count)` writes exactly `count` bytes of field data, inserting
  a comma before every field after the first in the current record
- Fields containing comma, double quote, CR, or LF are enclosed in double quotes.
  Each double quote inside a field is doubled. Other fields remain unquoted.
  Leading and trailing spaces are preserved.
- `WriteEndRecord()` writes CRLF and starts a fresh record. It does not flush.
  Call it after the last field when producing a terminated final record.
- These are the bytes passed to the destination. A sink may transform them:
  console standard streams use the host's text mode, which can translate LF on
  Windows. Use a binary `File` or a `String` destination when exact bytes matter.
- NUL and non-ASCII bytes are opaque data, not terminators. There is no UTF-8
  validation, BOM insertion, locale conversion, header inference, or column-count
  enforcement. The emitted quoting is compatible with [CsvReader](CSV_READER.md).
- A zero-length field does not dereference its input pointer. Its empty spelling
  becomes visible through the following comma or record ending.

| Operations on a fresh writer | Output bytes |
| --- | --- |
| No operations | Empty document: zero records. |
| `WriteEndRecord()` | CRLF: one record with one empty field. |
| One empty field, then `WriteEndRecord()` | CRLF: the same one-empty-field record. |
| Two empty fields, then `WriteEndRecord()` | Comma followed by CRLF. |
| One nonempty field, without `WriteEndRecord()` | A final record without a line ending. |
| One empty field, without `WriteEndRecord()` | Empty bytes. Indistinguishable from zero records. |

CSV has no separate spelling for a record containing zero fields. Ending a record
without writing fields therefore emits a one-empty-field record. End a record to
preserve a sole empty field. There is no automatic end-record operation on scope
exit. Repeated `WriteEndRecord()` calls intentionally emit blank records.

## Lifetimes and failures

The destination must remain alive and usable throughout writer use. Input must
remain alive and unchanged during each `WriteField` call. The destination must
not overwrite or invalidate those input bytes while writing: for example, do not
borrow input from the same owning `String` that receives the output. The writer
does not retain field pointers after the call.

Copying a writer duplicates its record position and failure state but shares the
destination. This is not an independent output stream: do not interleave writes
from copies whose comma positions would disagree. Moving preserves the position.
Destroying a writer never writes, flushes, closes, or destroys its destination.

A destination exception propagates unchanged. Some bytes may already have been
written. There is no output rollback. The writer then rejects both `WriteField`
and `WriteEndRecord` with `Exception`, without calling the destination again.
There is no reset operation: start a new writer only after the caller has dealt
with the incomplete output. This state is local to each writer copy. A copy made
before a failure cannot discover another copy's failure.

Contiguous field runs are passed directly to the sink. There is no complete
encoded-field buffer, field-size allocation, implicit flush, or hidden ownership
transfer. Allocation and IO failures in the sink remain ordinary exceptions.
