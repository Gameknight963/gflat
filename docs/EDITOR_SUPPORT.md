# Editor support

The Visual Studio extension provides editing, navigation, and native debugging
for `.gf` projects. Other editors can use the language server through LSP.

## Editing and navigation

- **Editing:** syntax highlighting, matching and closing brackets and quotes,
  selection surrounding, indentation, and comment commands
- **Completion:** visible symbols, members, and context keywords, with signature help
- **Navigation:** go to definition, Find All References, document highlights,
  workspace symbol search, and document outlines
- **Diagnostics:** compiler errors and warnings with source ranges
- **Debugging:** F5, Ctrl+F5, breakpoints, stepping, Locals, and native Watch expressions

Reference searches include unsaved project documents and distinguish same-named
symbols and overloads. Extension calls and ordinary calls share their declaration.
Document outlines include namespaces, types, and members.

The server provides hierarchical outlines when the client supports them and flat
symbol lists otherwise. Document highlights mark declarations and references alike.

Rename supports locals and explicit parameters, including references in `nameof`.
It requires an error-free project. The server checks each candidate rename for
new errors and changed bindings before applying it.

Clients supporting versioned workspace edits receive the unsaved document version.
Other clients receive range edits.

## Hovers and documentation

Hovers show signatures with source type names. Literal hovers show the checked
type. Visual Studio adds shared symbol icons and clickable source declarations.

