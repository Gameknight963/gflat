# MSBuild projects

A `.gfproj` is an MSBuild XML project. Visual Studio opens that same file through
Microsoft's Common Project System (CPS). No companion project is generated.
The CLI and language server evaluate it using MSBuild from the installed .NET SDK.

## Install the project SDK

Project files use MSBuild's native SDK mechanism:

```xml
<Project Sdk="Gflat.Sdk">
  <PropertyGroup>
    <OutputType>Executable</OutputType>
  </PropertyGroup>
</Project>
```

There are no installation paths or explicit build-rule imports in the project.
Install `Gflat.Sdk` once for the MSBuild host you use:

```text
gflat sdk install
```

This registers the current gflat distribution in the active .NET SDK's `Sdks`
directory. It may require administrator permissions. It is an explicit setup
command: ordinary project evaluation and builds never install files or elevate.
Run it again after moving gflat or selecting a newly installed .NET SDK.

Visual Studio's MSBuild has its own SDK directory. Install there too, for example
from an elevated terminal (substitute your edition/version):

```powershell
gflat sdk install --sdk-directory "C:/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Sdks"
```

Use `MSBuild/Current/Bin/Sdks` if that host's `MSBuildSDKsPath` points there instead.
`MSBuildSDKsPath` is MSBuild's SDK search directory, visible with
`dotnet msbuild -getProperty:MSBuildSDKsPath` on a normal .NET project, or the
corresponding Visual Studio `MSBuild.exe` command. Pass that directory to
`--sdk-directory`. The installer creates only its `Gflat.Sdk/Sdk` subdirectory;
it doesn't replace other SDKs or change the global SDK search path.

For the compiler bundled in a VSIX, invoke `Compiler/gflat.exe sdk install`
from the extension installation directory. The bundled SDK remains there;
registration points to it. The download also includes `build/install-sdk.ps1`.

Compiling individual source files does not need MSBuild SDK installation.
See Microsoft's [project SDK documentation](https://learn.microsoft.com/en-us/visualstudio/msbuild/how-to-use-project-sdk)
for SDK resolution and implicit props/targets imports.

## Creating a project

```text
gflat new "My Application"
gflat build "My Application/My Application.gfproj" --run
gflat new Utilities --library
gflat new Minimal --no-stdlib
```

`new` creates the SDK-style `.gfproj`, `main.gf`, and a `.gitignore` in a new or
empty directory. It never overwrites existing files. `--name Name` changes the
project filename. Applications use std and print a greeting by default.
`--library` creates a source library. `--no-stdlib` sets `NoStdLib` in the project.

The default `OutputType` is `Executable`. A source library uses `SourceLibrary`:

```xml
<Project Sdk="Gflat.Sdk">
  <PropertyGroup>
    <OutputType>SourceLibrary</OutputType>
    <NoStdLib>true</NoStdLib>
  </PropertyGroup>
</Project>
```

A `SourceLibrary` contributes source files to projects that reference it. Building
it checks those sources without producing an executable or LLVM output.
It has no `TargetPath`; inherited executable output settings are ignored. `Clean`
removes its generated source lists and build stamps, leaving other files intact.
Use an `Executable` project to run the library or emit LLVM output.

Source libraries don't produce binary libraries. Project references must name
source libraries. Transitive references are supported, diamond dependencies
deduplicated, and cycles rejected.

## Standard library selection

The SDK references its bundled `std/std.gfproj` unless `NoStdLib` is `true`.
With `NoStdLib`, no library reference or hidden compiler declarations are added.
This is how std itself builds. Explicit source files and project references remain
available, so applications can select their own runtime/library implementation.
Excluding std does not turn a bare pointer operation into a library-independent
operation: features that call allocation or exception services need implementations
of those services supplied as ordinary inputs.

```text
gflat main.gf --no-stdlib --run
gflat build app.gfproj --no-stdlib
```

The direct compiler API accepts `CompilationOptions { NoStdLib = true }` for
checking, emission, and editor analysis. Project/editor builds use the evaluated
source graph and never inject another copy of std. This controls source inputs;
it is not a promise to link without the host C startup/runtime libraries.

## Sources and references

The SDK includes `**/*.gf`, excluding `bin`, `obj`, `.git`, `.vs`, and
`.local-notes`. Use ordinary MSBuild items and conditions:

```xml
<Project Sdk="Gflat.Sdk">
  <ItemGroup>
    <Compile Remove="scratch/**/*.gf" />
    <ProjectReference Include="../Utilities/Utilities.gfproj" />
  </ItemGroup>
</Project>
```

Nested project directories are not automatically excluded: remove their globs
when needed. Set `EnableDefaultCompileItems` to `false` to list sources explicitly.
Standard `Directory.Build.props` and `Directory.Build.targets` files apply to
projects in their directory tree, including editor evaluation. Use them for shared
settings such as `NoStdLib` and shared build hooks.
Use one `.gfproj` per directory for editor discovery. SDK-owned props and targets
remain implementation files; projects can still use normal imports for their own
shared build settings. `GflatSdkRoot` can explicitly select another toolchain root
using an environment variable or `-p:GflatSdkRoot=/path/to/gflat`.

To pass program arguments, put them after `--`:

```text
gflat build examples/search/search.gfproj --run -- "input file.txt" "search text"
```

Everything after `--` is a program argument, including `-o` and `--check`.
The separator requires `--run`. The program runs in the caller's working directory
and its exit code becomes the compiler command's exit code.

## Building

Install the .NET 10 SDK, then build the compiler once:

