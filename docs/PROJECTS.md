# MSBuild projects

A `.gfproj` is an MSBuild XML project. Visual Studio opens that same file through
Microsoft's Common Project System (CPS). No companion project is generated.
The CLI and language server evaluate it using MSBuild from the installed .NET SDK.

## Creating a project

```text
gflat new "My Application"
gflat build "My Application/My Application.gfproj" --run
gflat new Utilities --library
gflat new Minimal --no-std
```

`new` creates a real `.gfproj`, `main.gf`, and a `.gitignore` for build/editor
outputs. It accepts a new or empty directory and never overwrites existing files.
`--name Name` changes the project filename without changing the destination
directory. By default, executables reference std and print a greeting through
`Console`; `--no-std` creates an executable returning zero without a std project
reference. `--library` creates a source library with a small example function.
The compiler's current built-in prelude/runtime behavior is unchanged by
`--no-std`; this option only omits the explicit library reference.

The command finds build rules beside the installed distribution or in its checkout
and writes relative imports/references. Keep the distribution's `build/` and `std/`
directories when installing it. These paths are visible in the generated project;
moving the project relative to the toolchain may require updating them. Open the
generated `.gfproj` directly or add it to your Visual Studio solution.

In a checkout, use `dotnet run --project gflat -- new "My Application"`.

## Project format

```xml
<Project DefaultTargets="Build">
  <Import Project="../../build/Gflat.props" />
  <!-- Paths are relative to this project. Adjust the imports for your directory. -->
  <ItemGroup>
    <Compile Remove="scratch/**/*.gf" />
    <ProjectReference Include="../../std/std.gfproj" />
  </ItemGroup>
  <Import Project="../../build/Gflat.targets" />
</Project>
```

`Gflat.props` includes `**/*.gf`, excluding `bin`, `obj`, `.git`, `.vs`, and
`.local-notes`. MSBuild `Compile` items, conditions, imports, and `Remove` entries
control the source list. Nested projects do not implicitly exclude their sources:
use `Compile Remove="nested/**/*.gf"` when necessary. Set
`EnableDefaultCompileItems` to `false` **before** importing the props file if you
want to list sources explicitly. Use one `.gfproj` per directory for editor discovery.

The default `GflatProjectKind` is `Executable`. A source library declares:

```xml
<PropertyGroup>
  <GflatProjectKind>SourceLibrary</GflatProjectKind>
</PropertyGroup>
```

Put this between the imports. Source libraries contribute sources to consumers;
they do not produce binary libraries. References must point to source libraries.
Transitive references are supported, diamond dependencies are deduplicated, and
cycles are errors. Building a source library checks its sources.

## Building

Install the .NET 10 SDK, then build the compiler once:

```text
dotnet build gflat
dotnet msbuild examples/hello/hello.gfproj -t:Build
dotnet msbuild examples/hello/hello.gfproj -t:Check
dotnet msbuild examples/hello/hello.gfproj -t:Build -p:GflatEmitIR=true
dotnet msbuild examples/hello/hello.gfproj -t:Clean
```

`Rebuild` and a no-op `Restore` target are also provided. `Configuration` defaults
to `Debug`; use `-p:Configuration=Release` to select another configuration. This
selects project conditions and output paths; it does not yet set LLVM optimization.
Native optimization still uses `GFLAT_OPT_LEVEL`.

The compiler CLI delegates project commands to these same targets:

```text
gflat check examples/hello/hello.gfproj
gflat build examples/hello/hello.gfproj --configuration Release --run
gflat build examples/hello/hello.gfproj --emit-ir
gflat clean examples/hello/hello.gfproj
```

In a checkout, substitute `dotnet run --project gflat --` for `gflat`.
Executables default to `bin/<Configuration>/<project-name>.exe`; LLVM output uses
`.ll`. `-o` overrides the CLI output. Clean removes the configured executable,
its default `.ll`, and the generated source list, preserving other files. Custom
IR outputs are retained. Clean currently requires an `.exe` target name.

