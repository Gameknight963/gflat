# Quoting and unquoting a UTF-8 JSON string

`std::JsonString` quotes or unquotes one counted UTF-8 string into caller-owned storage. It
is a string primitive, not a JSON parser, object model, or serializer. Its only
std source dependency is `Utf8.gf`; it performs no allocation and calls neither
libc nor the exception runtime. All counted-buffer methods are `nothrow`.

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

## Unquoting one complete literal

`TryGetUnquotedLength(input, count, ref length)` validates one complete JSON
string literal and reports its decoded UTF-8 byte count.
`TryUnquote(input, count, output, capacity, ref written)` performs the same
validation and capacity check before writing any decoded bytes.

Input must include both surrounding quotes, with no outside whitespace or
trailing data. Literal control bytes below 32, unescaped quotation marks,
unknown escapes, and malformed UTF-8 are rejected. Standard escapes `\"`, `\\`,
`\/`, `\b`, `\f`, `\n`, `\r`, and `\t` are decoded. `\uXXXX` accepts either
hexadecimal letter case. A high surrogate must be immediately followed by a
`\uXXXX` low surrogate; lone or mismatched surrogates are rejected. This API
always produces Unicode scalar values, even if a more permissive JSON reader
would accept an unpaired surrogate escape.

Output is counted UTF-8, with no terminator. Escaped U+0000 becomes an actual
zero byte inside that count. Raw non-ASCII UTF-8 and its equivalent Unicode
escapes produce the same bytes. No Unicode normalization occurs.

The empty literal `""` succeeds with length zero, even with zero output capacity;
it does not access output storage. Empty input is not a literal and fails
without reading input. On any malformed input or insufficient capacity, the
metadata count becomes zero and every output byte remains unchanged. The entire
input is validated, including a malformed suffix after valid text.

Unlike quoting, unquoting supports disjoint input/output buffers **or the exact
same starting address**. Other overlap is unsupported. Each source scalar or
complete escape is consumed before its UTF-8 bytes are written into the earlier
part of the buffer. Decoding only shrinks or preserves body byte lengths, so
required-length arithmetic cannot overflow the counted input size. The codec
retains no state or pointers between calls.

## Borrowing and buffer requirements

The caller supplies live input and writable output regions with their specified
byte counts. Scalar output metadata must be disjoint from both byte buffers.
Quoting requires disjoint input and output; unquoting additionally allows the
exact same starting address. The input must remain
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

## Comparing decoded text

`JsonString::ValueTextEquals(input, count, text, length)` compares one complete
quoted JSON string with counted, unquoted UTF-8 text. It is `nothrow`, writes
nothing, and uses no allocation, caller buffer, or owning String. Both input
regions may overlap; keep them alive and unchanged during the call.

Escapes are decoded using the same rules as `TryUnquote`: `"na\u006de"` equals
`name`, escaped surrogate pairs equal their UTF-8 scalar, and `\u0000` equals an
embedded null byte. Equality is exact decoded UTF-8 text, with no case folding
or Unicode normalization. Both inputs must finish together for equality.
Malformed JSON string syntax or UTF-8 returns false, as does a mismatch. False
may short-circuit without examining the remaining bytes, so this predicate is
not a replacement for the standalone validation methods.

Both counts are explicit; no C-string scanning or terminator is involved.
Zero-length text is valid and needs no readable text pointer. A quoted input
count below two returns false without reading either input. For other calls,
provide valid readable storage for both stated extents. The helper adds no
source dependencies beyond the existing `JsonString` and `Utf8` files.

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
