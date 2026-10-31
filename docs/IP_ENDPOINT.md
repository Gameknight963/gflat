# Numeric IP endpoints

`Standard::Net::IpEndpoint` holds an [IpAddress](IP_ADDRESS.md) and a `ushort` port for
configuration validation, canonical logs, and collection keys. It is a copyable fixed-size value.

For example, validate a configured upstream address before passing its value to
the rest of an application:

```csharp
using Standard; using Standard::Net;

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

The parser validates the numeric address and port. Port zero is representable. The type assigns no
binding, connection, or service policy to it.

## Construction and values

`new IpEndpoint()` is IPv4 `0.0.0.0:0`.
`new IpEndpoint(address, port)` copies an `IpAddress` and a `ushort`. Both
constructors are `nothrow` and allocate nothing. The immutable, nonthrowing
`Address` getter returns an independent address value, and `Port` returns the
stored `ushort`. There are no setters. Construct another endpoint to change it.

`IEquatable<IpEndpoint>` compares the complete address, including its family,
and port. IPv4 `192.0.2.1:443` differs from IPv6 `[::ffff:192.0.2.1]:443`.
Changing a port changes equality. `IHashable` combines the address hash and port.
Equal values have equal hashes. Hashes are collection hashes, not persistent
identifiers or a wire format. Copying, equality, hashing, and getters allocate
nothing and are `nothrow`.

## Parsing

`TryParse(text, count, &result)` accepts exactly one complete ASCII endpoint:

- IPv4 uses `address:port`, such as `192.0.2.1:8080`
- IPv6 requires `[address]:port`, such as `[2001:db8::1]:443`
- Ports contain one through five ASCII decimal digits and must be in `0..65535`.
  Leading zeros are accepted. `:00443` means port 443 and `:00000` means zero.
  Six digits are invalid even when their numeric value would fit.

Address syntax follows IpAddress, including strict four-octet IPv4 and its
accepted IPv6 spellings and dotted tails. Bracketed IPv4 and unbracketed IPv6 are
invalid. Brackets remain required for IPv4-mapped IPv6. The parser rejects
missing addresses or ports, whitespace, signs, non-ASCII port digits, extra
delimiters, trailing data, embedded counted NUL, hostnames, CIDR suffixes, and
zone/scope identifiers such as `%3` or `%eth0`. A bare numeric link-local address
can be stored, but an endpoint does not identify its interface. No default port
or implicit IPv4/mapped-IPv6 conversion is supplied.

Counted input is at most **53 bytes**: a 45-byte accepted IPv6 spelling, two
brackets, a colon, and five port digits. Count zero or above 53 fails without
reading input. `TryParse(cString, &result)` scans at most **54 bytes** for a NUL
terminator. The C-string form ends at that terminator. A counted input containing
the same terminator and further bytes is invalid.

Both Try overloads are `nothrow`, allocate nothing, and reset result to
`0.0.0.0:0` on failure. The complete result is staged before committing, so
properly aligned writable result storage may overlap fully backed readable
input. The failure reset may overwrite overlapping input too. Supply writable
result storage in every case. Raw input pointers must be readable for the bytes
the selected overload examines. The C-string overload requires readable input.
The signatures use non-null raw pointers. A counted length rejected before
reading, such as zero, does not dereference input. No pointers are retained.

`Parse(text, count)` and `Parse(cString)` return the value or throw
`Exception("Invalid IP endpoint")`. Successful parsing allocates nothing.
Throwing follows the normal Exception runtime contract.

## Formatting

`TryFormat(output, capacity, ref written)` writes canonical counted ASCII with
no NUL terminator. It uses IpAddress's canonical address formatting, wraps IPv6
in brackets, then appends a colon and the shortest decimal port. For example,
`[2001:0DB8::1]:00443` formats as `[2001:db8::1]:443`, and
`[0:0:0:0:0:ffff:c000:201]:00080` as `[::ffff:192.0.2.1]:80`.

Canonical output requires at most **47 bytes**. Formatting is `nothrow` and
allocates nothing. Insufficient capacity returns false, sets `written` to zero,
and leaves output unchanged. Zero capacity does not access output. The signature
still requires a non-null raw pointer. Extra capacity remains untouched.

All output is staged in a stack buffer before copying, so a fully backed writable
output region may overlap receiver storage. Keep `written` separate from both
output and receiver storage. Caller buffers remain caller-owned.

`IFormattable.Format(writer)` performs one counted write from stack storage. The
writer is borrowed without being retained, flushed, closed, or destroyed. Writer
exceptions propagate and may leave partial output. The endpoint has no String
dependency. Ordinary interpolation uses its formatting interface and follows
String's normal allocation behavior.

For selective sources, include `core/Net/IpEndpoint.gf`, `core/Net/IpAddress.gf`,
`core/ITextWriter.gf`, `core/IFormattable.gf`, the equality/comparison/hash interfaces, and
`core/Hashing/primitive_hashing.gf` with their normal Exception/runtime contracts.
