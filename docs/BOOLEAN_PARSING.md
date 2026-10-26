# Boolean parsing

`core/Text/boolean_parsing.gf` supplies standalone, allocation-free `std::TryParseBool`:

```cpp
bool TryParseBool(readonly(char)* text, bool* result) nothrow;
bool TryParseBool(readonly(char)* text, nuint count, bool* result) nothrow;
```

The only accepted words are ASCII `true` and `false`, in any combination of
uppercase and lowercase letters. The entire input must match. Whitespace is not
trimmed: `" true "` fails. Empty input, numeric aliases (`0` and `1`), signs,
underscores, extra letters, and non-ASCII bytes fail too.

`TryParseBool` returns true on success and stores the parsed value. On failure,
it returns false and stores false. A valid `false` word therefore differs from
failure through the function's return value:

```cpp
using std;
bool enabled = true;
bool valid = TryParseBool("FaLsE", &enabled); // valid=true, enabled=false
```

The C-string overload stops at the first zero byte. The counted overload parses
exactly the selected range and requires no terminator; zero bytes within that
range are invalid. For example, `"true\0suffix"` succeeds as a C string but fails
when the suffix is counted. Zero-length counted input is rejected without
reading the input pointer. Input must cover the requested readable bytes, and
the result pointer must designate writable bool storage. Input is read before
the result is stored, allowing suitably aligned overlapping input/output; that
store also changes the overlapping input bytes.

`core/Text/boolean_parsing_throwing.gf` adds optional `std::ParseBool` wrappers with
the same counted and C-string forms. They return the parsed bool or throw
`Exception` for invalid text. Include both sources to use these wrappers; only
the Try-only source is needed without an exception runtime. A `String` can be
parsed through `text.Data, text.Length` without copies or additional wrappers.

## Borrowed spans

Optional `core/Text/span_boolean_parsing.gf` adds
`text.TryParseBool(&result)` for `Span<readonly(char)>`; include
`Text/boolean_parsing.gf` as well. `core/Text/span_boolean_parsing_throwing.gf` adds
`text.ParseBool()` and also requires `Text/boolean_parsing_throwing.gf`. The adapters
forward the view's exact byte count to the existing parser, without trimming,
copying its bytes, retaining its pointer, or allocating intermediate storage.

Default and zero-length spans are invalid boolean text. The Try form returns
false and stores false without accessing input; the throwing form reports the
same invalid input through `Exception`. The Try adapter is `nothrow` and makes
no allocation. Successful `false` input still returns true from TryParseBool.

The view keeps borrowing its original storage; keep it readable and unchanged
throughout the call. An immutable span header is accepted. The result may overlap
suitably aligned writable input under the counted parser's rules above; storing
the result then changes those input bytes. A failed throwing parse leaves an
assignment target unchanged.
