# MSBuild projects

A `.gfproj` is an MSBuild XML project. Visual Studio opens that same file through
Microsoft's Common Project System (CPS). No companion project is generated.
The CLI and language server evaluate it using their bundled MSBuild engine.
Visual Studio uses its own MSBuild engine for CPS evaluation and IDE builds.

## SDK-style projects

The CLI and language server bundle `Gflat.Sdk` alongside their private MSBuild:

```xml
<Project Sdk="Gflat.Sdk">
  <PropertyGroup>
    <OutputType>Executable</OutputType>
  </PropertyGroup>
</Project>
```

Use the same `build`, `check`, and `clean` commands for this format. Project
references may use either SDK-style or explicit-import projects. Source selection,
standard-library selection, configurations, and incremental builds use the same
build rules. The CLI and language server resolve the SDK from their own package
and require no registration, NuGet download, or setup command. Their SDK search
directory is private to those processes; an inherited Visual Studio or .NET SDK
search path does not select their engine or SDK.

For Visual Studio and its standalone MSBuild, run the Windows extension's
[SDK setup script](EDITOR_SUPPORT.md#optional-sdk-style-project-setup). This is
separate from CLI setup. To select another toolchain for SDK-style projects,
set `GflatSdkRoot` in the environment before evaluation; SDK props are imported
before the project body. It must name a directory containing `build/Gflat.props`
and `build/Gflat.targets`.

## Build-rule imports

The Windows Visual Studio extension also provides optional
[SDK install/uninstall scripts](EDITOR_SUPPORT.md#optional-sdk-style-project-setup)
for SDK-style projects opened by Visual Studio's MSBuild. The explicit-import
format below works with the CLI's private engine without installing anything.

Projects import the toolchain's build rules explicitly. No MSBuild SDK registration
or administrator setup command is required:

```xml
<Project DefaultTargets="Build">
  <PropertyGroup>
    <GflatSdkRoot Condition="'$(GflatSdkRoot)' == '' and '$(GflatDefaultSdkRoot)' != ''">$(GflatDefaultSdkRoot)</GflatSdkRoot>
    <GflatSdkRoot Condition="'$(GflatSdkRoot)' == ''">../path/to/gflat</GflatSdkRoot>
  </PropertyGroup>
  <Import Project="$(GflatSdkRoot)/build/Gflat.props" />
  <PropertyGroup>
    <OutputType>Executable</OutputType>
  </PropertyGroup>
  <Import Project="$(GflatSdkRoot)/build/Gflat.targets" />
</Project>
```

`gflat new` writes the relative fallback path for you. The compiler and language
server supply `GflatDefaultSdkRoot` from their own distribution, so a moved project
can use the currently running toolchain. An explicit `GflatSdkRoot` takes priority.
Visual Studio supplies that property from the installed VSIX, including during
initial project evaluation. Its templates contain no installation-specific path.

For standalone MSBuild, the relative fallback must locate the toolchain. Otherwise
set `GflatSdkRoot` in the environment or pass `-p:GflatSdkRoot=/path/to/gflat`.
The directory must contain the complete `build/` directory, including the
precompiled tasks under `build/tasks/`; keep
`std/` there too when using the standard library. Builds do not install files into
.NET or Visual Studio.

Downloaded compiler and language-server bundles include MSBuild and the .NET runtime.
Using them does not require an installed .NET SDK, registering an SDK, or copying
files into Visual Studio. Clang and the native platform toolchain are still required
to produce native executables. The .NET SDK is required to build gflat itself from source.

## Creating a project

```text
gflat new "My Application"
gflat build "My Application/My Application.gfproj" --run
gflat new Utilities --library
gflat new Minimal --no-stdlib
```

`new` creates the MSBuild `.gfproj`, `main.gf`, and a `.gitignore` in a new or
empty directory. It never overwrites existing files. `--name Name` changes the
project filename. Applications use std and print a greeting by default.
`--library` creates a source library. `--no-stdlib` sets `NoStdLib` in the project.

The default `OutputType` is `Executable`. A source library uses `SourceLibrary`:

```xml
<Project DefaultTargets="Build">
  <Import Project="$(GflatSdkRoot)/build/Gflat.props" />
  <PropertyGroup>
    <OutputType>SourceLibrary</OutputType>
    <NoStdLib>true</NoStdLib>
  </PropertyGroup>
  <Import Project="$(GflatSdkRoot)/build/Gflat.targets" />
</Project>
```

A `SourceLibrary` contributes source files to projects that reference it. Building
it checks those sources without producing an executable or LLVM output.
It has no `TargetPath`; inherited executable output settings are ignored. `Clean`
removes its generated source lists and build stamps, leaving other files intact.
Use an `Executable` project to run the library or emit LLVM output.

Standalone library builds and checks validate a complete compilation, so every
`expect` declaration needs an implementation even when unused. A consuming project
can provide those implementations: references collect the library's sources
without separately building or checking the library first.

Source libraries don't produce binary libraries. Project references must name
source libraries. Transitive references are supported, diamond dependencies
deduplicated, and cycles rejected.

## Standard library selection

The build rules reference the bundled `std/std.gfproj` unless `NoStdLib` is `true`.
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

The build rules include `**/*.gf`, excluding `bin`, `obj`, `.git`, `.vs`, and
`.local-notes`. Use ordinary MSBuild items and conditions:

```xml
<Project DefaultTargets="Build">
  <Import Project="$(GflatSdkRoot)/build/Gflat.props" />
  <ItemGroup>
    <Compile Remove="scratch/**/*.gf" />
    <ProjectReference Include="../Utilities/Utilities.gfproj" />
  </ItemGroup>
  <Import Project="$(GflatSdkRoot)/build/Gflat.targets" />
</Project>
```

Nested project directories are not automatically excluded: remove their globs
when needed. Set `EnableDefaultCompileItems` to `false` to list sources explicitly.
Standard `Directory.Build.props` and `Directory.Build.targets` files apply to
projects in their directory tree, including editor evaluation. Use them for shared
settings such as `NoStdLib` and shared build hooks.
Use one `.gfproj` per directory for editor discovery. The toolchain props and targets
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

The bundled `gflat build`, `check`, and `clean` commands run the project targets
without an installed .NET SDK. You can also use an installed MSBuild directly.
For example, when developing gflat with the .NET 10 SDK:

```text
dotnet build gflat
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

An output newer than its configuration's success stamp is rebuilt, including when
another configuration overwrites a shared output directory. As with other
timestamp-based build checks, manually restoring old output timestamps can hide
an external modification; use `Rebuild` after doing so.

Library cleaning removes only intermediate files. Executable cleaning removes the
configured `TargetPath` and `GflatIROutput`, after checking that neither overlaps
sources, project files, or imports anywhere in the reference graph.

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

## Native libraries

Foreign `extern` declarations describe calls; native link requirements provide
the object files or libraries implementing them. Repeat these options on an
explicit-source command or an executable project build:

| CLI option | Project item | Meaning |
| --- | --- | --- |
| `--native-library NAME` | `<NativeLibrary Include="NAME" />` | A logical library name resolved by the selected native toolchain. Supply the name without `-l`. |
| `--native-library-file PATH` | `<NativeLibraryFile Include="PATH" />` | An explicit native object, archive, or other library file accepted by that linker. |
| `--native-library-dir PATH` | `<NativeLibraryDirectory Include="PATH" />` | A native library search directory. |

Quote paths containing spaces. A library name is not a gflat namespace or a
source-project reference. Native linking does not add declarations, generate
bindings, or change the foreign calling convention or type layout.

For example, a project can declare a Windows SDK library without adding a
binding to its functions:

```xml
<ItemGroup Condition="'$(OS)' == 'Windows_NT'">
  <NativeLibrary Include="bcrypt" />
</ItemGroup>
```

Use names and binary formats supported by your host toolchain. A native file
must match the target architecture and ABI; adding a file does not convert it.
Native code and its dependencies are built separately from the gflat sources.

### Requirements from source libraries

All three items may appear in a `SourceLibrary`. The final executable collects
requirements through its project-reference graph, so consumers do not need to
repeat each dependency's native items. File and directory paths are relative to
the project that declares them, not to the executable that eventually links.
Explicit CLI paths are relative to the command's working directory.

Native requirements are ordered with each owning project before its referenced
dependencies, preserving declaration order where dependency ordering allows.
Shared dependencies follow all their owners. Within a project, evaluated
`NativeLibrary` and `NativeLibraryFile` entries keep their interleaved order and
repeats; repeated libraries are not
removed. This matters for archive linkers that resolve dependencies from left
to right. Search directories keep first-occurrence order, with duplicates
removed. Additional CLI requirements append after the project's requirements;
they do not replace them.

For incremental builds, explicit native files are tracked as inputs. Logical
library names conservatively force a new link because search resolution can change.

`check`, `--check`, and `--emit-ir` validate the source and link metadata without
linking. Native files and libraries need not be installed for those operations.
An executable build invokes Clang with the collected requirements; missing or
incompatible native inputs are then native compiler/linker errors.

Link-time library selection does not configure runtime shared-library lookup.
The normal operating-system loader policy applies. gflat does not copy DLLs or
shared libraries beside the executable or add an rpath automatically.

### A small C object

Save this as `native_add.c` and build it with the native compiler for the same
host target as gflat:

```c
int native_add(int left, int right)
{
    return left + right;
}
```

```text
clang -c native_add.c -o native_add.o
```

Save the caller as `main.gf`:

```csharp
extern int native_add(int left, int right) nothrow;
int main() nothrow
{
    return native_add(20, 22) - 42;
}
```

```text
gflat main.gf --no-stdlib --native-library-file native_add.o --run
```

The program exits with zero. The explicit object supplies the C symbol; this
caller needs no standard-library source. Its `nothrow` declaration is the
foreign contract for this C implementation, not an inferred exception policy.

The same input can be recorded in a project, relative to that project file:

```xml
<Project Sdk="Gflat.Sdk">
  <PropertyGroup>
    <NoStdLib>true</NoStdLib>
  </PropertyGroup>
  <ItemGroup>
    <NativeLibraryFile Include="native_add.o" />
  </ItemGroup>
</Project>
```

Keep `main.gf`, `native_add.o`, and the project in the same directory, then build
and run the project normally.

## Visual Studio

Build and install the VSIX, restart Visual Studio, and open `gflat.slnx`.
The `std` and `hello` projects appear directly under the solution beside the C# tools.
Use **Add > Existing Project** for another `.gfproj`.

Use **File > New > Project** and search for **gflat** to create a Console
Application or Source Library directly in your solution. These are native Visual
Studio project templates, generated from the same starting files as `gflat new`.
They import the same build rules as CLI and standalone MSBuild projects. CPS
supplies the bundled toolchain path; no SDK registration is required. Templates
do not save a version-specific extension path in project files.

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

Set an executable `.gfproj` as the startup project and use **F5** to debug or
**Ctrl+F5** to run without debugging. CPS uses the active configuration's `TargetPath`
and Visual Studio's normal build and launch services. Source libraries and LLVM
IR output configurations cannot launch.

The **gflat application** launch page provides command arguments and a working
directory, which defaults to the project directory. Relative working directories
are resolved from that directory. These settings are stored per configuration in
`.gfproj.user`, which generated projects ignore in Git.

`DebugSymbols` defaults to `true` for Debug and `false` for other configurations.
It emits LLVM source locations and descriptions of parameters, locals, pointers,
buffers, and aggregate fields. Windows builds write a PDB beside the executable;
Linux builds embed DWARF. Standalone compilation uses `--debug` (or `-g`).
Use `--no-debug` to override the project setting. Optimization is independent;
start with optimization level 0 for predictable stepping and variable inspection.
Watch initially uses the native debugger's expression evaluator, so gflat-specific
expressions and property evaluation are not fully supported.

#### Exception debugging in Visual Studio

Debug builds report gflat exception type names, messages, and codes through the
native debugger. Use **Debug > Windows > Exception Settings > C++ Exceptions**
and add a gflat type name (for example `App::Problem`) to break when it is thrown.
gflat does not enable first-chance breaking by default or override your settings.
Explicit `throw;` reports a rethrow; internal cleanup propagation does not.

An exception reaching a termination boundary reports an unhandled event before
calling `Exceptions::Unhandled`. Continue execution to run the hook and terminate.
The preview reads the stored `Exception.message` and `Exception.code` fields;
it does not execute overridden getters. C++ transport events can still appear in
the Debug output and when breaking on all C++ exceptions.

The implementation uses [LLVM debug metadata](https://llvm.org/docs/SourceLevelDebugging.html)
and [Visual Studio's native debugger extension API](https://github.com/microsoft/ConcordExtensibilitySamples).
On Windows with Visual Studio and its extensibility tools installed, run
`editors/VisualStudio/test-debugger.ps1` after building the VSIX for a live
breakpoint, Watch, exception-settings, rethrow, and continuation test. It installs
only into a disposable experimental profile and cleans that profile afterward.

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

Package management and binary libraries are not included.

The previous `<GflatProject>` XML prototype is no longer supported. Convert it to
an MSBuild `<Project>` with the explicit imports shown above, and replace
`Sources`/`Reference` with `Compile`/`ProjectReference` items. Projects previously
using `<Project Sdk="Gflat.Sdk">` must likewise switch to those imports. The old
SDK registration command has been removed. Any registration files written by an
older version remain untouched; the import-based projects do not use them.

References: [Microsoft's CPS documentation](https://github.com/microsoft/VSProjectSystem)
and [MSBuild project files](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-project-file-schema-reference).

## Native optimization

`NoBuiltins` defaults to `false`, independently of `NoStdLib`. Set
`<NoBuiltins>true</NoBuiltins>` in a property group, or use `--no-builtins` with
standalone or project builds, to disable LLVM's recognition of libc builtin
patterns in generated functions. For example, an ordinary C-string scan can
otherwise be optimized into a `strlen` call even though the source called no
external function. Visual Studio exposes the same setting as **Disable libc
builtin recognition** on the project's General page.

`--builtins` explicitly restores the default behavior and overrides a project's
`NoBuiltins=true`. If both switches are supplied, the last one wins. Changing this
setting invalidates incremental compilation; `check` and source-library builds
accept the same override. The compiler API uses `CompilationOptions.NoBuiltins`.

This is not a guarantee of runtime-independent output. Explicit external calls,
explicit LLVM intrinsics, and other target lowering requirements can still need
libc or compiler runtime helpers. It neither excludes std sources nor changes
startup/linker settings; use `NoStdLib` and an appropriate native link setup
separately when building freestanding code.

Use `--optimization 0|1|2|3` with standalone compilation or project builds.
Projects default to 0 in Debug and 2 in Release. Set `GflatOptimizationLevel` in a
project property group, or use the optimization field in Visual Studio's project
properties. An explicit CLI option overrides the project setting.

For standalone compilation, `GFLAT_OPT_LEVEL` supplies the default when no CLI
option is given; otherwise the default is 0. Project builds use their evaluated
project setting instead of that environment variable. These levels control Clang
and LLVM optimization; they do not change language semantics.

Executable builds give gflat function definitions other than `main` internal LLVM
linkage, allowing LLVM to remove unused definitions. Native compilation also places
functions and data in separate sections and asks the linker to discard unreferenced sections,
including at optimization level 0. Native callbacks, virtual/interface tables,
static initialization and cleanup retain the functions they reference.

`--emit-ir` preserves externally linkable function definitions for callers in
other native object files and custom entry points. The compiler API likewise
preserves those definitions by default; use
`CompilationOptions { InternalizeFunctions = true }` for a complete executable
whose gflat functions are only called through that compilation or passed pointers.
This controls native linkage independently of gflat `public`/`private` access.

On Windows, 128-bit integer code uses the selected Clang installation's
`compiler-rt` builtins for operations such as division and remainder. That LLVM
installation must include its builtins library. This also applies to `NoStdLib`
programs: compiler arithmetic helpers are separate from gflat's standard library.
Other hosts keep their normal compiler runtime selection. See Clang's
[compiler runtime documentation](https://clang.llvm.org/docs/Toolchain.html#compiler-runtime).

```powershell
gflat build app/app.gfproj --configuration Release --optimization 2
```
