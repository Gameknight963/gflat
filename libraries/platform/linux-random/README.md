# Linux system random bytes

`Linux::SystemRandom::TryFill(output, count)` fills caller-owned storage using
libc `getentropy`. It requires glibc 2.25 or newer and Linux kernel 3.17 or newer.

```xml
<ItemGroup>
  <ProjectReference Include="$(GflatSdkRoot)/libraries/platform/linux-random/LinuxRandom.gfproj" />
</ItemGroup>
```

The project uses the toolchain's ordinary libc link and can be selected
independently of the clock library. Direct source users must target an ABI where
`nuint` matches C `size_t`. The required libc symbol must be available when linking
and loading the program.

```csharp
int main()
{
    byte[32] bytes;
    if (!Linux::SystemRandom::TryFill(bytes, 32)) return 1;
    // Use the bytes only after success.
    return 0;
}
```

## Results and storage

- Supply live writable storage for the complete requested range
- True means every byte was filled
- Zero count succeeds without accessing the buffer or calling libc
- False means a native call failed. Discard the complete output because earlier
  chunks and other requested bytes may have changed

The `nothrow` wrapper calls `getentropy` in chunks of at most 256 bytes and stops
at the first failure. libc handles interrupts and complete chunk delivery. The
call can block while the system entropy pool initializes, including across
handled signals. The wrapper retains no buffer pointer and makes no gflat
allocation. libc and the kernel may allocate internally.

Native references: [getentropy](https://man7.org/linux/man-pages/man3/getentropy.3.html),
[getrandom](https://man7.org/linux/man-pages/man2/getrandom.2.html).
