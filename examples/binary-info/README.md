# Binary metadata listing

```console
gflat build examples/binary-info/binary-info.gfproj --run -- program.exe
```

Reports a file's byte count, format, numeric machine code, and declared dependency
metadata. It reads PE32+ Windows images and little-endian ELF64 executables or shared
objects on either supported host.

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

## Output

- Machine codes use four lowercase hexadecimal digits from the file header
- Import and needed names retain table order and duplicates
- Empty dependency lists print `import: none` or `needed: none`
- An absent ELF interpreter prints `interpreter: absent`
- Names preserve printable ASCII, with quotes, backslashes, and other bytes
  escaped as `\xhh`
- CLR presence means a nonempty, file-backed CLR directory exists

The listing covers declared direct dependencies. Libraries loaded at runtime and
statically linked code require other forms of inspection.

## Supported subset and bounds

`FileBytes::ReadAll` loads and closes the input before inspection. The file-size limit
is 64 MiB. Memory use also includes Vector capacity and runtime overhead. Metadata
ranges are checked against file bounds, and names must terminate within their containing
file-backed region.

### PE32+

PE requires MZ/PE signatures, PE32+ optional magic and 1 through 96 sections. RVA
mapping uses header bytes or section raw bytes, excluding virtual zero-fill. Import
descriptors require an all-zero terminator within their directory. Overlapping
file-backed RVA ranges are rejected, and each metadata range must fit one mapping. The
listing covers ordinary import DLL names and CLR presence.

### ELF64

ELF requires class64, little-endian/version1, type executable/shared object, the 64-byte
header and ordinary 56-byte program entries. The explicit example limit is 256 program
headers. Extended program counts are rejected. Stripped images work without a section
table. PT_INTERP and PT_DYNAMIC must be unique when present. Dynamic records require
DT_NULL within their segment. Dependency names use DT_STRTAB/DT_STRSZ and file-backed
PT_LOAD mappings. Overlapping nonempty file-backed load VA ranges and duplicate
string-table address/size tags are rejected. Adjacent and empty ranges are permitted.
Metadata must fit one mapping.

## Errors

Exit codes are 0 for a completed listing, 1 for malformed/unsupported/over-limit input
or read/close/output/allocation failure, and 2 for wrong argument count. Diagnostics go
to stderr when writable. A stderr failure preserves the selected exit code. Output can
contain an already-written prefix when later parsing or output fails. A single operand
is a literal filename, including an option-like name.
