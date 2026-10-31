![GitHub Repo Views](https://gitviews.com/repo/gameknight963/gflat.svg?style=flat&label-color=purple)
[![compiler](https://github.com/Gameknight963/gflat/actions/workflows/test.yml/badge.svg)](https://github.com/Gameknight963/gflat/actions/workflows/test.yml)

# gflat

A compiled language with C#-inspired syntax, control over storage and layout,
scope-based destruction, and native code generation through LLVM.

gflat is experimental. The language, compiler, library, and project format change
frequently. Nightly builds are demos of the current work.

```csharp
using Standard::Text;
using Standard;

extern int printf(readonly(char)* text, ...);

int main()
{
    String message = s$"Hello from gflat! The answer is {42}.";
    printf("%s\n", message.Data);
    return 0;
}
```

Try the [interpolation project](examples/interpolation/interpolation.gfproj) or
browse the [examples](examples/README.md).

## Language and library

- Structs and classes with constructors, deterministic cleanup, properties, and indexers
- Interfaces, generics, and borrowed extension methods
- Raw pointers, nullable references, shallow `readonly` and deep `immutable` views
- Replaceable allocation and managed references (`T^`)
- Native stack-unwinding exceptions
- Custom string prefixes and interpolation
- Compile-time name, layout, and type queries
- [Standard](Standard/README.md): strings, collections, queries, text and binary formats, console and file IO

The [Visual Studio extension](docs/EDITOR_SUPPORT.md) provides highlighting,
completion, navigation, hovers, and native debugging. A language server supports
other LSP clients.

## Download

These links use [nightly.link](https://github.com/oprypin/nightly.link) to download
the latest successful `master` build.

| Download | Contents |
| --- | --- |
| [Windows x64](https://nightly.link/Gameknight963/gflat/workflows/test.yml/master/gflat-win-x64.zip) | Compiler, language server, Standard, build rules, examples, and docs |
| [Linux x64](https://nightly.link/Gameknight963/gflat/workflows/test.yml/master/gflat-linux-x64.zip) | The same bundle in a `.tar.gz` that preserves executable permissions |
| [Visual Studio extension](https://nightly.link/Gameknight963/gflat/workflows/test.yml/master/gflat-visual-studio.zip) | VSIX for Visual Studio 2022 17.14+ and Visual Studio 2026, x64 |

### Requirements

Bundles include the .NET runtime and MSBuild. Native builds also need Clang and
the platform linker and C/C++ development libraries:

- **Windows:** Visual Studio's **Desktop development with C++** workload and **C++ Clang Compiler for Windows**, or an equivalent LLVM/MSVC toolchain
- **Linux:** CI tests Ubuntu 24.04 with Clang 18. Install the toolchain with `sudo apt install clang-18 build-essential`

Set `GFLAT_CLANG` to the Clang executable if needed. On Windows, the compiler also
searches Visual Studio installations.

### Run an example

After extracting the Windows ZIP, run from its directory:

```powershell
./compiler/gflat.exe build examples/interpolation/interpolation.gfproj --run
```

On Linux, unzip the download and extract its tarball:

```sh
tar -xzf gflat-linux-x64.tar.gz
GFLAT_CLANG=clang-18 ./compiler/gflat build examples/interpolation/interpolation.gfproj --run
```

Add the bundle's `compiler` directory to PATH to use `gflat` anywhere. Keep the
bundle's directory layout intact.

### Create a project

```text
gflat new MyApp
gflat build MyApp/MyApp.gfproj --run
```

You can also compile individual files with `gflat main.gf helper.gf --run`.
Projects include Standard by default. Direct source commands include the minimal
runtime sources and accept additional library files explicitly. See
[project setup and library selection](docs/PROJECTS.md).

Run `gflat doctor --native` to check the toolchain if setup fails.

### Visual Studio

Install the VSIX and restart Visual Studio. Use **File > New > Project**, search
for **gflat**, and choose a Console Application or Source Library. You can also
add an existing `.gfproj` to a solution.

See [editor setup](docs/EDITOR_SUPPORT.md) for SDK-style projects and other editors.

## Build from source

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and
the native toolchain above.

```sh
dotnet build gflat/gflat.csproj
dotnet run --project gflat -- build examples/interpolation/interpolation.gfproj --run
dotnet test gflat.Tests/gflat.Tests.csproj
dotnet test gflat.LanguageServer.Tests/gflat.LanguageServer.Tests.csproj
```

| Solution | Contents |
| --- | --- |
| [gflat.slnx](gflat.slnx) | Compiler, CLI, language server, and tests |
| [Standard/Standard.slnx](Standard/Standard.slnx) | Standard library |
| [examples/examples.slnx](examples/examples.slnx) | Runnable programs |
| [Visual Studio extension](editors/VisualStudio/gflat.VisualStudio.slnx) | VSIX |

For extension builds and validation, see [editor development](docs/EDITOR_SUPPORT.md).
See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution and test instructions.

## Documentation and feedback

- [Language reference](docs/LANGUAGE_SPEC.md)
- [Projects and build commands](docs/PROJECTS.md)
- [Editor support](docs/EDITOR_SUPPORT.md)
- [Standard library](Standard/README.md)

For bug reports, include a small reproducing program, your OS, and the commit
from the bundle's `BUILD-INFO.json`. For source builds, use `git rev-parse HEAD`.

## License

[MIT](LICENSE). See [third-party notices](THIRD_PARTY_NOTICES.md) for bundled dependencies.
