# Boolean parsing

`core/BooleanParsing.gf` supplies standalone, allocation-free `std::TryParseBool`:

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

`core/BooleanParsingThrowing.gf` adds optional `std::ParseBool` wrappers with
the same counted and C-string forms. They return the parsed bool or throw
`Exception` for invalid text. Include both sources to use these wrappers; only
the Try-only source is needed without an exception runtime. A `String` can be
parsed through `text.Data, text.Length` without copies or additional wrappers.
