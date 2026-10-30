# Linux system random bytes

Optional libc-based Linux source library, requiring glibc 2.25 or newer and
Linux kernel 3.17 or newer. Reference
`libraries/platform/linux-random/LinuxRandom.gfproj` to use
`Linux::SystemRandom::TryFill(byte* output, nuint count)`.

```xml
<ItemGroup>
  <ProjectReference Include="$(GflatSdkRoot)/libraries/platform/linux-random/LinuxRandom.gfproj" />
</ItemGroup>
```

This project uses the ordinary Linux toolchain's libc link; no extra native
library flag is required. It needs no Standard source/runtime definitions. It is
separate from `Linux.gfproj`, so existing clock-only consumers do not acquire
its source or newer glibc symbol requirement. Direct source users must target
an ABI where `nuint` matches C `size_t`; the source does not detect the OS or
impose a pointer-width restriction. No library installation or older-system
symbol fallback is supplied.

```csharp
int main()
{
    byte[32] bytes;
    if (!Linux::SystemRandom::TryFill(bytes, 32)) return 1;
    // Use the bytes only after success.
    return 0;
}
```

The nonthrowing wrapper calls `getentropy` in requests of at most 256 bytes.
Each successful native call supplies its complete requested chunk. Interrupt
and short-read handling is delegated to libc; no errno ABI, raw syscall,
file descriptor, RNG algorithm, persistent state or retained pointer is added.
The wrapper performs no gflat allocation; libc and the kernel may allocate
internally. Zero count succeeds without calling libc or touching the pointer.

For a positive count, supply live writable storage covering the requested
extent. A true result means every requested byte was filled. On false, earlier
chunks and possibly other output bytes may have changed: discard the entire
result. There is no rollback, valid-prefix or error-status retrieval
promise. The wrapper makes no further calls after a native failure. The call may block while the
system entropy pool is initialized, even across handled signals; no timeout or
cancellation API is provided.

The glibc and kernel requirements must already be met. A missing libc symbol
can prevent loading/linking; it is not turned into a false result.

Primary documentation: [getentropy](https://man7.org/linux/man-pages/man3/getentropy.3.html),
[underlying getrandom](https://man7.org/linux/man-pages/man2/getrandom.2.html).
