# Optional libraries

These source-library projects are included explicitly by applications. They are
distributed alongside std, without becoming part of its default source set.

| Project | Provider | Target |
| --- | --- | --- |
| [Windows](platform/windows/Windows.gfproj) | `Windows::PerformanceCounter` | Windows x64 |
| [Linux](platform/linux/Linux.gfproj) | `Linux::MonotonicClock` | Linux LP64 |

The clock providers implement std's static `IMonotonicClock` contract. A program
can use `std::Stopwatch<Windows::PerformanceCounter>`, for example, or define a
local alias for that specialization. See [interval timing](../docs/STOPWATCH.md).

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
