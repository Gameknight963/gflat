// Standard streams are C library macros, not portable linker symbols.
#include <stdio.h>

void* __gflat_stdio_stdin(void) { return stdin; }
void* __gflat_stdio_stdout(void) { return stdout; }
void* __gflat_stdio_stderr(void) { return stderr; }
