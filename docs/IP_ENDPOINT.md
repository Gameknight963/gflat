# Numeric IP endpoints

`Standard::Net::IpEndpoint` holds an IpAddress and ushort port:

```csharp
using Standard;
using Standard::Net;

int main()
{
    readonly(char)* configured = "[2001:0DB8::1]:00443";
    IpEndpoint upstream = new();
    if (!IpEndpoint::TryParse(configured, &upstream)) return 1;
    IpAddress expectedAddress = IpAddress::Parse("2001:db8::1");
    IpAddress actualAddress = upstream.Address;
    if (!actualAddress.Equals(&expectedAddress) || upstream.Port != (ushort)443)
        return 2;

    char[47] canonical;
    nuint written = 0;
    if (!upstream.TryFormat(canonical, 47, ref written)) return 3;
    // canonical contains the 17 counted bytes [2001:db8::1]:443.
    // Pass canonical and written together to a counted writer or logging API.
    return 0;
}
```

## Construction and values

`new IpEndpoint(address, port)` creates a value with Address and Port getters.
The default is `0.0.0.0:0`. Equality and hashing include both address family
and port.

## Parsing

TryParse accepts counted text or a C string. The complete form is:

| Family | Example |
| --- | --- |
| IPv4 | `192.0.2.1:8080` |
| IPv6, including mapped IPv4 | `[2001:db8::1]:443` |

Ports have one through five decimal digits in 0–65535. Leading zeroes are
accepted. Address syntax follows [IpAddress](IP_ADDRESS.md). Brackets are
required for IPv6. Counted input is limited to 53 bytes.

Failure returns false and resets the result to `0.0.0.0:0`. Parse throws
`Exception("Invalid IP endpoint")` instead. Interface scope for a link-local
address must be supplied separately.

## Formatting

TryFormat emits counted canonical ASCII using at most 47 bytes. IPv6 remains
bracketed and the port uses shortest decimal spelling. Short capacity returns
false, zeros written, and preserves output. Format and interpolation use the
same representation.

Parsing permits result/input overlap. Formatting permits receiver/output overlap.
Keep written-count metadata separate from receiver and output.

Source: `core/Net/IpEndpoint.gf`
