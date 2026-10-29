using std::IO;
using std::Net;
namespace Windows;

namespace tcp_detail
{
    private extern int WSAStartup(ushort version, void* data) nothrow;
    private extern int WSACleanup() nothrow;
    private extern int WSAGetLastError() nothrow;
    private extern nuint socket(int family, int kind, int protocol) nothrow;
    private extern int connect(nuint socket, readonly(void)* address, int length) nothrow;
    private extern int recv(nuint socket, char* data, int count, int flags) nothrow;
    private extern int send(nuint socket, readonly(char)* data, int count, int flags) nothrow;
    private extern int shutdown(nuint socket, int how) nothrow;
    private extern int closesocket(nuint socket) nothrow;
}

/// Owns one blocking WinSock TCP connection and its startup reference.
/// Default construction is closed; move transfers both resources.
public struct TcpClient : ITextWriter
{
    private struct Session
    {
        private bool active;

        public Session() nothrow { active = false; }

        public static Session Start()
        {
            Session result = new();
            // Win64 WSADATA is 408 bytes with 8-byte alignment. Reserve 512
            // aligned bytes, and inspect only its first WORD (negotiated version).
            nuint[64] data;
            for (nuint i = 0; i < 64; i++) data[i] = 0;
            int error = tcp_detail::WSAStartup((ushort)514, (void*)&data[0]);
            if (error != 0) throw new SocketException("WinSock startup failed", error);
            result.active = true;
            ushort* version = (ushort*)&data[0];
            if (*version != (ushort)514)
                throw new SocketException("WinSock 2.2 is unavailable", 10092);
            return move result;
        }

        // Keep ownership on failure so explicit Close or destruction can retry.
        public int Release() nothrow
        {
            if (!active) return 0;
            if (tcp_detail::WSACleanup() != 0) return tcp_detail::WSAGetLastError();
            active = false;
            return 0;
        }

        ~Session() { this.Release(); }
    }

    private Session session;
    private nuint handle;

    public TcpClient() nothrow
    {
        session = new();
        handle = (nuint)0 - 1;
    }

    private static bool HasCompatibleLayout() nothrow => sizeof(nuint) == 8;

    /// Connects a numeric IPv4 or IPv6 endpoint. IPv6 flow and scope are zero.
    /// This is blocking; no DNS lookup, proxy, timeout or TLS layer is added.
    public static TcpClient Connect(IpEndpoint endpoint)
    {
        if (!HasCompatibleLayout()) throw new IOException("TcpClient requires Windows 64-bit");
        TcpClient result = new();
        result.session = Session::Start();
        IpAddress address = endpoint.Address;
        int family = 2;
        int length = 16;
        nuint addressOffset = 4;
        if (!address.IsIpv4) { family = 23; length = 28; addressOffset = 8; }
        // sockaddr_in is 16 bytes; sockaddr_in6 is 28 bytes. Eight-byte alignment
        // also satisfies their native 4-byte requirement. Unused fields stay zero.
        nuint[4] storage;
        for (nuint i = 0; i < 4; i++) storage[i] = 0;
        ushort* nativeFamily = (ushort*)&storage[0];
        *nativeFamily = (ushort)family;
        byte* bytes = (byte*)&storage[0];
        ushort port = endpoint.Port;
        bytes[2] = (byte)(port >> 8);
        bytes[3] = (byte)port;
        nuint written = 0;
        if (!address.TryWriteBytes(bytes + addressOffset, address.ByteCount, ref written))
            throw new IOException("Socket address encoding failed");
        result.handle = tcp_detail::socket(family, 1, 6);
        if (result.handle == (nuint)0 - 1)
        {
            int error = tcp_detail::WSAGetLastError();
            throw new SocketException("Socket creation failed", error);
        }
        if (tcp_detail::connect(result.handle, (readonly(void)*)&storage[0], length) != 0)
        {
            int error = tcp_detail::WSAGetLastError();
            throw new SocketException("Socket connection failed", error);
        }
        return move result;
    }

    public bool IsOpen { immutable get nothrow => handle != (nuint)0 - 1; }

    private void RequireOpen()
    {
        if (handle == (nuint)0 - 1) throw new IOException("TcpClient is closed");
    }

    /// Reads up to count bytes. A short read is normal; zero means peer EOF.
    /// Does not append a terminator. A closed client rejects even zero count.
    public nuint Read(char* data, nuint count)
    {
        this.RequireOpen();
        if (count == 0) return 0;
        nuint request = count;
        if (request > 2147483647) request = 2147483647;
        int read = tcp_detail::recv(handle, data, (int)request, 0);
        if (read < 0)
        {
            int error = tcp_detail::WSAGetLastError();
            throw new SocketException("Socket read failed", error);
        }
        return (nuint)read;
    }

    /// Loops through short reads; peer EOF before count throws EndOfStreamException.
    /// Earlier input has already been consumed and stored when an error escapes.
    public void ReadExactly(char* data, nuint count)
    {
        this.RequireOpen();
        nuint offset = 0;
        while (offset < count)
        {
            nuint read = this.Read(data + offset, count - offset);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }

    /// Sends every counted byte on success, preserving embedded zeros.
    /// Failure may follow a sent prefix; success does not promise peer delivery.
    public void Write(readonly(char)* data, nuint count)
    {
        this.RequireOpen();
        nuint offset = 0;
        while (offset < count)
        {
            nuint request = count - offset;
            if (request > 2147483647) request = 2147483647;
            int sent = tcp_detail::send(handle, data + offset, (int)request, 0);
            if (sent < 0)
            {
                int error = tcp_detail::WSAGetLastError();
                throw new SocketException("Socket write failed", error);
            }
            if (sent == 0) throw new IOException("Socket write made no progress");
            offset += (nuint)sent;
        }
    }

    /// Half-closes sending. Receiving remains available until EOF or Close.
    public void ShutdownSend()
    {
        this.RequireOpen();
        if (tcp_detail::shutdown(handle, 1) != 0)
        {
            int error = tcp_detail::WSAGetLastError();
            throw new SocketException("Socket send shutdown failed", error);
        }
    }

    /// On socket-close failure ownership remains here. On successful socket close,
    /// IsOpen becomes false; a cleanup failure retains the startup ref for retry.
    public void Close()
    {
        if (handle != (nuint)0 - 1)
        {
            if (tcp_detail::closesocket(handle) != 0)
            {
                int error = tcp_detail::WSAGetLastError();
                throw new SocketException("Socket close failed", error);
            }
            handle = (nuint)0 - 1;
        }
        int error = session.Release();
        if (error != 0) throw new SocketException("WinSock cleanup failed", error);
    }

    ~TcpClient()
    {
        // Session's field destructor runs after this body, even on error paths.
        if (handle != (nuint)0 - 1) tcp_detail::closesocket(handle);
    }
}