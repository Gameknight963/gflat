# Windows executable without the CRT

A Windows x64 experiment using `<NoStdLib>true</NoStdLib>`, a small runtime written in
gflat, and an assembly stack-probing helper.

## Run

From the repository root, in PowerShell:

```powershell
./experiments/no-crt-windows/build.ps1
```

Building requires the .NET 10 SDK, LLVM (`clang`, `lld-link`, `llvm-readobj`, and
`llvm-objdump`), and the Windows SDK's x64 `kernel32.lib`. The resulting basic
executable links directly to Windows APIs.

The script builds the compiler, emits IR through `NoCrt.gfproj`, then compiles and links
explicitly. Its optional parameters are `-Compiler` (a prebuilt gflat DLL), `-Clang`,
`-WindowsSdkLibraryDirectory` (containing `kernel32.lib`), and `-OutputDirectory`. Use
this script for the native experiment. Ordinary project builds use the hosted linker
configuration.

## What the basic program does

- Enters the gflat `Start` function through the linker's `/ENTRY`
- Copies and modifies a fixed-size stack buffer, checking that the original stays intact
- Writes and verifies a 32 KiB stack buffer, exercising guard-page stack growth
- Allocates through `Memory` backed by the Windows process heap
- Constructs a struct in that storage using its implicit default constructor,
  accesses its field, invokes its destructor, and frees the storage
- Writes `gflat without CRT` and exits with status zero

## Native helpers and linking

Allocation delegates to `HeapAlloc`. `StackProbe.S` supplies `__chkstk`, whose
register-based calling convention cannot be expressed as an ordinary gflat function. It
probes each intervening page and preserves the requested size and caller's stack
pointer. See Microsoft's [stack-probing helper
documentation](https://learn.microsoft.com/en-us/windows/win32/devnotes/-win32-__chkstk).

The link uses `/NODEFAULTLIB` and exactly our program object, our stack-probe object,
and `kernel32.lib`. The script checks the basic executable's six imports: `ExitProcess`,
`GetProcessHeap`, `GetStdHandle`, `HeapAlloc`, `HeapFree`, and `WriteFile`, all from
`KERNEL32.dll`. These are direct imports. Windows supplies its own underlying
dependencies.

## Observed results

Both `-O0` and `-O2` are tested. Measurements on this machine:

| Case | `-O0` | `-O2` |
| --- | --- | --- |
| Basic program | 3,584-byte executable, runs | 4,608-byte executable, runs |
| Placement construction with a user constructor | Runs | Runs |
| Ordinary allocating `new*` | Exception-runtime symbols unresolved | Exception-runtime symbols unresolved |
| Throw/catch | Exception-runtime symbols unresolved | Exception-runtime symbols unresolved |
| `Standard::String` with our Memory implementation | Exception-runtime symbols unresolved | Exception-runtime symbols and `strlen` unresolved |

The constructor probe stores one integer field and must pass at both optimization
levels. Constructors that actually require exception cleanup still need runtime support.

`new*` must support its allocation-failure exception. String also throws for allocation
failure, overflow, and invalid indexing. These cases need an exception implementation
even when allocation is supplied independently of libc. The optimized String case
additionally demonstrates a compiler-generated library call: LLVM introduces `strlen`
while optimizing the emitted IR. Passing `-ffreestanding` when compiling that IR
preserved the transformation in this run.

The script records missing symbols for probes that require the exception bridge.
Failures in the basic program, the constructor case, compilation, execution, or import
checks fail the script. Other probes that become linkable are executed and checked as
well.

Artifacts go into ignored `artifacts/no-crt-windows/`: `results.json`, emitted IR,
objects, executables, link maps, linker logs, import listings, PE headers, unwind
metadata, and machine-code disassembly. File sizes depend on the toolchain.

`NoStdLib` selects the experiment's sources. The build script supplies its own startup
and linker configuration. Supporting the remaining probes requires an independent
exception runtime.
