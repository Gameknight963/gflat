using std::IO;
namespace Windows;

/// WinSock failure. GetCode returns the native WSA error code.
public class SocketException : IOException
{
    public SocketException(readonly(char)* message, int error) : base(message)
    {
        code = error;
    }
}