```text
dotnet build gflat
dotnet run --project gflat -- sdk install
dotnet msbuild examples/hello/hello.gfproj -t:Build
dotnet msbuild examples/hello/hello.gfproj -t:Check
dotnet msbuild examples/hello/hello.gfproj -t:Build -p:GflatEmitIR=true
dotnet msbuild examples/hello/hello.gfproj -t:Clean
```

`Rebuild` and a no-op `Restore` target are also provided. `Configuration` defaults
to `Debug` when the project/shared props do not select another value; use
`-p:Configuration=Release` to override it. CLI `--configuration` and `--platform`
likewise override project settings; without them, the evaluated root configuration
is used for both the build and referenced-project source selection. This
selects project conditions, output paths, and the default LLVM optimization level
(Debug: 0, Release: 2). Set `GflatOptimizationLevel` to override that default.

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
its default `.ll`, the generated source list, and incremental stamps, preserving other files. Custom
IR outputs are retained. Clean currently requires an `.exe` target name.

The targets choose `GflatCompilerCommand` if explicitly configured, otherwise the
compiler shipped with the SDK distribution/VSIX, the checkout's Debug compiler
if available, or `gflat` from PATH. Keep the installed SDK and its build/std assets together, as supplied in nightlies and the VSIX.

### Incremental CLI builds

`gflat build` skips executable compilation when its output and success stamp are
newer than all inputs. The input manifests include sources, referenced projects,
evaluated imports, compiler and runtime files, and the native driver. Adding or
removing sources and changing build options also invalidates the build. A failed
compilation removes the success stamp; `gflat clean` removes it too. Library
builds and `gflat check` always run analysis.

Visual Studio and direct MSBuild builds still compile every time unless the
caller supplies the complete evaluated import graph in `GflatProjectInputs`.
The CLI supplies that graph automatically. A custom compiler command remains
uncached unless it declares its dependencies as `GflatCompilerInputs` items.
Native caching also requires an existing driver executable. Names supplied through
`--clang` or `GFLAT_CLANG` resolve through PATH. A bare project `GflatClang` value
that does not identify an existing file remains uncached. This uses MSBuild timestamp checks,
so changing file contents while preserving their timestamps is not detected.

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

Build and install the VSIX, restart Visual Studio, and open `gflat.slnx`.
The `std` and `hello` projects appear directly under the solution beside the C# tools.
Use **Add > Existing Project** for another `.gfproj`.

Use **File > New > Project** and search for **gflat** to create a Console
Application or Source Library directly in your solution. These are native Visual
Studio project templates, generated from the same starting files as `gflat new`.
They use the same `Gflat.Sdk` as CLI and standalone MSBuild projects. Install the
SDK for Visual Studio's MSBuild host as described above; templates do not save a
version-specific extension path in project files.

No special solution folder is required. Visual Studio's optional
[solution folders](https://learn.microsoft.com/en-us/visualstudio/ide/solutions-and-projects-in-visual-studio#solution-folder)
only group projects in Solution Explorer; they do not change project loading or builds.

CPS owns the project tree, standard file commands, project-file editing and reload,
configurations, property pages, and MSBuild invocation. The old handwritten
hierarchy and its custom file dialogs have been removed. The extension supplies a
`.gf` item template, App/Library project templates, and the bundled compiler and SDK locations.

The gflat projects remain excluded from the checked-in solution's automatic build
so `dotnet build/test gflat.slnx` can bootstrap the tools. Build a gflat project
explicitly in VS, or enable it in Configuration Manager after the compiler exists.
The available configurations are `Debug|AnyCPU` and `Release|AnyCPU`; `AnyCPU` is
an MSBuild configuration label, not a native ABI selection.

### Running in Visual Studio

Set an executable `.gfproj` as the startup project and use **Debug > Start
Without Debugging** (**Ctrl+F5**). CPS uses the active configuration's `TargetPath`
and Visual Studio's normal build and launch services. Source libraries and LLVM
IR output configurations cannot launch.

The **gflat application** launch page provides command arguments and a working
directory, which defaults to the project directory. Relative working directories
are resolved from that directory. These settings are stored per configuration in
`.gfproj.user`, which generated projects ignore in Git.

F5 source debugging is not available yet: the compiler does not emit source debug
metadata. The launch provider accepts Start Without Debugging only.

The language server discovers the nearest project and includes referenced sources
and unsaved buffers. Evaluation is cached between edits; source and imported
project-file changes invalidate it. Creating a nearer project or shared
`Directory.Build.props`/`Directory.Build.targets` file also refreshes evaluation.
Invalid project files produce diagnostics while
the previous valid source graph remains available. Editor evaluation currently
uses `Debug|AnyCPU` independently of the active VS build configuration.

## Checking an installation

Run `gflat doctor` to report the host target, compiler directory, SDK location,
MSBuild project evaluation, and selected Clang executable. It does not install
software or change your project. `gflat doctor --native` also compiles and runs
a small exception unwinding probe, checking the native compiler, linker, and C++
runtime together. Temporary probe files are removed afterward.

Use `--clang path` to check a specific driver; otherwise selection follows normal
builds, including `GFLAT_CLANG`. Exit code 0 means the requested checks passed;
2 means at least one check failed. A successful basic check does not establish
that native linking works; use `--native` for that.

## Remaining work

Source debugging, package management,
and binary libraries are not included.

The previous `<GflatProject>` XML prototype is no longer supported. Convert it to
`<Project Sdk="Gflat.Sdk">`, and replace `Sources`/`Reference` with MSBuild
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
