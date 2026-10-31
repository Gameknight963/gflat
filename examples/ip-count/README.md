# Count IP addresses

Read IPv4 or IPv6 addresses from stdin and print occurrence counts in
first-seen order. Equivalent spellings share one count:

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

## Input and filtering

Each nonblank line contains a plain numeric address. Surrounding ASCII whitespace
is ignored. Lines can contain at most 255 bytes, excluding LF or CRLF endings.

`--network <CIDR>` counts only addresses inside that network. Its base must have
zero host bits, such as `192.0.2.0/24`. Every line is validated before filtering.
IPv4 and IPv4-mapped IPv6 are separate families, so an IPv4 filter excludes
mapped IPv6 addresses.

See [IpAddress](../../docs/IP_ADDRESS.md) and [IpNetwork](../../docs/IP_NETWORK.md)
for accepted numeric spellings.

## Results and errors

All input is checked before counts are printed. Invalid arguments or addresses
exit 2, with line numbers for invalid input. Overlong lines, IO, allocation, or
counter overflow exit 1. Errors go to stderr. Success exits 0.
