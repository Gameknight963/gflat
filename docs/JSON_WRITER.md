# JSON writer

`Standard::Json::JsonWriter` writes a UTF-8 document through `ITextWriter`:

```csharp
using Standard::Text;
using Standard;
using Standard::Json;

String output = s"";
JsonWriter writer = new(&output);
writer.WriteStartObject();
writer.WritePropertyName("name");
writer.WriteString("Ada");
writer.WritePropertyName("scores");
writer.WriteStartArray();
writer.WriteNumber(42);
writer.WriteBoolean(true);
writer.WriteNull();
writer.WriteEndArray();
writer.WriteEndObject();
writer.Complete();
// output contains {"name":"Ada","scores":[42,true,null]}
```

Construct with `new(destination, true)` for two-space indentation and LF line
breaks. Compact output is the default. Empty containers stay inline, and output
has no trailing newline.

## Operations

| Operation | Effect |
| --- | --- |
| `WriteStartObject`, `WriteStartArray` | Opens a container |
| `WriteEndObject`, `WriteEndArray` | Closes the current matching container |
| `WritePropertyName(text)` or `(data, length)` | Writes a quoted UTF-8 name. A value must follow. |
| `WriteString(text)` or `(data, length)` | Writes quoted UTF-8 contents |
| `WriteBoolean(value)`, `WriteNull()` | Writes the JSON literal |
| `WriteNumber(value)` | Writes an integer through 128-bit `extralong` |
| `WriteRawNumber(data, length)` | Validates and preserves an exact JSON number token |
| `Complete()` | Checks that one root was written and all containers are closed |

Commas and colons are automatic. Counted names and string values can include
NUL through escapes. Strings follow [JsonString](JSON_STRING.md) rules. Duplicate
property names are allowed. Raw numbers can retain large values, exponents, and
negative zero without conversion to a machine numeric type.

Call Complete explicitly. It writes nothing, can succeed repeatedly, and lets
you supply missing values or closing delimiters after an incomplete-document error.

## Failure and ownership

Ordering errors, invalid text or numbers, and incomplete documents throw
JsonWriteException before writing that operation. Nesting allocation also happens
before output. Correct these conditions and retry.

A destination exception may leave partial JSON and permanently fails the writer.
Later writes and Complete throw JsonWriteException. Handle the partial output
before starting another document.

Keep the destination alive and at the same address. Input must be separate from
storage it may modify, such as writing a String's borrowed bytes back into itself.
JsonWriter is move-only. Finish its document and flush the concrete destination
explicitly as needed.

Source: `core/Json/JsonWriter.gf`
