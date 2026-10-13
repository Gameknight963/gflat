// Default exception base. Automatically available unless the program supplies one.
class Exception
{
    public readonly(char*) message;
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

    public readonly virtual readonly(char)* GetMessage()
    {
        return message;
    }

    public readonly virtual int GetCode()
    {
        return code;
    }
}
