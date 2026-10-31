# IP network prefixes

`Standard::Net::IpNetwork` holds a network address and prefix length. The default
is IPv4 `0.0.0.0/0`.

```csharp
using Standard::Text;
using Standard;
using Standard::Net;

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

`new IpNetwork(address, prefixLength)` requires zero host bits. Prefix length
ranges are 0–32 for IPv4 and 0–128 for IPv6. Invalid lengths or host bits throw
Exception. `FromAddress(address, prefixLength)` clears host bits explicitly.

| Member | Meaning |
| --- | --- |
| NetworkAddress | Canonical base address |
| PrefixLength | Number of network bits |
| Contains(address) | Address matches the prefix |
| Contains(network) | Entire other prefix is inside this one |
| Overlaps(network) | Prefixes share at least one address |

Different families fail containment and overlap checks, including IPv4 versus
mapped IPv6. A family's /0 covers that family alone. Membership includes the
numeric network and broadcast addresses where present.

## Parsing

TryParse accepts counted text or a C string in address/prefix form. The prefix
has one through three decimal digits. Leading zeroes are allowed, so /024 is /24.
Host bits must be zero: `192.0.2.37/24` fails. Use FromAddress to normalize it.

The input limit is 49 bytes. Failure returns false and resets output to
`0.0.0.0/0`. Parse throws `Exception("Invalid IP network")` instead.

## Formatting and value contracts

TryFormat writes canonical address/prefix ASCII using at most 43 bytes. Short
capacity returns false, zeros written, and preserves output. Format and
interpolation use the same spelling. Equality and hashing include family,
address, and prefix.

Parsing permits result/input overlap. Formatting permits receiver/output overlap.
Keep written-count metadata separate from receiver and output.

### Numeric range endpoint

LastAddress sets every host bit to one. It and NetworkAddress give the inclusive
numeric range:

```cpp
using Standard;
using Standard::Net;

int main()
{
    IpNetwork network = IpNetwork::Parse("192.0.2.128/25");
    IpAddress last = network.LastAddress;
    IpAddress expected = IpAddress::Parse("192.0.2.255");
    if (!last.Equals(&expected) || !network.Contains(&last)) return 1;
    return 0;
}
```

A full-width prefix has identical first and last addresses. LastAddress includes
the numeric broadcast endpoint when one exists. Applications choose usable-host
policy separately.

Source: `core/Net/IpNetwork.gf`
