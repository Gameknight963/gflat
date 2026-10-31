# JSON text

`Standard::Json::JsonText::Minify(input, count, output)` removes whitespace
outside strings from one UTF-8 JSON document. Quotes, escape spelling, number
spelling, property order, and duplicate names are preserved.

Input is validated before the first output call. Invalid JSON throws
JsonParseException and produces no output. Keep input separate from storage
the writer may modify. A writer exception can leave an output prefix.

## Pretty-printing

`PrettyPrint(input, count, output, indentSpaces = 2)` uses the same validation
and token preservation. It emits LF, spaces for indentation, and one space
after property colons. Empty containers stay inline. Zero indentation width
still produces line breaks. Output has no trailing newline.

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

```csharp
using Standard;
using Standard::Text;
using Standard::Json;

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

Indentation-size overflow throws `Exception("JSON indentation length overflow")`
before writing. A destination may still fail after accepting part of the output.
For binary files, emitted LF bytes are preserved. A Console destination can apply
its host text-mode translation.

Source: `core/Json/JsonText.gf`
