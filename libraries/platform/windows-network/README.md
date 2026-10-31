# Windows TCP client

`Windows::TcpClient` owns a blocking numeric IPv4/IPv6 TCP connection on 64-bit
Windows. Reference its source project to include the `ws2_32` native library:

```xml
<ItemGroup>
  <ProjectReference Include="$(GflatSdkRoot)/libraries/platform/windows-network/WindowsNetwork.gfproj" />
</ItemGroup>
```

Direct source users supply `--native-library ws2_32`.

## Connecting and exchanging bytes

Default construction is closed. `Connect(IpEndpoint)` opens a connection, and
`IsOpen` reports socket ownership. The move-only value owns its socket and WinSock
startup reference. It implements `ITextWriter` and `Standard::IO::IByteReader`.

| API | Behavior |
| --- | --- |
| `Read(data, count)` | Reads up to 2,147,483,647 bytes and returns the actual count, or zero at peer EOF |
| `ReadExactly(data, count)` | Fills the range or throws `EndOfStreamException` on early EOF |
| `Write(data, count)` | Sends all counted bytes, including embedded zeros |
| `ShutdownSend()` | Ends sending while receiving remains available |
| `Close()` | Reports native close and cleanup errors. Repeated successful close is a no-op |

For a protocol where the peer replies after request EOF and then ends its reply:

```csharp
void PrintReply(Standard::Net::IpEndpoint endpoint)
{
    Windows::TcpClient client = Windows::TcpClient::Connect(endpoint);
    client.Write("ping\n", 5);
    client.ShutdownSend();
    char[1024] data;
    while (true)
    {
        nuint count = client.Read(data, 1024);
        if (count == 0) break;
        Console::Write(data, count);
    }
    client.Close();
}
```

TCP supplies a byte stream. A read can return a different grouping of bytes than
the sender's writes. Follow the protocol's framing rules, such as lengths,
delimiters, or EOF.

## Lifetime and failures

- Keep buffers valid for the full count and coordinate shared-client calls.
  `Close` must not race with IO
- Closed clients reject IO and shutdown, including zero-count IO
- IO failures can leave an already consumed or sent prefix
- `SocketException` derives from `IOException` and captures the WSA error code
- A failed socket close retains ownership for retry
- If socket close succeeds but WinSock cleanup fails, `IsOpen` is false and
  another `Close` retries cleanup
- Destruction attempts cleanup without reporting errors

Write success means the OS accepted the bytes. To confirm protocol delivery,
perform the appropriate half-close and receive through the peer's completion
signal before closing. Final WinSock cleanup can discard queued data.

## Scope

Endpoints are numeric IPv4 or IPv6 with scope zero. Operations block according to
native OS behavior. DNS, TLS, listeners, socket options, timeouts, and async IO
would require additional APIs.

Native lifetime references: [WSAStartup](https://learn.microsoft.com/en-us/windows/win32/api/winsock/nf-winsock-wsastartup),
[closesocket](https://learn.microsoft.com/en-us/windows/win32/api/winsock/nf-winsock-closesocket),
[WSACleanup](https://learn.microsoft.com/en-us/windows/win32/api/winsock/nf-winsock-wsacleanup).
