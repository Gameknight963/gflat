# Convert timestamps and Unix time

`time-convert <mode> <value>` converts between explicit-offset timestamps and
Unix time:

| Mode | Input | Output |
| --- | --- | --- |
| `utc` | Explicit-offset timestamp | Canonical UTC timestamp |
| `seconds` | Explicit-offset timestamp | Unix seconds |
| `milliseconds` | Explicit-offset timestamp | Unix milliseconds |
| `from-seconds` | Signed decimal long | UTC timestamp |
| `from-milliseconds` | Signed decimal long | UTC timestamp |

```powershell
gflat build examples/time-convert/time-convert.gfproj -o artifacts/time-convert.exe
./artifacts/time-convert.exe utc 2024-06-01T12:30:00+02:00
# 2024-06-01T10:30:00Z
./artifacts/time-convert.exe seconds 1969-12-31T23:59:59.9999999Z
# -1
./artifacts/time-convert.exe from-milliseconds -1
# 1969-12-31T23:59:59.9990000Z
```

Timestamps use `yyyy-MM-ddTHH:mm:ss[.fraction]` followed by `Z` or an offset
such as `+02:00`. Fractions have one through seven digits. Offsets are within
14 hours of UTC, and both local and UTC dates must fit years 1 through 9999.

Unix conversion rounds down to the containing second or millisecond, including
negative fractions before 1970. UTC output uses `Z` and prints seven fraction
digits when the fraction is nonzero. See [DateTimeOffset](../../docs/DATE_TIME_OFFSET.md)
for supported ranges.

Exit codes are 0 for success, 2 for invalid arguments or syntax, and 1 for
out-of-range dates or execution failure. Errors go to stderr.
