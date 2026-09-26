# gflat

An experimental compiled language with C#-inspired syntax, explicit pointers,
scope-based destruction, and native code generation through LLVM IR and Clang.

**Work in progress.** The language, standard library, compiler, and project format
can change without notice. There are no stable releases or compatibility promises.

```gflat
using std;

extern int printf(readonly(char)* text, ...);

int main()
{
    String message = s$"Hello from gflat! The answer is {42}.";
    printf(c"%s\n", message.Data);
    return 0;
}
```

This example uses std; see the runnable
[interpolation project](examples/interpolation/interpolation.gfproj).

## What works today

- Structs, classes, interfaces, generics, properties, and indexers.
- Pointers, readonly access, custom allocators, and deterministic destruction.
- Exceptions propagated through return values, with explicit `throws` boundaries.
- Custom string literal prefixes and string interpolation.
- Multiple source files and MSBuild `.gfproj` projects.
- A language server and Visual Studio integration with diagnostics, completion,
  syntax highlighting, navigation, and rich hovers.
- Native Windows x64 and Linux x64 builds, tested at O0 and O2.

The standard library is small and incomplete. Visual Studio debugging is not
implemented. Other architectures and cross-compilation are not supported.

## Download the latest development build

These links select the latest successful `master` workflow using
[nightly.link](https://github.com/oprypin/nightly.link). Builds happen on pushes,
not on a release schedule; no tags are required.

| Download | Contents |
| --- | --- |
| [Windows x64](https://nightly.link/Gameknight963/gflat/workflows/test.yml/master/gflat-win-x64.zip) | Compiler, language server, std, project files, examples, and docs |
| [Linux x64](https://nightly.link/Gameknight963/gflat/workflows/test.yml/master/gflat-linux-x64.zip) | The same bundle, wrapped in a `.tar.gz` to preserve executable permissions |
| [Visual Studio extension](https://nightly.link/Gameknight963/gflat/workflows/test.yml/master/gflat-visual-studio.zip) | VSIX for Visual Studio 2022 17.14+ / Visual Studio 2026, x64 |

Public links require a public repository and a successful build containing these
artifacts. While the repository is private, use the artifacts on the
[Actions page](https://github.com/Gameknight963/gflat/actions/workflows/test.yml)
with an authorized GitHub account. Artifacts expire after 90 days; if development
pauses longer than that, rerun the workflow or build from source.

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), including MSBuild.
- Clang and the native platform linker/C development libraries.
  - Windows: Visual Studio's **Desktop development with C++** workload and
    **C++ Clang tools for Windows**, or an equivalent LLVM/MSVC toolchain.
  - Linux: Ubuntu 24.04 is tested, using Clang 18 and the system C toolchain.
    For example: `sudo apt install clang-18 build-essential`.
- Set `GFLAT_CLANG` to the Clang executable if it is not on PATH. On Windows the
  compiler also searches Visual Studio installations.

Compiler bundles use the installed .NET runtime. The VSIX bundles its own compiler,
language server, and runtime; `.gfproj` builds still need the SDK and native toolchain.

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

For Visual Studio, extract and install the VSIX, restart VS, then open a `.gfproj`
or add it to an existing solution. The VSIX build number increases automatically
so newer builds can replace older ones; it is not a language stability version.

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
./editors/VisualStudio/test-package.ps1
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

[MIT](LICENSE). Bundled dependencies retain their own licenses; see
[third-party notices](THIRD_PARTY_NOTICES.md).
