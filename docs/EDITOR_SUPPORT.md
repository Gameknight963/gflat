# Editor support

The first editor milestone provides live compiler diagnostics. Hover, navigation,
completion, syntax coloring, build commands, and debugging are not implemented yet.

## Project layout

| Project | Responsibility |
| --- | --- |
| `gflat.Compiler` | Shared parser, type checker, diagnostics, and LLVM IR emitter |
| `gflat` | Command-line driver and native toolchain invocation |
| `gflat.LanguageServer` | LSP process; document snapshots, workspace sources, and analysis scheduling |
| `gflat.Tests` | Compiler, native execution, and CLI regression tests |
| `gflat.LanguageServer.Tests` | Workspace and protocol tests, including a real server process |
| `editors/VisualStudio` | Windows VSIX client and packaging; launches the server over stdio |

The root solution builds the compiler, CLI, server, and tests. The VS extension has
its own solution and build script so ordinary .NET builds do not require Visual
Studio's extension toolchain. Neither the compiler nor the server references VS APIs.

## Visual Studio installation

The extension targets Visual Studio 2022 17.14 and Visual Studio 2026, x64. Build it
on Windows with the .NET 10 SDK and Visual Studio MSBuild installed:

```powershell
./editors/VisualStudio/build.ps1
```

For a nonstandard Visual Studio installation, pass `-MSBuild` with the path to
`MSBuild.exe`. The script publishes the language server with its .NET runtime and
packages it in:

```text
editors/VisualStudio/bin/Release/net472/gflat.VisualStudio.vsix
```

Open that VSIX to install it, follow the installer's restart instructions, and open
a `.gf` file. Errors and warnings appear through Visual Studio's LSP support. The
package does not install the CLI, Clang, or a gflat project system. Building the
package does not install it into your IDE automatically.

The installed server is the packaged copy; it does not automatically track this
checkout. For an update, increment the VSIX manifest and client assembly versions,
then rebuild and install the VSIX. For a development rebuild with the same version,
uninstall the old extension first.

## Source files in a workspace

Without a configuration file, each open `.gf` document is analyzed separately.
Unrelated programs in the same folder are not combined automatically.

For multiple files, open the source folder in your editor and put
`gflat-workspace.json` at the workspace root:

```json
{
  "sources": ["main.gf", "std/Buffer.gf", "std/StringView.gf"]
}
```

This is an editor source list, not a build or package format. Paths are relative to
that file, may include `..`, and must list actual files (no globs). All listed files
form one compilation. The CLI still takes the same paths as command-line arguments.
Malformed configuration, duplicate entries, and unreadable sources produce diagnostics.

Unsaved text overrides disk contents for open files. Closing a configured file makes
analysis use its saved contents again. Closing an unconfigured file clears its
diagnostics. Open files outside the source list are analyzed independently.

The server watches configured source files and the configuration for disk changes.
Clients may also send `workspace/didChangeWatchedFiles`. This version uses a single
workspace root (the first folder if a client supplies several); without a root it
uses the directory of the first opened document. Only `file:` URIs are supported.

## Other editors

Build the server with `dotnet build gflat.LanguageServer`, then configure any LSP
client to start:

```text
dotnet /absolute/path/to/gflat.LanguageServer/bin/Debug/net10.0/gflat.LanguageServer.dll
```

Use language ID `gflat`, file extension `.gf`, and the source directory as the root.
The server uses JSON-RPC over stdin/stdout. Stderr is reserved for server logs. It
advertises full-document synchronization, accepts incremental updates as well,
uses UTF-16 positions, and publishes versioned diagnostics. It performs no native
compilation and does not invoke Clang.

## Analysis and maintenance

Analysis waits briefly for typing to pause and runs one compiler analysis at a time.
New edits invalidate older results. Already-running compiler analysis currently
finishes in the background; cancellation skips queued work and prevents obsolete
results from being published. Parsing/type checking are reused directly from the
compiler, and expected source errors are returned by `Compiler.Analyze`.

This is whole-compilation analysis, not incremental semantic analysis. Syntax errors
can prevent semantic checking, and a semantic error may stop checking later members.
An unexpected compiler exception is reported as `GFLS0002` instead of being presented
as an ordinary source error; the server can analyze the next edit afterward.

Run the portable server tests with:

```text
dotnet test gflat.LanguageServer.Tests
```

The root `dotnet test` also runs these tests. The VS packaging CI job builds the VSIX
and runs the server tests against the executable extracted from the package. Manual
IDE checks are still needed for activation, squiggles, and the Error List UI.

Implementation references: [LSP 3.17](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/)
and [Visual Studio LSP integration](https://learn.microsoft.com/en-us/visualstudio/extensibility/adding-an-lsp-extension?view=vs-2022).
