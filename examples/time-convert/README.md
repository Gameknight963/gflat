# Convert explicit-offset timestamps and Unix time

`time-convert <mode> <value>` performs one conversion with DateTimeOffset:

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

Timestamp syntax is `yyyy-MM-ddTHH:mm:ss[.fraction]` followed by uppercase `Z`
or `+HH:mm`/`-HH:mm`. Fractions have 1 through 7 digits, offsets are whole minutes
within ±14 hours, and both wall and UTC dates must fit years 1 through 9999.
No local offset or named time zone is inferred. Numeric inputs accept one optional
leading sign and ASCII decimal digits, including leading zeros. Whitespace,
radix prefixes, separators and decimal points are rejected.

Unix conversion rounds down to the containing second or millisecond, including
negative fractions before 1970. Canonical UTC output uses `Z`, omits a zero
fraction and otherwise prints seven fraction digits. Valid factory ranges are
`-62135596800..253402300799` seconds and
`-62135596800000..253402300799999` milliseconds.

The tool formats directly to stdout, adds a platform newline and explicitly
flushes. It creates no owning String and uses no files, network or system clock.
Successful conversion exits `0`; incorrect arity, mode or input syntax exits `2`
with a static stderr diagnostic. A valid long outside the factory's date range,
or an execution/output exception, exits `1` with `time-convert: ` and the original
message. Parsing and conversion finish before output; an output failure may leave
a prefix. See [DateTimeOffset](../../docs/DATE_TIME_OFFSET.md) for value contracts.
