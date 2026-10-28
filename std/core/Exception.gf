// Default exception base. Automatically available unless the program supplies one.
// Borrows its null-terminated message; constructors do not copy or free the bytes.
// Keep message storage alive while the exception or any copy can be inspected,
// including after stack unwinding. String literals satisfy this requirement.
class Exception
{
    public immutable(char*) message;
    public int code;

    public Exception()
    {
        message = "";
        code = 1;
    }

    public Exception(readonly(char)* msg)
    {
        message = msg;
        code = 1;
    }

    public Exception(readonly(char)* msg, int c)
    {
        message = msg;
        code = c;
    }

    public immutable virtual readonly(char)* GetMessage()
    {
        return message;
    }

    public immutable virtual int GetCode()
    {
        return code;
    }
}
