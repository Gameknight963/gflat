# Common IP address values

`IpAddress` exposes named value properties for common endpoint addresses.

| Property | Address |
| --- | --- |
| `Any` | IPv4 `0.0.0.0` |
| `Loopback` | IPv4 `127.0.0.1` |
| `Broadcast` | IPv4 `255.255.255.255` |
| `None` | Same numeric address as `Broadcast` |
| `Ipv6Any` | IPv6 `::` |
| `Ipv6Loopback` | IPv6 `::1` |
| `Ipv6None` | Same numeric address as `Ipv6Any` |

```csharp
using Standard::Net;

int main()
{
    IpEndpoint local = new(IpAddress::Loopback, (ushort)8080);
    IpAddress address = local.Address;
    if (!IpAddress::IsLoopback(address)) return 1;
    IpAddress v4 = IpAddress::Any;
    IpAddress v6 = IpAddress::Ipv6Any;
    if (v4.Equals(&v6)) return 2;
    return 0;
}
```

Each property returns an independent value, without parsing or allocating.
IPv4 and IPv6 unspecified addresses remain distinct families. These are numeric
values, not queries about interfaces or routing on the current machine.
`None` and `Ipv6None` are address values rather than nullable missing-value markers.

The values follow [.NET IPAddress](https://learn.microsoft.com/en-us/dotnet/api/system.net.ipaddress).
