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
    printf(c"%s\n", message.Data);
    return 0;
}
```

This example uses std; see the runnable
[interpolation project](examples/interpolation/interpolation.gfproj).

## What works

- Structs, classes, interfaces, generics, properties, and indexers.
- Pointers, readonly access, custom allocators, and deterministic destruction.
- Exceptions propagated through return values, with explicit `throws` boundaries.
   > Unhandled exceptions in a nonthrowing method result in an unsuccessful exit, this is intentional
- Custom string literal prefixes and string interpolation. For example, create a `std::String` with the 's' prefix as shown in the example above
- Multiple source files and MSBuild `.gfproj` projects.
- A language server and Visual Studio integration with diagnostics, completion,
  syntax highlighting, navigation, and rich hovers.
- Native Windows x64 and Linux x64 builds


## _Maybe_ in the future

 - Generators?
 - Reflection? (compile time)
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
- Clang and the native platform linker/C development libraries.
  - Windows: Visual Studio's **Desktop development with C++** workload and
    **C++ Clang Compiler for Windows**, or an equivalent LLVM/MSVC toolchain.
  - Linux: Ubuntu 24.04 is tested, using Clang 18 and the system C toolchain.
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

For Visual Studio, extract and install the VSIX, restart VS, then open a `.gfproj`
or add it to an existing solution. The VSIX build number increases automatically
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

[MIT](LICENSE). Bundled dependencies retain their own licenses. See
[third-party notices](THIRD_PARTY_NOTICES.md)
