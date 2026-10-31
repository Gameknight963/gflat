# Inspect binary dependencies

Show a binary file's format, machine code, and declared dependencies:

```console
gflat build examples/binary-info/binary-info.gfproj --run -- program.exe
```

Example PE output:

```text
format: PE32+
bytes: 2048
machine: 0x8664
clr-directory: absent
import: "KERNEL32.dll"
```

Example ELF output:

```text
format: ELF64LE
bytes: 16384
machine: 0x003e
interpreter: "/lib64/ld-linux-x86-64.so.2"
needed: "libc.so.6"
```

## Supported files

The inspector reads PE32+ Windows images and little-endian ELF64 executables
or shared objects on either supported host. Input is limited to 64 MiB and
loaded completely into memory.

- PE files must have 1 through 96 sections. The output lists ordinary import
  DLLs and whether a file-backed CLR directory is present
- ELF files may have at most 256 program headers. Extended counts are rejected.
  The output lists the interpreter and direct `DT_NEEDED` entries

Names keep directory order and duplicates. Quotes, backslashes, controls, and
non-ASCII bytes use `\xhh` escapes. The listing covers declared direct
dependencies. Statically linked code and libraries loaded at runtime require
other inspection.

Exit codes are 0 for a completed listing, 1 for unsupported or malformed input
or IO failure, and 2 for invalid arguments. Errors go to stderr. A later
metadata error can leave earlier output already written.
