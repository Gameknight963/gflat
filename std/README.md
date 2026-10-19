# Standard library

Projects include this source library by default. `NoStdLib` disables the default
reference; applications can then select individual source files or provide their
own runtime contracts. See [project setup](../docs/PROJECTS.md).

## Core

Core algorithms and data structures contain no direct libc calls. Owning types
allocate through the replaceable `Memory::Allocate`/`Free` API; its default
implementation lives in `libc`.

| Area | APIs | Details |
| --- | --- | --- |
| Text | `String`, `ITextWriter`, `IFormattable`, `IStringConvertable` | [Strings](../docs/STRINGS.md), [formatting](core/README.md#strings-and-formatting) |
| Caller-buffer text | `BufferWriter` | [Allocation-free formatting](../docs/BUFFER_WRITER.md) |
| Borrowed text parts | `TextParts`, String/Span `Split` | [Allocation-free splitting](../docs/TEXT_PARTS.md) |
| Contiguous storage | `Vector<T>`, `Span<T>` | [Ownership and views](core/README.md#vectors) |
| Collections | `Queue<T>`, `Stack<T>`, `PriorityQueue<T>`, `BitSet` | [Owning collections](core/Collections/OWNING_COLLECTIONS.md) |
| Hash collections | `HashMap<K,V>`, `HashSet<T>` | [Borrowed lookup and ownership](../docs/HASH_COLLECTIONS.md) |
| Contracts | `IHashable`, `IEquatable<T>`, `IComparable<T>` | [Primitive and String implementations](../docs/HASHING.md) |
| Algorithms | `Sort`, `Reverse`, `Partition`, bounds/search helpers, `Comparer<T>` | [Span algorithms](core/Collections/ALGORITHMS.md) |
| Queries | `IEnumerable<T,E>`, `Where`, `Select`, `Skip`, `Take`, aggregates | [Lazy queries](core/Collections/README.md) |
| Numeric text | `TryParseInt`, `ParseInt`, matching methods for other integer widths | [Strict decimal parsing](../docs/INTEGER_PARSING.md) |
| Unicode | `Utf8` strict scalar decoding, encoding, validation, counting | [UTF-8](../docs/UTF8.md) |
| ASCII bytes | `Ascii` classification and case conversion | [Byte utilities](../docs/ASCII.md) |
| Binary text | `Base64`, `Hex`, optional String conveniences | [Caller-buffer codecs](../docs/BINARY_CODECS.md) |
| Paths | `Paths`, `PathStyle` | [Lexical Unix/Windows paths](core/Paths/README.md) |
| Numbers | `Math`, `PseudoRandom` | [Scalar math](../docs/MATH.md), [seeded randomness](../docs/PSEUDORANDOM.md) |
| Bits | `BitOperations` | [Counts and rotations](../docs/BIT_OPERATIONS.md) |
| Temporary storage | `Arena` | [Independent raw-storage arenas](../docs/ARENA.md) |

Containers own stored values. Raw pointers stored as elements borrow their
pointees; they do not cause automatic deletion. Views and enumerators borrow
their sources. Do not retain them across operations that invalidate storage.

## Libc and host runtime

| Area | APIs |
| --- | --- |
| Console | `Console::Write`, `WriteLine`, `ReadLine`, `TryReadLine`, `Flush` |
| Files | `File`, `FileText`, `FileLines`, `FileBinary` |
| Allocation | Default replaceable `Memory::Allocate` and `Memory::Free` |
| Managed storage | Replaceable collector selection and `TracingCollector` |
| Exceptions | Default unhandled exception termination |
| Floating text | Floating-point formatting overloads |

See [host APIs](libc/README.md). File and console operations accept caller-owned
character buffers; `String` methods are conveniences rather than a requirement.
Explicit file `Close` can report errors; destruction closes without throwing.

## Runnable examples

- [Word count](../examples/word-count/README.md): streamed input and owning hash keys.
- [Pathfinding](../examples/pathfinding/README.md): weighted shortest paths using a priority queue.
- [Search](../examples/search/README.md): chunked file processing.
- [Queries](../examples/queries/main.gf): lazy, allocation-free collection pipelines.

This remains an experimental library. There are no thread-safety guarantees for
mutable containers, no locale-sensitive text operations, and no cryptographic
guarantees for ordinary hashes or seeded random values.
