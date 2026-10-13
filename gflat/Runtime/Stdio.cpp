// Standard streams are C library macros, not portable linker symbols.
#include <cstdio>
extern "C" {
void* __gflat_stdio_stdin() noexcept { return stdin; }
void* __gflat_stdio_stdout() noexcept { return stdout; }
}
