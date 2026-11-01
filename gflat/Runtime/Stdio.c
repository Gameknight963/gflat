#ifndef _WIN32
#define _FILE_OFFSET_BITS 64
#define _POSIX_C_SOURCE 200809L
#endif

#include <errno.h>
#include <stdint.h>
#include <stdio.h>
#include <sys/stat.h>
#ifdef _WIN32
#include <io.h>
#else
#include <sys/types.h>
#include <unistd.h>
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

int64_t __gflat_stdio_length(void* stream) {
#ifdef _WIN32
    int descriptor = _fileno((FILE*)stream);
    struct _stat64 details;
    if (descriptor < 0) { errno = EBADF; return -1; }
    if (_fstat64(descriptor, &details) != 0) return -1;
    return details.st_size;
#else
    struct stat details;
    if (fstat(fileno((FILE*)stream), &details) != 0) return -1;
    int64_t length = (int64_t)details.st_size;
    if ((off_t)length != details.st_size) { errno = EOVERFLOW; return -1; }
    return length;
#endif
}

int __gflat_stdio_set_length(void* stream, int64_t length) {
#ifdef _WIN32
    int descriptor = _fileno((FILE*)stream);
    if (descriptor < 0) { errno = EBADF; return -1; }
    return _chsize_s(descriptor, length);
#else
    off_t native_length = (off_t)length;
    if ((int64_t)native_length != length) { errno = EOVERFLOW; return -1; }
    return ftruncate(fileno((FILE*)stream), native_length);
#endif
}

int __gflat_stdio_path_kind(const char* path) {
#ifdef _WIN32
    struct _stat64 details;
    if (_stat64(path, &details) != 0) return -1;
    if ((details.st_mode & _S_IFMT) == _S_IFREG) return 1;
    if ((details.st_mode & _S_IFMT) == _S_IFDIR) return 2;
#else
    struct stat details;
    if (stat(path, &details) != 0) return -1;
    if (S_ISREG(details.st_mode)) return 1;
    if (S_ISDIR(details.st_mode)) return 2;
#endif
    return 0;
}
