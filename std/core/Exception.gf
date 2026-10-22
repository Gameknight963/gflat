// Default exception base. Automatically available unless the program supplies one.
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
