# XML project prototype

`.gfproj` is gflat's own project format. It is not an MSBuild project. The CLI,
language server, and Visual Studio adapter use the same loader in `gflat.Projects`.

```xml
<GflatProject Kind="Executable">
  <!-- Paths are relative to this file, not the working directory. -->
  <Sources Include="**/*.gf" />
  <Sources Exclude="scratch/**" />
  <Reference Path="../../std/std.gfproj" />
</GflatProject>
```

`Kind` is required and is either `Executable` or `SourceLibrary`. A source library
contributes sources to consumers; it does not produce a binary library. Only source
libraries can be referenced. References are transitive, diamond dependencies are
deduplicated, and cycles are errors.

Without any `Sources Include`, the default is `**/*.gf`. Use one Include or Exclude
per Sources element. Patterns support `*`, `?`, and `**`; use `/` for portable paths.
Exclusions apply to every include. `bin`, `obj`, `.git`, `.vs`, and `.local-notes`
directories are always excluded. Directory symlinks are not followed, and nested
directories containing another `.gfproj` belong to that project. Source patterns
stay inside the project directory; references may use `..`. Use one project per
directory. Unknown elements/attributes and malformed XML are errors. XML comments
are supported; DTDs and external entities are not.

## Command line

```text
dotnet run --project gflat -- check examples/hello/hello.gfproj
dotnet run --project gflat -- build examples/hello/hello.gfproj --run
dotnet run --project gflat -- build examples/hello/hello.gfproj --emit-ir
dotnet run --project gflat -- clean examples/hello/hello.gfproj
```

With a published compiler, replace `dotnet run --project gflat --` with `gflat`.
Existing explicit `.gf` command lines still work. Executables default to
`bin/<project-name>.exe` on Windows. `--emit-ir` writes `bin/<project-name>.ll`.
`-o` overrides the output path. Source-library builds perform semantic checking;
an empty source library is valid. Clean deletes only the known default outputs,
not arbitrary files in `bin` or custom `-o` outputs.

Project loading and checking are portable .NET code. **Native code generation
still has the compiler's existing Windows x64 ABI limitation.** This prototype
does not add a Linux target or claim that existing generated IR has a Linux ABI.

## Visual Studio

Build and install VSIX 0.3.0 or later, restart Visual Studio, then open `gflat.slnx`.
The `gflat sources` solution folder contains `std` and `hello` beside the C# projects.
For your own project, create its XML file and use **Add > Existing Project**.
If a solution was opened before the extension was installed, VS may remember its
gflat projects as unloaded; use **Reload Project** after installing the extension.

The prototype provides a project tree and a context menu with:

- Open / Edit project file, without unloading the project.
- New `.gf` file, New folder, Add existing file (copies into the selected folder).
- Rename and Delete. Only empty folders can be renamed or deleted by this prototype.
- Build and Clean, using the bundled CLI, with output and compiler errors in VS.

Save an edit to `.gfproj` to apply it. Reload is debounced; malformed XML produces
an Error List entry and leaves the previous valid tree in place. Building always
reads the current file and fails on invalid configuration. File-system additions
and removals update the tree. Source files are included by patterns rather than
rewriting XML on every file operation, so comments are preserved. A rename that
would exclude a file is rejected; edit the source patterns first.

The gflat projects are excluded from the checked-in solution's automatic build
configuration so plain `dotnet build/test gflat.slnx` can still build the C# tools.
Use the gflat project's **Build** context menu in VS. The prototype exposes one
`Debug|Any CPU` project configuration; this does not change the native target ABI.

Diagnostics discover the nearest `.gfproj` above each open `.gf` file and include
referenced libraries, including unsaved source buffers. The server caches project
evaluation between edits and invalidates it on file-system changes. Invalid XML
is reported while the last valid project graph remains available. Existing
`gflat-workspace.json` files continue to work for sources not owned by a project.

## Prototype boundaries

There are no project templates, property pages, debugger/startup-project support,
configuration-specific options, incremental builds, package restore, binary
libraries, or general build scripting yet. File operations use a small custom menu;
full VS automation, drag-and-drop, source-control integration, and folder rename
refactoring are not implemented. This is intended to test whether the everyday
editing/building workflow is useful before investing in those features.

The Visual Studio adapter uses the native project/hierarchy interfaces. This is
more maintenance than an MSBuild-backed project, but project semantics remain
independent of Visual Studio and can be reimplemented in gflat later.
