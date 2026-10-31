# Optional libraries

Reference these source-library projects explicitly to use platform APIs. They
are distributed alongside Standard.

| Project | Provider | Target |
| --- | --- | --- |
| [Windows](platform/windows/Windows.gfproj) | `Windows::PerformanceCounter`, `Windows::SystemClock` | Windows x64 |
| [Windows random bytes](platform/windows-random/WindowsRandom.gfproj) | `Windows::SystemRandom` | Windows 8+ x64. Explicit `bcrypt` linkage |
| [Windows TCP](platform/windows-network/WindowsNetwork.gfproj) | `Windows::TcpClient`, `Windows::SocketException` | Windows 64-bit. Explicit `ws2_32` linkage |
| [Linux](platform/linux/Linux.gfproj) | `Linux::MonotonicClock`, `Linux::SystemClock` | Linux LP64 |
| [Linux random bytes](platform/linux-random/LinuxRandom.gfproj) | `Linux::SystemRandom` | Linux. Glibc 2.25+, kernel 3.17+ |

The monotonic providers implement Standard's static `IMonotonicClock` contract. A program
can use `Standard::Time::Stopwatch<Windows::PerformanceCounter>`, for example, or define a
local alias for that specialization. See [interval timing](../docs/STOPWATCH.md).

Each platform's `SystemClock::GetUtcNow()` returns a `Standard::Time::DateTime` interpreted
as UTC. Wall-clock time can jump when adjusted. Use the monotonic providers for
intervals. The project READMEs describe platform requirements and precision.

Reference the appropriate project after importing Gflat.props:

```xml
<ItemGroup>
  <ProjectReference Include="$(GflatSdkRoot)/libraries/platform/windows/Windows.gfproj" />
</ItemGroup>
```

Choose the project that matches the application's target.
Open [the standard-library solution](../Standard/Standard.slnx) to edit these libraries
alongside Standard.
