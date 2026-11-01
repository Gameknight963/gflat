# CSV reader

`Standard::Csv::CsvReader` reads fields from a complete byte buffer.

## Importing counted values

This example sums an inventory's quantity column and prints `20`:

```csharp
using Standard;
using Standard::Text;
using Standard::Csv;

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

Call Read until false to validate every record. Assemble chunked IO input before
constructing the reader.

## Byte-oriented profile

Fields use [RFC 4180](https://www.rfc-editor.org/rfc/rfc4180#section-2) quoting,
with LF accepted alongside CRLF and arbitrary byte values allowed in data.

- Commas separate fields
- A quoted field starts with `"` and escapes a quote as `""`
- Commas and line endings inside quotes remain field data
- Outside quotes, LF or CRLF ends a record. Bare CR fails.
- After a closing quote, a separator, record ending, or EOF must follow

Spaces are data, so ` "x"` and `"x" ` fail. Headers are ordinary records.
The caller chooses column-count and header policy.

| Input | Records |
| --- | --- |
| Empty | None |
| LF or CRLF | One empty field |
| `,` | Two empty fields |
| `a,` | `a` and an empty field |
| Final unterminated fields | One final record |

## Field metadata and decoding

After a successful Read:

| Member | Meaning |
| --- | --- |
| `HasValue` | A current field exists, including an empty one |
| `FieldOffset` | Byte offset of its raw spelling |
| `RawField` | Span including surrounding and doubled quotes |
| `ValueLength` | Decoded byte length |
| `IsQuoted` | Whether the field was enclosed |
| `EndOfRecord` | This is the record's last field |

`TryCopyValue(output, capacity, ref written)` decodes the field without advancing.
Short capacity or no current field returns false, zeros `written`, and preserves
the buffer. Results are counted bytes without a terminator.

## Reusable owning output

`CsvReaderText::CopyValue` decodes into a String and reuses its capacity:

```csharp
using Standard;
using Standard::Text;
using Standard::Csv;

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

An empty field clears the String. No current field throws, and growth failure
preserves its old contents. Keep output separate from the reader's input.

## Reading owning records

The optional `csv_reader_records.gf` module adds `TryReadRecord(ref row)` and
`TryReadRecord(reader, ref row)`. Each successful call fills a `Vector<String>`
with decoded fields, reusing its vector capacity and existing String storage.

```csharp
using Standard::Collections;
using Standard::Csv;
using Standard::Text;

int main()
{
    readonly(char)* input = "name,value\n\"a,b\",12\n";
    CsvReader reader = new(input, CString::Length(input));
    Vector<String> row = new();
    if (!reader.TryReadRecord(ref row) || row.Count != 2) return 1;
    if (!row[0].Equals("name") || !row[1].Equals("value")) return 2;
    if (!reader.TryReadRecord(ref row) || !row[0].Equals("a,b")) return 3;
    if (reader.TryReadRecord(ref row) || row.Count != 0) return 4;
    return 0;
}
```

EOF clears the row and returns false. A blank line yields one empty String, and
a trailing comma yields a final empty field. Owned field values survive reader
reset or changes to the original input.

After a parsing or allocation error, the row contains only fields fully decoded
during that call. The reader has advanced. Its existing failure and `Reset`
rules still apply. Input bytes must be separate from all output String storage.

Call at a record boundary. If a manual `Read()` has left the reader between
fields, the helper throws before changing the row or advancing. `Reset()` returns
to the start of the input. Select `CsvReaderText.gf`, `string_span.gf`, `Vector`,
`String`, and their dependencies along with the record helper.

## Lifetimes, aliases, and errors

Keep input alive and unchanged for the reader and saved RawField spans. Reader
copies have independent positions over those bytes. Buffer decoding requires
output separate from input and reader state. Keep `written` separate too.

Malformed input throws CsvParseException with the offending byte or EOF Offset.
Failure clears current-field metadata and requires Reset before another Read.
Reset restarts the same input.

Sources: `core/Csv/CsvReader.gf`, `CsvParseException.gf`, and `CsvReaderText.gf`
for String output. See [CSV to JSON](../examples/csv-to-json/README.md) for a converter.
