# Windows clocks

Reference `libraries/platform/windows/Windows.gfproj` for Windows x64 clock
providers. The project includes Standard.

```csharp
using Standard::Time;
alias Timer = Standard::Time::Stopwatch<Windows::PerformanceCounter>;
```

`Windows::PerformanceCounter` measures elapsed time with QueryPerformanceCounter
and its fixed frequency. Use it through `Stopwatch` for intervals. Native query
failures throw an exception. Successful readings make no application allocation.
The imports link through Kernel32 with the Windows toolchain's default libraries.

## UTC wall-clock time

`Windows::SystemClock::GetUtcNow()` returns a `Standard::Time::DateTime` representing
UTC:

```csharp
using Standard::Time;
Standard::Time::DateTime utc = Windows::SystemClock::GetUtcNow();
utc.Format(Console::Out);
Console::WriteLine();
```

- Requires Windows 8 / Windows Server 2012 or newer, x64
- Converts GetSystemTimePreciseAsFileTime's unsigned 100ns count from the 1601
  epoch to DateTime's year-1 epoch
- Throws for values beyond year 9999
- Follows system clock adjustments, including backward jumps

`DateTime` carries a civil date and time. Its formatter leaves timezone labeling
to the caller, so add a UTC marker when serializing these values. The 100ns unit
is the representation's resolution. Actual clock accuracy depends on the host.

Native references: [QueryPerformanceCounter](https://learn.microsoft.com/en-us/windows/win32/api/profileapi/nf-profileapi-queryperformancecounter),
[QueryPerformanceFrequency](https://learn.microsoft.com/en-us/windows/win32/api/profileapi/nf-profileapi-queryperformancefrequency),
[GetSystemTimePreciseAsFileTime](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getsystemtimepreciseasfiletime),
[FILETIME](https://learn.microsoft.com/en-us/windows/win32/api/minwinbase/ns-minwinbase-filetime).
