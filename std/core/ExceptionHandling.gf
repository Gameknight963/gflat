// Called at main, a nothrow boundary, or a destructor when an exception escapes.
// The pointer is borrowed and may be null for a foreign exception.
// Returning from this hook terminates the process. Throwing from it also terminates.
extern void* __gflat_eh_stderr();
extern int fputs(readonly(char*) text, void* stream);
extern void exit(int status);

namespace Exceptions {
    public weak void Unhandled(Exception*? error) {
        fputs(c"Unhandled exception\n", __gflat_eh_stderr());
        exit(1);
    }
}
