# Standard library

Standard provides strings, collections, queries, text and binary formats, and file
and console IO. Projects include it by default. Set `NoStdLib` to select your own
source files and runtime implementation.

Open [Standard.slnx](Standard.slnx) in Visual Studio to work on the library.
Start with the [text-processing tutorial](../docs/STDLIB_GETTING_STARTED.md), or
browse the [API index](../docs/STANDARD_LIBRARY_INDEX.md).

## Namespaces and files

Directories follow namespaces within two source roots:

- `core` contains portable algorithms and data structures
- `libc` contains host runtime integration

Owning core types allocate through `Memory::Allocate` and `Memory::Free`. Their
default implementations live in `libc`. Both roots can contribute to the same
namespace.

| Namespace | APIs |
| --- | --- |
| `Standard` | General equality, ordering, and hashing contracts. `Uuid`, `Version`, `Environment`, `EnvironmentText` |
| `Standard::Collections` | Containers, iteration contracts, collection algorithms |
| `Standard::Queries` | Query extensions and supporting sequences |
| `Standard::Text` | Strings, Unicode, text writers, formatting, text parsing |
| `Standard::Numerics` | Scalar math, arbitrary-precision numbers, bit operations, seeded randomness |
| `Standard::Memory` | Spans, owning pointers, arenas, memory operations |
| `Standard::IO` | Files, paths, borrowed byte readers, IO exceptions |
| `Standard::Binary` | Binary cursors, byte-order operations, bit fields, variable-length integers |
| `Standard::Encoding` | Base32, Base64, hexadecimal, percent encoding |
| `Standard::Compression` | Bounded raw DEFLATE, gzip member, and zlib stream decoding |
| `Standard::Json`, `Standard::Csv` | Format readers, writers, helpers, and exceptions |
| `Standard::Time` | Dates, times, durations, clocks, timers |
| `Standard::Net` | Numeric addresses, endpoints, and networks |
| `Standard::Hashing` | Checksums and digest implementations |
| `Standard::Threading` | Atomic integer operations |
| `Standard::Reflection` (planned) | Reflection descriptors |

Ordinary library APIs use these subject namespaces. Reflection's move is
deferred: `TypeInfo` and `TypeKind` currently remain in `Standard`.
Deliberate global APIs remain valid, including `Console`, `Exception`, `Memory`,
and the selected `GC` alias.

Use `using Standard::Text;` for `String`, its `s`/`s$` literals, and borrowed text views.
The formatting contracts `ITextWriter`, `IFormattable`, and `IStringConvertable`
remain global. Primitive `Format` overloads remain in `Standard`.

## Core

- [Strings, spans, vectors, and formatting](core/README.md)
- [Collections and algorithms](core/Collections/OWNING_COLLECTIONS.md)
- [Lazy queries](core/Queries/README.md)
- [Complete API index](../docs/STANDARD_LIBRARY_INDEX.md#core)

Containers own their values. Views and raw pointers borrow storage. Keep each
owner alive while using its views, and reacquire views after operations that
invalidate storage. See the [ownership guide](../docs/CORE_LIBRARY.md#vectors).

## Libc and host runtime

- [Console, files, environment variables, and managed memory](libc/README.md)
- [Choosing library sources](../docs/PROJECTS.md#standard-library-selection)
- [Optional platform libraries](../libraries/README.md) for clocks, random bytes,
  and Windows TCP

File and console APIs accept caller-owned buffers and owning `String` values.
Call `File.Close()` explicitly to report final flush and close errors.

## Runnable examples

Try [word count](../examples/word-count/README.md),
[JSON formatting](../examples/json-pretty/README.md), or
[file checksums](../examples/checksum/README.md).
The [example index](../examples/README.md) lists all runnable projects.
