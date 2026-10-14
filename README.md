![GitHub Repo Views](https://gitviews.com/repo/gameknight963/gflat.svg?style=flat&label-color=purple)
[![compiler](https://github.com/Gameknight963/gflat/actions/workflows/test.yml/badge.svg)](https://github.com/Gameknight963/gflat/actions/workflows/test.yml)

# gflat

A compiled language with C#-inspired syntax, pointers,
scope-based destruction, and native code generation through LLVM

**Work in progress.** The language, standard library, compiler, and project format
can change without notice. Consider currently nightly releases as "demos"

Also this readme might be out of date

```gflat
using std;

extern int printf(readonly(char)* text, ...);

int main()
{
    String message = s$"Hello from gflat! The answer is {42}.";
    printf("%s\n", message.Data);
    return 0;
}
```

This example uses std; see the runnable
[interpolation project](examples/interpolation/interpolation.gfproj).

## What works

- Structs, classes, interfaces, generics, properties, and indexers.
- Pointers, readonly access, custom allocators, and deterministic destruction.
- Nullable pointers with local null-check analysis.
- Namespace extension methods with ordinary value, reference, and readonly receivers.
- Managed pointers (`T^`) with replaceable GC hooks and a single-threaded,
  non-moving conservative collector. See the [managed example](examples/managed/README.md).
- Native stack-unwinding exceptions, with optional `nothrow` termination boundaries.
- Custom string literal prefixes and string interpolation. For example, create a `std::String` with the 's' prefix as shown in the example above
- Multiple source files and MSBuild `.gfproj` projects.
- Project creation with `gflat new`, configurable optimization, and `inline` hints.
- Compile-time `typeof(T)` metadata for names, layout, and recursive type structure.
- A language server and Visual Studio integration with diagnostics, completion,
  syntax highlighting, reference searches, document outlines, and rich hovers.
- A small standard library with owning strings, vectors, console/file IO,
  and allocation-free integer queries. See the [sequence tool](examples/seq/README.md).
- Native Windows x64 and Linux x64 builds


## _Maybe_ in the future

 - Generators?
 - Compile-time member reflection?
 - Random important features it's missing
 - Probably a package manager
 - More editors support (vs code probably)

## Download

These links select the latest successful `master` workflow using
[nightly.link](https://github.com/oprypin/nightly.link). Builds happen on pushes, there's no release schedule, remember I just decided to make this public because why not

| Download | Contents |
| --- | --- |
| [Windows x64](https://nightly.link/Gameknight963/gflat/workflows/test.yml/master/gflat-win-x64.zip) | Compiler, language server, std, project files, examples, and docs |
| [Linux x64](https://nightly.link/Gameknight963/gflat/workflows/test.yml/master/gflat-linux-x64.zip) | The same bundle, wrapped in a `.tar.gz` to preserve executable permissions |
| [Visual Studio extension](https://nightly.link/Gameknight963/gflat/workflows/test.yml/master/gflat-visual-studio.zip) | VSIX for Visual Studio 2022 17.14+ / Visual Studio 2026, x64 |

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), including MSBuild.
- Clang and the native platform linker and C/C++ development libraries.
  - Windows: Visual Studio's **Desktop development with C++** workload and
    **C++ Clang Compiler for Windows**, or an equivalent LLVM/MSVC toolchain.
  - Linux: Ubuntu 24.04 is tested, using Clang 18 and the system C/C++ toolchain.
    For example: `sudo apt install clang-18 build-essential`.
   > Note: I am developing gflat on Windows, Linux support may be spotty atm
- Set `GFLAT_CLANG` to the Clang executable if it is not on PATH. On Windows the compiler also searches Visual Studio installations.

After extracting the Windows ZIP, run from its directory:

```powershell
./compiler/gflat.exe build examples/interpolation/interpolation.gfproj --run
```

For Linux, unzip the download, extract its tarball into an empty directory, and run:

```sh
tar -xzf gflat-linux-x64.tar.gz
GFLAT_CLANG=clang-18 ./compiler/gflat build examples/interpolation/interpolation.gfproj --run
```

You can also compile individual files: `gflat main.gf helper.gf --run`.
Add the bundle's `compiler` directory to PATH to use `gflat` anywhere.
Keep std and the `build` directory together; example projects use relative imports.

Create a project with `gflat new MyApp`, then run
`gflat build MyApp/MyApp.gfproj --run`. See [project setup](docs/PROJECTS.md) for
library projects and optimization settings, or try the [line-search utility](examples/search/README.md).

If setup fails, run `gflat doctor --native` to check SDK discovery, MSBuild,
Clang, and native exception runtime linking.

For Visual Studio, extract and install the VSIX, restart VS, then use **File > New >
Project** and search for **gflat**. Choose a Console Application or Source Library,
or open an existing `.gfproj` and add it to a solution. The VSIX build number increases
ONLY so newer builds can replace older ones; it is not a language version.

## Build from source

With the requirements above installed:

```sh
dotnet build gflat/gflat.csproj
dotnet run --project gflat -- build examples/interpolation/interpolation.gfproj --run
dotnet test gflat.Tests/gflat.Tests.csproj
dotnet test gflat.LanguageServer.Tests/gflat.LanguageServer.Tests.csproj
```

Use the explicit test projects: the solution also contains `.gfproj` files, which
are not .NET test projects. Set `GFLAT_OPT_LEVEL=2` to test optimized native output.

Build the VSIX on Windows using Visual Studio MSBuild:

```powershell
./editors/VisualStudio/build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./editors/VisualStudio/test-package.ps1
```

## Documentation and feedback

- [Language specification](docs/LANGUAGE_SPEC.md)
- [Projects and build commands](docs/PROJECTS.md)
- [Editor support](docs/EDITOR_SUPPORT.md)
- [Standard library core](std/core/README.md) and [libc integration](std/libc/README.md)

Bug reports and small examples are welcome. Include your OS, the source program,
and the commit from `BUILD-INFO.json` in the downloaded bundle (also inside the VSIX).
For a source build, use `git rev-parse HEAD`.

## License

[MIT](LICENSE). Bundled dependencies retain their own licenses. See
[third-party notices](THIRD_PARTY_NOTICES.md)
