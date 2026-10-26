# JSON writer

`std::Json::JsonWriter` writes one UTF-8 JSON document to a borrowed
`ITextWriter*`. It owns its nesting stack, not the destination. It never closes,
flushes, or destroys the destination, and destruction writes nothing. Keep the
destination alive and at a stable address until the writer is no longer used.

```csharp
using std::Text;
using std; using std::Json;

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

`JsonWriter(destination, bool indented = false)` keeps compact output as the
default. Pass `true` to emit two-space indentation and LF newlines to the destination.
Array items and object properties start on their own indented lines. Nonempty
containers put their closing delimiter on a line at their parent's depth; empty
`[]` and `{}` remain inline. Indented properties use one space after the colon.
Root scalar values receive no extra whitespace, and no final newline is appended.
There are no locale, custom indentation, or newline options. Indentation writes
directly to the destination without buffering text or allocating extra storage.
Destination behavior still applies: a console in host text mode can translate
line endings; a binary File preserves the emitted LF bytes.

## Operations

- `WriteStartObject`, `WriteEndObject`, `WriteStartArray`, and `WriteEndArray`
  manage nesting. Commas and colons are emitted automatically.
- `WritePropertyName(text)` writes an object name. The counted overload
  `WritePropertyName(data, length)` permits embedded NULs. A value must follow
  each name before another name or the closing object delimiter.
- `WriteString(text)` writes a null-terminated string. `WriteString(data, length)`
  writes counted text, retaining embedded NULs through JSON escapes. Both names
  and values require strict UTF-8 and use `JsonString`'s escape policy.
- `WriteBoolean(value)` and `WriteNull()` write their JSON literals.
- `WriteNumber(value)` supports `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`,
  `long`, `ulong`, `nint`, `nuint`, and signed 128-bit `extralong`. It uses invariant
  integer formatting in a fixed local buffer (40 bytes for `extralong`, 21 for
  the other widths), without allocating number text. There is no floating-point
  overload yet.
- `WriteRawNumber(data, length)` validates exactly one JSON number occupying the
  entire counted input, then preserves its spelling. No surrounding whitespace,
  leading plus, extra values, NaN, or infinity are accepted. It reuses
  `JsonReader`'s grammar and does not convert through a machine floating-point
  type. This preserves large integers, exponents, and negative zero when
  forwarding reader tokens.
- `Complete()` verifies that exactly one root value has been written and every
  container is closed. It emits no bytes and can be called repeatedly. Calling
  it too early does not prevent supplying a missing value or closing delimiter.

Duplicate property names are allowed; no key set or DOM is constructed. Nesting
is iterative with a growing `Vector<byte>`, rather than recursive or a fixed
large stack buffer. Only nesting state is buffered; document bytes are forwarded
to the destination as each operation runs.

## Failure and ownership

Invalid ordering, incomplete documents, invalid strings, and invalid raw numbers
throw `JsonWriteException`. Validation happens before writing any part of that
operation. A start operation reserves its nesting capacity before writing its
delimiter, so allocation failure leaves the parent value position and output
unchanged. Correct the invalid operation or allocation condition and retry.

If the destination's `Write` throws, that original exception propagates. Some
bytes, including a comma or part of an escaped string, may already have been
written. The JSON writer then rejects every further write and `Complete()` with
`JsonWriteException`; it cannot roll back external output. Construct a new writer
for another document or destination. There is no reset, automatic completion,
or implicit flush. Earlier successful output remains after any later failure.
This also applies if writing indentation, a newline, or the space after a colon
fails. Invalid operations and failed nesting allocation emit no extra whitespace
and remain retryable under the same validation rules as compact output.

Input buffers must remain valid and unchanged throughout each call. In
particular, do not pass a view into the same destination `String` or another
buffer which the destination may reallocate or modify while writing. The
`ITextWriter` interface cannot establish arbitrary aliasing or lifetime. Writer
callbacks must not move or destroy the JSON writer or its borrowed input.

Moving a `JsonWriter` transfers its nesting state; implicit copying is rejected
because its state owns storage. Moving the destination while it is borrowed is
also invalid. Call the destination's own flush operation explicitly if needed.

For explicit source selection, include `Json/JsonWriter.gf`, `Json/JsonWriteException.gf`,
`Json/json_string_writer.gf`, `Json/JsonString.gf`, `Text/utf8.gf`, `IntegerFormatting.gf`,
`Json/JsonReader.gf`, `Json/JsonParseException.gf`, `Json/JsonTokenKind.gf`, `Text.gf`
(`ITextWriter`), and the ordinary `Vector` dependencies. No DOM, reflection, GC, or libc-specific
writer implementation is required.

The shape follows the conventional forward-only
[.NET Utf8JsonWriter](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.utf8jsonwriter)
API. The failure latch and borrowed-destination contract described here are
gflat's own guarantees.
