# CSV reader

`Standard::Csv::CsvReader` reads fields from a borrowed byte buffer.
`Read()` emits one validated field at a time. `EndOfRecord` identifies a record's
last field. Read through `false` to validate the whole input. Stopping early does
not validate its remaining suffix.

## Importing counted values

This complete program sums the quantity column of a two-column inventory. Quoted
names may contain commas or doubled quotes. Only the numeric column is decoded
into a caller-owned buffer. Numeric parsing and column-count policy remain explicit.

```csharp
using Standard; using Standard::Text; using Standard::Csv;

int main()
{
    readonly(char)* document = "\"bolts, large\",12\r\n\"m4 \"\"nut\"\"\",8\r\n";
    CsvReader reader = new(document, CString::Length(document));
    char[32] number;
    nuint column = 0;
    extralong total = 0;
    while (reader.Read())
    {
        if (column >= 2) throw new Exception("Expected two CSV columns");
        if (column == 1)
        {
            nuint written = 0;
            if (!reader.TryCopyValue(number, 32, ref written))
                throw new Exception("Quantity exceeds caller buffer");
            long quantity = 0;
            if (!TryParseLong(number, written, &quantity))
                throw new Exception("Invalid quantity");
            total += (extralong)quantity;
        }
        column++;
        if (reader.EndOfRecord)
        {
            if (column != 2) throw new Exception("Expected two CSV columns");
            column = 0;
        }
    }
    total.Format(Console::Out);
    Console::WriteLine();
    Console::Flush();
    return 0;
}
```

It prints `20`. The 32-byte quantity limit is this caller's choice. The reader
does not impose a fixed field length. This is a complete-buffer reader: input
split across IO chunks must first be assembled by the caller.

## Byte-oriented profile

The quoting rules follow [RFC 4180, section 2](https://www.rfc-editor.org/rfc/rfc4180#section-2),
with explicit byte-oriented extensions: LF record separators are accepted alongside
CRLF, and field data need not be ASCII. It does not implement the RFC's exact
ASCII-only `TEXTDATA` grammar.

- Commas separate fields. Outside quotes, CRLF or LF ends a record. Bare CR is
  rejected, with the error pointing at the expected LF position or end of input.
- A field may start with `"`. Inside it, `""` represents one quote. Commas and
  arbitrary CR/LF bytes are data. Embedded line endings are preserved exactly.
- After a closing quote, only comma, a record ending, or EOF is accepted. A quote
  in an unquoted field is an error. Spaces are data, not ignored padding around
  enclosure: ` "x"` and `"x" ` are invalid.
- Other bytes, including NUL and non-ASCII values, are opaque counted data. No
  UTF-8 validation, BOM stripping, locale rules, normalization, comment syntax, or
  delimiter guessing is performed.
- Headers are ordinary records. The caller decides whether to skip a header or
  require equal field counts. Empty fields and blank records are retained.

| Input bytes | Records |
| --- | --- |
| Empty input | Zero records. |
| LF or CRLF | One record with one empty field. |
| `,` | One record with two empty fields. |
| `a,` | One record containing `a` and an empty field. |
| `a` followed by LF | One record containing `a`. No extra record. |
| `a` followed by two LFs | A record containing `a`, then a one-empty-field record. |
| `""` | One record with one quoted empty field. |

A terminal comma introduces an empty final field. An empty file means zero
records. Use a quoted empty field or a record ending to represent one empty value.
The final record need not have a line ending.

## Field metadata and decoding

After a successful `Read()`:

- `HasValue` is true, including for a valid empty field
- `FieldOffset` is the raw field's byte offset in the original input
- `RawField` is a borrowed `Span<readonly(char)>` over the exact lexeme, including
  surrounding quotes and doubled quotes. It excludes the following separator.
- `ValueLength` counts decoded bytes: outer quotes are removed and doubled quotes
  count once. It is independent of NUL bytes within the value.
- `IsQuoted` identifies an enclosed field. `EndOfRecord` is true for the last field
  of a record, including a final record without a line ending.

`TryCopyValue(output, capacity, ref written)` decodes the current field into a
caller-owned buffer. It writes counted bytes, without a terminator. It is
`immutable` and `nothrow`, does not allocate, and does not advance the reader.
Repeated copies produce the same value. Insufficient capacity returns false,
sets `written` to zero, and leaves all output bytes unchanged. A valid empty field
succeeds with capacity zero without dereferencing output.

Before the first read, after completion, after `Reset`, and after a failed read,
there is no current field. `TryCopyValue` then returns false with `written` zero.
Metadata is cleared: offsets and lengths are zero, booleans false, and `RawField`
is a canonical empty span with null `Data`. A valid empty unquoted field also has
an empty `RawField`. `HasValue` distinguishes it from no current field.

## Reusable owning output

The optional `CsvReaderText` convenience decodes into an initialized `String`,
reusing its capacity between fields:

```csharp
using Standard; using Standard::Text; using Standard::Csv;
int main()
{
    readonly(char)* input = "first,\"two\"\"quotes\"";
    CsvReader reader = new(input, CString::Length(input));
    String field = new();
    int count = 0;
    while (reader.Read())
    {
        CsvReaderText::CopyValue(&reader, ref field);
        count++;
    }
    return count - 2;
}
```

`CopyValue` does not advance the reader or validate an encoding. It preserves
embedded NUL bytes and maintains the String's trailing terminator. Empty fields
clear the string while retaining capacity. Calls without a current field throw
before changing the output. Allocation failure during growth also preserves its
old bytes, length, capacity, and allocation. Output storage must be separate from
the reader and its entire borrowed input, as with `TryCopyValue`.

For selective builds using this convenience, include `Csv/CsvReaderText.gf`
and the String dependencies.

## Lifetimes, aliases, and errors

The caller must keep the entire input alive and unchanged throughout reader use.
A saved `RawField` borrows the input independently of the reader: advancing,
resetting, copying, moving, or destroying reader metadata does not free or modify
those bytes. It does not prolong their lifetime. Copying a reader duplicates its
cursor state and allows independent advancement. It does not copy input bytes.

The destination buffer must be disjoint from the entire input and from reader
storage. The `written` variable must be separate from input, reader storage, and
the output buffer. In-place or overlapping decoding is not supported by this
reader: mutating input would invalidate borrowed fields and repeated decoding.

Malformed CSV throws `CsvParseException`, whose readonly `Offset` identifies the
unexpected byte or EOF. A failed read clears the current field and latches failure.
Another `Read` throws until `Reset()` restarts the same input. `Reset` does not
change its storage or contents. Successful `Read` and `TryCopyValue` calls allocate nothing.
Constructing or throwing an exception uses the ordinary exception runtime.

For explicit source selection include `core/Csv/CsvReader.gf`,
`core/Csv/CsvParseException.gf`, and `Memory/Span.gf` with its query-interface dependencies.
The complete standard library includes these files automatically.

See [CSV to JSON](../examples/csv-to-json/README.md) for a complete converter
using a reusable decoded field buffer, and [CsvWriter](CSV_WRITER.md) for
writing counted fields through an `ITextWriter`.
