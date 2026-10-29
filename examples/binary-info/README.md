# Binary metadata listing

```console
gflat build examples/binary-info/binary-info.gfproj --run -- program.exe
```

Reads a file as data and reports its byte count, format and numeric machine code,
then the declared dependency metadata. It never executes the inspected file.
The inspector can run on either host and inspect either supported format:
PE32+ (64-bit Windows images) or ELF64 little-endian executables/shared objects.

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

Sizes above are illustrative, not expected sizes for generated programs. Machine
is the header's numeric value in four lowercase hex digits, not an execution test.
PE import and ELF needed names retain table order and duplicates. Empty import or
needed lists print `import: none` or `needed: none`. An absent ELF interpreter
prints `interpreter: absent`. Names are bounded NUL-terminated byte strings;
printable ASCII is preserved except quotes and backslashes. Other bytes print as
`\xhh`, including quotes/backslashes, controls and bytes outside ASCII.

PE CLR reporting only describes whether a nonempty, file-backed CLR data-directory
entry exists. It does not parse the CLR header or prove that managed code is valid.
These are declared direct imports/DT_NEEDED entries, not all runtime dependencies.
No imports, interpreter or CLR directory does not mean a program has no runtime,
system calls, dynamically loaded libraries or statically linked runtime code.
Paths are not resolved; dependent files are not opened or inspected recursively.

## Supported subset and bounds

FileBytes::ReadAll loads the input into one owned Vector before borrowing bytes
for inspection. The explicit example limit is 64 MiB of file bytes; libc and Vector
capacity/temporary overhead are additional. All growth finishes before views are
created. The input is closed before parsing. Fixed integers use checked little-
endian BinaryBufferReader seeks; offsets/counts and narrowing conversions are
bounded by the captured file. Mapped names must terminate within their file-backed
section, header region, interpreter segment or declared dynamic string table.
No unbounded C-string scan is performed on file bytes.

PE requires MZ/PE signatures, PE32+ optional magic and 1 through 96 sections.
Only ordinary import DLL names and CLR-directory presence are inspected. RVA
mapping uses header bytes or section raw bytes, excluding virtual zero-fill.
Import descriptors must have an all-zero terminator within their declared directory.
Overlapping nonempty file-backed RVA ranges (including the header region) are
rejected. A requested metadata range must fit a single mapping. PE32, raw COFF
objects, ZIPs, delay/bound imports, symbols, relocations, resources, certificates,
loader compatibility and full Windows-image validity are outside scope.

ELF requires class64, little-endian/version1, type executable/shared object, the
64-byte header and ordinary 56-byte program entries. The explicit example limit
is 256 program headers; extended program counts are rejected. Section headers
are unused, so stripped images need no section table. PT_INTERP and PT_DYNAMIC
must be unique when present. Dynamic records require DT_NULL within their segment.
Dependency names use DT_STRTAB/DT_STRSZ and file-backed PT_LOAD mappings. Overlapping
nonempty file-backed load VA ranges and duplicate string-table address/size tags
are rejected. Adjacent and zero-size ranges are permitted; metadata still must fit
one mapping. ELF32, big-endian, relocatable/core files, processor-specific loader
semantics, relocations and full ELF validity are outside scope.

This is a bounded metadata inspector, not an executable conformance/security
verdict, dependency resolver, disassembler or extraction tool. It modifies no
input bytes or executable metadata. It uses existing Std APIs and no native PE/ELF
inspection libraries.

Exit codes are 0 for a completed listing, 1 for malformed/unsupported/over-limit
input or read/close/output/allocation failure, and 2 for wrong argument count.
Diagnostics go to stderr when writable; a stderr failure preserves the selected
exit code. Output can contain an already-written prefix when later parsing or
output fails. A single operand is a literal filename, including an option-like
name. No command-line switches are interpreted.
