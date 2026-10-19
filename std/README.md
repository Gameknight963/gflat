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
| C strings | `CString` | [Borrowed null-terminated byte utilities](../docs/CSTRING.md) |
| Caller-buffer text | `BufferWriter` | [Allocation-free formatting](../docs/BUFFER_WRITER.md) |
| Writer line helpers | `ITextWriter*` `WriteLine` overloads | [Borrowed writer conveniences](../docs/TEXT_WRITERS.md) |
| Borrowed text parts | `TextParts`, String/Span `Split` | [Allocation-free splitting](../docs/TEXT_PARTS.md) |
| Borrowed text lines | `TextLines` | [Counted LF and CRLF line views](../docs/TEXT_LINES.md) |
| Contiguous storage | `Vector<T>`, `Span<T>`, typed span `CopyTo` | [Ownership and views](core/README.md#vectors), [copying initialized elements](core/Collections/OWNING_COLLECTIONS.md#copying-borrowed-spans) |
| Collections | `Queue<T>`, `Stack<T>`, `PriorityQueue<T>`, `BitSet` | [Owning collections](core/Collections/OWNING_COLLECTIONS.md) |
| Double-ended storage | `Deque<T>` | [Owning ring buffer](../docs/DEQUE.md) |
| Stable node storage | `LinkedList<T>` | [Owning linked lists and node handles](../docs/LINKED_LIST.md) |
| Hash collections | `HashMap<K,V>`, `HashSet<T>` | [Borrowed lookup and ownership](../docs/HASH_COLLECTIONS.md) |
| Ordered maps | `SortedMap<K,V>` | [Sorted contiguous entries and explicit comparison](../docs/SORTED_MAP.md) |
| Contracts | `IHashable`, `IEquatable<T>`, `IComparable<T>` | [Primitive and String implementations](../docs/HASHING.md) |
| Algorithms | `Sort`, `Reverse`, `Shuffle`, `Partition`, bounds/search helpers, `Comparer<T>` | [Span algorithms](core/Collections/ALGORITHMS.md) |
| Queries | `IEnumerable<T,E>`, `Where`, `Select`, `Skip`, `Take`, `SkipWhile`, `TakeWhile`, aggregates and `TryFirst`/`TryLast`/`TrySingle` | [Lazy queries](core/Collections/README.md) |
| Numeric text | `TryParseInt`, `ParseInt`, matching methods for other integer widths | [Strict decimal parsing](../docs/INTEGER_PARSING.md) |
| Caller-buffer numbers | `IntegerFormatting::TryFormat` | [Decimal formatting without a writer or allocation](../docs/INTEGER_FORMATTING.md) |
| Unicode | `Utf8`, incremental `Utf8Decoder`, borrowed `Utf8Scalars` | [UTF-8 buffers](../docs/UTF8.md), [chunked decoding](../docs/UTF8_DECODER.md), [scalar iteration](../docs/UTF8_SCALARS.md) |
| UTF-16 and conversion | `Utf16`, incremental `Utf16Decoder`, borrowed `Utf16Scalars`, `UnicodeTranscoding` | [Code units and chunked decoding](../docs/UTF16.md), [scalar iteration](../docs/UTF16_SCALARS.md), [strict caller-buffer transcoding](../docs/UNICODE_TRANSCODING.md) |
| ASCII bytes | `Ascii` classification and case conversion | [Byte utilities](../docs/ASCII.md) |
| Binary text | `Base64`, `Hex`, optional String conveniences | [Caller-buffer codecs](../docs/BINARY_CODECS.md) |
| JSON quoting | `JsonString` | [Strict UTF-8 string literals in caller buffers](../docs/JSON_STRING.md) |
| URI components | `PercentEncoding`, optional String conveniences | [Percent encoding](../docs/BINARY_CODECS.md#component-percent-encoding) |
| Paths | `Paths`, `PathStyle` | [Lexical Unix/Windows paths](core/Paths/README.md) |
| Numbers | `Math`, `PseudoRandom` | [Scalar math](../docs/MATH.md), [seeded randomness](../docs/PSEUDORANDOM.md) |
| Bits | `BitOperations` | [Counts, rotations, and byte reversal](../docs/BIT_OPERATIONS.md) |
| Floating representations | `BitConverter` | [Exact floating-point bit conversions](../docs/BIT_CONVERTER.md) |
| Binary integers | `BinaryPrimitives` | [Checked byte-order reads and writes](../docs/BINARY_PRIMITIVES.md) |
| Byte searches | `ByteSearch` | [Counted binary substring search](../docs/BYTE_SEARCH.md) |
| Binary buffers | `BinaryBufferReader`, `BinaryBufferWriter` | [Borrowed cursors and bounded payloads](../docs/BINARY_CURSORS.md) |
| Streaming checksums | `CRC32`, `Adler32` | [Incremental byte checksums](../docs/CHECKSUMS.md) |
| Temporary storage | `Arena` | [Independent raw-storage arenas](../docs/ARENA.md) |
| Raw memory | `Memory::Copy`, `Move`, `Fill`, `Compare`, `Equal` | [Byte operations and linking](../docs/MEMORY.md) |

Containers own stored values. Raw pointers stored as elements borrow their
pointees; they do not cause automatic deletion. Views and enumerators borrow
their sources. Do not retain them across operations that invalidate storage.

## Libc and host runtime

| Area | APIs |
| --- | --- |
| Console | `Console::Write`, `WriteLine`, `Read`, `ReadByte`, `ReadExactly`, `ReadLine`, `TryReadLine`, `Out`, `Error`, flush operations |
| Files | `File`, `FileText`, `FileBytes`, `FileLines`, `FileLineSequence`, `FileBinary` |
| Allocation | Default replaceable `Memory::Allocate` and `Memory::Free` |
| Managed storage | Replaceable collector selection and `TracingCollector` |
| Exceptions | Default unhandled exception termination |
| Floating text | Floating-point formatting overloads |
| Environment | Borrowed `Environment::GetVariable` and owning `EnvironmentText` conveniences |

See [host APIs](libc/README.md). File and console operations accept caller-owned
character buffers; `String` methods are conveniences rather than a requirement.
Explicit file `Close` can report errors; destruction closes without throwing.

## Runnable examples

- [Word count](../examples/word-count/README.md): streamed input and owning hash keys.
- [File checksums](../examples/checksum/README.md): streaming CRC32 and Adler32 over binary files.
- [Hex dump](../examples/hexdump/README.md): byte offsets, hex columns, and printable ASCII with stack-backed formatting.
- [Unicode conversion](../examples/transcode/README.md): strict UTF-8/UTF-16LE conversion with validation before output-file creation.
- [Pathfinding](../examples/pathfinding/README.md): weighted shortest paths using a priority queue.
- [Search](../examples/search/README.md): chunked file processing.
- [Queries](../examples/queries/main.gf): lazy, allocation-free collection pipelines.

This remains an experimental library. There are no thread-safety guarantees for
mutable containers, no locale-sensitive text operations, and no cryptographic
guarantees for ordinary hashes or seeded random values.
