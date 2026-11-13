![GitHub Repo Views](https://gitviews.com/repo/gameknight963/gflat.svg?style=flat&label-color=purple)
[![compiler](https://github.com/Gameknight963/gflat/actions/workflows/test.yml/badge.svg)](https://github.com/Gameknight963/gflat/actions/workflows/test.yml)

## AI disclaimer

This project has significant portions written by AI, including READMEs and other documentation. I'll rewrite the documentation myself eventually, but if the usage of AI bothers you, this project is not for you.

# G♭ 
(gflat)

A compiled language with C#-inspired syntax, through LLVM.

It provides control over storage and layout, scope-based destruction, 
and lets you shoot yourself in the foot with memory if that's your style.

The language is changing frequenty. Expect breaking changes. 
Nightly builds are "demos" of the current work.

```cpp
using Standard::Text;
using Standard;

extern "C" int printf(readonly(char)* text, ...);

int main()
{
    String message = s$"Hello from gflat! The answer is {42}.";
    printf("%s\n", message.Data);
    return 0;
}
```

Try this [interpolation project](examples/interpolation/interpolation.gfproj) or
browse the other [examples](examples/README.md).

## Language and library

- Structs and classes with constructors, destructors, auto-properties, indexers and other operators

- Interfaces and generics with constraints
- Raw pointers, nullable pointers, replacable allocator
- shallow `readonly` and deep `immutable` views
- Managed references with a replacable GC (`T^`)
- Stack-unwinding exceptions
- Custom string prefixes
- Interpolation with custom string prefixes. See [String's implementation](Standard/core/Text/String.gf) of the 's' prefix.
  > Creating a C string as the result of interpolation is not a feature for now because I'm
  > not sure how to do it safely. Either you allocate a new string on the heap
  > (easy to leak), or on the stack (could cause memory bugs).
  > There's probably a good solution to this, so it may be a feature in the future.
  C strings can be interpolated into an owning `String`, for example `s$"Hello {name}"` where `name` is `readonly(char)*`.
- [Preprocessor macros and textual includes](docs/PREPROCESSOR.md), including conditional compilation
- Compile-time name, layout, and type queries

[Standard](Standard/README.md) includes strings, collections, queries, text and 
binary formats, console and file IO.

Visual Studio is the only well-supported editor at the moment. A language server supports
other LSP clients.

[Visual Studio extension](docs/EDITOR_SUPPORT.md)

## Download

These links use [nightly.link](https://github.com/oprypin/nightly.link) to download
the latest successful `master` build.

| Download | Contents |
| --- | --- |
| [Windows x64](https://nightly.link/Gameknight963/gflat/workflows/test.yml/master/gflat-win-x64.zip) | Compiler, language server, Standard, build rules, examples, and docs |
| [Linux x64](https://nightly.link/Gameknight963/gflat/workflows/test.yml/master/gflat-linux-x64.zip) | The same bundle in a `.tar.gz` that preserves executable permissions |
| [Visual Studio extension](https://nightly.link/Gameknight963/gflat/workflows/test.yml/master/gflat-visual-studio.zip) | VSIX for Visual Studio 2022 17.14+ and Visual Studio 2026, x64 |

### Requirements

Bundles include the .NET runtime and MSBuild. You'll also need Clang,
the platform linker, and C/C++ development libraries:

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

Add the bundle's `compiler` directory to PATH to use `gflat` anywhere. You must keep the
bundle's directory layout intact though.

### Create a project

```text
gflat new MyApp
gflat build MyApp/MyApp.gfproj --run
```

You can also compile individual files with `gflat main.gf helper.gf --run`.
Projects and direct `gflat` source-file commands include Standard by default.

`gfc` is the compiler for explicit source inputs, such as `gfc main.gf --check`. It does not discover projects or add Standard. Use `gflat main.gf --no-stdlib` to opt out of Standard in the driver. See
[project setup and library selection](docs/PROJECTS.md).

Run `gflat doctor --native` to check the toolchain if setup fails.

### Visual Studio

Install the VSIX and restart Visual Studio. Use **File > New > Project**, filter language by 'G♭', 
and choose a Console Application or Source Library. You can also
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

## Documentation and feedback

- [Language reference](docs/LANGUAGE_SPEC.md)
- [Projects and build commands](docs/PROJECTS.md)
- [Editor support](docs/EDITOR_SUPPORT.md)
- [Standard library](Standard/README.md)

For bug reports, include a small reproducing program, your OS, and the commit
from the bundle's `BUILD-INFO.json`. For source builds, use `git rev-parse HEAD`.

## Licensing

The G♭ compiler, standard library, examples, and 
everything else in this repo is licensed to you under the [MIT](LICENSE). 

See [third-party notices](THIRD_PARTY_NOTICES.md) for bundled dependencies.
