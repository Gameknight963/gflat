# Windows executable without the CRT

An isolated Windows x64 experiment using the existing compiler and
`<NoStdLib>true</NoStdLib>`. It exercises a small runtime written in gflat,
with an assembly stack-probing helper. It does not change the compiler or
the standard library.

## Run

From the repository root, in PowerShell:

```powershell
./experiments/no-crt-windows/build.ps1
```

Building requires the .NET 10 SDK, LLVM (`clang`, `lld-link`, `llvm-readobj`,
and `llvm-objdump`), and the Windows SDK's x64 `kernel32.lib`. This experiment
does not link MSVC libraries, a C/C++ runtime, or compiler-rt. The resulting
basic executable runs without .NET.

The script builds the compiler, emits IR through `NoCrt.gfproj`, then compiles
and links explicitly. Its optional parameters are `-Compiler` (a prebuilt
gflat DLL), `-Clang`, `-WindowsSdkLibraryDirectory` (containing `kernel32.lib`),
and `-OutputDirectory`. Use this script for the native experiment; an ordinary
project Build still uses the normal hosted linker configuration.

## What the basic program does

- Enters the gflat `Start` function directly through the linker's `/ENTRY`.
- Copies and modifies a fixed-size stack buffer, checking the original remains intact.
- Writes and verifies a 32 KiB stack buffer, exercising guard-page stack growth.
- Allocates through our `Memory` API backed by the Windows process heap.
- Constructs a struct in that storage using its implicit default constructor,
  accesses its field, invokes its destructor, and frees the storage.
- Writes `gflat without CRT` and exits with status zero.

Allocation currently delegates to `HeapAlloc`; this is not a custom heap manager.
`StackProbe.S` supplies `__chkstk`, whose register-based calling convention cannot
be expressed as an ordinary gflat function. It probes each intervening page and
preserves the requested size and caller's stack pointer. See Microsoft's
[stack-probing helper documentation](https://learn.microsoft.com/en-us/windows/win32/devnotes/-win32-__chkstk).

The link uses `/NODEFAULTLIB` and exactly our program object, our stack-probe
object, and `kernel32.lib`. The script checks the basic executable's six imports:
`ExitProcess`, `GetProcessHeap`, `GetStdHandle`, `HeapAlloc`, `HeapFree`, and
`WriteFile`, all from `KERNEL32.dll`. These are direct imports; Windows supplies
its own underlying dependencies.

## Observed results

Both `-O0` and `-O2` are tested. Initial measurements on this machine:

| Case | `-O0` | `-O2` |
| --- | --- | --- |
| Basic program | Runs; 3,584-byte executable | Runs; 4,608-byte executable |
| Placement construction with a user constructor | Exception-runtime symbols unresolved | Runs after optimization removes exception machinery |
| Ordinary allocating `new*` | Exception-runtime symbols unresolved | Exception-runtime symbols unresolved |
| Throw/catch | Exception-runtime symbols unresolved | Exception-runtime symbols unresolved |
| `std::String` with our Memory implementation | Exception-runtime symbols unresolved | Exception-runtime symbols and `strlen` unresolved |

The constructor is deliberately trivial: it only stores an integer field.
Its unoptimized dependency shows that the compiler currently generates exception
handling even when this constructor's body does not need it. Optimization makes
this particular case work, which does not establish general constructor support.

`new*` must support its allocation-failure exception. String also throws for
allocation failure, overflow, and invalid indexing. These cases need an exception
implementation even when allocation is supplied independently of libc. The
optimized String case additionally demonstrates a compiler-generated library call:
its source contains no explicit `strlen` call. Passing `-ffreestanding` when
compiling already-generated IR did not prevent that transformation in this run.

The probes deliberately omit the existing C++ exception bridge. An unresolved
probe is an experiment result, and the script records its missing symbols.
Failures in the basic program, compilation, execution, or import checks fail the
script. A probe that becomes linkable is executed and checked as well.

Artifacts go into ignored `artifacts/no-crt-windows/`: `results.json`, emitted
IR, objects, executables, link maps, linker logs, import listings, PE headers,
unwind metadata, and machine-code disassembly. File sizes depend on the toolchain.

This establishes that the current `NoStdLib` property is sufficient for the
experiment's source selection. Startup/linker control and exception-runtime
independence remain separate requirements for a supported no-CRT configuration.
