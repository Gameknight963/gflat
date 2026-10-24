# Windows performance counter

Optional Windows x64 source library. Add a project reference to `libraries/platform/windows/Windows.gfproj`; it uses the normal std reference. It is outside automatic std inclusion.

```csharp
alias Timer = std::Stopwatch<Windows::PerformanceCounter>;
```

The provider queries the Windows performance counter and its fixed frequency. It measures intervals, not calendar dates. Timestamp/frequency failures throw exceptions with static messages. Successful readings require no application allocation.

The imports use Kernel32 and resolve through the current Windows toolchain's default native link. No additional linker flags are needed. This provider supports Windows x64; its declarations do not support 32-bit Windows calling conventions.

Primary documentation: [QueryPerformanceCounter](https://learn.microsoft.com/en-us/windows/win32/api/profileapi/nf-profileapi-queryperformancecounter), [QueryPerformanceFrequency](https://learn.microsoft.com/en-us/windows/win32/api/profileapi/nf-profileapi-queryperformancefrequency), [LARGE_INTEGER](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-large_integer-r1).
