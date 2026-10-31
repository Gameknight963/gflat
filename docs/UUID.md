# UUID values

`Standard::Uuid` holds 128 identifier bits. The default is Nil, with all bits zero.
`Uuid(upper, lower)` and High/Low use network-order numeric halves. For
`00112233-4455-6677-8899-aabbccddeeff`, High is `0x0011223344556677` and Low is
`0x8899aabbccddeeff`.

```csharp
using Standard::Text;
using Standard;

Uuid id = Uuid::Parse("00112233-4455-6677-8899-aabbccddeeff");
String label = s$"id={id}";
```

## Parsing and formatting

```csharp
static bool TryParse(readonly(char)* text, nuint count, Uuid* result) nothrow;
static bool TryParse(readonly(char)* text, Uuid* result) nothrow;
static Uuid Parse(readonly(char)* text, nuint count);
static Uuid Parse(readonly(char)* text);
immutable bool TryFormat(char* output, nuint capacity, ref nuint written) nothrow;
immutable void Format(ITextWriter* output);
```

Parsing requires 36 bytes in 8-4-4-4-12 hexadecimal groups. Either letter case is
accepted. Every bit pattern is valid, including unrecognized version/variant bits.
TryParse returns false and writes Nil on failure. Parse throws Exception.

TryFormat writes 36 lowercase bytes without a terminator. Short capacity returns
false, zeros written, and preserves output. Format and interpolation use the same
spelling.

## Canonical bytes

TryFromBytes accepts exactly 16 bytes, with the first eight forming High and the
remaining eight Low, most significant byte first. Invalid count returns false
and writes Nil. FromBytes throws instead. TryWriteBytes emits the same 16-byte
order, returning false and zero written when capacity is short.

Parsing/from-bytes results may overlap aligned input storage. Formatting/byte
output may overlap the receiver. Keep written-count variables separate from
output and receiver. Initialize ordinary local results before taking their
address, as these pointer outputs do not establish local initialization.

## Identity and ordering

Equality compares all bits. Ordering compares unsigned High, then Low, matching
canonical byte order. Uuid implements the equality, ordering, hashing, and text
formatting contracts used by collections and interpolation.

The canonical representation follows
[RFC 9562 section 4](https://www.rfc-editor.org/rfc/rfc9562.html#section-4).

## Command-line example

```csharp
using Standard::Text;
using Standard;

int main(int argc, char** argv)
{
    if (argc != 2) {
        Console::Error.WriteLine("Usage: uuid-check <identifier>");
        return 2;
    }
    Uuid id = new();
    if (!Uuid::TryParse(argv[1], &id)) {
        Console::Error.WriteLine("Expected a UUID in 8-4-4-4-12 hexadecimal groups");
        return 2;
    }
    id.Format(Console::Out);
    Console::WriteLine();
    Console::Flush();
    return 0;
}
```

## Version 4 generation with an explicit source

UuidGenerator accepts a random-byte callback:

```csharp
static bool TryCreateVersion4(bool(byte*, nuint)* fill, ref Uuid result);
static Uuid CreateVersion4(bool(byte*, nuint)* fill);
```

The callback receives a temporary 16-byte buffer and must fill every byte before
returning true. Its storage lasts through the callback alone. The generator sets
version 4 and variant bits, retaining 122 random bits.

False from the callback makes TryCreateVersion4 write Nil and return false.
CreateVersion4 throws Exception instead. Callback exceptions propagate and
preserve the previous result. Choose a suitable random source for the intended
identifier use.

After referencing the Windows random library:

```csharp
Uuid id = UuidGenerator::CreateVersion4(Windows::SystemRandom::TryFill);
```

Linux provides `Linux::SystemRandom::TryFill`. The application selects the
platform library or supplies its own callback.

Sources: `core/Uuid.gf`, `core/UuidGenerator.gf`
