# Windows system random bytes

`Windows::SystemRandom::TryFill(output, count)` fills caller-owned storage using
BCryptGenRandom's system-preferred generator. It requires Windows 8 or newer, x64.

```xml
<ItemGroup>
  <ProjectReference Include="$(GflatSdkRoot)/libraries/platform/windows-random/WindowsRandom.gfproj" />
</ItemGroup>
```

Project references propagate the `bcrypt` link requirement. Direct source users
supply `--native-library bcrypt`. The native toolchain must provide its import
library. This project can be selected independently of the clock library.

```csharp
int main()
{
    byte[32] bytes;
    if (!Windows::SystemRandom::TryFill(bytes, 32)) return 1;
    // Use the bytes only after success.
    return 0;
}
```

## Results and storage

- Supply writable storage for the requested count
- True means the complete range was filled
- Zero count succeeds without accessing the buffer or calling Windows
- Counts above 4,294,967,295 bytes and positive requests on a non-64-bit layout
  return false before accessing storage
- A native failure returns false. Discard the complete output range because any
  bytes in it may have changed

The wrapper is `nothrow`, makes one native call, and retains no buffer pointer.
It requires only the native API. Windows may allocate internally.

Native reference: [BCryptGenRandom](https://learn.microsoft.com/en-us/windows/win32/api/bcrypt/nf-bcrypt-bcryptgenrandom).
