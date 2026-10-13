// Called at main, a nothrow boundary, or a destructor when an exception escapes.
// The pointer is borrowed and may be null for a foreign exception.
// Returning or throwing from this hook triggers runtime termination with status 1.
extern void exit(int status);

namespace Exceptions {
    public weak void Unhandled(Exception*? error) {
        int status = 1;
        if (error != null) status = error!.GetCode();
        exit(status);
    }
}
