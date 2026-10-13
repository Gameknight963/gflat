// Called at main, a nothrow boundary, or a destructor when an exception escapes.
// The pointer is borrowed and may be null for a foreign exception.
// The native runtime passes the returned status to libc exit().
// Throwing from this hook terminates with status 1.
namespace Exceptions {
    public weak int Unhandled(Exception*? error) {
        if (error == null) return 1;
        return error!.GetCode();
    }
}
