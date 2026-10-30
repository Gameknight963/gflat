# Windows clocks

Optional Windows x64 source library. Add a project reference to `libraries/platform/windows/Windows.gfproj`; it uses the normal Standard reference. It is outside automatic Standard inclusion.

```csharp
using Standard::Time;
alias Timer = Standard::Time::Stopwatch<Windows::PerformanceCounter>;
```

The provider queries the Windows performance counter and its fixed frequency. It measures intervals, not calendar dates. Timestamp/frequency failures throw exceptions with static messages. Successful readings require no application allocation.

The imports use Kernel32 and resolve through the current Windows toolchain's default native link. No additional linker flags are needed. This provider supports Windows x64; its declarations do not support 32-bit Windows calling conventions.

Primary documentation: [QueryPerformanceCounter](https://learn.microsoft.com/en-us/windows/win32/api/profileapi/nf-profileapi-queryperformancecounter), [QueryPerformanceFrequency](https://learn.microsoft.com/en-us/windows/win32/api/profileapi/nf-profileapi-queryperformancefrequency), [LARGE_INTEGER](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-large_integer-r1).

## UTC wall-clock time

`Windows::SystemClock::GetUtcNow()` returns a `Standard::Time::DateTime` interpreted as
UTC by this method's contract. DateTime itself still carries no timezone or
UTC/local marker. For example, keep the interpretation explicit at the call site:

```csharp
using Standard::Time;
Standard::Time::DateTime utc = Windows::SystemClock::GetUtcNow();
utc.Format(Console::Out);
Console::WriteLine();
```

The method calls Kernel32 GetSystemTimePreciseAsFileTime, then checks and
converts its unsigned 100ns count from the 1601 epoch to DateTime's year-1
epoch. This API requires Windows 8 / Windows Server 2012 or newer, with the
existing Windows x64 calling-convention support. There is no older-OS fallback.
FILETIME cannot represent dates before 1601; returned values beyond year 9999
throw a static-message Exception. The native function returns void and exposes
no failure status.

Wall-clock readings may jump forwards or backwards when the system clock is
adjusted. Use Stopwatch<Windows::PerformanceCounter> for elapsed durations.
100ns representation does not guarantee 100ns clock accuracy. DateTime formatting
emits no Z or offset suffix; label serialized timestamps as UTC yourself when
needed. No local-time conversion, timezone database, leap-second field, or
cross-host synchronization guarantee is supplied. Successful calls make no
application allocation and add no default clock/provider to core Standard.

Primary ABI references: [GetSystemTimePreciseAsFileTime](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getsystemtimepreciseasfiletime),
[FILETIME layout and epoch](https://learn.microsoft.com/en-us/windows/win32/api/minwinbase/ns-minwinbase-filetime).
