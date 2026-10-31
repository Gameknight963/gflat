# Numeric IP addresses

`Standard::Net::IpAddress` represents IPv4 or IPv6. The default is IPv4 `0.0.0.0`.
IsIpv4 identifies its family.

```csharp
using Standard::Text;
using Standard;
using Standard::Net;

int main()
{
    IpAddress address = IpAddress::Parse("2001:0DB8:0000:0000:0:0:0:1");
    String message = s$"Address: {address}";
    if (!message.Equals("Address: 2001:db8::1")) return 1;
    return 0;
}
```

## Parsing

TryParse accepts counted text or a C string and writes an address result.
Failure returns false and resets the result to IPv4 zero. Parse throws
`Exception("Invalid IP address")` instead.

| Form | Accepted syntax |
| --- | --- |
| IPv4 | Four decimal octets from 0 through 255 in shortest decimal spelling |
| IPv6 | Eight hexadecimal words, with one optional `::` replacing one or more zero-valued words |
| IPv6 with dotted tail | Final IPv4 address contributes two words |

Input must be a complete numeric address, at most 45 bytes. Use
[IpEndpoint](IP_ENDPOINT.md) for ports or [IpNetwork](IP_NETWORK.md) for CIDR.
IPv4 and IPv4-mapped IPv6 remain distinct values. Link-local interface scope
must be supplied separately by the application.

## Network bytes

`TryFromBytes(data, count, &result)` accepts exactly four bytes for IPv4 or
sixteen for IPv6, most significant byte first. Other lengths return false and
reset the result to IPv4 zero. FromBytes throws on invalid length.

ByteCount identifies the representation length. TryWriteBytes writes that many
bytes. A serialized record must retain the length to preserve address family.

## Formatting and value contracts

TryFormat writes counted ASCII, requiring at most 39 bytes. Short capacity returns
false, zeros written, and preserves output. Format sends the same text to a writer.

- IPv4 uses shortest decimal octets
- IPv6 uses lowercase hex and compresses the first longest zero run of at least two words
- IPv4-mapped IPv6 uses `::ffff:` followed by dotted IPv4

Parsing can overlap its result with input. Formatting can overlap the receiver.
Keep written-count metadata separate from output and receiver storage.

Equality includes family. Ordering places IPv4 before IPv6, then compares numeric
address values. These contracts allow addresses as collection keys.

Syntax follows [RFC 3986](https://www.rfc-editor.org/rfc/rfc3986.html#section-3.2.2)
and [RFC 4291](https://www.rfc-editor.org/rfc/rfc4291.html#section-2.2).
Formatting follows [RFC 5952](https://www.rfc-editor.org/rfc/rfc5952.html#section-4).

Source: `core/Net/IpAddress.gf`
