namespace gflat
{
    public static class Prelude
    {
        public const string Source = @"
abstract class Attribute
{
    public Attribute() {}
}

class Exception
{
    public readonly(char*) message;
    public int code;

    public Exception()
    {
        message = c"""";
        code = 0;
    }

    public Exception(readonly(char)* msg)
    {
        message = msg;
        code = 0;
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
";

        public const string AllocatorSource = @"
extern void*? malloc(nuint size);
extern void free(void* ptr);

namespace Allocator
{
public weak void*? Allocate(nuint size)
{
    return malloc(size);
}

public weak void Free(void* ptr)
{
    free(ptr);
}
}
";
    }
}
