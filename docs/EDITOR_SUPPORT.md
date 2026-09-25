# Editor support

The Visual Studio extension provides live compiler diagnostics, syntax highlighting,
bracket matching, automatic bracket/quote closing, selection surrounding, block
indentation, and line/block comment commands. Version 0.10.0 adds scope/member completion,
hover, signature help, go to definition, and semantic highlighting through the built-in LSP UI.
Debugging is not implemented yet.
Version 0.4.0 uses [MSBuild projects and CPS](PROJECTS.md) for integration into an existing solution.

## Project layout

| Project | Responsibility |
| --- | --- |
| `gflat.Projects` | MSBuild evaluation snapshots, source membership, and project references |
| `gflat.Compiler` | Shared parser, type checker, diagnostics, and LLVM IR emitter |
| `gflat` | Command-line driver and native toolchain invocation |
| `gflat.LanguageServer` | LSP process; document snapshots, workspace sources, and analysis scheduling |
| `gflat.Tests` | Compiler, native execution, and CLI regression tests |
| `gflat.LanguageServer.Tests` | Workspace, protocol, and editor grammar tests |
| `editors/shared` | Portable TextMate grammar and language configuration |
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
package bundles a compiler for project builds; Clang must be installed separately. Building the
package does not install it into your IDE automatically. Project evaluation and builds also require the .NET 10 SDK, including when using the bundled server.

The installed server is the packaged copy; it does not automatically track this
checkout. For an update, increment the VSIX manifest and client assembly versions,
then rebuild and install the VSIX. For a development rebuild with the same version,
uninstall the old extension first.

## Editing features

Version 0.2.0 adds theme-aware highlighting for keywords, built-in types, type
declarations, function names, numbers, comments, strings, escapes, and custom
string prefixes such as `my_2string"hello"`. This is lexical highlighting; names
are not classified using type information. An unfinished quote stops coloring at
the end of its line, while block comments continue until `*/`.

The editor matches `{}`, `[]`, and `()`, closes brackets and quotes outside
comments/strings, and supports surrounding selected text with these pairs.
Angle brackets are not automatically closed because they also mean comparisons.
Pressing Enter between `{}` creates an indented line. Use Visual Studio's standard
comment/uncomment commands (`Ctrl+K, Ctrl+C` / `Ctrl+K, Ctrl+U`).

These features use [Visual Studio's TextMate and language configuration support](https://learn.microsoft.com/en-us/visualstudio/extensibility/language-configuration).
Editor options can disable automatic brace completion or selection surrounding;
check Text Editor settings if a feature is disabled. Colors follow the current
theme. The portable definitions live in `editors/shared`; another editor can
register the same grammar and configuration without depending on Visual Studio.

After installing an update, manually check a `.gf` file in your IDE: keyword and
comment colors, typing quotes/brackets, Enter inside `{}`, selection surrounding,
and comment/uncomment commands. Automated tests exercise grammar tokenization and
packaged assets, but do not drive the Visual Studio UI.

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
compiler. `AnalysisSnapshot` retains the source nodes before lowering together with
checker results and diagnostics. One editor model is shared by all requests for that
compilation; it includes unsaved project sources. Symbol references are resolved on the
analysis worker and reused by hover, definition, and semantic-token requests.

This is whole-compilation analysis, not incremental semantic analysis. Syntax errors
can prevent semantic checking, and a semantic error may stop checking later members.
Completion can recover declaration types for incomplete member accesses and unclosed
blocks. This is not full error-tolerant type checking: ambiguous calls in broken code
may have no member suggestions, and malformed syntax can prevent a source tree from
being retained. Generic signatures may show template parameters rather than substituted
arguments.
An unexpected compiler exception is reported as `GFLS0002` instead of being presented
as an ordinary source error; the server can analyze the next edit afterward.

Run the portable server and editor grammar tests with:

```text
dotnet test gflat.LanguageServer.Tests
```

The root `dotnet test` also runs these tests. The VS packaging CI job builds the VSIX
and runs tests against the server, grammar, and configuration extracted from the
package. TextMateSharp is a test-only dependency used to tokenize the grammar; it
is not bundled into the extension or server. Manual
IDE checks are still needed for activation, the completion/hover/signature UI, theme
colors, navigation, squiggles, and the Error List UI. Automated tests exercise protocol
responses and packaged assets; they do not drive Visual Studio.

For CPS changes, also load a `.gfproj` in a solution, add and rename a source file,
rename its nonempty folder, edit a `Compile Remove` entry externally, and verify
the tree updates. Check Build and Clean from Visual Studio. Setting
`GflatEmitIR` to `true` lets this project-system smoke test run without Clang.
Run these checks in an experimental VS profile when developing the extension.

Implementation references: [LSP 3.17](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/)
and [Visual Studio LSP integration](https://learn.microsoft.com/en-us/visualstudio/extensibility/adding-an-lsp-extension?view=vs-2022).

## Editor queries and colors

- Completion includes visible locals, parameters, fields, methods, free functions,
  types, namespaces, and context keywords. `.` and `:` trigger completion; `::`
  narrows it to namespace/type members. Visual Studio handles filtering and commit.
- Hover shows a declaration signature. Go to definition targets its source name,
  including unsaved files in the same project. Synthetic prelude symbols have no
  file navigation target.
- Signature help follows the innermost call and counts arguments without counting
  commas in nested calls.
- Full and range semantic tokens supplement the TextMate grammar. The default
  legend uses standard LSP names. The VS client sets the initialization option
  `visualStudioClassifications: true` to use the built-in Roslyn classification
  names (class, struct, method, local, parameter, property, control keyword, etc.).
  Colors come from the active VS theme and Fonts and Colors settings, not RGB values
  in this extension. Other clients do not need this option.
- Diagnostics preserve compiler start/length spans, including multiline expressions
  and zero-width insertion locations for missing delimiters.

Requests arriving during analysis wait for that generation. `$/cancelRequest` cancels
queued requests; edits invalidate old requests with LSP ContentModified. Queries do
not invoke LLVM or repeat compilation.
