#ifndef _WIN32
#define _FILE_OFFSET_BITS 64
#define _POSIX_C_SOURCE 200809L
#endif

#include <errno.h>
#include <stdint.h>
#include <stdio.h>
#ifndef _WIN32
#include <sys/types.h>
#endif

// Standard streams and seek origins are C library macros.
void* __gflat_stdio_stdin(void) { return stdin; }
void* __gflat_stdio_stdout(void) { return stdout; }
void* __gflat_stdio_stderr(void) { return stderr; }

int __gflat_stdio_seek(void* stream, int64_t offset, int origin) {
    int native_origin;
    switch (origin) {
        case 0: native_origin = SEEK_SET; break;
        case 1: native_origin = SEEK_CUR; break;
        case 2: native_origin = SEEK_END; break;
        default: errno = EINVAL; return -1;
    }
#ifdef _WIN32
    return _fseeki64((FILE*)stream, offset, native_origin);
#else
    off_t native_offset = (off_t)offset;
    if ((int64_t)native_offset != offset) { errno = EOVERFLOW; return -1; }
    return fseeko((FILE*)stream, native_offset, native_origin);
#endif
}

int64_t __gflat_stdio_tell(void* stream) {
#ifdef _WIN32
    return _ftelli64((FILE*)stream);
#else
    off_t position = ftello((FILE*)stream);
    int64_t result = (int64_t)position;
    if ((off_t)result != position) { errno = EOVERFLOW; return -1; }
    return result;
#endif
}
