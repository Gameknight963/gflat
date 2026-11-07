# Windows executable without the CRT

Build a small Windows x64 executable using a runtime written in gflat and an
assembly stack-probing helper:

```powershell
./experiments/no-crt-windows/build.ps1
```

## Requirements

- .NET 10 SDK
- LLVM tools: `clang`, `lld-link`, `llvm-readobj`, and `llvm-objdump`
- Windows SDK x64 `kernel32.lib`

The script accepts `-Compiler` for a prebuilt gflat DLL, `-Clang`,
`-WindowsSdkLibraryDirectory`, and `-OutputDirectory`.

## What it exercises

The basic program copies a value buffer, grows the stack through a 32 KiB
allocation, allocates from the Windows process heap, and constructs and
destroys a struct in that storage. It prints `gflat without CRT` and exits.

`NoStdLib` selects its source files. The script supplies `Start` as the entry
point and links with `/NODEFAULTLIB` against `kernel32.lib`. `StackProbe.S`
provides `__chkstk`, whose register-based calling convention needs the assembly
helper. See [Microsoft's stack-probing documentation](https://learn.microsoft.com/en-us/windows/win32/devnotes/-win32-__chkstk).

## Recorded results

These measurements came from the experiment's original run:

| Case | `-O0` | `-O2` |
| --- | --- | --- |
| Basic program | 3,584-byte executable, runs | 4,608-byte executable, runs |
| Placement construction with a user constructor | Runs | Runs |
| Ordinary allocating `new*` | Exception-runtime symbols unresolved | Exception-runtime symbols unresolved |
| Throw/catch | Exception-runtime symbols unresolved | Exception-runtime symbols unresolved |
| `Standard::String` with our Memory implementation | Exception-runtime symbols unresolved | Exception-runtime symbols and `strlen` unresolved |

Allocation failure, throwing, and String operations require an exception
runtime. LLVM also introduced `strlen` while optimizing the String probe.

The script records unresolved symbols for those probes. Failures in the basic
program, placement-constructor probe, compilation, execution, or import checks
fail the script. Other probes are run when they become linkable.

Results, IR, objects, binaries, linker logs, imports, and disassembly are written
to ignored `artifacts/no-crt-windows/`. Binary sizes depend on the toolchain.
