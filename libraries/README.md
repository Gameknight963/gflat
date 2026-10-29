# Optional libraries

These source-library projects are included explicitly by applications. They are
distributed alongside std, without becoming part of its default source set.

| Project | Provider | Target |
| --- | --- | --- |
| [Windows](platform/windows/Windows.gfproj) | `Windows::PerformanceCounter`, `Windows::SystemClock` | Windows x64 |
| [Windows random bytes](platform/windows-random/WindowsRandom.gfproj) | `Windows::SystemRandom` | Windows 8+ x64; explicit `bcrypt` linkage |
| [Windows TCP](platform/windows-network/WindowsNetwork.gfproj) | `Windows::TcpClient`, `Windows::SocketException` | Windows 64-bit; explicit `ws2_32` linkage |
| [Linux](platform/linux/Linux.gfproj) | `Linux::MonotonicClock`, `Linux::SystemClock` | Linux LP64 |
| [Linux random bytes](platform/linux-random/LinuxRandom.gfproj) | `Linux::SystemRandom` | Linux; glibc 2.25+, kernel 3.17+ |

The monotonic providers implement std's static `IMonotonicClock` contract. A program
can use `std::Time::Stopwatch<Windows::PerformanceCounter>`, for example, or define a
local alias for that specialization. See [interval timing](../docs/STOPWATCH.md).

Each platform's `SystemClock::GetUtcNow()` returns a `std::Time::DateTime` interpreted
as UTC. Wall-clock time can jump when adjusted; use the monotonic providers for
intervals. The project READMEs describe platform requirements and precision.

Reference the appropriate project after importing Gflat.props:

```xml
<ItemGroup>
  <ProjectReference Include="$(GflatSdkRoot)/libraries/platform/windows/Windows.gfproj" />
</ItemGroup>
```

Choose the project that matches the application's target. Merely distributing
both providers does not import their native functions into ordinary std builds.
Open [the standard-library solution](../std/std.slnx) to edit these libraries
alongside std.