The optional `Exceptions:` section lists known outgoing exception types. It
excludes caught exceptions and failures terminated by a `nothrow` boundary.
See [Quick Info preferences](#quick-info-preferences).

Write declaration documentation with consecutive `///` lines immediately before
the declaration and its attributes:

- A blank `///` line separates paragraphs
- A physical blank line or ordinary comment detaches the preceding documentation
- Text is displayed literally, including XML tags, Markdown, and links
- Generic and inherited members use their original declaration's documentation
- Aliases use their own documentation

## Project layout

| Project | Responsibility |
| --- | --- |
| `gflat.Projects` | MSBuild evaluation snapshots, source membership, and project references |
| `gflat.Compiler` | Shared parser, type checker, diagnostics, and LLVM IR emitter |
| `gflat` | Command-line driver and native toolchain invocation |
| `gflat.LanguageServer` | LSP process, document snapshots, workspace sources, and analysis scheduling |
| `gflat.Tests` | Compiler, native execution, and CLI regression tests |
| `gflat.LanguageServer.Tests` | Workspace, protocol, and editor grammar tests |
| `editors/shared` | Portable TextMate grammar and language configuration |
| `editors/VisualStudio` | Windows VSIX client, packaging, and server launch over stdio |

The root solution builds the compiler, CLI, server, and tests. The extension has
its own solution and build script. The compiler and server use portable .NET APIs.

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

Open the VSIX to install it, follow the installer's restart instructions, and open
a `.gf` file. The package includes the compiler, language server, MSBuild, and .NET
runtime. Install Clang separately. Visual Studio's project system uses the IDE's
MSBuild.

### Optional SDK-style project setup

For `<Project Sdk="Gflat.Sdk">`, install the VSIX first, then run the accompanying
script with Visual Studio closed. Right-click **Run with PowerShell**, or run:

```powershell
.\install-sdk.ps1
```

If Windows denies write access, the script relaunches itself with a UAC prompt.
Accept it to continue. The elevated window stays open until you press Enter, so
you can read the result. Use `-WhatIf` to preview the changes.

It selects the newest installed Visual Studio and finds its installed gflat VSIX.
To select another installation or supply the SDK directory explicitly:

```powershell
.\install-sdk.ps1 -VisualStudioPath 'C:\Program Files\Microsoft Visual Studio\18\Community' -SdkRoot 'C:\path\to\extension\Sdk'
.\uninstall-sdk.ps1 -VisualStudioPath 'C:\Program Files\Microsoft Visual Studio\18\Community'
```

`-SdkRoot` must contain `build/Gflat.props` and `build/Gflat.targets`. The scripts
create only two forwarding imports and an ownership marker under
`<Visual Studio>/MSBuild/Sdks/Gflat.Sdk`. The SDK and compiler stay in the VSIX.
Run install again after updating the VSIX to refresh the forwarding paths. Run
uninstall before removing the VSIX. Both scripts support `-WhatIf`, and uninstall
refuses to delete an unrecognized SDK directory or unexpected files. Restart
Visual Studio after setup. `sdk-paths.ps1` must remain beside the two scripts.

This registration applies to the selected Visual Studio and its MSBuild.
The CLI and language server resolve their bundled SDK independently.
`test-sdk-installation.ps1` covers install, update, uninstall, and SDK-style builds
in a temporary fake Visual Studio installation.

To update the installed server, increment the VSIX manifest version, rebuild,
and install the new VSIX. Uninstall the old extension first when rebuilding
with the same version.

## Editing features

Highlighting combines a TextMate grammar with semantic classifications from the
compiler. Keywords, types, methods, literals, escapes, and custom string prefixes
follow the active theme.

- `{}`, `[]`, and `()` match and close automatically outside comments and strings
- Quotes close automatically and can surround a selection
- Enter between `{}` creates an indented line
- `Ctrl+K, Ctrl+C` comments a selection, and `Ctrl+K, Ctrl+U` uncomments it

Automatic completion and selection surrounding follow Visual Studio's editor
settings. The portable definitions live in `editors/shared` and use
[TextMate and language configuration support](https://learn.microsoft.com/en-us/visualstudio/extensibility/language-configuration).

## Source files in a workspace

The server discovers the nearest `.gfproj` and includes its referenced sources.
For source folders without a project, each open `.gf` file is analyzed separately.

For multiple files, open the source folder in your editor and put
`gflat-workspace.json` at the workspace root:

```json
{
  "sources": ["main.gf", "Standard/Buffer.gf", "Standard/StringView.gf"]
}
```

The list selects one compilation for editor analysis. Paths are relative to the
configuration file and may include `..`. List individual files. Pass the same
source paths to the CLI when building.
Malformed configuration, duplicate entries, and unreadable sources produce diagnostics.

Unsaved text overrides disk contents for open files. Closing a configured file makes
analysis use its saved contents again. Closing an unconfigured file clears its
diagnostics. Open files outside the source list are analyzed independently.

The server watches configured source files and the configuration for disk changes.
Clients may also send `workspace/didChangeWatchedFiles`. This version uses a single
workspace root (the first folder if a client supplies several). Without a root it
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
uses UTF-16 positions, and publishes versioned diagnostics.

## Analysis and maintenance

Analysis waits briefly for typing to pause and runs one compilation at a time.
New edits invalidate older results. Cancellation skips queued work and suppresses
obsolete results while running compiler work finishes in the background.

The server shares parsing and type checking with the compiler. `AnalysisSnapshot`
retains source nodes, checker results, and diagnostics. Requests reuse one symbol
index for the compilation, including unsaved sources.

Incomplete member accesses and unclosed blocks have completion recovery.
Malformed syntax and ambiguous calls can still prevent semantic results. Generic
signatures may retain template parameters. Unexpected compiler failures produce
`GFLS0002`, and analysis resumes on the next edit.

Run the portable server and editor grammar tests with:

```text
dotnet test gflat.LanguageServer.Tests
```

The root `dotnet test` also runs these tests. Package CI tests the extracted server,
grammar, configuration, and Visual Studio rich-content conversion. Run
`test-package.ps1` in Windows PowerShell (`powershell.exe`) because its installed
SDK harness uses .NET Framework APIs.

Check the live IDE after extension changes:

- Extension activation, Error List, and diagnostic squiggles
- Completion, hover, signature help, navigation, and theme colors
- Quotes and brackets, Enter inside `{}`, selection surrounding, and comment commands

For CPS changes, also load a `.gfproj` in a solution, add and rename a source file,
rename its nonempty folder, edit a `Compile Remove` entry externally, and verify
the tree updates. Check Build and Clean from Visual Studio. Setting
`GflatEmitIR` to `true` lets this project-system smoke test run without Clang.
Run these checks in an experimental VS profile when developing the extension.

Implementation references: [LSP 3.17](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/)
and [Visual Studio LSP integration](https://learn.microsoft.com/en-us/visualstudio/extensibility/adding-an-lsp-extension?view=vs-2022).

## Editor queries and colors

- Completion includes visible locals, parameters, fields, methods, free functions,
  types, namespaces, and context keywords. `.` and `:` trigger completion.
  `::` narrows it to namespace/type members. Visual Studio handles filtering and commit.
- Hover shows a declaration signature. Go to definition targets its source name,
  including unsaved files in the same project. Automatically included standard-library
  symbols navigate to their shipped `.gf` sources. Explicit Standard project sources and
  their unsaved edits take precedence over bundled copies.
- Signature help follows the innermost call and counts arguments without counting
  commas in nested calls.
- Full and range semantic tokens supplement the TextMate grammar. The default
  legend uses standard LSP names. The VS client sets the initialization option
  `visualStudioClassifications: true` to use the built-in Roslyn classification
  names (class, struct, method, local, parameter, property, control keyword, etc.).
  Colors come from the active VS theme and Fonts and Colors settings.
- Diagnostics preserve compiler start/length spans, including multiline expressions
  and zero-width insertion locations for missing delimiters.

Requests arriving during analysis wait for that generation. `$/cancelRequest` cancels
queued requests. Edits invalidate old requests with LSP ContentModified.

### Rich hover

Visual Studio receives classified text and shared icons through `VSInternalHover`.
Signatures include modifiers, generic parameters, argument names, `nothrow`, and
accessor restrictions. Type hovers can show size and alignment for complete layouts.

The client supplies a `KnownMonikers` catalog through `hoverIcons`. The server sends
`_vs_rawContent` using portable types, and Visual Studio renders it with its LSP
Quick Info UI. A small native Quick Info source adds navigation callbacks to
classified runs. Other clients receive Markdown.

The packaged `gflat.tmTheme` maps lexical highlighting to the same native
classifications used by semantic highlighting. Method names and declaration
keywords can therefore use their final colors while analysis runs.

## Quick Info preferences

The Visual Studio language options include a gflat **IntelliSense** page, registered
through the SDK's language editor options API. **Show known exceptions** defaults
to off. **Show type size and alignment** defaults to on. Visual Studio persists
these user preferences. Applying changes updates tooltips without restarting the
language server. The exception list contains types identified by analysis.

Other LSP clients can supply `gflat.showKnownExceptions` and `gflat.showTypeLayout`
as nested initialization options (`{"gflat":{"showKnownExceptions":true}}`) or
in `workspace/didChangeConfiguration`'s `settings` object. Colors continue to use
Visual Studio's existing theme classifications.
