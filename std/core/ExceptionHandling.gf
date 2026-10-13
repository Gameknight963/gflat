// Called at main, a nothrow boundary, or a destructor when an exception escapes.
// The pointer is borrowed and may be null for a foreign exception.
// Returning from this hook terminates the process. Throwing from it also terminates.
namespace Exceptions {
    public weak void Unhandled(Exception*? error) {}
}