The targets choose `GflatCompilerCommand` if explicitly configured, otherwise the
VSIX's bundled compiler when building in Visual Studio, the checkout's Debug
compiler if available, or `gflat` from PATH. Outside this checkout, distribute
`build/Gflat.props`, `build/Gflat.targets`, and `build/Rules` with your build setup
and adjust imports. An SDK/package distribution mechanism is not implemented yet.

`GflatExceptionReserveBytes` sets the native exception runtime's emergency reserve
for the executable. The default is 65536 bytes; zero disables the reserve. Set it
in a project `PropertyGroup`, or use the **Exception reserve (bytes)** project
property in Visual Studio. `--exception-reserve-bytes N` overrides the project
setting for compiler CLI builds; MSBuild also accepts
`-p:GflatExceptionReserveBytes=N`. Referenced source libraries do not link their
own runtime or choose a separate reserve. Checking and LLVM-only output validate
the setting but do not allocate or link a reserve. The setting will not require
an exception runtime in future builds that omit it.

MSBuild runs build hooks normally. Editor evaluation does not execute targets.
Files generated by build targets are not automatically generated for editor analysis.
Explicit source commands (`gflat main.gf helper.gf`) still work without loading
MSBuild; a future compiler written in gflat can keep that interface.

Project evaluation, checking, and native builds work on Windows x64 and Linux x64.
Native builds need Clang and the host's C linker/toolchain. The compiler selects
the host target; cross-compilation is not supported.

## Visual Studio

Build and install VSIX 0.4.0, restart Visual Studio, and open `gflat.slnx`.
The `std` and `hello` projects appear directly under the solution beside the C# tools.
Use **Add > Existing Project** for another `.gfproj`.

No special solution folder is required. Visual Studio's optional
[solution folders](https://learn.microsoft.com/en-us/visualstudio/ide/solutions-and-projects-in-visual-studio#solution-folder)
only group projects in Solution Explorer; they do not change project loading or builds.

CPS owns the project tree, standard file commands, project-file editing and reload,
configurations, property pages, and MSBuild invocation. The old handwritten
hierarchy and its custom file dialogs have been removed. The extension supplies a
`.gf` item template and the bundled compiler location.

The gflat projects remain excluded from the checked-in solution's automatic build
so `dotnet build/test gflat.slnx` can bootstrap the tools. Build a gflat project
explicitly in VS, or enable it in Configuration Manager after the compiler exists.
The available configurations are `Debug|AnyCPU` and `Release|AnyCPU`; `AnyCPU` is
an MSBuild configuration label, not a native ABI selection.

The language server discovers the nearest project and includes referenced sources
and unsaved buffers. Evaluation is cached between edits; source and imported
project-file changes invalidate it. Invalid project files produce diagnostics while
the previous valid source graph remains available. Editor evaluation currently
uses `Debug|AnyCPU` independently of the active VS build configuration.

## Remaining work

Debugger/startup-project support, incremental native builds, package management,
and binary libraries are not included. There is no Visual Studio New Project
wizard yet; use `gflat new` and add the generated project to your solution.

The previous `<GflatProject>` XML prototype is no longer supported. Convert it to
`<Project>`, import the build files, and replace `Sources`/`Reference` with MSBuild
`Compile`/`ProjectReference` items as shown above.

References: [Microsoft's CPS documentation](https://github.com/microsoft/VSProjectSystem)
and [MSBuild project files](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-project-file-schema-reference).

## Native optimization

Use `--optimization 0|1|2|3` with standalone compilation or project builds.
Projects default to 0 in Debug and 2 in Release. Set `GflatOptimizationLevel` in a
project property group, or use the optimization field in Visual Studio's project
properties. An explicit CLI option overrides the project setting.

For standalone compilation, `GFLAT_OPT_LEVEL` supplies the default when no CLI
option is given; otherwise the default is 0. Project builds use their evaluated
project setting instead of that environment variable. These levels control Clang
and LLVM optimization; they do not change language semantics.

```powershell
gflat build app/app.gfproj --configuration Release --optimization 2
```
