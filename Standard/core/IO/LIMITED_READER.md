# Limit a consumer to one payload

`Standard::IO::LimitedReader` gives a borrowed `IByteReader` a byte allowance.
A parser can read to that boundary while leaving the next record for another
consumer. The adapter stores a source pointer, remaining allowance, and failure
state.

| Member | Contract |
| --- | --- |
| `LimitedReader(input, limit) nothrow` | Borrows a source cursor with an independent allowance |
| `Remaining` | Unused validated allowance, available through an immutable `nothrow` getter |
| `Read(data, count)` | Makes at most one source call with `min(count, Remaining)` and deducts its validated result |

## A length-prefixed frame with a retained tail

This program stores a two-byte little-endian length, `abc`, and a marker in a
`BinaryBufferReader`. `ReadPayload` reads until EOF. LimitedReader presents the
payload boundary as EOF, then the original cursor reads the marker.

The program checks the declared length against its eight-byte destination before
reading. It also detects EOF before the complete declared payload arrives.

```csharp
using Standard::Binary;
using Standard::IO;

// A consumer that reads until its input ends, without knowing the outer framing.
nuint ReadPayload(IByteReader* input, char* output, nuint capacity)
{
    nuint total = 0;
    while (total < capacity)
    {
        nuint count = input.Read(output + total, capacity - total);
        if (count > capacity - total) throw new IOException("Invalid payload byte count");
        if (count == 0) break;
        total += count;
    }
    return total;
}

int main()
{
    // Little-endian length 3, payload "abc", then a marker for the next consumer.
    byte[6] packet = [3, 0, 97, 98, 99, 126];
    BinaryBufferReader wire = new(packet, 6);
    ushort length = 0;
    if (!wire.TryReadUInt16LittleEndian(ref length)) return 1;
    if (length > 8) return 2;

    char[8] payload;
    LimitedReader frame = new(&wire, (nuint)length);
    nuint received = ReadPayload(&frame, payload, 8);
    if (received != (nuint)length || frame.Remaining != 0) return 3;
    if (received != 3 || payload[0] != 'a' || payload[1] != 'b' || payload[2] != 'c') return 4;

    // The consumer's final read saw the frame boundary, not the marker.
    char untouched = '!';
    if (frame.Read(&untouched, 1) != 0 || untouched != '!') return 5;
    if (wire.Position != 5 || wire.Remaining != 1) return 6;

    byte marker = 0;
    if (!wire.TryReadByte(ref marker) || marker != 126 || wire.Remaining != 0) return 7;
    return 0;
}
```

Validate protocol lengths and application limits before giving the adapter to a
consumer. Its allowance follows the supplied length, including a malformed length
that extends into the next record. For an in-memory `BinaryBufferReader`,
`TryReadSlice` can expose the complete payload at once. That operation advances
the parent immediately, whereas LimitedReader advances its source as reads occur.

## Short reads, EOF, and zero requests

A short positive read is normal. Use `ReadExactly` to fill a complete range or
throw `EndOfStreamException`. Partial input and output remain consumed or modified
when a read fails.

A zero result means:

- `Remaining == 0`: the allowance is exhausted and the source was left untouched
- `Remaining > 0` with a positive request: the source reached EOF early

Zero-count requests return zero without accessing the source or output, after
checking the adapter's failure state. Exhausted allowances behave the same for
positive requests. Output needs room for the capped request and must be disjoint
from the adapter and source state. Bytes are counted, including zero and high-bit
values.

## Cursor and storage lifetime

- Keep the source and its backing storage alive, unmoved, and usable
- Route consuming reads through the adapter while its allowance applies
- Finish or deliberately discard a partial payload before reading the next frame
- With a BufferedReader source, read the tail through that same BufferedReader

The source stays open when the adapter leaves scope. Copies share its cursor but
have independent allowances and failure states. Two copied three-byte allowances
can consume six bytes in total. Nest LimitedReaders when both allowances must
apply to the same reads, and keep both adapters alive and unmoved.

## Failures

A source exception or an excessive returned byte count latches failure and keeps
the last validated `Remaining`. Every later read throws `IOException` before
calling the source, including zero-count requests. The original exception
propagates unchanged. Over-reporting raises `IOException`.

A failed source can already have consumed input or modified output. Discard the
adapter after failure. Earlier copies share the affected source position even
though their own failure flags remain clear.

## Source selection

Include `IO/LimitedReader.gf`, `IO/IByteReader.gf`, `IO/IOException.gf`, and the core
`Exception` source. Exception storage requires the chosen memory and exception
runtime. Normal byte transfer uses the source directly and allocates no buffer.

The framing example also uses BinaryBufferReader, BinaryPrimitives, and Memory
byte-copy support. See [native linking](../../../docs/MEMORY.md#native-linking)
for optimizer-generated byte operations.
