# Numeric IP addresses

`std::Net::IpAddress` is an IPv4 or IPv6 numeric value for configuration validation,
canonical logs, comparison, and collection keys. It owns no heap storage and
consults no operating-system services. Its two private `ulong` fields hold
network-order numeric payloads; a private family discriminator distinguishes
IPv4 from IPv6. Default construction is IPv4 `0.0.0.0`. Immutable `IsIpv4`
reports whether the address belongs to that family.

```csharp
using std::Text;
using std; using std::Net;
int main()
{
    IpAddress address = IpAddress::Parse("2001:0DB8:0000:0000:0:0:0:1");
    String message = s$"Address: {address}";
    if (!message.Equals("Address: 2001:db8::1")) return 1;
    return 0;
}
```

## Parsing

`IpAddress::TryParse(text, count, &result)` accepts a complete ASCII address.
IPv4 requires four decimal octets from `0` through `255`, separated by dots.
Leading zeros are rejected except for the single digit `0`; abbreviated,
hexadecimal, and octal forms are rejected.

IPv6 accepts eight hexadecimal words, each one through four digits, with either
letter case. One `::` may replace one or more zero words. A final dotted IPv4
tail is accepted and counts as two words; it follows the same strict decimal
rules. For example, `::FFFF:192.0.2.1` and `0:0:0:0:0:ffff:c000:201` are equal.
An ordinary IPv4 `192.0.2.1` remains a different value.

Whitespace, signs, embedded NUL, trailing data, brackets, ports, CIDR suffixes,
zone identifiers, and hostnames are invalid. A bare link-local IPv6 address can
be represented, but the value does not specify its interface. Parsing validates
numeric syntax; it does not establish reachability or assign network policy.

Counted input is at most 45 bytes. Zero length or a length above 45 fails without
accessing input. `TryParse(cString, &result)` scans at most 46 bytes to check for
the terminator. `TryParse` is `nothrow`, allocates nothing, and resets the result
to IPv4 `0.0.0.0` on failure. All needed input is read before committing the
result, so properly aligned result storage may overlap fully backed readable
input. The failure reset can also overwrite overlapping input bytes.

`IpAddress::Parse(text, count)` and `IpAddress::Parse(cString)` return the parsed
value or throw `Exception("Invalid IP address")`. Successful parsing allocates
nothing. Supply readable input and writable, properly aligned result storage;
the API retains no pointers.

## Network bytes

Immutable `ByteCount` is `4` for IPv4 and `16` for IPv6.
`IpAddress::TryFromBytes(input, count, &result)` accepts exactly that many
network-order bytes: the most significant address byte comes first. Every bit
pattern is valid for those two lengths. Four bytes always create IPv4; sixteen
always create IPv6, including IPv4-mapped addresses. An IPv4 address and its
mapped IPv6 form remain distinct values with different byte counts.

Any other length returns false and resets the result to IPv4 `0.0.0.0` without
reading input. The complete input is read into a local value before the result
is committed, so readable input may overlap properly aligned, writable result
storage. The result store, including a failure reset, can overwrite overlapping
input. `FromBytes(input, count)` returns that value or throws
`Exception("IP address requires 4 or 16 bytes")` for an invalid length.

`TryWriteBytes(output, capacity, ref written)` writes exactly `ByteCount`
network-order bytes without a terminator or family tag. Extra capacity remains
untouched. Insufficient capacity returns false, sets `written` to zero, and
leaves output unchanged; zero capacity does not access output. The writer
stages every output byte before committing, so output may overlap receiver
storage when backed by enough writable bytes. Keep `written` separate from
both output and receiver storage. Byte buffers remain caller-owned.

`ByteCount`, `TryFromBytes`, and `TryWriteBytes` are `nothrow` and allocate
nothing. Successful `FromBytes` also allocates nothing. This byte representation
is independent of native endianness, struct padding, and native socket layouts.
A surrounding serialization format must preserve whether the address has four
or sixteen bytes; a padded sixteen-byte IPv4 field would instead decode as IPv6.

## Formatting and value contracts

`TryFormat(output, capacity, ref written)` writes canonical ASCII without a null
terminator. IPv4 uses four decimal octets. IPv6 uses lowercase hexadecimal with
leading zeros omitted. The longest zero run of at least two words becomes `::`;
ties use the first run. A single zero word stays `0`. IPv4-mapped IPv6 addresses
use `::ffff:` followed by dotted decimal, such as `::ffff:192.0.2.1`. Other
addresses parsed with dotted tails format as hexadecimal IPv6 words.

Output requires at most 39 bytes. `TryFormat` is `nothrow` and allocates nothing.
Insufficient capacity returns false, sets `written` to zero, and leaves output
unchanged. At zero capacity, output is not accessed. Supply writable output
storage for the stated capacity. Formatting prepares every byte in a stack
buffer before writing, so the output may overlap the receiver when enough
writable storage backs the output region. Keep `written` separate from both
output and receiver storage.

`IFormattable.Format(writer)` performs one counted write from stack storage.
The writer is borrowed; it is not retained, flushed, or closed. Writer failures
propagate and may leave partial output. IpAddress has no String dependency;
ordinary interpolation uses its formatting interface. Creating an interpolated
String follows String's normal allocation behavior.

`IEquatable<IpAddress>` compares the family and complete numeric payload.
`IComparable<IpAddress>` places all IPv4 addresses before IPv6 addresses, then
compares unsigned network-order numeric payloads. This is a deterministic value
ordering. `IHashable` includes both family and payload, and equal values have
equal hashes. Hashes are collection hashes, not a persistent representation.
Constructing, copying, comparing, hashing, and successful parsing/formatting
allocate nothing. Exact-address filtering can use equality or hash collections.

This API has no sockets, DNS, endpoints, subnet matching, address classification,
or automatic conversion between IPv4 and IPv4-mapped IPv6. For selective source
use, include `core/Net/IpAddress.gf`, `core/ITextWriter.gf`, `core/IFormattable.gf`, the equality/comparison/hash
interfaces, and `core/Hashing/primitive_hashing.gf` with their normal Exception/runtime contracts.

Strict dotted decimal follows [RFC 3986 section 3.2.2](https://www.rfc-editor.org/rfc/rfc3986.html#section-3.2.2).
IPv6 input follows the numeric forms in [RFC 4291 section 2.2](https://www.rfc-editor.org/rfc/rfc4291.html#section-2.2).
Canonical output follows [RFC 5952 sections 4 and 5](https://www.rfc-editor.org/rfc/rfc5952.html#section-4),
using mixed dotted notation for the IPv4-mapped prefix. This is original gflat
source, without copied runtime implementation code.
