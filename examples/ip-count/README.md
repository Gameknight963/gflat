# Count IP addresses

Read one IPv4 or IPv6 address per line from standard input. Equivalent spellings share
one count. Output uses canonical addresses in first-seen order, followed by a tab and
the occurrence count.

```powershell
gflat build examples/ip-count/ip-count.gfproj -o artifacts/ip-count.exe
Get-Content addresses.txt | ./artifacts/ip-count.exe
Get-Content addresses.txt | ./artifacts/ip-count.exe --network 192.0.2.0/24
```

For example:

```text
2001:0DB8:0:0:0:0:0:1
192.0.2.1
2001:db8::1
::ffff:192.0.2.1
```

produces:

```text
2001:db8::1	2
192.0.2.1	1
::ffff:192.0.2.1	1
```

## Network filtering

With no arguments, every valid input address is counted. The optional `--network <CIDR>`
counts only addresses contained in that network, retaining first-seen order among the
matching values. The CIDR base must have zero host bits: `192.0.2.0/24` is valid, while
`192.0.2.37/24` is rejected. Prefix lengths may have one through three decimal digits,
including leading zeroes. See [IpNetwork](../../docs/IP_NETWORK.md) for strict parsing
and containment rules.

IPv4 and IPv4-mapped IPv6 remain different address families. Even an IPv4 `/0` filter
excludes mapped IPv6 values. An IPv6 filter only matches IPv6. Input lines contain plain
numeric addresses. Ports, brackets, subnet suffixes, and zone identifiers are rejected.
See [IpAddress](../../docs/IP_ADDRESS.md) for the strict parsing and canonical
formatting rules.

## Input

ASCII whitespace around a line is ignored, as are blank lines. The caller-owned input
buffer accepts at most 255 payload bytes per line, including whitespace. LF and CRLF
both end lines. Parsed keys own their numeric values independently of that reused
buffer. The hash table and first-seen vector allocate through `Memory`.

## Errors

Invalid arguments or CIDR text exit `2` before reading input. Invalid addresses produce
a line-number diagnostic and exit `2`. Every input line is validated before filtering.
Malformed text outside the selected network is still an error. Input is fully validated
before counts are printed, so an invalid suffix produces no stdout, even after matching
lines have been counted. IO failures, overlong lines, allocation failures, and counter
overflow exit `1`. Output failures can leave a printed prefix. Success exits `0`.
