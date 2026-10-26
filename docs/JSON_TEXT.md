# JSON text

`std::Json::JsonText::Minify(readonly(char)* input, nuint count, ITextWriter* output)`
validates one complete counted UTF-8 JSON document, then writes it without JSON
whitespace outside strings. It preserves every other byte: escape spelling,
number spelling, property order, and duplicate property names. It adds no newline
and does not flush the writer.

Validation uses `JsonReader`, including its strict UTF-8 and surrogate rules.
Empty input is invalid. Invalid or incomplete input throws `JsonParseException`
with the reader's message and byte offset. Validation failures, including nesting
stack allocation failure, produce no output. The reader's stack is released
before the first output call. A later writer exception propagates unchanged and
may leave partial output; calls retain no state.

Input is borrowed: keep all `count` bytes live and unchanged throughout the call.
They must not overlap storage that the destination may modify or reallocate.
The writer consumes each input run synchronously and must not retain its pointer.
`Minify` uses no owning string, DOM, or output scratch buffer; only the
reader's nesting stack may allocate.

## Pretty-printing

`JsonText::PrettyPrint(input, count, output, indentSpaces = 2)` formats the same
strict counted JSON input while preserving every quoted token and numeric lexeme.
It emits LF line breaks, the requested number of spaces per nesting level, and
one space after property colons. Zero indentation spaces still inserts newlines.
Empty containers stay inline, and no trailing newline is appended.

For example, `{ "a" : [1, 2], "b" : {} }` becomes:

```json
{
  "a": [
    1,
    2
  ],
  "b": {}
}
```

The entire document is validated and each indentation length checked before
output begins. Invalid input or nesting-stack allocation failure makes no writer
calls. An indentation length exceeding `nuint` throws
`Exception("JSON indentation length overflow")` before any output. A root scalar
or empty root container requires no indentation even if `indentSpaces` is large.
There is no accumulated total output count; expansion is streamed and the
destination's own capacity and resource limits still apply.

```csharp
using std; using std::Json;
int main()
{
    readonly(char)* input = "{ \"a\" : [1, 2], \"b\" : {} }";
    char[128] data;
    BufferWriter writer = new(data, 128);
    JsonText::PrettyPrint(input, CString::Length(input), &writer);
    if (!CString::Equals(data, "{\n  \"a\": [\n    1,\n    2\n  ],\n  \"b\": {}\n}")) return 1;
    return 0;
}
```

Formatting then uses a lexical scan and a fixed 64-byte space block. No decoded
string, numeric conversion, DOM, or additional parser stack is needed. Reader
storage is released before the first output call. String escapes, large numbers,
fractional zeroes, exponent spelling, duplicate names, and property order are
retained. This does not sort keys or canonicalize JSON.

Input and writer borrowing rules match `Minify`. The writer must consume the
temporary space block during each `Write` and must not retain its pointer. The
formatter never flushes, closes, or destroys the destination. Destination failures
propagate unchanged and can leave a prefix; a retry can duplicate that prefix.
Binary destinations retain LF bytes, while a CRT console in text mode can
translate them to platform newlines. The adapter itself allocates only the
reader's nesting stack; a destination writer can allocate normally.

For selective source use, include `Json/JsonText.gf` alongside the dependencies of
`JsonReader` and `ITextWriter`; `String` and `JsonWriter` are not required.
