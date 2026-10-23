# Count IP addresses

Read one IPv4 or IPv6 address per line from standard input. Equivalent spellings
share one count; output uses canonical addresses in first-seen order, followed by
a tab and the occurrence count.

```powershell
gflat build examples/ip-count/ip-count.gfproj -o artifacts/ip-count.exe
Get-Content addresses.txt | ./artifacts/ip-count.exe
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

IPv4 and IPv4-mapped IPv6 remain different address families. This tool does not
perform DNS lookups, contact the network, or accept ports, brackets, subnet
prefixes, or zone identifiers. See [IpAddress](../../docs/IP_ADDRESS.md) for the
strict parsing and canonical formatting rules.

ASCII whitespace around a line is ignored, as are blank lines. The caller-owned
input buffer accepts at most 255 payload bytes per line, including whitespace.
LF and CRLF both end lines. Parsed keys own their numeric values independently
of that reused buffer; no String or GC allocation is needed to retain addresses.
The hash table and first-seen vector allocate through `Memory`.

Invalid addresses produce a line-number diagnostic and exit `2`. Input is fully
validated before counts are printed, so an invalid suffix produces no stdout.
IO failures, overlong lines, allocation failures, and counter overflow exit `1`.
Output failures can leave a printed prefix. Success exits `0`.
