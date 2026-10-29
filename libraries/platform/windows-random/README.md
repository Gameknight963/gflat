# Windows system random bytes

Optional Windows 8 or newer, x64 source library. Reference
`libraries/platform/windows-random/WindowsRandom.gfproj` to use
`Windows::SystemRandom::TryFill(byte* output, nuint count)`.

```xml
<ItemGroup>
  <ProjectReference Include="$(GflatSdkRoot)/libraries/platform/windows-random/WindowsRandom.gfproj" />
</ItemGroup>
```

The project explicitly supplies the native `bcrypt` library requirement to the
final executable. Direct source/CLI consumers must supply `--native-library bcrypt`.
The current Windows toolchain must already provide the import library; this
library does not install dependencies. It is separate from `Windows.gfproj`,
so clock-only consumers acquire neither the random source nor this link requirement.
It needs no std source or runtime definitions.

```csharp
int main()
{
    byte[32] bytes;
    if (!Windows::SystemRandom::TryFill(bytes, 32)) return 1;
    // Use the bytes only after success.
    return 0;
}
```

The nonthrowing method uses `BCryptGenRandom` with no algorithm handle and the
system-preferred generator flag. It returns true for `STATUS_SUCCESS` and false
for other native statuses. It contains no random algorithm, persistent state,
startup work, handle ownership, or retained buffer pointer. The wrapper performs
no gflat allocation; Windows may allocate internally.

For a positive count, the caller must provide writable storage covering that
many bytes. Zero succeeds without calling Windows or touching the pointer.
Requests above 4,294,967,295 bytes are rejected before access, rather than split
into multiple calls. Positive requests also fail before access if the compiled
pointer layout is not 64-bit; this check is not operating-system detection.

Preflight rejection leaves storage unchanged. After a native failure, any bytes
in the requested range may have changed: discard the entire result. No rollback,
valid-prefix, automatic retry, or error-status retrieval is provided. The API
produces bytes rather than passwords, formatted tokens or bounded integers.

Primary documentation: [BCryptGenRandom](https://learn.microsoft.com/en-us/windows/win32/api/bcrypt/nf-bcrypt-bcryptgenrandom).
