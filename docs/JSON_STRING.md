# Quoting a UTF-8 JSON string

`std::JsonString` quotes one counted UTF-8 string into caller-owned storage. It
is a string primitive, not a JSON parser, object model, or serializer. Its only
std source dependency is `Utf8.gf`; it performs no allocation and calls neither
libc nor the exception runtime. Both methods are `nothrow`.

```csharp
nuint needed = 0;
if (std::JsonString::TryGetQuotedLength(input, length, ref needed))
{
    nuint written = 0;
    bool success = std::JsonString::TryQuote(input, length, output, capacity, ref written);
}
```

`length`, `capacity`, `needed`, and `written` count bytes. The output includes
the opening and closing double quotes, with **no null terminator**. Empty input
requires two bytes and produces `""`. Input is counted, so embedded U+0000 is
data and becomes `\u0000` rather than terminating the scan.

Input must encode Unicode scalar values strictly as UTF-8. Malformed sequences,
overlong encodings, encoded surrogates, truncated sequences, and values above
U+10FFFF fail. Valid non-ASCII UTF-8 is preserved byte for byte, including
supplementary scalars, noncharacters, and U+FEFF inside the quoted string.

Quotation marks and backslashes are escaped. Backspace, tab, line feed, form
feed, and carriage return use `\b`, `\t`, `\n`, `\f`, and `\r`. Remaining
control bytes below 32 use `\u00XX` with uppercase hexadecimal digits. Slashes,
angle brackets, ampersands, and U+2028/U+2029 are not additionally escaped.
This follows the JSON string rules in
[RFC 8259, section 7](https://www.rfc-editor.org/rfc/rfc8259#section-7).
The result is JSON text, not an HTML or JavaScript escaping policy.

`TryGetQuotedLength` validates the entire input and checks size arithmetic. On
success it returns the exact required byte count; on failure it returns false
and sets `needed` to zero. `TryQuote` performs that same preflight and checks
capacity before writing output. Failure sets `written` to zero and leaves
every output byte unchanged. Success changes exactly `written` output bytes;
storage after the closing quote remains untouched.

## Borrowing and buffer requirements

The caller supplies live input and writable output regions with their specified
byte counts. Input, output, and scalar output metadata must be mutually
disjoint. In-place or overlapping quoting is unsupported. The input must remain
unchanged between validation and writing, including through other threads or
aliases. No pointer is retained after the call.

Zero-length input requires no input read, but pointer arguments remain
nonnullable under the normal language rules. `nothrow` does not validate raw
pointers or make undersized backing allocations safe. Time is linear in input
length and extra storage is constant.

The [freestanding executable test](FREESTANDING_CORE.md) selects these sources
with `NoStdLib` and the explicit `NoBuiltins` optimizer policy. Both Windows x64
O0/O2 builds quote runtime-populated bytes and check escapes and atomic failures;
their only import is `KERNEL32.dll!ExitProcess`. The same test retains a Linux
x64 syscall-startup branch for CI. This is evidence for that tested configuration,
not a blanket guarantee about backend ABI helpers on every target.

## Writing through ITextWriter

The optional `std/core/JsonStringWriter.gf` adapter adds:

```csharp
void WriteJsonString(this ITextWriter* output, readonly(char)* input, nuint length);
```

Include it alongside `JsonString.gf` and `Utf8.gf`. For example:

```csharp
char[128] storage;
std::BufferWriter buffer = new(storage, 128);
ITextWriter* output = &buffer;
output.WriteJsonString("hello", 5);
```

The adapter validates the entire input and quoted length before its first writer
call. Invalid UTF-8 or size overflow throws `Exception` without writing. After
successful validation, it writes the opening quote, maximal spans requiring no
escaping, individual escapes, and the closing quote. Each escape uses the core
`JsonString` encoder with an eight-byte stack buffer; no second escape policy or
intermediate owning String is involved. Empty input makes two quote writes.

The adapter itself allocates no heap storage during successful writing. An
underlying String or other writer may allocate as part of its own behavior.
Writer failures propagate, and **may leave partial output**: this adapter does
not promise the counted-buffer API's atomicity across multiple `Write` calls.
For all-or-nothing destination updates, quote into a sufficiently large caller
buffer first, then use a writer with the required commit behavior.

Input must remain valid and unchanged throughout the call, separate from writer
state and destination storage. A writer consumes each supplied span during
`Write` and must not retain its pointer; escape bytes live in temporary stack
storage. The adapter borrows the writer and never destroys or closes it.
BufferWriter, String, File, and custom `ITextWriter` implementations can all be
used through the same interface pointer. The adapter depends on the normal
writer/exception contracts; adding it does not add those dependencies to the
standalone counted-buffer quoting source.
