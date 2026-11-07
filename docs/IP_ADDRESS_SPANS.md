# Parse and format address spans

`IpAddress.Parse`, `TryParse`, `FromBytes`, and `TryFromBytes` accept readonly
spans. `TryFormat` and `TryWriteBytes` accept writable destination spans.

```csharp
using Standard::Memory;
using Standard::Net;

int main()
{
    readonly(char)* input = "[2001:db8::1]";
    IpAddress address = IpAddress::Parse(new Span<readonly(char)>(input + 1, 11));
    byte[16] bytes;
    nuint written = 0;
    if (!address.TryWriteBytes(new Span<byte>(&bytes[0], 16), ref written)) return 1;
    IpAddress copy = IpAddress::FromBytes(new Span<readonly(byte)>(&bytes[0], written));
    if (!address.Equals(&copy)) return 2;
    return 0;
}
```

Text spans must contain a complete strict numeric IPv4 or IPv6 address. No
terminator is required, and embedded nulls are invalid. Byte input contains
exactly four bytes for IPv4 or sixteen for IPv6, in network order.

Invalid input makes `TryParse` and `TryFromBytes` return false and reset the
output to `0.0.0.0`. The throwing forms raise `Exception` instead. Valid parsing
and byte construction allocate nothing. All `Try` methods are `nothrow` and
allocate nothing on either outcome.

Short destination spans return false, set `written` to zero, and preserve all
bytes. Successful text output has no terminator. The pointer APIs' aliasing
rules still apply. Keep `written` separate from the receiver and destination.
See [numeric IP addresses](IP_ADDRESS.md) for supported spellings and ranges.

Selective source builds also include `Memory/Span.gf`, `Queries/Enumerable.gf`,
and `Collections/IEnumerable.gf` alongside the existing address dependencies.
