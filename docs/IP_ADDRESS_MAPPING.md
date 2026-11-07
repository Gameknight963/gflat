# Mapping and classifying IP addresses

`IpAddress` provides `MapToIpv4()`, `MapToIpv6()`, and prefix classification
properties in its core source. These APIs do not perform network operations.

```csharp
using Standard::Net;

int main()
{
    IpAddress original = IpAddress::Parse("192.0.2.1");
    IpAddress mapped = original.MapToIpv6();
    if (!mapped.IsIpv6 || !mapped.IsIpv4MappedToIpv6) return 1;
    IpAddress restored = mapped.MapToIpv4();
    if (!restored.Equals(&original)) return 2;
    if (!IpAddress::IsLoopback(IpAddress::Parse("127.12.34.56"))) return 3;
    return 0;
}
```

`MapToIpv6()` maps IPv4 to `::ffff:a.b.c.d` and leaves an IPv6 value unchanged.
`MapToIpv4()` extracts the last four bytes of any IPv6 address and leaves IPv4
unchanged. It does not require an IPv4-mapped prefix. Check
`IsIpv4MappedToIpv6` first when accepting only mapped addresses.

| Property | IPv6 prefix |
| --- | --- |
| `IsIpv6Multicast` | `ff00::/8` |
| `IsIpv6LinkLocal` | `fe80::/10` |
| `IsIpv6SiteLocal` | `fec0::/10`, the deprecated site-local range |
| `IsIpv6UniqueLocal` | `fc00::/7` |
| `IsIpv6Teredo` | `2001:0000::/32` |
| `IsIpv4MappedToIpv6` | `::ffff:0:0/96` |

All prefix properties are false for IPv4. `IsIpv6` complements `IsIpv4`.
`IpAddress::IsLoopback(value)` follows .NET's rule: any IPv4 address in
`127.0.0.0/8`, IPv6 `::1`, or the single mapped value `::ffff:127.0.0.1`.
Other mapped `127/8` values are not classified as loopback by this helper.

Mapping returns a new value without changing its source. These operations
are `nothrow` and do not allocate. They classify numeric bits only; the type
does not carry an IPv6 scope identifier or infer reachability or routing.
