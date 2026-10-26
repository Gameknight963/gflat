# IP network prefixes

`std::Net::IpNetwork` represents an IPv4 or IPv6 network prefix using an `IpAddress`
and a prefix length. It is an ordinary fixed value with no owned allocation or
operating-system state. The default is IPv4 `0.0.0.0/0`.

```csharp
using std; using std::Net;
int main()
{
    IpNetwork network = IpNetwork::Parse("192.0.2.0/024");
    IpAddress host = IpAddress::Parse("192.0.2.37");
    if (!network.Contains(&host)) return 1;
    String text = s$"{network}";
    if (!text.Equals("192.0.2.0/24")) return 2;
    IpNetwork normalized = IpNetwork::FromAddress(host, 24);
    if (!network.Equals(&normalized)) return 3;
    IpNetwork child = IpNetwork::Parse("192.0.2.128/25");
    IpNetwork neighbor = IpNetwork::Parse("192.0.3.0/24");
    if (!network.Contains(&child) || !network.Overlaps(&child) || network.Overlaps(&neighbor)) return 4;
    return 0;
}
```

## Construction and containment

`new IpNetwork(networkAddress, prefixLength)` requires zero host bits after the
prefix. Prefix lengths are `0` through `32` for IPv4 and `0` through `128` for
IPv6. An invalid length throws `Exception("IP network prefix is out of range")`;
a base with host bits throws `Exception("IP network address has host bits")`.

`IpNetwork::FromAddress(address, prefixLength)` explicitly clears host bits,
returning the containing network. It uses the same prefix bounds and retains
the address family. `NetworkAddress` returns an address value copy and
`PrefixLength` returns an `int`; both getters are immutable and `nothrow`.

`Contains(&address)` tests only prefix membership. It includes every matching
address, including any network or broadcast address; it does not determine
whether an address is suitable for host assignment. Different address families
return false even for `/0`. IPv4-mapped IPv6 remains IPv6, with no automatic
conversion. A `/32` IPv4 or `/128` IPv6 prefix contains exactly its own address.
Normalization below the mapped marker's prefix can clear that marker while
still retaining IPv6 family. IPv4 and IPv6 `/0` cover their own families only.

`Contains(&otherNetwork)` tests whether the complete other prefix is inside this
one, including equality. Sharing a base address alone is insufficient when the
other prefix is broader. `Overlaps(&otherNetwork)` tests whether the two prefixes
share any address. Overlap is symmetric; same-family CIDR prefixes either nest
or are disjoint. Different families return false for both operations, including
IPv4 versus mapped IPv6 and either family's `/0`. Both methods accept immutable
receivers and inputs, are `nothrow`, and allocate nothing.

## Parsing

`TryParse(text, count, &result)` accepts one complete address, a slash, and one
through three ASCII decimal prefix digits. Leading zeroes in the prefix are
accepted, so `/024` means `/24`. Four digits, signs, whitespace, embedded NUL,
trailing data, ports, brackets, and zones are rejected. Address syntax follows
the existing strict [IpAddress](IP_ADDRESS.md) rules, including its accepted
IPv6 spellings and mixed dotted tails. Host bits must already be zero: parsing
`192.0.2.37/24` fails rather than silently normalizing it.

Counted input is at most 49 bytes. Zero length or a count above 49 fails without
reading input. The C-string overload scans at most 50 bytes to find a terminator.
Both Try methods are `nothrow` and reset output to `0.0.0.0/0` on false. The
complete result is staged before committing; properly aligned writable output
may overlap fully backed readable input. Failure resets can overwrite that
input too. The input remains caller-owned and no pointers are retained.

`Parse(text, count)` and `Parse(cString)` return the value or throw
`Exception("Invalid IP network")`. Successful construction and parsing allocate
nothing. All prefix masks use network-order bytes through IpAddress's public
byte bridge, independently of native struct layout or byte order.

## Formatting and value contracts

`TryFormat(output, capacity, ref written)` writes canonical ASCII without a
terminator: the canonical IpAddress spelling, slash, and shortest decimal prefix.
It uses at most 43 bytes and is `nothrow`. Insufficient capacity returns false,
sets `written` to zero, and leaves every output byte unchanged. Zero capacity
does not touch output. Formatting prepares the whole result in stack storage
before copying, so output may overlap a fully backed writable receiver region.
The `written` reference must be disjoint from output and receiver storage.

`IFormattable.Format(writer)` performs one counted write from stack storage. It
borrows the writer without retaining, flushing, closing or destroying it. Writer
exceptions propagate and can leave partial output. There is no String dependency;
interpolation follows String's normal owning allocation behavior.

`IEquatable<IpNetwork>` compares family, complete network address, and prefix.
`IHashable` includes the address and prefix; equal values have equal hashes.
Hashes are collection hashes, not persistent identifiers. Equality, hashing,
containment and the default constructor are `nothrow` and allocate nothing.
Ordinary copies retain only values. Throwing errors follow normal Exception
runtime behavior; caller-selected writers may allocate or throw.

For selective sources, include `core/Net/IpNetwork.gf`, `core/Net/IpAddress.gf`, their
equality/hash/formatting interfaces and `core/Hashing/primitive_hashing.gf` with normal Exception
contracts. No sockets, DNS, interface identifiers, routing state, address lists,
or implicit family conversions are involved.

The canonical-base policy follows Microsoft's documented
[IPNetwork contract](https://learn.microsoft.com/en-us/dotnet/api/system.net.ipnetwork)
and its [.NET 8 implementation](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Net.Primitives/src/System/Net/IPNetwork.cs).
Later .NET implementations change constructor normalization and mapped-address
membership behavior. The strict parsing, explicit normalization and family rules
above are gflat's own contracts, independent of the host .NET version.

### Numeric range endpoint

`LastAddress` returns the last numeric address in the prefix: it retains the
network bits and sets every host bit to one. Together, `NetworkAddress` and
`LastAddress` give the inclusive numeric range, in the original address family.
The getter is immutable and `nothrow`, returns an ordinary `IpAddress` value
copy, and allocates nothing.

```cpp
using std; using std::Net;
int main()
{
    IpNetwork network = IpNetwork::Parse("192.0.2.128/25");
    IpAddress last = network.LastAddress;
    IpAddress expected = IpAddress::Parse("192.0.2.255");
    if (!last.Equals(&expected) || !network.Contains(&last)) return 1;
    return 0;
}
```

A full-width `/32` IPv4 or `/128` IPv6 prefix has the same first and last
address. A `/0` prefix ends at its family's all-one address. IPv4-mapped IPv6
prefixes remain IPv6. This is a numeric endpoint, not the last usable host:
it does not exclude IPv4 network/broadcast addresses or assign broadcast
semantics to IPv6.
