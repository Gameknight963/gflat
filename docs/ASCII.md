# ASCII byte utilities

`core/Ascii.gf` provides `std::Ascii` functions for the language's byte-sized
`char`. These functions do not interpret individual UTF-8 bytes as Unicode
characters. They work without std, libc, allocation, or exception support.

Classification functions are `IsAscii`, `IsDigit`, `IsLower`, `IsUpper`,
`IsLetter`, `IsLetterOrDigit`, `IsHexDigit`, `IsWhiteSpace`, `IsControl`, and
`IsPrintable`. Every function accepts a `char` and returns `bool`.

- ASCII comprises bytes 0 through 127.
- Letters are A–Z and a–z; decimal digits are 0–9.
- Hex digits additionally include A–F and a–f.
- Whitespace is space, tab, line feed, vertical tab, form feed, and carriage return.
- Control bytes are 0–31 and 127.
- Printable bytes are 32–126, including space.

All classification functions return false for bytes 128–255.
`ToLower(char)` and `ToUpper(char)` convert ASCII letters and preserve every
other byte, including bytes above 127. Results do not depend on locale or the
machine's signed `char` conventions.

`TryHexValue(char input, byte* result)` recognizes one hexadecimal digit and
writes its numerical value from 0 through 15. Invalid input returns false and
writes zero. Use `std::Utf8` to decode Unicode scalar values before applying
any future Unicode character operations.
