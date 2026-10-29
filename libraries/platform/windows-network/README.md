# Windows TCP client

Optional `Windows::TcpClient` for blocking numeric IPv4/IPv6 TCP connections on
64-bit Windows. Reference `WindowsNetwork.gfproj`; its source-library reference
propagates the `ws2_32` native link dependency. It is separate from portable std.

```xml
<ItemGroup>
  <ProjectReference Include="$(GflatSdkRoot)/libraries/platform/windows-network/WindowsNetwork.gfproj" />
</ItemGroup>
```

Direct source/CLI consumers also supply `--native-library ws2_32`. Project
references propagate that requirement to the final executable.

The move-only value owns its socket and WinSock startup reference. Default
construction is closed. `Connect(IpEndpoint)` opens a connection; `IsOpen` reports
whether it still owns a socket. It implements counted `ITextWriter::Write` and
`std::IO::IByteReader::Read`.

| API | Behavior |
| --- | --- |
| `Read(char* data, nuint count)` | One read, up to 2,147,483,647 bytes; returns the actual count, zero at peer EOF. |
| `ReadExactly(char* data, nuint count)` | Loops over short reads; early EOF throws `std::IO::EndOfStreamException`. |
| `Write(readonly(char)* data, nuint count)` | Sends all counted bytes on success; embedded zeros are preserved. |
| `ShutdownSend()` | Ends sending while receiving remains available. |
| `Close()` | Checks native close/cleanup errors; successful repeated close is a no-op. |

Closed clients reject I/O and shutdown, including zero-count I/O. Read/Write errors
can follow an already consumed/sent prefix. `SocketException` derives from
`std::IO::IOException`; `GetCode()` returns the captured native WSA error.
A failed socket close retains ownership for retry. If the socket closes but
WinSock cleanup fails, `IsOpen` is false and another `Close` retries that cleanup.
Destruction attempts cleanup without reporting errors.

Keep borrowed buffers valid for the full count. Coordinate calls on a shared
client; Close must not race with I/O. Write success means the OS accepted bytes,
not that the peer received them. Use protocol-appropriate half-close and receive
until EOF before closing when graceful delivery matters. The last WinSock cleanup
can discard queued data, so destruction performs no hidden blocking drain.

IPv6 uses scope0; zone identifiers, DNS, TLS, async, listeners, socket options and
timeout configuration are outside this API. Blocking operations follow native OS
waiting behavior. No global initializer, background thread, or polling is used.

For a protocol where the peer replies after request EOF and then ends its reply:

```csharp
void PrintReply(std::Net::IpEndpoint endpoint)
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

TCP is a byte stream; read boundaries are independent of write boundaries.
Other protocols may use lengths or delimiters instead of EOF.

Native lifetime details: [WSAStartup](https://learn.microsoft.com/en-us/windows/win32/api/winsock/nf-winsock-wsastartup),
[closesocket](https://learn.microsoft.com/en-us/windows/win32/api/winsock/nf-winsock-closesocket),
and [WSACleanup](https://learn.microsoft.com/en-us/windows/win32/api/winsock/nf-winsock-wsacleanup).
