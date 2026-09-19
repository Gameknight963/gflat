namespace gflat
{
    public static class Prelude
    {
        public const string Source = @"
class Exception
{
    public readonly char* message;
    public int code;

    public Exception()
    {
        message = c"""";
        code = 0;
    }

    public Exception(readonly char* msg)
    {
        message = msg;
        code = 0;
    }

    public Exception(readonly char* msg, int c)
    {
        message = msg;
        code = c;
    }

    public readonly virtual readonly char* GetMessage()
    {
        return message;
    }

    public readonly virtual int GetCode()
    {
        return code;
    }
}
";

        public const string AllocatorSource = @"
extern void*? malloc(ulong size);
extern void free(void* ptr);

void*? __gflat_alloc(ulong size)
{
    return malloc(size);
}

void __gflat_free(void* ptr)
{
    free(ptr);
}
";
    }
}
