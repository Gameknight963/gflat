# Limit a consumer to one payload

`Standard::IO::LimitedReader` gives an existing `IByteReader` a byte allowance.
A parser can read until that bounded input ends without consuming the next
record through the same source. It borrows the source; it owns no buffer and
does not allocate a read-ahead buffer, close, rewind, or drain anything.

The type lives in `Standard/core/IO/LimitedReader.gf` and implements `IByteReader`.

| Member | Contract |
|---|---|
| `LimitedReader(IByteReader* input, nuint limit) nothrow` | Borrows one source cursor and starts an independent allowance. Construction does not read input. |
| `Remaining` | Immutable, `nothrow` observer of the unused validated allowance. This is not the source's remaining length. |
| `Read(char* data, nuint count)` | Makes at most one source call with `min(count, Remaining)`, then deducts the validated returned count. |

## A length-prefixed frame with a retained tail

This complete program stores a two-byte little-endian length, the payload
`abc`, and a marker in a `BinaryBufferReader`. `ReadPayload` knows only
`IByteReader`: it stops at input end and would read the marker if given the
unbounded wire directly. The `LimitedReader` makes the payload boundary look
like EOF to that consumer. The original cursor then reads the untouched marker.

The header length is validated against the caller's eight-byte storage before
the payload is consumed. A zero return with a positive allowance means early
EOF, which the example detects by comparing the received count and Remaining.

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

The program returns zero on success and prints nothing.

The bound applies to the declared payload length. It cannot determine whether a
malformed header accidentally includes bytes belonging to the next record.
Validate protocol lengths and application limits before handing input to a
consumer. When a complete bounded region is already in memory and the consumer
accepts `BinaryBufferReader`, its existing `TryReadSlice` may be simpler.
`TryReadSlice` advances the parent immediately; LimitedReader advances its source
only as bytes are actually read and works with any `IByteReader` implementation.

## Short reads, EOF, and zero requests

A short positive result is normal. LimitedReader does not loop to fill a request.
Use `ReadExactly(&frame, data, count)` when a consumer requires an exact count;
that helper loops through short reads and throws `EndOfStreamException` on early
EOF. Previously consumed input and output bytes remain consumed or modified.

`Read` returning zero has two distinct explanations:

- `Remaining == 0`: the allowance is exhausted, so the source was not called.
- `Remaining > 0` and count was positive: the source reported EOF before the
  declared boundary. The unfulfilled allowance remains visible.

A zero count returns zero without calling the source or touching data. Exhausted
allowances behave the same even for a positive count. The adapter still validates
its own failure state first. Unlike BufferedReader's zero-count policy, it does
not ask the underlying source to validate a zero request.

Output must have room for the capped request and must not overlap the adapter's
state or the source's retained input/state. No terminator is added. Embedded zero
and high-bit bytes are ordinary counted data.

## Cursor and storage lifetime

Keep the concrete source alive, unmoved, and usable while the adapter is used.
Keep the source's captured backing bytes alive as well. Do not interleave other
consuming source access: the adapter cannot account for bytes read elsewhere.
Leaving its scope does not close the source or consume an unfinished payload.
Finish or deliberately discard the payload before treating following input as
the next frame.

If the source is a BufferedReader, continue reading the tail through that same
BufferedReader. Its private read-ahead buffer may already contain later bytes;
LimitedReader does not make bypassing that buffer safe. The bound concerns the
logical source supplied to the adapter, not an underlying operating-system file
position.

LimitedReader copies share the source cursor but retain independent allowances
and failure flags. They do not form one shared cap. For example, a completed
three-byte adapter followed by a previously copied three-byte adapter can consume
six source bytes in total. Use a nested LimitedReader when both allowances must
apply to the same reads, and keep both adapter values alive and unmoved for that
nesting.

## Failures

If the source throws, or reports more bytes than the capped request, the adapter
keeps its last validated Remaining value and latches failure. Later Read calls
throw IOException without calling the source, including zero-count calls. A
source may have consumed an unknown prefix before throwing, so retrying against
the old allowance would not reliably enforce the bound. There is no rollback,
automatic draining, or reset operation.
An earlier copy does not recover a failed source: its independent flag says
nothing about where that shared cursor stopped.

The original source exception propagates unchanged. Over-reporting raises
IOException. These exceptions use the language's ordinary exception runtime;
"no buffer allocation" does not mean thrown exceptions require no runtime storage.
On a provider contract violation the adapter cannot undo bytes already written by
that provider.

## Source selection

The adapter's direct types are IByteReader and IOException, whose base is the core
Exception. Its own source imports no libc API. Its source-only check uses those
four files with NoStdLib and explicit host memory wrappers for exception
storage. Normal byte transfer creates no buffer allocation.
The framing example additionally uses the core BinaryBufferReader,
BinaryPrimitives, and Memory byte-copy support. Native exception support and any
optimizer-generated byte-operation calls are separate from this adapter's API;
see the [raw-memory linking notes](../../../docs/MEMORY.md#native-linking).
